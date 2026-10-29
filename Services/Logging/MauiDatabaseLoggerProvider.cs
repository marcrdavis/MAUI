// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Persists MAUI ILogger events to the metadata database without blocking request threads on database writes.

using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Authentication;
using MoveItAutomationUI.Services.Metadata;

namespace MoveItAutomationUI.Services.Logging;

internal sealed record MauiApplicationLogRecord(
    DateTime TimestampUtc,
    LogLevel Level,
    string Category,
    EventId EventId,
    string Username,
    string EnvironmentKey,
    string RequestPath,
    string TraceId,
    string Message,
    string Exception,
    string PropertiesJson);

public sealed class MauiApplicationLogChannel
{
    private const int Capacity = 10000;
    private readonly Channel<MauiApplicationLogRecord> _channel = Channel.CreateBounded<MauiApplicationLogRecord>(new BoundedChannelOptions(Capacity)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.DropOldest
    });

    internal ChannelReader<MauiApplicationLogRecord> Reader => _channel.Reader;
    internal bool TryWrite(MauiApplicationLogRecord record) => _channel.Writer.TryWrite(record);
}

public sealed class MauiDatabaseLoggerProvider : ILoggerProvider
{
    private readonly MauiApplicationLogChannel _channel;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IConfiguration _configuration;

    public MauiDatabaseLoggerProvider(
        MauiApplicationLogChannel channel,
        IHttpContextAccessor httpContextAccessor,
        IConfiguration configuration)
    {
        _channel = channel;
        _httpContextAccessor = httpContextAccessor;
        _configuration = configuration;
    }

    public ILogger CreateLogger(string categoryName)
        => new MauiDatabaseLogger(categoryName, _channel, _httpContextAccessor, _configuration);

    public void Dispose()
    {
    }

    private sealed class MauiDatabaseLogger : ILogger
    {
        private const string MauiCategoryPrefix = "MoveItAutomationUI";
        private readonly string _category;
        private readonly MauiApplicationLogChannel _channel;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IConfiguration _configuration;

        public MauiDatabaseLogger(
            string category,
            MauiApplicationLogChannel channel,
            IHttpContextAccessor httpContextAccessor,
            IConfiguration configuration)
        {
            _category = category ?? string.Empty;
            _channel = channel;
            _httpContextAccessor = httpContextAccessor;
            _configuration = configuration;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        // The normal ASP.NET logging filters still determine enabled levels. This provider only narrows storage to MAUI-owned categories.
        public bool IsEnabled(LogLevel logLevel)
            => logLevel != LogLevel.None
               && (_category.Equals(MauiCategoryPrefix, StringComparison.Ordinal)
                   || _category.StartsWith(MauiCategoryPrefix + ".", StringComparison.Ordinal));

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            string message;
            try
            {
                message = formatter(state, exception) ?? string.Empty;
            }
            catch
            {
                return;
            }

            if (message.Length == 0 && exception is null) return;

            var context = _httpContextAccessor.HttpContext;
            var username = context?.User?.Identity?.IsAuthenticated == true
                ? context.User.Identity?.Name ?? string.Empty
                : string.Empty;
            var serverKey = context?.User?.FindFirst(MauiAuthenticationDefaults.ServerKeyClaimType)?.Value ?? string.Empty;
            var environmentKey = ResolveEnvironmentKey(serverKey);
            var requestPath = context?.Request.Path.Value ?? string.Empty;
            var traceId = Activity.Current?.TraceId.ToString() ?? context?.TraceIdentifier ?? string.Empty;

            _channel.TryWrite(new MauiApplicationLogRecord(
                DateTime.UtcNow,
                logLevel,
                _category,
                eventId,
                username,
                environmentKey,
                requestPath,
                traceId,
                message,
                exception?.ToString() ?? string.Empty,
                BuildPropertiesJson(state)));
        }

        private string ResolveEnvironmentKey(string serverKey)
        {
            if (string.IsNullOrWhiteSpace(serverKey)) return string.Empty;
            try
            {
                var servers = _configuration.GetSection($"{MoveItOptions.SectionName}:Servers").Get<List<MoveItServerDefinition>>() ?? new List<MoveItServerDefinition>();
                var server = servers.FirstOrDefault(item => string.Equals(item.Key, serverKey, StringComparison.OrdinalIgnoreCase));
                if (server is null) return serverKey;
                return string.IsNullOrWhiteSpace(server.EnvironmentKey) ? server.Key : server.EnvironmentKey;
            }
            catch
            {
                return serverKey;
            }
        }

        private static string BuildPropertiesJson<TState>(TState state)
        {
            if (state is not IEnumerable<KeyValuePair<string, object?>> properties) return string.Empty;

            try
            {
                var values = properties
                    .Where(pair => !pair.Key.Equals("{OriginalFormat}", StringComparison.Ordinal))
                    .ToDictionary(
                        pair => pair.Key,
                        pair => pair.Value?.ToString() ?? string.Empty,
                        StringComparer.OrdinalIgnoreCase);
                return values.Count == 0 ? string.Empty : JsonSerializer.Serialize(values);
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();
        public void Dispose()
        {
        }
    }
}

public sealed class MauiDatabaseLogWriter : BackgroundService
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);
    private const int MaximumBatchSize = 100;

    private readonly MauiApplicationLogChannel _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MauiDatabaseSchemaState _schemaState;
    private readonly IOptionsMonitor<ApplicationLogOptions> _options;
    private DateTime _nextCleanupUtc = DateTime.MinValue;

    public MauiDatabaseLogWriter(
        MauiApplicationLogChannel channel,
        IServiceScopeFactory scopeFactory,
        MauiDatabaseSchemaState schemaState,
        IOptionsMonitor<ApplicationLogOptions> options)
    {
        _channel = channel;
        _scopeFactory = scopeFactory;
        _schemaState = schemaState;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(FlushInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                if (!_schemaState.Initialized) continue;
                await PersistAvailableAsync(stoppingToken);
                await PurgeExpiredAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (_schemaState.Initialized)
            {
                try { await PersistAvailableAsync(CancellationToken.None); } catch { }
            }
        }
    }

    private async Task PersistAvailableAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var batch = new List<MauiApplicationLogRecord>(MaximumBatchSize);
            while (batch.Count < MaximumBatchSize && _channel.Reader.TryRead(out var record))
                batch.Add(record);

            if (batch.Count == 0) return;

            for (var attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<MetadataDbContext>();
                    db.ApplicationLogs.AddRange(batch.Select(record => new ApplicationLogEntity
                    {
                        TimestampUtc = record.TimestampUtc,
                        Level = TrimTo(record.Level.ToString(), 16),
                        Category = TrimTo(record.Category, 256),
                        EventId = record.EventId.Id,
                        EventName = TrimTo(record.EventId.Name ?? string.Empty, 128),
                        Username = TrimTo(record.Username, 128),
                        EnvironmentKey = TrimTo(record.EnvironmentKey, 64),
                        RequestPath = TrimTo(record.RequestPath, 512),
                        TraceId = TrimTo(record.TraceId, 64),
                        Message = record.Message,
                        Exception = record.Exception,
                        PropertiesJson = record.PropertiesJson
                    }));
                    await db.SaveChangesAsync(cancellationToken);
                    break;
                }
                catch when (attempt < 2)
                {
                    try { await Task.Delay(100, cancellationToken); } catch { break; }
                }
                catch
                {
                    // Database logging must never destabilize the application or recursively log its own failures.
                }
            }
        }
    }


    private static string TrimTo(string value, int maximumLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maximumLength) return value ?? string.Empty;
        return value[..maximumLength];
    }

    private async Task PurgeExpiredAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        if (now < _nextCleanupUtc) return;
        _nextCleanupUtc = now.Add(CleanupInterval);

        var retentionDays = Math.Clamp(
            _options.CurrentValue.RetentionDays.Value,
            ApplicationLogOptions.MinimumRetentionDays,
            ApplicationLogOptions.MaximumRetentionDays);
        var cutoff = now.AddDays(-retentionDays);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MetadataDbContext>();
            await db.ApplicationLogs.Where(item => item.TimestampUtc < cutoff).ExecuteDeleteAsync(cancellationToken);
        }
        catch
        {
            // Cleanup is best-effort and must not interfere with normal application logging.
        }
    }
}

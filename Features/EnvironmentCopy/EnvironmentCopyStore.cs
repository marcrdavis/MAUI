using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace MoveItAutomationUI.Features.EnvironmentCopy;

// Plans and protected material stay server-side. The durable report contains identities/outcomes only.
public sealed class EnvironmentCopyStore
{
    private readonly ConcurrentDictionary<string, CopyPlan> _plans = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ProgressSnapshot> _progress = new(StringComparer.Ordinal);
    private readonly string _directory;
    private readonly IDataProtector _protector;
    private readonly SemaphoreSlim _journal = new(1, 1);
    private readonly IOptionsMonitor<EnvironmentCopyOptions> _options;
    private readonly ILogger<EnvironmentCopyStore>? _logger;
    internal Channel<CopyPlan> Queue { get; } = Channel.CreateBounded<CopyPlan>(new BoundedChannelOptions(10) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
    public EnvironmentCopyStore(IWebHostEnvironment environment, IDataProtectionProvider protection, IOptionsMonitor<EnvironmentCopyOptions> options, ILogger<EnvironmentCopyStore>? logger = null)
    {
        _directory = Path.Combine(environment.ContentRootPath, "Data", "EnvironmentCopy");
        _protector = protection.CreateProtector("MAUI.EnvironmentCopy.Recovery.v1"); _options = options; _logger = logger;
    }
    internal void Add(CopyPlan plan)
    {
        PrunePlans();
        if (_plans.Count >= 10) throw new InvalidOperationException("Too many copy plans are open. Close an existing plan or wait for it to expire.");
        _plans[plan.Id] = plan;
    }
    internal CopyPlan Get(string id, string owner)
    {
        if (!_plans.TryGetValue(id ?? "", out var plan) || !plan.Owner.Equals(owner, StringComparison.OrdinalIgnoreCase)
            || !plan.Started && plan.ExpiresUtc <= DateTimeOffset.UtcNow) throw new InvalidOperationException("This copy plan expired. Analyze the selection again.");
        return plan;
    }
    internal void Forget(CopyPlan plan)
    {
        _plans.TryRemove(plan.Id, out _); _progress.TryRemove(plan.Id, out _); plan.Passphrase = "";
        foreach (var item in plan.Items)
        {
            if (item.KeyBytes is not null) System.Security.Cryptography.CryptographicOperations.ZeroMemory(item.KeyBytes);
            item.KeyBytes = null; item.NativeXml = ""; item.PortableXml = ""; item.DestinationXml = ""; item.DestinationPortableXml = "";
            item.Definition.Clear(); item.Metadata = null;
        }
    }
    internal void PrunePlans()
    {
        foreach (var plan in _plans.Values.Where(x => !x.Started && x.ExpiresUtc < DateTimeOffset.UtcNow).ToArray())
        {
            if (!plan.Gate.Wait(0)) continue;
            try { if (!plan.Started) Forget(plan); } finally { plan.Gate.Release(); }
        }
    }
    internal async Task DiscardAsync(string id, string owner)
    {
        CopyPlan plan;
        try { plan = Get(id, owner); } catch (InvalidOperationException) { return; }
        await plan.Gate.WaitAsync();
        try { if (!plan.Started) Forget(plan); } finally { plan.Gate.Release(); }
    }
    // Small immutable live snapshots keep long phases visible without rewriting the durable
    // journal for every inventory read. Write-ahead IDs/outcomes still use SaveReportAsync.
    internal void UpdateProgress(CopyReport report, string phase, CopyPlanItem? item = null)
    {
        var key = item?.Source.Key ?? "";
        if (report.Phase != phase || report.CurrentObjectKey != key) report.PhaseStartedUtc = DateTimeOffset.UtcNow;
        report.Phase = phase; report.CurrentObjectKey = key;
        report.CurrentObjectType = item?.Source.Type ?? ""; report.CurrentObjectName = item?.Choice.Name ?? "";
        _progress[report.OperationId] = new(phase, report.PhaseStartedUtc, key, report.CurrentObjectType, report.CurrentObjectName);
    }
    private sealed record ProgressSnapshot(string Phase, DateTimeOffset? StartedUtc, string Key, string Type, string Name)
    {
        internal void Apply(CopyReport report)
        {
            report.Phase = Phase; report.PhaseStartedUtc = StartedUtc; report.CurrentObjectKey = Key;
            report.CurrentObjectType = Type; report.CurrentObjectName = Name;
        }
    }
    public async Task SaveReportAsync(CopyReport report, CancellationToken ct = default)
    {
        ValidateId(report.OperationId); await _journal.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(_directory); var path = Path.Combine(_directory, report.OperationId + ".json");
            var temp = path + ".tmp"; await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(report), ct); File.Move(temp, path, true);
        }
        finally { _journal.Release(); }
    }
    public async Task<CopyReport> ReadReportAsync(string id, string owner, CancellationToken ct = default)
    {
        ValidateId(id); await _journal.WaitAsync(ct);
        try
        {
            var path = Path.Combine(_directory, id + ".json");
            if (!File.Exists(path) || File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddDays(-Math.Clamp(_options.CurrentValue.RecoveryRetentionDays.Value, 1, 90)))
                throw new InvalidOperationException("The copy report is unavailable or its retention period has ended.");
            var report = ParseReport(await File.ReadAllTextAsync(path, ct), id);
            if (!report.InitiatedBy.Equals(owner, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("This copy operation belongs to another administrator.");
            if (!report.CompletedUtc.HasValue)
            {
                if (_progress.TryGetValue(id, out var live)) live.Apply(report);
                if (_plans.TryGetValue(id, out var plan)) report.CancelRequested = plan.CancelRequested;
            }
            return report;
        }
        finally { _journal.Release(); }
    }
    internal async Task CaptureRecoveryAsync(CopyPlan plan, CopyPlanItem item, CancellationToken ct)
    {
        // DP-protected complete configuration is separate from the downloadable operation report.
        Directory.CreateDirectory(_directory);
        var payload = JsonSerializer.Serialize(new CopyRecoveryDefinition(plan.Destination, item.Choice.TargetId, item.Source.Type, item.DestinationXml));
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(item.Source.Key)));
        await File.WriteAllTextAsync(Path.Combine(_directory, plan.Id + "-" + key + ".recovery"), _protector.Protect(payload), ct);
    }
    public async Task<CopyRecoveryDefinition> ReadRecoveryAsync(string operationId, string key, string owner, CancellationToken ct = default)
    {
        // Read the report first for authorization, then protect the physical read from retention cleanup.
        var report = await ReadReportAsync(operationId, owner, ct);
        if (!report.Items.Any(x => x.Key == key && x.RecoveryCaptured)) throw new InvalidOperationException("No retained recovery definition exists for this object.");
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)));
        await _journal.WaitAsync(ct);
        try
        {
            var path = Path.Combine(_directory, operationId + "-" + hash + ".recovery");
            if (!File.Exists(path)) throw new InvalidOperationException("The protected recovery definition is unavailable.");
            return JsonSerializer.Deserialize<CopyRecoveryDefinition>(_protector.Unprotect(await File.ReadAllTextAsync(path, ct)))
                ?? throw new InvalidOperationException("The protected recovery definition could not be read.");
        }
        finally { _journal.Release(); }
    }

    // Separate from startup reconciliation: a timer must never mark a running copy Interrupted.
    // The journal gate also protects report/recovery downloads. Returned byte arrays are independent of disk.
    internal async Task PruneFilesAsync(CancellationToken ct)
    {
        await _journal.WaitAsync(ct);
        try
        {
            if (!Directory.Exists(_directory)) return;
            var cutoff = DateTime.UtcNow.AddDays(-Math.Clamp(_options.CurrentValue.RecoveryRetentionDays.Value, 1, 90));
            foreach (var path in Directory.EnumerateFiles(_directory).OrderBy(path => path.EndsWith(".json", StringComparison.Ordinal) ? 1 : 0).ToArray())
            {
                ct.ThrowIfCancellationRequested();
                var file = Path.GetFileName(path);
                if (file.Length < 32 || !Guid.TryParseExact(file[..32], "N", out _)) continue;
                var id = file[..32];
                if (_plans.TryGetValue(id, out var plan) && plan.Started) continue;
                if (!(file == id + ".json" || file == id + ".json.tmp" || file.EndsWith(".recovery", StringComparison.Ordinal))) continue;
                try
                {
                    var reportPath = Path.Combine(_directory, id + ".json");
                    // Recovery material lives at least as long as its report. Do not expire an older
                    // recovery file from an operation that completed within the current retention window.
                    var retained = File.Exists(reportPath) ? File.GetLastWriteTimeUtc(reportPath) : File.GetLastWriteTimeUtc(path);
                    if (retained < cutoff) File.Delete(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { _logger?.LogWarning("Copy retention could not remove {Journal} ({ErrorType}); cleanup will retry.", file, ex.GetType().Name); }
            }
        }
        finally { _journal.Release(); }
    }
    internal void RequestCancel(string id, string owner) => Get(id, owner).CancelRequested = true;
    internal async Task StartupAsync(CancellationToken ct)
    {
        if (!Directory.Exists(_directory)) return;
        await PruneFilesAsync(ct);
        foreach (var path in Directory.EnumerateFiles(_directory))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (!path.EndsWith(".json", StringComparison.Ordinal)) continue;
                var report = ParseReport(await File.ReadAllTextAsync(path, ct), Path.GetFileNameWithoutExtension(path));
                if (report.CompletedUtc.HasValue) continue;
                report.Status = "Interrupted"; report.Phase = "Interrupted"; report.CurrentObjectKey = ""; report.CurrentObjectType = ""; report.CurrentObjectName = ""; report.CompletedUtc = DateTimeOffset.UtcNow;
                report.Items = report.Items.Select(item => item.CheckoutCleanup == "Pending" ? item with { CheckoutCleanup = "Unconfirmed after restart" } : item).ToList();
                report.Message = "MAUI restarted before completion. Inspect recorded destination IDs and any object marked Not verified before starting a new copy. No writes were replayed.";
                await SaveReportAsync(report, ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
            {
                _logger?.LogWarning("Copy journal {Journal} could not be reconciled ({ErrorType}); other reports will still be checked.", Path.GetFileName(path), ex.GetType().Name);
            }
        }
    }
    private static CopyReport ParseReport(string json, string id)
    {
        const string message = "The saved copy report is incomplete or unreadable. Inspect the destination before starting another copy.";
        CopyReport? report;
        try { report = JsonSerializer.Deserialize<CopyReport>(json); }
        catch (JsonException ex) { throw new InvalidOperationException(message, ex); }
        // JSON can explicitly null non-nullable members. Validate the durable boundary before
        // ownership checks, progress calculations or startup reconciliation use these values.
        if (report is null || report.OperationId != id || string.IsNullOrWhiteSpace(report.InitiatedBy)
            || report.Items is null || report.Items.Any(item => item is null || item.Key is null || item.Status is null || item.Skipped is null || item.MetadataNotes is null
                || item.Skipped.Any(value => value is null) || item.MetadataNotes.Any(value => value is null)))
            throw new InvalidOperationException(message);
        // Older reports mixed expected metadata differences with warnings. Normalize only the
        // known informational messages; actual metadata/audit/folder write failures remain warnings.
        report.Items = report.Items.Select(item =>
        {
            static bool MetadataDifference(string value) => value.StartsWith("Metadata skipped:", StringComparison.Ordinal)
                || value.StartsWith("Metadata '", StringComparison.Ordinal);
            var metadata = item.Skipped.Where(MetadataDifference).Select(value => value.StartsWith("Metadata skipped:", StringComparison.Ordinal)
                ? value["Metadata skipped:".Length..].TrimStart() : value);
            return item with { Skipped = item.Skipped.Where(value => !MetadataDifference(value)).ToArray(), MetadataNotes = item.MetadataNotes.Concat(metadata).Distinct(StringComparer.Ordinal).ToArray() };
        }).ToList();
        if (report.Status is "Completed" or "Completed with skips" or "Completed with warnings") report.SetSuccessfulCompletionStatus();
        if (report.Phase == "Completed with skips") report.Phase = report.Status;
        return report;
    }
    private static void ValidateId(string id) { if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("Invalid copy operation ID."); }
}

public sealed record CopyRecoveryDefinition(string Environment, string TargetId, string Type, string Xml);

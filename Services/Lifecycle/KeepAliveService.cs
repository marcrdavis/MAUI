// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Local HTTP activity can prevent idle shutdown; it cannot start a stopped IIS application.

using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Services.Lifecycle;

internal sealed record KeepAliveAddress(Uri Url, IPAddress LocalAddress, int LocalPort);

internal sealed class KeepAliveTarget
{
    internal const string Path = "/_maui/keep-alive";
    internal const string Header = "X-MAUI-KeepAlive";
    private readonly string _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private KeepAliveAddress? _address;

    public KeepAliveTarget() { }

    internal KeepAliveAddress? Address => Volatile.Read(ref _address);
    internal string Token => _token;

    internal bool Accepts(string? token) => token is not null && token.Length == _token.Length
        && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(_token), Encoding.UTF8.GetBytes(token));

    // IIS supplies the actual local connection endpoint. Only Host/SNI and the application
    // PathBase come from the request; the HTTP transport below never resolves that Host.
    internal void Observe(HttpContext context)
    {
        var address = context.Connection.LocalIpAddress;
        var port = context.Connection.LocalPort;
        if (address is null || port is < 1 or > 65535 || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)
            || !context.Request.Host.HasValue || context.Request.Scheme is not ("http" or "https")) return;
        // IIS can report IPv4 connections as IPv6-mapped addresses. Use the actual
        // IPv4 address rather than depending on the socket's dual-mode defaults.
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        try
        {
            var url = new UriBuilder(context.Request.Scheme, context.Request.Host.Host, port,
                context.Request.PathBase.ToUriComponent() + Path).Uri;
            Interlocked.Exchange(ref _address, new KeepAliveAddress(url, address, port));
        }
        catch (UriFormatException)
        {
            // A malformed Host cannot become the worker's target or break normal routing.
        }
    }
}

// This endpoint intentionally runs before login/Setup/Upgrade routing. It returns no
// configuration or dependency health, touches no session, and accepts only this process's token.
internal sealed class KeepAliveMiddleware
{
    private readonly RequestDelegate _next;
    public KeepAliveMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, KeepAliveTarget target)
    {
        if (context.Request.Path.Equals(new PathString(KeepAliveTarget.Path)))
        {
            context.Response.Headers.CacheControl = "no-store";
            var tokens = context.Request.Headers[KeepAliveTarget.Header];
            context.Response.StatusCode = HttpMethods.IsGet(context.Request.Method) && tokens.Count == 1 && target.Accepts(tokens[0])
                ? StatusCodes.Status204NoContent : StatusCodes.Status404NotFound;
            return;
        }
        target.Observe(context);
        await _next(context);
    }
}

internal interface IKeepAlivePinger
{
    Task PingAsync(KeepAliveAddress address, string token, CancellationToken cancellationToken);
}

internal sealed class KeepAlivePinger : IKeepAlivePinger
{
    public KeepAlivePinger() { }

    public async Task PingAsync(KeepAliveAddress address, string token, CancellationToken cancellationToken)
    {
        using var handler = new SocketsHttpHandler
        {
            UseProxy = false, UseCookies = false, AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            // Pin the connection to MAUI's observed local endpoint. A forged Host header
            // cannot turn the worker into a remote URL fetcher. Normal TLS/SNI validation remains enabled.
            ConnectCallback = async (_, ct) =>
            {
                var socket = new Socket(address.LocalAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(address.LocalAddress, address.LocalPort), ct);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch { socket.Dispose(); throw; }
            }
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        using var request = new HttpRequestMessage(HttpMethod.Get, address.Url)
        {
            Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
        request.Headers.Add(KeepAliveTarget.Header, token);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode != HttpStatusCode.NoContent)
            throw new HttpRequestException("MAUI's local keep-alive endpoint did not confirm the request.", null, response.StatusCode);
    }
}

internal sealed class KeepAliveService : BackgroundService
{
    private readonly IOptionsMonitor<KeepAliveOptions> _options;
    private readonly KeepAliveTarget _target;
    private readonly IKeepAlivePinger _pinger;
    private readonly ILogger<KeepAliveService> _logger;
    public KeepAliveService(IOptionsMonitor<KeepAliveOptions> options, KeepAliveTarget target,
        IKeepAlivePinger pinger, ILogger<KeepAliveService> logger)
    {
        _options = options;
        _target = target;
        _pinger = pinger;
        _logger = logger;
    }

    private readonly Channel<bool> _changes = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true, SingleWriter = false
    });

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var subscription = _options.OnChange((_, _) => _changes.Writer.TryWrite(true));
        var failed = false;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var settings = _options.CurrentValue;
                if ((settings.Enabled?.Value ?? true) && _target.Address is { } address)
                {
                    try
                    {
                        await _pinger.PingAsync(address, _target.Token, stoppingToken);
                        if (failed) _logger.LogInformation("Keep Alive reached MAUI again.");
                        failed = false;
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                    catch (Exception ex) when (ex is HttpRequestException or IOException or SocketException or OperationCanceledException)
                    {
                        // One warning per failure episode; avoid recurring toast/log noise.
                        if (!failed) _logger.LogWarning(ex, "Keep Alive could not reach MAUI. Check the IIS binding and HTTPS certificate. IIS startup/preload is still required after a shutdown.");
                        failed = true;
                    }
                }

                using var wait = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                wait.CancelAfter(settings.GetInterval());
                try { await _changes.Reader.ReadAsync(wait.Token); }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) { }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}

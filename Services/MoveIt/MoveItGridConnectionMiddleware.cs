// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI

using System.Net.Http;
using System.Net.Sockets;

namespace MoveItAutomationUI.Services.MoveIt;

/// <summary>
/// Observes transport outcomes only while a MOVEit-backed grid is loading/refreshing.
/// No probing, retrying, global outage state, or changes to editor/write behavior.
/// </summary>
public sealed class MoveItGridConnectionMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Maui-MoveIt-Connection";
    internal static readonly object ObservationKey = new();

    // Explicit read-only scope: adding a future grid is an intentional choice. Local reports,
    // editors, checkout heartbeats, imports, and POST actions do not participate.
    public static bool IsGridRead(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method)) return false;
        var controller = request.RouteValues["controller"]?.ToString();
        var action = request.RouteValues["action"]?.ToString();
        return controller?.ToLowerInvariant() switch
        {
            "tasks" when action?.Equals("UpcomingRunsGrid", StringComparison.OrdinalIgnoreCase) == true => true,
            "tasks" or "hosts" => action is not null &&
                (action.Equals("InitialGrid", StringComparison.OrdinalIgnoreCase) || action.Equals("RefreshGrid", StringComparison.OrdinalIgnoreCase)),
            "resources" => action?.ToLowerInvariant() is "initialscriptsgrid" or "refreshscriptsgrid"
                or "initialdatelistsgrid" or "refreshdatelistsgrid"
                or "initialglobalparametersgrid" or "refreshglobalparametersgrid"
                or "initialkeysandcertificatesgrid" or "refreshkeysandcertificatesgrid" or "resourcegrouppermissions",
            "records" => action?.ToLowerInvariant() is "taskrunbatch" or "fileactivitybatch" or "auditbatch",
            _ => false
        };
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsGridRead(context.Request)) { await next(context); return; }
        var observation = new MoveItGridConnectionObservation();
        context.Items[ObservationKey] = observation;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = observation.Outcome;
            return Task.CompletedTask;
        });
        try { await next(context); }
        catch (Exception exception) when (!context.Response.HasStarted
            && !context.RequestAborted.IsCancellationRequested
            && observation.HasTransportFailure
            && MoveItGridConnectionObservation.Classify(exception) is not null)
        {
            // Controllers that already handle errors keep their existing contract. An otherwise
            // unhandled transport failure gets a small JSON error instead of an HTML 500 page.
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new { message = observation.Outcome == "timeout"
                ? "MOVEit did not respond before the request timed out."
                : "MAUI cannot connect to MOVEit." }, context.RequestAborted);
        }
    }
}

/// <summary>
/// The factory reuses handlers, so request observations live in HttpContext.Items, never in
/// handler fields. Concurrent grid requests/users/environments cannot share their outcomes.
/// </summary>
public sealed class MoveItGridConnectionHandler(IHttpContextAccessor accessor) : DelegatingHandler
{
    private static readonly HttpRequestOptionsKey<GridRead> ReadObservationKey = new("Maui.MoveItGridRead");
    private sealed record GridRead(MoveItGridConnectionObservation Observation, CancellationToken RequestAborted);

    // ResponseHeadersRead completes the handler before JSON/text buffering. Carry only the
    // request-local observation and abort token into the shared body reader, never credentials
    // or a retained HttpContext. A failed body read must replace the apparent header success.
    internal static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.RequestMessage is null || !response.RequestMessage.Options.TryGetValue(ReadObservationKey, out var read))
            return await response.Content.ReadAsStringAsync(cancellationToken);
        try { return await response.Content.ReadAsStringAsync(cancellationToken); }
        catch (Exception exception)
        {
            if (response.IsSuccessStatusCode && !read.RequestAborted.IsCancellationRequested)
            {
                var outcome = MoveItGridConnectionObservation.Classify(exception);
                if (outcome is not null) read.Observation.Record(response.RequestMessage.RequestUri?.AbsoluteUri ?? string.Empty, outcome);
            }
            throw;
        }
    }

    // Authentication uses HttpClient's buffered completion mode. Observe a body failure
    // occurring inside SendAsync as well; preserve its original timeout and retry semantics.
    internal static async Task<HttpResponseMessage> SendBufferedAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try { return await client.SendAsync(request, cancellationToken); }
        catch (Exception exception)
        {
            if (request.Options.TryGetValue(ReadObservationKey, out var read) && !read.RequestAborted.IsCancellationRequested)
            {
                var outcome = MoveItGridConnectionObservation.Classify(exception);
                if (outcome is not null) read.Observation.Record(request.RequestUri?.AbsoluteUri ?? string.Empty, outcome);
            }
            throw;
        }
    }

    // MOVEit Records queries use POST for read-only report retrieval. Token renewal is not
    // grid data and must not, by itself, clear a connection warning.
    private static bool IsDataRead(HttpRequestMessage request)
        => request.Method == HttpMethod.Get || request.Method == HttpMethod.Post
            && (request.RequestUri?.AbsolutePath.Contains("/api/v1/reports/", StringComparison.OrdinalIgnoreCase) == true
                || request.RequestUri?.AbsolutePath.Contains("/api/v0/reports/", StringComparison.OrdinalIgnoreCase) == true);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var context = accessor.HttpContext;
        var observation = context?.Items[MoveItGridConnectionMiddleware.ObservationKey] as MoveItGridConnectionObservation;
        if (observation is null) return await base.SendAsync(request, cancellationToken);
        request.Options.Set(ReadObservationKey, new GridRead(observation, context!.RequestAborted));
        var key = request.RequestUri?.AbsoluteUri ?? string.Empty;
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            response.RequestMessage ??= request;
            // Token renewal is not a successful grid-data read. HTTP errors establish a response,
            // but do not count as recovery. A later successful retry replaces the earlier outcome.
            if (!response.IsSuccessStatusCode) observation.Record(key, "responded");
            else if (IsDataRead(request)) observation.Record(key, "live");
            return response;
        }
        catch (Exception exception)
        {
            // HttpClient's timeout cancels its linked send token. Browser navigation/cancellation
            // cancels RequestAborted instead and must never raise an outage warning.
            var outcome = context!.RequestAborted.IsCancellationRequested ? null : MoveItGridConnectionObservation.Classify(exception);
            if (outcome is not null) observation.Record(key, outcome);
            throw;
        }
    }
}

internal sealed class MoveItGridConnectionObservation
{
    private readonly object gate = new();
    private readonly Dictionary<string, string> outcomes = new(StringComparer.Ordinal);
    internal void Record(string key, string outcome) { lock (gate) outcomes[key] = outcome; }
    internal bool HasTransportFailure => Outcome is "unreachable" or "timeout";
    internal string Outcome
    {
        get
        {
            lock (gate)
            {
                if (outcomes.ContainsValue("unreachable")) return "unreachable";
                if (outcomes.ContainsValue("timeout")) return "timeout";
                if (outcomes.ContainsValue("responded")) return "responded";
                return outcomes.ContainsValue("live") ? "live" : "none";
            }
        }
    }

    // Do not classify an ordinary HTTP 500, TLS/certificate failure, permission denial,
    // malformed response, or arbitrary application exception as network unavailability.
    internal static string? Classify(Exception exception)
    {
        if (exception is OperationCanceledException or TimeoutException) return "timeout";
        if (exception is HttpIOException { HttpRequestError: HttpRequestError.ResponseEnded }) return "unreachable";
        if (exception is HttpRequestException { StatusCode: not null }) return null;
        if (exception is HttpRequestException http)
        {
            if (http.HttpRequestError == HttpRequestError.SecureConnectionError) return null;
            if (http.InnerException is SocketException { SocketErrorCode: SocketError.TimedOut }) return "timeout";
            if (http.HttpRequestError is HttpRequestError.NameResolutionError or HttpRequestError.ConnectionError or HttpRequestError.ResponseEnded) return "unreachable";
        }
        if (exception is SocketException socket)
            return socket.SocketErrorCode switch
            {
                SocketError.TimedOut => "timeout",
                SocketError.HostNotFound or SocketError.TryAgain or SocketError.NoData
                    or SocketError.ConnectionRefused or SocketError.ConnectionReset or SocketError.ConnectionAborted
                    or SocketError.NetworkDown or SocketError.NetworkUnreachable or SocketError.NetworkReset
                    or SocketError.HostDown or SocketError.HostUnreachable => "unreachable",
                _ => null
            };
        return exception.InnerException is not null ? Classify(exception.InnerException) : null;
    }
}

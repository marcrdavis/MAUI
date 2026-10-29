using System.Globalization;
using System.Text.Json.Nodes;

namespace MoveItAutomationUI.Services.MoveIt.Hosts;

// Native connection tests/listings can legitimately outlast ordinary inventory reads.
// One budget covers headers AND response bodies; cleanup gets its own uncancelled budget.
internal static class HostOperationTimeout
{
    internal static readonly TimeSpan Minimum = TimeSpan.FromMinutes(5);
    internal static readonly TimeSpan Maximum = TimeSpan.FromMinutes(30);
    private static readonly AsyncLocal<Scope?> Current = new();
    internal static Scope? ActiveScope => Current.Value;

    internal static TimeSpan ForRequest(JsonObject request)
    {
        var native = Calculate(request);
        if (Current.Value is not { } savedHost) return native;
        if (native > savedHost.Timeout) savedHost.Timeout = native;
        return savedHost.Timeout;
    }

    // Browse envelopes omit some saved settings (notably retry counts). Carry only
    // the timing budget through the async call; never add invented fields to native JSON.
    internal static Scope ForHost(JsonObject host)
    {
        var previous = Current.Value;
        var scope = new Scope(Calculate(host), previous);
        Current.Value = scope;
        return scope;
    }

    private static TimeSpan Calculate(JsonObject request)
    {
        var login = HostDefinitionJson.GetNode(request, "logininfo") as JsonObject;
        long Seconds(JsonObject? source, string name)
            => source is not null && long.TryParse(HostResourceContracts.Text(source, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                ? Math.Clamp(number, 0, (long)Maximum.TotalSeconds) : 0;
        var connection = Math.Max(Seconds(request, "DefConnTimeoutSecs"), Seconds(login, "conntimeoutsecs"));
        var data = Math.Max(Seconds(request, "DefDataTimeoutSecs"), Seconds(login, "datatimeoutsecs"));
        var retries = Seconds(request, "DefRetryCount");
        var wait = Seconds(request, "DefRetryTimeoutSecs");
        var total = (connection + data) * (retries + 1) + wait * retries + 30;
        return TimeSpan.FromSeconds(Math.Clamp(total, (long)Minimum.TotalSeconds, (long)Maximum.TotalSeconds));
    }

    internal sealed class Scope(TimeSpan timeout, Scope? previous) : IDisposable
    {
        // Response enrichment can expose a larger limit than the sparse checkout did.
        // Keep it on the shared scope so finally/cleanup observes the actual request budget.
        internal TimeSpan Timeout { get; set; } = timeout;
        public void Dispose() => Current.Value = previous;
    }
}

using System.Text.Json;
using System.Text.Json.Nodes;

namespace MoveItAutomationUI.Services.MoveIt.Hosts;

public sealed partial class HostDefinitionSaveService
{
    // An individual Host GET can be a flat definition with no Type discriminator.
    // Keep that original projection for revision/field checks; resolve ONLY its type
    // from a unique exact-ID match in fresh inventory, never from editable fields.
    private async Task<(string TypeCode, JsonObject Host, JsonObject Wrapper)> ReadHostForSaveAsync(
        string id, CancellationToken cancellationToken)
    {
        var wrapper = await _restClient.GetHostAsync(id, cancellationToken);
        var (type, host) = await ResolveHostForSaveAsync(wrapper, id, cancellationToken);
        return (type, host, wrapper);
    }

    private async Task<(string TypeCode, JsonObject Host)> ResolveHostForSaveAsync(
        JsonObject wrapper, string id, CancellationToken cancellationToken, string? verifiedType = null)
    {
        var (type, host) = HostDefinitionJson.UnwrapHost(wrapper);
        if (HostResourceContracts.Text(host, "ID") != id)
            throw new InvalidOperationException("MOVEit returned a different or incomplete Host identity. Reload the Host before saving.");
        if (type.Length > 0) return (type, host);
        // A flat checkout may use the type just corroborated by a fresh read while
        // this same checkout is held. Explicit checkout types are never overridden.
        if (!string.IsNullOrEmpty(verifiedType)) return (verifiedType, host);
        var definitions = await _restClient.GetHostsAsync(cancellationToken);
        var matches = definitions.Select(HostDefinitionJson.UnwrapHost)
            .Where(value => HostResourceContracts.Text(value.Host, "ID") == id).ToArray();
        if (matches.Length != 1 || matches[0].TypeCode.Length == 0)
            throw new InvalidOperationException("The Host type could not be verified from MOVEit inventory. Reload the Host before saving.");
        return (matches[0].TypeCode, host);
    }

    // Controlled diagnostics retain the failed stage without logging native bodies,
    // submitted credentials, opaque definitions or arbitrary exception messages.
    private static string SaveFailureReason(Exception failure) => failure switch
    {
        HostPartialSaveException savedFailure when savedFailure.VerificationReason.Length > 0 => savedFailure.VerificationReason,
        MoveItAuthenticationException => "authentication",
        MoveItApiException api when api.StatusCode is not null => "HTTP-" + (int)api.StatusCode,
        OperationCanceledException => "canceled",
        JsonException => "invalid-json-response",
        HttpRequestException => "transport-error",
        ObjectCheckoutReleaseException => "checkout-release-unconfirmed",
        _ => "response-or-verification-failed"
    };
}

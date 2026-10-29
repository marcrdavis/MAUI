using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MoveItAutomationUI.Services.MoveIt.Hosts;

internal static partial class HostV0Creation
{
    // A successful POST supplies ownership identity. Reconcile only that ID; never
    // discover a created object by name, replay POST, fall back to COM or delete it.
    private static async Task VerifyCreatedAsync(IMoveItRestClient rest, string id, string name, string type, CancellationToken cancellationToken)
    {
        Exception? lastReadFailure = null;
        try
        {
            if (MatchesCreated(await rest.GetHostAsync(id, cancellationToken), id, name, type)) return;
            lastReadFailure = new HostCreationReadbackException("incomplete-individual-projection");
        }
        catch (MoveItAuthenticationException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (HostCreationReadbackException) { throw; } // Conflicting identity is never overridden by another read.
        catch (Exception ex) when (ex is MoveItApiException or HttpRequestException or JsonException)
        {
            lastReadFailure = ex;
            if (ex is MoveItApiException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden }) throw;
        }

        var delays = new[] { 0, 200, 500, 1000 };
        for (var attempt = 0; attempt < delays.Length; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (delays[attempt] > 0) await Task.Delay(delays[attempt], cancellationToken);
            try
            {
                // Use the same uncached REST inventory as the grid. Definition GET can
                // be unavailable or incomplete immediately after native shell creation.
                var visible = await rest.GetHostsAsync(cancellationToken);
                var matches = visible.Where(wrapper => Text(HostDefinitionJson.UnwrapHost(wrapper).Host, "ID") == id).ToArray();
                if (matches.Length > 1) throw new HostCreationReadbackException("ambiguous-inventory-id");
                if (matches.Length == 1 && MatchesCreated(matches[0], id, name, type)) return;
                lastReadFailure = new HostCreationReadbackException(matches.Length == 0 ? "created-id-not-visible" : "incomplete-inventory-projection");
            }
            catch (MoveItAuthenticationException) { throw; }
            catch (OperationCanceledException) { throw; }
            catch (HostCreationReadbackException) { throw; }
            catch (Exception ex) when (ex is MoveItApiException or HttpRequestException or JsonException)
            {
                lastReadFailure = ex;
                if (ex is MoveItApiException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden }) throw;
            }
        }
        throw lastReadFailure ?? new HostCreationReadbackException("readback-unavailable");
    }

    private static bool MatchesCreated(JsonObject wrapper, string id, string name, string type)
    {
        var (actualType, host) = HostDefinitionJson.UnwrapHost(wrapper);
        var actualId = Text(host, "ID"); var actualName = Text(host, "Name");
        if (actualId.Length > 0 && actualId != id) throw new HostCreationReadbackException("id-mismatch");
        if (actualName.Length > 0 && actualName != name) throw new HostCreationReadbackException("name-mismatch");
        var recognizedType = Profiles.ContainsKey(actualType) || actualType is "FileSystem" or "AS1" or "AS2" or "AS3";
        if (recognizedType && !actualType.Equals(type, StringComparison.OrdinalIgnoreCase))
            throw new HostCreationReadbackException("type-mismatch");
        return actualId.Length > 0 && actualName.Length > 0 && recognizedType;
    }
}

internal sealed class HostCreationReadbackException(string reason) : InvalidOperationException("Created Host verification failed: " + reason)
{
    public string Reason { get; } = reason;
}

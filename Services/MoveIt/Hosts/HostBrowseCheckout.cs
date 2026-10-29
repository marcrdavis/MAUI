using System.Text.Json.Nodes;

namespace MoveItAutomationUI.Services.MoveIt.Hosts;

// All Host browsers share ownership handling; protocol requests remain in their captured adapters.
internal static class HostBrowseCheckout
{
    internal static Task<T> ReadAsync<T>(IMoveItRestClient rest, string id,
        Func<JsonObject, CancellationToken, Task<T>> browse, CancellationToken ct)
    {
        JsonObject? checkout = null;
        HostOperationTimeout.Scope? operationBudget = null;
        return ObjectCheckout.ReadAsync("Host", id,
            async token => { checkout = await rest.CheckoutHostV0Async(id, token); },
            async token =>
            {
                // A lifetime lease already owns checkout. Read saved settings without another POST/DELETE.
                var wrapper = checkout ?? await rest.GetHostAsync(id, token);
                var (type, host) = HostDefinitionJson.UnwrapHost(wrapper);
                using var timing = HostOperationTimeout.ForHost(host);
                operationBudget = timing;
                var returnedId = HostDefinitionJson.ReadNode(HostDefinitionJson.GetNode(host, "ID"));
                if (returnedId.Length > 0 && returnedId != id)
                    throw new InvalidOperationException("MOVEit returned a different Host while browsing. Close and reload the Host.");
                if (type.Length == 0 && returnedId == id)
                {
                    var inventory = await rest.GetHostsAsync(token);
                    var matches = inventory.Select(HostDefinitionJson.UnwrapHost)
                        .Where(item => HostDefinitionJson.ReadNode(HostDefinitionJson.GetNode(item.Host, "ID")) == id).ToArray();
                    if (matches.Length != 1 || matches[0].TypeCode.Length == 0)
                        throw new InvalidOperationException("The Host type could not be verified for browsing. Reload the Host.");
                    wrapper = new JsonObject { [matches[0].TypeCode] = host.DeepClone() };
                }
                return await browse(wrapper, token);
            }, token => rest.ReleaseHostCheckoutV0Async(id, token), ct, () => operationBudget?.Timeout ?? HostOperationTimeout.Minimum);
    }
}

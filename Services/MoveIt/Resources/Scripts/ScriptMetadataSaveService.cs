using Microsoft.Extensions.Options;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;

namespace MoveItAutomationUI.Services.MoveIt.Resources.Scripts;

// Built-in Script metadata belongs to MAUI. Both resource Information and the
// editor use this path, with no Custom Scripts license, native write or checkout.
public sealed class ScriptMetadataSaveService(IMoveItSessionService sessions, IAutomationScriptService scripts,
    IMetadataRepository metadata, IMetadataValueValidationService validation, IOptionsSnapshot<MetadataUiOptions> options)
{
    public async Task<ObjectMetadataSnapshot> SaveBuiltInAsync(string id, IReadOnlyDictionary<string, string>? input, CancellationToken ct = default)
    {
        var session = sessions.Get() ?? throw new MoveItAuthenticationException("MOVEit session expired. Please sign in again.");
        id = ScriptConfiguration.Id(id);
        var script = await scripts.GetScriptAsync(id, ct);
        if (!script.IsBuiltIn) throw new InvalidOperationException("Use the Script editor to save custom Script properties.");
        var values = await validation.FilterAndValidateAsync(session.GetEnvironmentKey(), options.Value.Scripts, input, ct);
        await metadata.SaveObjectMetadataAsync(session.GetEnvironmentKey(), "Script", id, values, session.Username, ct);
        return await metadata.GetObjectMetadataAsync(session.GetEnvironmentKey(), "Script", id, ct);
    }
}

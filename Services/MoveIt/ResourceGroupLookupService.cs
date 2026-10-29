// Shared, current-user MOVEit Resource Group inventory and validation.
// No cross-user cache: MOVEit filters this API by the authenticated principal.
using System.Text.Json.Nodes;

namespace MoveItAutomationUI.Services.MoveIt;

public interface IResourceGroupLookupService
{
    Task<IReadOnlyList<JsonObject>> GetVisibleDefinitionsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetVisibleNamesAsync(CancellationToken cancellationToken = default);
    Task<bool> IsVisibleAsync(string groupName, CancellationToken cancellationToken = default);
    bool IsVisible(IReadOnlyList<JsonObject> groups, string groupName);
    string ReadName(JsonObject? group);
}

public sealed class ResourceGroupLookupService : IResourceGroupLookupService
{
    public static string FormatAssignments(JsonNode? node)
        => node is JsonArray groups ? string.Join(", ", groups.Select(value => value?.ToString().Trim() ?? "").Where(value => value.Length > 0))
            : node?.ToString().Trim() ?? "";

    private readonly IMoveItRestClient _rest;
    public ResourceGroupLookupService(IMoveItRestClient rest) => _rest = rest;

    public Task<IReadOnlyList<JsonObject>> GetVisibleDefinitionsAsync(CancellationToken cancellationToken = default)
        => _rest.GetResourceGroupsAsync(cancellationToken);

    public async Task<IReadOnlyList<string>> GetVisibleNamesAsync(CancellationToken cancellationToken = default)
        => (await GetVisibleDefinitionsAsync(cancellationToken))
            .Select(ReadName)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public async Task<bool> IsVisibleAsync(string groupName, CancellationToken cancellationToken = default)
        => IsVisible(await GetVisibleDefinitionsAsync(cancellationToken), groupName);

    public bool IsVisible(IReadOnlyList<JsonObject> groups, string groupName)
    {
        if (groups is null || string.IsNullOrWhiteSpace(groupName)) return false;
        return groups.Any(group => ReadName(group).Equals(groupName.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public string ReadName(JsonObject? group)
    {
        if (group is null) return string.Empty;
        foreach (var key in new[] { "Name", "GroupName" })
        {
            var value = group.FirstOrDefault(pair => pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value?.ToString()?.Trim();
            if (!string.IsNullOrEmpty(value)) return value;
        }
        return string.Empty;
    }
}

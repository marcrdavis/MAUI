using System.Reflection;
using System.Text.Json.Nodes;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class ResourceGroupLookupTests
{
    [Fact]
    public async Task VisibleNames_NormalizesAliasesAndDeduplicates()
    {
        var client = DispatchProxy.Create<IMoveItRestClient, GroupRestProxy>();
        var groups = new JsonObject[]
        {
            new() { ["Name"] = "Finance" },
            new() { ["GroupName"] = " finance " },
            new() { ["name"] = "Operations" },
            new() { ["Name"] = "" }
        };
        ((GroupRestProxy)(object)client).Groups = groups;
        var service = new ResourceGroupLookupService(client);
        var names = await service.GetVisibleNamesAsync();
        Assert.Equal(new[] { "Finance", "Operations" }, names);
        Assert.True(service.IsVisible(groups, " finance "));
        Assert.False(service.IsVisible(groups, "NOT_VISIBLE"));
    }

    public class GroupRestProxy : DispatchProxy
    {
        public IReadOnlyList<JsonObject> Groups { get; set; } = Array.Empty<JsonObject>();
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IMoveItRestClient.GetResourceGroupsAsync))
                return Task.FromResult(Groups);
            throw new NotSupportedException(targetMethod?.Name);
        }
    }
}

using System.Text.Json.Nodes;
using MoveItAutomationUI.Services.Configuration;

namespace MoveItAutomationUI.Tests.Configuration;

public sealed class AppSettingsJsonTests
{
    [Fact]
    public void RequiredSection_ReturnsExistingObject()
    {
        var root = JsonNode.Parse("""{ "Ui": { "Enabled": true } }""")!.AsObject();
        var section = AppSettingsJson.RequiredSection(root, "Ui");
        Assert.True(section["Enabled"]!.GetValue<bool>());
    }

    [Fact]
    public void RequiredSection_RejectsMissingSection()
    {
        var root = new JsonObject();
        Assert.Throws<InvalidOperationException>(() => AppSettingsJson.RequiredSection(root, "Ui"));
    }

    [Fact]
    public void GetOrCreateSection_ClonesTemplateInsteadOfAliasingIt()
    {
        var root = new JsonObject();
        var template = JsonNode.Parse("""{ "Backup": { "Marker": "template" } }""")!.AsObject();

        var created = AppSettingsJson.GetOrCreateSection(root, "Backup", template);
        created["Marker"] = "installation";

        Assert.Equal("installation", root["Backup"]!["Marker"]!.GetValue<string>());
        Assert.Equal("template", template["Backup"]!["Marker"]!.GetValue<string>());
    }

    [Fact]
    public void GetOrCreateSection_DoesNotOverwriteMalformedExistingNode()
    {
        var root = JsonNode.Parse("""{ "Backup": "invalid" }""")!.AsObject();
        Assert.Throws<InvalidOperationException>(() => AppSettingsJson.GetOrCreateSection(root, "Backup"));
        Assert.Equal("invalid", root["Backup"]!.GetValue<string>());
    }

    [Fact]
    public void SetSettingValue_WithTemplate_PreservesInstallationValueAndBackfillsMissingMetadata()
    {
        var root = JsonNode.Parse("""{ "Backup": { "RetentionDays": { "Value": 7, "Description": "custom description" } } }""")!.AsObject();
        var template = JsonNode.Parse("""{ "Backup": { "RetentionDays": { "Value": 30, "Name": "Backup Retention", "Description": "template description", "Type": "List" } } }""")!.AsObject();

        AppSettingsJson.SetSettingValue(root, "Backup", "RetentionDays", 14, template);

        var setting = root["Backup"]!["RetentionDays"]!.AsObject();
        Assert.Equal(14, setting["Value"]!.GetValue<int>());
        Assert.Equal("custom description", setting["Description"]!.GetValue<string>());
        Assert.Equal("Backup Retention", setting["Name"]!.GetValue<string>());
        Assert.Equal("List", setting["Type"]!.GetValue<string>());
    }

    [Fact]
    public void SetNestedSettingValue_CreatesMissingPathFromTemplate()
    {
        var root = new JsonObject();
        var template = JsonNode.Parse("""{ "Ui": { "Tasks": { "Refresh": { "Value": 10, "Name": "Refresh", "Type": "Integer" } } } }""")!.AsObject();

        AppSettingsJson.SetNestedSettingValue(root, "Ui", "Tasks", "Refresh", 20, template);

        var setting = root["Ui"]!["Tasks"]!["Refresh"]!.AsObject();
        Assert.Equal(20, setting["Value"]!.GetValue<int>());
        Assert.Equal("Refresh", setting["Name"]!.GetValue<string>());
        Assert.Equal("Integer", setting["Type"]!.GetValue<string>());
    }

    [Fact]
    public void MergeMissingProperties_DoesNotOverwriteExistingValues()
    {
        var target = JsonNode.Parse("""{ "Value": 5, "Description": "custom" }""")!.AsObject();
        var template = JsonNode.Parse("""{ "Value": 30, "Description": "template", "Type": "Integer" }""")!.AsObject();

        AppSettingsJson.MergeMissingProperties(target, template);

        Assert.Equal(5, target["Value"]!.GetValue<int>());
        Assert.Equal("custom", target["Description"]!.GetValue<string>());
        Assert.Equal("Integer", target["Type"]!.GetValue<string>());
    }
}

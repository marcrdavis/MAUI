// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Shared JSON helpers for the structured MAUI appsettings document.

using System.Text.Json;
using System.Text.Json.Nodes;

namespace MoveItAutomationUI.Services.Configuration;

public static class AppSettingsJson
{
    // Purpose: Returns a required top-level configuration section without changing the document.
    public static JsonObject RequiredSection(JsonObject root, string sectionName) =>
        root[sectionName]?.AsObject()
        ?? throw new InvalidOperationException($"Configuration section '{sectionName}' is missing.");

    // Purpose: Returns an existing JSON-object section or safely creates it when it is absent.
    // When a release template is supplied, the new section is cloned from that template so
    // setting metadata and non-editable companion properties are retained. Existing malformed
    // nodes are never silently replaced.
    public static JsonObject GetOrCreateSection(JsonObject root, string sectionName, JsonObject? templateRoot = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (string.IsNullOrWhiteSpace(sectionName)) throw new ArgumentException("Configuration section name is required.", nameof(sectionName));

        if (!root.TryGetPropertyValue(sectionName, out var existing) || existing is null)
        {
            var created = CloneTemplateObject(templateRoot?[sectionName]) ?? new JsonObject();
            root[sectionName] = created;
            return created;
        }

        return existing as JsonObject
            ?? throw new InvalidOperationException($"Configuration section '{sectionName}' exists but is not a JSON object.");
    }

    // Purpose: Returns an existing child object or safely creates it when absent, optionally
    // cloning the corresponding release-template object. A conflicting non-object node is
    // treated as malformed configuration and is not overwritten.
    public static JsonObject GetOrCreateObject(
        JsonObject parent,
        string propertyName,
        string configurationPath,
        JsonObject? templateParent = null)
    {
        ArgumentNullException.ThrowIfNull(parent);
        if (string.IsNullOrWhiteSpace(propertyName)) throw new ArgumentException("Configuration property name is required.", nameof(propertyName));

        if (!parent.TryGetPropertyValue(propertyName, out var existing) || existing is null)
        {
            var created = CloneTemplateObject(templateParent?[propertyName]) ?? new JsonObject();
            parent[propertyName] = created;
            return created;
        }

        return existing as JsonObject
            ?? throw new InvalidOperationException($"Configuration property '{configurationPath}' exists but is not a JSON object.");
    }

    // Purpose: Updates a SettingDefinition Value, safely creating a missing section/setting.
    // Existing setting metadata is preserved. Newly introduced settings are cloned from the
    // shipped template when available so older installation-owned appsettings.json files gain
    // the complete current SettingDefinition rather than only a bare Value property.
    public static void SetSettingValue<T>(
        JsonObject root,
        string sectionName,
        string settingName,
        T value,
        JsonObject? templateRoot = null)
    {
        // Preserve the original strict behavior for callers such as Initial Setup that are
        // expected to operate on a complete seed document. Configuration > Settings supplies
        // the release template explicitly to opt into safe create-if-missing behavior.
        if (templateRoot is null)
        {
            var requiredSection = RequiredSection(root, sectionName);
            var requiredSetting = requiredSection[settingName] as JsonObject
                ?? throw new InvalidOperationException($"Configuration setting '{sectionName}:{settingName}' is missing or is not a SettingDefinition JSON object.");
            requiredSetting["Value"] = JsonSerializer.SerializeToNode(value);
            return;
        }

        var section = GetOrCreateSection(root, sectionName, templateRoot);
        var templateSection = templateRoot[sectionName] as JsonObject;

        JsonObject setting;
        if (!section.TryGetPropertyValue(settingName, out var existing) || existing is null)
        {
            setting = CloneTemplateObject(templateSection?[settingName]) ?? new JsonObject();
            section[settingName] = setting;
        }
        else
        {
            setting = existing as JsonObject
                ?? throw new InvalidOperationException($"Configuration setting '{sectionName}:{settingName}' exists but is not a SettingDefinition JSON object.");
        }

        MergeMissingProperties(setting, templateSection?[settingName] as JsonObject);
        setting["Value"] = JsonSerializer.SerializeToNode(value);
    }

    // Purpose: Updates a SettingDefinition value in a nested section while using the same
    // create-if-missing and malformed-node guardrails as top-level editable settings.
    public static void SetNestedSettingValue<T>(
        JsonObject root,
        string sectionName,
        string childSectionName,
        string settingName,
        T value,
        JsonObject? templateRoot = null)
    {
        var section = GetOrCreateSection(root, sectionName, templateRoot);
        var templateSection = templateRoot?[sectionName] as JsonObject;
        var childSection = GetOrCreateObject(
            section,
            childSectionName,
            $"{sectionName}:{childSectionName}",
            templateSection);
        var templateChild = templateSection?[childSectionName] as JsonObject;

        JsonObject setting;
        if (!childSection.TryGetPropertyValue(settingName, out var existing) || existing is null)
        {
            setting = CloneTemplateObject(templateChild?[settingName]) ?? new JsonObject();
            childSection[settingName] = setting;
        }
        else
        {
            setting = existing as JsonObject
                ?? throw new InvalidOperationException($"Configuration setting '{sectionName}:{childSectionName}:{settingName}' exists but is not a SettingDefinition JSON object.");
        }

        MergeMissingProperties(setting, templateChild?[settingName] as JsonObject);
        setting["Value"] = JsonSerializer.SerializeToNode(value);
    }

    // Purpose: Adds properties that are absent from an existing object without overwriting any
    // installation-owned values. This is intentionally shallow: callers choose the exact object
    // boundary whose release-defined companion properties are safe to backfill.
    public static void MergeMissingProperties(JsonObject target, JsonObject? templateObject)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (templateObject is null) return;

        foreach (var property in templateObject)
        {
            if (target.TryGetPropertyValue(property.Key, out var existing) && existing is not null) continue;
            target[property.Key] = property.Value?.DeepClone();
        }
    }

    private static JsonObject? CloneTemplateObject(JsonNode? node)
        => node is JsonObject templateObject ? templateObject.DeepClone().AsObject() : null;
}

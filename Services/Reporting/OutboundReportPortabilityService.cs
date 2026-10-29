// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Portable import/export for saved MAUI Report definitions. Report execution history is intentionally not portable.
// Uses MAUI's shared .maui package container so format/version/size validation stays consistent with other import/export features.

using System.Text.Json;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.Security;

namespace MoveItAutomationUI.Services.Reporting;

public interface IOutboundReportPortabilityService
{
    Task<(byte[] Content, string FileName)> ExportAsync(IReadOnlyList<string> reportIds, string username, bool isAdmin, CancellationToken cancellationToken = default);
    Task<OutboundReportImportPreflightResult> PreflightImportAsync(IFormFile file, string ownerUsername, CancellationToken cancellationToken = default);
    Task<OutboundReportImportResult> ImportAsync(IFormFile file, string ownerUsername, CancellationToken cancellationToken = default);
}

public sealed class OutboundReportPortabilityService : IOutboundReportPortabilityService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly MetadataDbContext _db;
    private readonly IReportScheduleCalculator _scheduleCalculator;
    private readonly IMauiSecurityService _security;

    public OutboundReportPortabilityService(
        MetadataDbContext db,
        IReportScheduleCalculator scheduleCalculator,
        IMauiSecurityService security)
    {
        _db = db;
        _scheduleCalculator = scheduleCalculator;
        _security = security;
    }

    public async Task<(byte[] Content, string FileName)> ExportAsync(IReadOnlyList<string> reportIds, string username, bool isAdmin, CancellationToken cancellationToken = default)
    {
        var ids = NormalizeIds(reportIds);
        if (ids.Count == 0) throw new ArgumentException("Select at least one Report to export.");
        if (ids.Count > MauiPortablePackage.MaxItems) throw new ArgumentException($"A MAUI Report export can contain at most {MauiPortablePackage.MaxItems} Reports.");

        var usernameKey = NormalizeOwnerKey(username);
        var query = _db.OutboundReports.AsNoTracking().Where(item => ids.Contains(item.Id));
        if (!isAdmin) query = query.Where(item => item.OwnerUsernameKey == usernameKey);
        var rows = await query
            .ToListAsync(cancellationToken);
        if (rows.Count != ids.Count)
            throw new ArgumentException("One or more selected Reports no longer exist or you do not have access to them. Refresh the grid and try again.");

        var byId = rows.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        var items = ids.Select(id => BuildItem(byId[id])).ToArray();
        var content = MauiPortablePackage.Build("Report", items, DateTimeOffset.UtcNow);
        return (content, MauiPortablePackage.BuildBulkFileName("Report", rows.Count));
    }

    public async Task<OutboundReportImportPreflightResult> PreflightImportAsync(IFormFile file, string ownerUsername, CancellationToken cancellationToken = default)
    {
        var definitions = await ParseAsync(file, cancellationToken);
        var existingKeys = await ExistingKeysAsync(ownerUsername, cancellationToken);

        var results = new List<OutboundReportImportPreflightItem>(definitions.Count);
        foreach (var definition in definitions)
        {
            var validationMessage = ValidateDefinition(definition, existingKeys, _security.IsAdmin(ownerUsername));
            results.Add(new OutboundReportImportPreflightItem(definition.Title, definition.EnvironmentKey, string.IsNullOrEmpty(validationMessage), validationMessage));
        }

        var errorCount = results.Count(item => !item.Valid);
        var summaryMessage = errorCount == 0
            ? $"Package is valid. {results.Count} Report{(results.Count == 1 ? string.Empty : "s")} will be imported disabled for review."
            : $"Package was read successfully, but {errorCount} Report{(errorCount == 1 ? string.Empty : "s")} has import validation issues. Review the item details.";
        return new OutboundReportImportPreflightResult(results.Count, errorCount, results, summaryMessage);
    }

    public async Task<OutboundReportImportResult> ImportAsync(IFormFile file, string ownerUsername, CancellationToken cancellationToken = default)
    {
        var definitions = await ParseAsync(file, cancellationToken);
        var existingKeys = await ExistingKeysAsync(ownerUsername, cancellationToken);
        var validationErrors = definitions
            .Select(definition => new { definition, message = ValidateDefinition(definition, existingKeys, _security.IsAdmin(ownerUsername)) })
            .Where(item => !string.IsNullOrEmpty(item.message))
            .ToArray();
        if (validationErrors.Length > 0) throw new InvalidOperationException(validationErrors[0].message);

        var owner = (ownerUsername ?? string.Empty).Trim();
        if (owner.Length == 0) throw new InvalidOperationException("The current MAUI user could not be determined.");
        var ownerKey = NormalizeOwnerKey(owner);
        var now = DateTimeOffset.UtcNow;
        var importedTitles = new List<string>(definitions.Count);

        foreach (var definition in definitions)
        {
            // Portable Report imports never start delivery automatically. The importing user owns the
            // new definition and reviews recipients, schedule, environment, and unattended-data readiness
            // before enabling it. Admins can subsequently manage any imported definition.
            _db.OutboundReports.Add(new OutboundReportEntity
            {
                Id = Guid.NewGuid().ToString("D"),
                EnvironmentKey = MoveItEnvironmentKey.Normalize(definition.EnvironmentKey),
                OwnerUsername = owner,
                OwnerUsernameKey = ownerKey,
                GridKey = definition.GridKey,
                Title = definition.Title,
                Description = definition.Description,
                Recipients = definition.Recipients,
                ViewStateJson = definition.ViewStateJson,
                SourceContextJson = OutboundReportSourceRoutes.NormalizeJson(definition.SourceContextJson, definition.GridKey),
                ScheduleJson = definition.ScheduleJson,
                Enabled = false,
                NextRunUtc = null,
                LastRunUtc = null,
                LastStatus = "Imported",
                LastMessage = "Imported disabled. Review the Report definition and enable it when ready.",
                CreatedUtc = now,
                ModifiedUtc = now
            });
            importedTitles.Add(definition.Title);
            existingKeys.Add(DuplicateKey(definition.EnvironmentKey, definition.Title));
        }

        await _db.SaveChangesAsync(cancellationToken);
        return new OutboundReportImportResult(definitions.Count, definitions.Count, importedTitles);
    }

    private async Task<HashSet<string>> ExistingKeysAsync(string ownerUsername, CancellationToken cancellationToken)
    {
        var ownerKey = NormalizeOwnerKey(ownerUsername);
        var existing = await _db.OutboundReports.AsNoTracking()
            .Where(item => item.OwnerUsernameKey == ownerKey)
            .Select(item => new { item.EnvironmentKey, item.Title })
            .ToListAsync(cancellationToken);
        return existing.Select(item => DuplicateKey(item.EnvironmentKey, item.Title)).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private string ValidateDefinition(PortableReportDefinition definition, IReadOnlySet<string> existingKeys, bool ownerIsAdmin)
    {
        if (definition.Title.Length == 0 || definition.Title.Length > 200) return "Report Title is required and cannot exceed 200 characters.";
        if (definition.Description.Length > 1000) return $"Report '{definition.Title}' has a Description longer than 1000 characters.";
        if (definition.EnvironmentKey.Length == 0 || definition.EnvironmentKey.Length > MoveItEnvironmentKey.MaxLength) return $"Report '{definition.Title}' has an invalid Environment Key.";
        if (!OutboundReportGridRegistry.TryGetLabel(definition.GridKey, out _)) return $"Report '{definition.Title}' references unsupported grid '{definition.GridKey}'.";
        if (!OutboundReportSourcePolicy.IsAllowed(definition.GridKey, ownerIsAdmin)) return $"Report '{definition.Title}' uses a source that requires MAUI Administrator access.";
        if (existingKeys.Contains(DuplicateKey(definition.EnvironmentKey, definition.Title))) return $"A Report named '{definition.Title}' already exists in environment '{definition.EnvironmentKey}'.";

        try
        {
            _ = OutboundReportRepository.NormalizeRecipients(definition.Recipients);
            var viewState = JsonSerializer.Deserialize<ReportGridViewState>(definition.ViewStateJson, JsonOptions);
            if (viewState is null || viewState.SchemaVersion != 1 || viewState.Columns.Count == 0) return $"Report '{definition.Title}' contains an invalid saved grid view.";
            _ = OutboundReportSourceRoutes.NormalizeJson(definition.SourceContextJson, definition.GridKey);
            var schedule = JsonSerializer.Deserialize<ReportScheduleDefinition>(definition.ScheduleJson, JsonOptions) ?? throw new JsonException();
            _ = _scheduleCalculator.ValidateAndNormalize(schedule, schedule.TimeZoneId);
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException or TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return $"Report '{definition.Title}' is invalid: {ex.Message}";
        }
        return string.Empty;
    }

    private static XElement BuildItem(OutboundReportEntity report)
    {
        var definition = new XElement("ReportDefinition",
            new XElement("EnvironmentKey", report.EnvironmentKey),
            new XElement("OwnerUsername", report.OwnerUsername),
            new XElement("GridKey", report.GridKey),
            new XElement("Title", report.Title),
            new XElement("Description", report.Description),
            new XElement("Recipients", report.Recipients),
            new XElement("ViewStateJson", report.ViewStateJson ?? string.Empty),
            new XElement("SourceContextJson", OutboundReportSourceRoutes.NormalizeJson(report.SourceContextJson, report.GridKey)),
            new XElement("ScheduleJson", report.ScheduleJson ?? string.Empty),
            new XElement("Enabled", report.Enabled));

        // Package item names must be unique even when two MOVEit environments contain Reports
        // with the same title. The ReportDefinition retains the actual user-facing title.
        var packageName = $"{report.EnvironmentKey} :: {report.Title} :: {report.Id}";
        return MauiPortablePackage.BuildItem(
            report.Id,
            packageName,
            "Report",
            definition,
            new XElement("MauiMetadata", new XAttribute("ObjectType", "Report")));
    }

    private static async Task<IReadOnlyList<PortableReportDefinition>> ParseAsync(IFormFile file, CancellationToken cancellationToken)
    {
        var bytes = await ReadPackageBytesAsync(file, cancellationToken);
        var package = MauiPortablePackage.Parse(bytes, "Report", "Report");
        var definitions = new List<PortableReportDefinition>(package.Items.Count);
        var packageKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in package.Items)
        {
            if (!string.Equals(item.Type, "Report", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Package item '{item.Name}' is not a Report definition.");
            var definitionElement = item.Configuration;
            if (!definitionElement.Name.LocalName.Equals("ReportDefinition", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Package item '{item.Name}' does not contain a ReportDefinition configuration.");

            var definition = new PortableReportDefinition(
                Required(definitionElement, "EnvironmentKey"),
                Required(definitionElement, "GridKey").ToLowerInvariant(),
                Required(definitionElement, "Title"),
                Value(definitionElement, "Description"),
                Required(definitionElement, "Recipients"),
                Required(definitionElement, "ViewStateJson"),
                Required(definitionElement, "SourceContextJson"),
                Required(definitionElement, "ScheduleJson"));

            if (!packageKeys.Add(DuplicateKey(definition.EnvironmentKey, definition.Title)))
                throw new InvalidOperationException($"The MAUI Report export contains more than one Report named '{definition.Title}' for environment '{definition.EnvironmentKey}'.");
            definitions.Add(definition);
        }
        return definitions;
    }

    private static async Task<byte[]> ReadPackageBytesAsync(IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length <= 0) throw new InvalidOperationException("Select a non-empty MAUI Report export file.");
        if (file.Length > MauiPortablePackage.MaxPackageBytes) throw new InvalidOperationException("The MAUI Report export exceeds the 50 MB portability limit.");
        await using var source = file.OpenReadStream();
        using var memory = new MemoryStream();
        await source.CopyToAsync(memory, cancellationToken);
        if (memory.Length > MauiPortablePackage.MaxPackageBytes) throw new InvalidOperationException("The MAUI Report export exceeds the 50 MB portability limit.");
        return memory.ToArray();
    }

    private static IReadOnlyList<string> NormalizeIds(IReadOnlyList<string> reportIds)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in reportIds ?? Array.Empty<string>())
        {
            if (!Guid.TryParse((raw ?? string.Empty).Trim(), out var id)) throw new ArgumentException("One or more Report identifiers are invalid.");
            var normalized = id.ToString("D");
            if (seen.Add(normalized)) result.Add(normalized);
        }
        return result;
    }

    private static string Required(XElement parent, string name)
    {
        var value = Value(parent, name).Trim();
        if (value.Length == 0) throw new InvalidOperationException($"Report export field '{name}' is required.");
        return value;
    }

    private static string Value(XElement parent, string name)
        => parent.Elements().FirstOrDefault(element => element.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value ?? string.Empty;

    private static string DuplicateKey(string environmentKey, string title)
        => $"{MoveItEnvironmentKey.Normalize(environmentKey)}\u001f{(title ?? string.Empty).Trim()}";

    private static string NormalizeOwnerKey(string username)
    {
        var value = (username ?? string.Empty).Trim();
        if (value.Length == 0) throw new InvalidOperationException("The current MAUI user could not be determined.");
        return value.ToUpperInvariant();
    }

    private sealed record PortableReportDefinition(
        string EnvironmentKey,
        string GridKey,
        string Title,
        string Description,
        string Recipients,
        string ViewStateJson,
        string SourceContextJson,
        string ScheduleJson);
}

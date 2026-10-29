using Microsoft.Extensions.Options;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Configuration;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Features.EnvironmentCopy;

public sealed partial class EnvironmentCopyCoordinator(IServiceScopeFactory scopes, IDataAccessCredentialService credentials,
    IMoveItServerCatalog servers, EnvironmentCopyStore store, IOptionsMonitor<EnvironmentCopyOptions> options)
{
    public bool Enabled => options.CurrentValue.Enabled.Value && EnvironmentCopyAvailability.HasMultipleEnvironments(servers.GetServers());
    private void RequireEnabled() { if (!Enabled) throw new InvalidOperationException("Copy Between Environments is disabled in Experimental settings."); }
    public IReadOnlyList<CopyEnvironment> Environments()
    {
        RequireEnabled();
        return servers.GetServers().GroupBy(MoveItEnvironmentKey.Resolve, StringComparer.OrdinalIgnoreCase).Select(group =>
        {
            var accounts = group.Where(x => credentials.IsConfigured(x.Key, out _)).Select(x => x.DataAccessUsername!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase);
            return new CopyEnvironment(group.Key, group.Key, credentials.IsEnvironmentConfigured(group.Key), string.Join(", ", accounts));
        }).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    private Task<CopyEnvironmentConnection> ConnectAsync(string environment, CancellationToken ct)
    {
        RequireEnabled(); var key = MoveItEnvironmentKey.Normalize(environment);
        if (!servers.GetServers().Any(x => MoveItEnvironmentKey.Resolve(x) == key)) throw new ArgumentException("Choose a configured MOVEit environment.");
        return CopyEnvironmentConnection.OpenAsync(scopes, credentials, key, ct);
    }
    public async Task<object> ConnectionAsync(string environment, CancellationToken ct)
    {
        // The destination selector needs authentication status, not five full object inventories.
        await using var connection = await ConnectAsync(environment, ct);
        return new { account = connection.State.Username, environment = connection.State.GetEnvironmentKey() };
    }
    public async Task<object> InventoryAsync(string environment, CancellationToken ct)
    {
        await using var connection = await ConnectAsync(environment, ct);
        var objects = await connection.UseAsync(services => EnvironmentCopyObjects.InventoryAsync(services, ct));
        return new { objects, account = connection.State.Username, environment = connection.State.GetEnvironmentKey() };
    }
    internal static object View(CopyPlan plan) => new
    {
        planId = plan.Id, source = plan.Source, destination = plan.Destination, plan.SourceAccount, plan.DestinationAccount,
        groups = plan.Groups, folders = plan.Folders, groupsToCreate = plan.Review is null ? [] : MissingGroups(plan, plan.Review),
        items = plan.Items.Select(item => new
        {
            key = item.Source.Key, type = item.Source.Type, sourceId = item.Source.Id, sourceName = item.Source.Name, subtype = item.Source.Subtype,
            selected = item.Selected, reason = item.Selected ? "Selected" : "Required dependency", sourceGroups = item.Source.AssignedGroups, item.Choice,
            targets = plan.DestinationObjects.Where(target => EnvironmentCopyObjects.Compatible(item.Source, target)).ToArray(),
            matches = plan.DestinationObjects.Where(target => target.Type == item.Source.Type && (target.Id == item.Source.Id || target.Name.Equals(item.Source.Name, StringComparison.OrdinalIgnoreCase)
                || EnvironmentCopyObjects.SameKey(item.Source, target))).ToArray(),
            issues = item.Validation.ToArray(), skipped = item.Skipped.ToArray(), metadataNotes = item.MetadataNotes.ToArray(), credentialStatus = item.CredentialStatus,
            canCreate = !item.Source.ReuseOnly && !(item.Source.Type == "KeyCertificate" && item.Source.Subtype == "Ssh" && item.PortableXml.Length == 0),
            canOverwrite = !item.Source.ReuseOnly && item.Source.Type != "KeyCertificate",
            canRename = item.Source.Type != "KeyCertificate" && !item.Source.ReuseOnly
        }).ToArray(),
        unresolved = plan.Items.Count(item => item.Validation.Count > 0), reviewToken = plan.ReviewToken, placement = Placements(plan),
        notes = new[] { "New and overwritten Tasks remain disabled. Reused objects are unchanged.", "Only recognized structured references are remapped. Object IDs embedded in Script source or free-form macro text require manual review." }
    };
    internal static IReadOnlyList<CopyPlacement> Placements(CopyPlan plan, CopyReviewRequest? placement = null)
    {
        var review = placement ?? plan.Review;
        if (review is null) return [];
        return plan.Items.Where(item => item.Choice.Action != "reuse").Select(item =>
        {
            var before = item.Choice.Action == "overwrite" ? item.DestinationGroupsBefore : [];
            var after = DestinationGroups(plan, item, review) ?? [];
            var changed = item.Choice.Action == "overwrite" && !before.Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(after.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
            var folder = item.Source.Type != "Task" ? "" : item.Choice.Action == "overwrite" ? "Existing destination folder retained"
                : review.FolderMode == "root" ? "Root" : review.FolderMode == "new" ? review.FolderPath
                : plan.Folders.FirstOrDefault(x => x.Id == review.FolderId)?.Name ?? "Destination folder";
            return new CopyPlacement(item.Choice.Name, item.Source.Type, item.Choice.Action, before, after, folder, changed)
            {
                SourceGroups = item.Source.AssignedGroups,
                DestinationGroupsPreserved = item.Choice.Action == "overwrite" && review.PreserveDestinationGroups
            };
        }).ToArray();
    }
}

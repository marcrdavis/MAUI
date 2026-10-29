using System.Text.Json.Nodes;
using System.Xml.Linq;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;

namespace MoveItAutomationUI.Features.EnvironmentCopy;

// This feature owns planning and orchestration only. Object services retain their CRUD transports.
public sealed class EnvironmentCopyOptions
{
    public const string SectionName = "EnvironmentCopy";
    public SettingDefinition<bool> Enabled { get; set; } = new()
    {
        Name = "Copy Between Environments", Value = false, Type = "Boolean", Experimental = true,
        Description = "Copy selected objects and Task dependencies between configured MOVEit environments. Requires both MOVEit and MAUI administrator access and Data Access credentials for both environments.",
        PossibleValues = [false, true]
    };
    public SettingDefinition<int> RecoveryRetentionDays { get; set; } = new()
    {
        Name = "Copy recovery retention", Value = 30, Type = "Number",
        Description = "Days to retain operation reports and protected overwrite recovery definitions (1–90)."
    };
}

public sealed record CopySelection(string Type, string Id);
public sealed record CopyEnvironment(string Key, string Name, bool CredentialsConfigured, string Account);
public sealed record CopyObject(string Type, string Id, string Name, string Subtype, string Groups = "", bool BuiltIn = false, string Fingerprint = "", bool Running = false, string PublicPrivate = "")
{
    // Exact assignments from structured object definitions or Resource Group membership data.
    // Never split display text or infer membership from export XML.
    public IReadOnlyList<string> AssignedGroups { get; init; } = [];
    public string Key => Type + "|" + Id;
    public bool ReuseOnly => BuiltIn || Type == "Host" && Id == "0";
}
public sealed record CopyChoice(string Key, string Action, string Name, string TargetId = "");
// HA nodes share one environment. Use the same identity rules as copy connections.
public static class EnvironmentCopyAvailability
{
    public const string Requirement = "Requires two or more MOVEit environments configured in MAUI.";
    public static bool HasMultipleEnvironments(IEnumerable<MoveItServerDefinition> servers) =>
        servers.Select(MoveItEnvironmentKey.Resolve).Distinct(StringComparer.OrdinalIgnoreCase).Take(2).Count() == 2;
}

public sealed record CopySelectionRequest(string Source, IReadOnlyList<CopySelection> Objects);
public sealed record CopyAnalyzeRequest(string Source, string Destination, IReadOnlyList<CopySelection> Objects, bool IncludeDependencies = true);
public sealed record CopyResolveRequest(string PlanId, IReadOnlyList<CopyChoice> Choices, string Prefix = "", string Suffix = "", string NameScope = "tasks", bool UniqueNames = true);
public sealed record CopyReviewRequest(string PlanId, string GroupMode = "none", string Group = "", string FolderMode = "root", int? FolderId = null,
    string FolderPath = "", bool CreateFolder = false, bool CopyMetadata = true, bool PreserveDestinationMetadata = true,
    bool PreserveDestinationGroups = true, bool PreserveDestinationCredentials = true, bool RecoveryCopies = true, bool ConfirmOverwrite = false,
    bool IncludeTlsPrivateKeys = true, bool AllowIncompleteCredentials = false);
public sealed record CopyStartRequest(string PlanId, string ReviewToken, bool ConfirmOverwrite = false);
public sealed record CopyDifference(string Property, string Source, string Destination, string Status);
public sealed record CopyPlacement(string Name, string Type, string Action, IReadOnlyList<string> BeforeGroups, IReadOnlyList<string> AfterGroups, string Folder, bool AccessChanged)
{
    public IReadOnlyList<string> SourceGroups { get; init; } = [];
    public bool DestinationGroupsPreserved { get; init; }
}
public sealed record CopyResult(string Key, string Type, string SourceId, string SourceName, string Action, string DestinationId, string DestinationName,
    string Status, string Message, IReadOnlyList<string> Skipped, bool RecoveryCaptured = false, string CredentialStatus = "", string CheckoutCleanup = "Not required")
{
    // Metadata compatibility differences are informational. Skipped retains follow-up warnings;
    // an entire unattempted object is represented by Status = "Skipped" instead.
    public IReadOnlyList<string> MetadataNotes { get; init; } = [];
    public string Severity => Status is "Failed" or "Not verified" ? "error"
        : Status == "Skipped" || Skipped.Count > 0 ? "warning" : MetadataNotes.Count > 0 ? "metadata" : "none";
}
public sealed class CopyReport
{
    public string OperationId { get; set; } = "";
    public string InitiatedBy { get; set; } = "";
    public string Source { get; set; } = "";
    public string Destination { get; set; } = "";
    public string SourceAccount { get; set; } = "";
    public string DestinationAccount { get; set; } = "";
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset? CompletedUtc { get; set; }
    public string Status { get; set; } = "Queued";
    public int TotalObjects { get; set; }
    public int CompletedObjects => Items.Count(item => item.Status is "Created" or "Overwritten" or "Reused");
    public string Phase { get; set; } = "";
    public DateTimeOffset? PhaseStartedUtc { get; set; }
    public int PhaseElapsedSeconds => PhaseStartedUtc.HasValue ? (int)Math.Clamp((DateTimeOffset.UtcNow - PhaseStartedUtc.Value).TotalSeconds, 0, int.MaxValue) : 0;
    public string CurrentObjectKey { get; set; } = "";
    public string CurrentObjectType { get; set; } = "";
    public string CurrentObjectName { get; set; } = "";
    public bool CancelRequested { get; set; }
    public string Message { get; set; } = "";
    public string Placement { get; set; } = "";
    public List<CopyResult> Items { get; set; } = [];
    public IReadOnlyList<string> GroupsPlanned { get; set; } = [];
    public IReadOnlyList<string> GroupsCreated { get; set; } = [];
    public string GroupCreationStatus { get; set; } = "Not requested";
    internal void SetSuccessfulCompletionStatus() => Status = Items.Any(item => item.Skipped.Count > 0) ? "Completed with warnings" : "Completed";
}

internal sealed class CopyPlan
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public required string Owner { get; init; }
    public required string Source { get; init; }
    public required string Destination { get; init; }
    public DateTimeOffset ExpiresUtc { get; } = DateTimeOffset.UtcNow.AddMinutes(30);
    public bool IncludeDependencies { get; init; }
    public List<CopyPlanItem> Items { get; } = [];
    public List<CopyObject> DestinationObjects { get; set; } = [];
    public IReadOnlyList<string> Groups { get; set; } = [];
    public IReadOnlyList<MetadataFolderRecord> Folders { get; set; } = [];
    public string SourceAccount { get; set; } = "";
    public string DestinationAccount { get; set; } = "";
    public string Passphrase { get; set; } = "MauiCopy-1-" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)) + "!";
    public string ReviewToken { get; set; } = "";
    public CopyReviewRequest? Review { get; set; }
    public bool Started { get; set; }
    public volatile bool CancelRequested;
    public SemaphoreSlim Gate { get; } = new(1, 1);
}

internal sealed class CopyPlanItem(CopyObject source, bool selected)
{
    public CopyObject Source { get; set; } = source;
    public bool Selected { get; } = selected;
    public JsonObject Definition { get; set; } = new();
    public string NativeXml { get; set; } = "";
    public string PortableXml { get; set; } = "";
    public string Revision { get; set; } = "";
    public PortableMetadataPackage? Metadata { get; set; }
    public List<string> Dependencies { get; } = [];
    public List<string> Issues { get; } = [];
    public CopyChoice Choice { get; set; } = new(source.Key, "", source.Name);
    public string DestinationRevision { get; set; } = "";
    public string DestinationXml { get; set; } = "";
    public IReadOnlyList<string> DestinationGroupsBefore { get; set; } = [];
    public string DestinationPortableXml { get; set; } = "";
    public string ImpactRevision { get; set; } = "";
    public List<string> Validation { get; } = [];
    public List<string> Skipped { get; } = [];
    public List<string> MetadataNotes { get; } = [];
    public byte[]? KeyBytes { get; set; }
    public string KeyFileName { get; set; } = "";
    public string CredentialStatus { get; set; } = source.Type is "Task" or "Host" ? "Protected credential transfer" : source.Type == "KeyCertificate" ? "Key transfer capability check" : "Not applicable";
}

internal sealed record CopyDefinition(JsonObject Json, string Xml, string Revision);

// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

namespace MoveItAutomationUI.Models;

public sealed class QuickTransferPageViewModel
{
    public IReadOnlyList<QuickTransferHostOption> SourceHosts { get; init; } = Array.Empty<QuickTransferHostOption>();
    public IReadOnlyList<QuickTransferHostOption> DestinationHosts { get; init; } = Array.Empty<QuickTransferHostOption>();
    public IReadOnlyList<QuickTransferPgpKeyOption> PgpKeys { get; init; } = Array.Empty<QuickTransferPgpKeyOption>();
    public bool PgpLicensed { get; init; }
    public bool AutoFormatNewTaskHostNames { get; init; } = true;
    public string LoadError { get; init; } = string.Empty;
    public bool ConfirmationEmailAvailable { get; init; }
    public string ConfirmationEmailUnavailableReason { get; init; } = string.Empty;
}

public sealed record QuickTransferHostOption(
    string Id,
    string Name,
    string TypeCode,
    string Type,
    bool BrowseSupported,
    string RootKind = "");

public sealed record QuickTransferPgpKeyOption(
    string Id,
    string Name,
    string Uid,
    string PublicPrivate);

public sealed class QuickTransferRequest
{
    public string SourceHostId { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string SourceFileSpec { get; set; } = string.Empty;
    public string SourceRootName { get; set; } = string.Empty;
    public string SourceRootId { get; set; } = string.Empty;
    public bool DeleteSourceFileAfterSuccess { get; set; }
    public string ProcessingMode { get; set; } = "none";
    public string PgpKeyId { get; set; } = string.Empty;
    public string DestinationHostId { get; set; } = string.Empty;
    public string DestinationPath { get; set; } = string.Empty;
    public string DestinationFileName { get; set; } = string.Empty;
    public bool OverwriteExistingFile { get; set; } = true;
    public string DestinationRootName { get; set; } = string.Empty;
    public string DestinationRootId { get; set; } = string.Empty;
    public bool CreateTraditionalTask { get; set; }
    public string TaskName { get; set; } = string.Empty;
    public bool DeleteTemporaryTaskAfterSuccess { get; set; } = true;
    public bool SendTransferConfirmationEmail { get; set; }
    public string ConfirmationEmailRecipients { get; set; } = string.Empty;
    public string TransferToken { get; set; } = string.Empty;
}

public sealed class QuickTransferCancelRequest
{
    public string TransferToken { get; set; } = string.Empty;
}


public sealed record QuickTransferPreparationResult(
    string TransferToken,
    string TaskId,
    string TaskName,
    bool PersistentTask,
    bool DeleteTemporaryTaskAfterSuccess,
    bool ReusedPreparedTask);

public sealed record QuickTransferStartResult(
    string TransferToken,
    string TaskId,
    string TaskName,
    string NominalStart,
    bool PersistentTask,
    bool DeleteTemporaryTaskAfterSuccess);

public sealed record QuickTransferStatusResult(
    string TransferToken,
    string TaskId,
    string TaskName,
    string State,
    string Message,
    int? PercentComplete,
    long? CurrentFileBytes,
    long? TotalFileBytes,
    int? FilesTransferred,
    double? TotalBytesTransferred,
    bool Completed,
    bool Success);


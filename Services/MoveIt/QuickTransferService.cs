// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MoveItAutomationUI.Services.MoveIt.TraditionalTasks;
using MoveItAutomationUI.Services.MoveIt.Tasks;
using Microsoft.Extensions.Caching.Memory;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.Reporting;

namespace MoveItAutomationUI.Services.MoveIt;

public interface IQuickTransferService
{
    Task<QuickTransferPageViewModel> GetPageModelAsync(bool autoFormatNewTaskHostNames, CancellationToken cancellationToken = default);
    Task<QuickTransferPageViewModel> RefreshReferencesAsync(bool autoFormatNewTaskHostNames, CancellationToken cancellationToken = default);
    Task<QuickTransferPreparationResult> PrepareAsync(QuickTransferRequest request, CancellationToken cancellationToken = default);
    Task<QuickTransferStartResult> StartAsync(QuickTransferRequest request, CancellationToken cancellationToken = default);
    Task<QuickTransferStatusResult> CancelAsync(string transferToken, CancellationToken cancellationToken = default);
    Task<QuickTransferStatusResult> GetStatusAsync(string transferToken, CancellationToken cancellationToken = default);
}

public sealed class QuickTransferService : IQuickTransferService
{
    private const string CachePrefix = "MoveItAutomationUI:QuickTransfer:";
    private static readonly TimeSpan PreparedLifetime = TimeSpan.FromHours(2);
    private static readonly TimeSpan RunCorrelationEarlyTolerance = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RunCorrelationLateTolerance = TimeSpan.FromMinutes(15);

    private readonly IAutomationHostService _hosts;
    private readonly IAutomationTaskService _tasks;
    private readonly IAutomationScriptService _scripts;
    private readonly IAutomationKeyCertificateService _keys;
    private readonly IAutomationReportService _reports;
    private readonly IAutomationHostBrowseService _browse;
    private readonly ITraditionalTaskHostProfileCatalog _profiles;
    private readonly ITraditionalTaskDefinitionComposer _composer;
    private readonly ITaskCrudCoordinator _taskCrud;
    private readonly IMoveItRestClient _rest;
    private readonly IMoveItSessionService _session;
    private readonly IMetadataRepository _metadata;
    private readonly IMemoryCache _cache;
    private readonly IQuickTransferCleanupTracker _cleanupTracker;
    private readonly IOutboundReportEmailService _outboundEmail;
    private readonly ILogger<QuickTransferService> _logger;

    public QuickTransferService(
        IAutomationHostService hosts,
        IAutomationTaskService tasks,
        IAutomationScriptService scripts,
        IAutomationKeyCertificateService keys,
        IAutomationReportService reports,
        IAutomationHostBrowseService browse,
        ITraditionalTaskHostProfileCatalog profiles,
        ITraditionalTaskDefinitionComposer composer,
        ITaskCrudCoordinator taskCrud,
        IMoveItRestClient rest,
        IMoveItSessionService session,
        IMetadataRepository metadata,
        IMemoryCache cache,
        IQuickTransferCleanupTracker cleanupTracker,
        IOutboundReportEmailService outboundEmail,
        ILogger<QuickTransferService> logger)
    {
        _hosts = hosts;
        _tasks = tasks;
        _scripts = scripts;
        _keys = keys;
        _reports = reports;
        _browse = browse;
        _profiles = profiles;
        _composer = composer;
        _taskCrud = taskCrud;
        _rest = rest;
        _session = session;
        _metadata = metadata;
        _cache = cache;
        _cleanupTracker = cleanupTracker;
        _outboundEmail = outboundEmail;
        _logger = logger;
    }

    public Task<QuickTransferPageViewModel> GetPageModelAsync(bool autoFormatNewTaskHostNames, CancellationToken cancellationToken = default)
        => BuildPageModelAsync(autoFormatNewTaskHostNames, false, cancellationToken);

    public Task<QuickTransferPageViewModel> RefreshReferencesAsync(bool autoFormatNewTaskHostNames, CancellationToken cancellationToken = default)
        => BuildPageModelAsync(autoFormatNewTaskHostNames, true, cancellationToken);

    private async Task<QuickTransferPageViewModel> BuildPageModelAsync(bool autoFormatNewTaskHostNames, bool forceRefresh, CancellationToken cancellationToken)
    {
        var session = RequireSession();
        var pgpLicensed = MoveItLicensePolicy.GetCapabilities(session).PgpLicensed;
        var hostSnapshotTask = _hosts.GetHostSnapshotAsync(forceRefresh, cancellationToken);
        var keySnapshotTask = pgpLicensed
            ? _keys.GetSnapshotAsync(forceRefresh, cancellationToken)
            : Task.FromResult(new KeyCertificateCacheSnapshot(Array.Empty<AutomationKeyCertificate>(), DateTimeOffset.UtcNow));
        await Task.WhenAll(hostSnapshotTask, keySnapshotTask);

        var visibleHosts = (await hostSnapshotTask).Hosts
            .Where(host => !host.Id.Equals("0", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var sourceHosts = visibleHosts
            .Where(host => _profiles.Find(host.TypeCode)?.SourceWriteSupported == true)
            .Select(ToHostOption)
            .OrderBy(host => host.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var destinationHosts = visibleHosts
            .Where(host => _profiles.Find(host.TypeCode)?.DestinationWriteSupported == true)
            .Select(ToHostOption)
            .OrderBy(host => host.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var pgpKeys = (await keySnapshotTask).Items
            .Where(IsSelectableQuickTransferPgpKey)
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => new QuickTransferPgpKeyOption(item.Id, item.Name, item.Uid, item.PublicPrivate))
            .ToArray();

        var smtpStatus = _outboundEmail.GetConfigurationStatus();
        return new QuickTransferPageViewModel
        {
            SourceHosts = sourceHosts,
            DestinationHosts = destinationHosts,
            PgpLicensed = pgpLicensed,
            PgpKeys = pgpKeys,
            AutoFormatNewTaskHostNames = autoFormatNewTaskHostNames,
            ConfirmationEmailAvailable = smtpStatus.IsConfigured,
            ConfirmationEmailUnavailableReason = smtpStatus.IsConfigured ? string.Empty : "SMTP is not configured. Configure SMTP under Configuration > Settings > SMTP Configuration to enable transfer confirmation emails."
        };
    }

    public async Task<QuickTransferStartResult> StartAsync(QuickTransferRequest request, CancellationToken cancellationToken = default)
    {
        var preparation = await PrepareAsync(request, cancellationToken);
        var normalizedRequest = NormalizeRequest(request);
        var state = GetState(preparation.TransferToken) ?? throw new InvalidOperationException("The prepared Quick Transfer is no longer available. Prepare it again.");
        await state.Gate.WaitAsync(cancellationToken);
        try
        {
            lock (state.SyncRoot)
            {
                if (state.Completed)
                    throw new InvalidOperationException(state.Cancelled ? "This Quick Transfer was cancelled." : "This Quick Transfer has already completed.");
                if (state.CancelRequested)
                {
                    MarkCancelled(state, "Transfer cancelled before the MOVEit start request was submitted.");
                    PutState(state);
                    throw new InvalidOperationException("This Quick Transfer was cancelled before it started.");
                }
                if (state.Started)
                    throw new InvalidOperationException("This Quick Transfer has already been started.");
                if (state.StartAttempted)
                    throw new InvalidOperationException("This Quick Transfer start request has already been submitted. Check its status before attempting another transfer.");

                // Mark the attempt before crossing the native start boundary. If the HTTP response is lost after
                // MOVEit accepts the request, a second browser request must not launch the same transfer twice.
                state.StartAttempted = true;
                state.StartedUtc = DateTimeOffset.UtcNow;
                PutState(state);
            }

            // Persist the intended cleanup *before* crossing the non-idempotent Run boundary.
            // If tracking cannot be recorded, do not start a disposable Task that would be orphaned.
            if ((!state.PersistentTask && state.DeleteAfterSuccess) || normalizedRequest.SendTransferConfirmationEmail)
            {
                try
                {
                    await _cleanupTracker.TrackAsync(state.Token, state.TaskId, state.TaskName,
                        state.StartedUtc!.Value, RequireSession(), CancellationToken.None,
                        !state.PersistentTask && state.DeleteAfterSuccess,
                        normalizedRequest.SendTransferConfirmationEmail ? normalizedRequest.ConfirmationEmailRecipients : string.Empty,
                        state.SourceHostName, normalizedRequest.SourcePath, normalizedRequest.SourceFileSpec,
                        state.DestinationHostName, normalizedRequest.DestinationPath);
                }
                catch
                {
                    state.StartAttempted = false; // Native Run was not submitted.
                    state.StartedUtc = null;
                    PutState(state);
                    throw;
                }
            }

            // Cancel can arrive while durable cleanup tracking is being written.
            // Do not submit the native start after cancellation has already been requested.
            if (state.CancelRequested || state.Completed)
            {
                if ((!state.PersistentTask && state.DeleteAfterSuccess) || normalizedRequest.SendTransferConfirmationEmail)
                    await _cleanupTracker.StopTrackingAsync(state.Token, CancellationToken.None);
                lock (state.SyncRoot)
                {
                    MarkCancelled(state, "Transfer cancelled before MOVEit started the Task. The prepared Task was retained.");
                    PutState(state);
                }
                throw new InvalidOperationException("This Quick Transfer was cancelled before it started.");
            }

            try
            {
                // Starting a MOVEit Task is the point of no return. A browser disconnect must not cancel the native
                // start and encourage an unsafe duplicate retry. Cancellation is handled separately through the
                // exact running-task identity so a hung/lost browser response cannot trigger a duplicate start.
                var run = await _tasks.RunTaskNowAsync(state.TaskId, CancellationToken.None);
                lock (state.SyncRoot)
                {
                    state.Started = true;
                    state.StartOutcomeUnknown = false;
                    state.NominalStart = run.NominalStart;
                    PutState(state);
                }

                if ((!state.PersistentTask && state.DeleteAfterSuccess) || normalizedRequest.SendTransferConfirmationEmail)
                {
                    try { await _cleanupTracker.SetNominalStartAsync(state.Token, state.NominalStart, CancellationToken.None); }
                    catch (Exception ex) { _logger.LogError(ex, "Quick Transfer {TaskId} started but cleanup tracking could not update the run identity; safe background tracking remains pending.", state.TaskId); }
                }
                if (state.CancelRequested)
                    await TryCancelVisibleRunAsync(state, CancellationToken.None);

                _logger.LogInformation(
                    "Quick Transfer started. TaskId={TaskId}, TaskName={TaskName}, Persistent={Persistent}, DeleteAfterSuccess={DeleteAfterSuccess}, NominalStart={NominalStart}.",
                    state.TaskId, state.TaskName, state.PersistentTask, state.DeleteAfterSuccess, state.NominalStart);

                return new QuickTransferStartResult(
                    state.Token, state.TaskId, state.TaskName, state.NominalStart, state.PersistentTask, state.DeleteAfterSuccess);
            }
            catch (Exception ex)
            {
                // Once RunTaskNowAsync returned, the native start outcome is known. A later cancellation
                // error must surface as a cancellation error rather than being mislabeled outcome-unknown.
                if (state.Started) throw;

                // This native start call uses CancellationToken.None deliberately. Therefore an
                // OperationCanceledException here is not a browser-request cancellation; it can represent
                // a client/transport timeout after MOVEit may already have accepted the start. Treat an
                // exception before a run identity is returned as outcome-unknown and never submit a second start.
                if (!state.Cancelled)
                {
                    state.StartOutcomeUnknown = true;
                    PutState(state);
                }
                _logger.LogError(ex,
                    "Quick Transfer start outcome is unknown. TaskId={TaskId}, TaskName={TaskName}, TransferToken={TransferToken}. Verify running/history state before retrying.",
                    state.TaskId, state.TaskName, state.Token);
                throw new InvalidOperationException(
                    "MAUI could not confirm the outcome of the MOVEit start request. Do not submit the transfer again until its status is verified; the prepared Task has been retained for troubleshooting.", ex);
            }
        }
        finally
        {
            state.Gate.Release();
        }
    }

    public async Task<QuickTransferStatusResult> CancelAsync(string transferToken, CancellationToken cancellationToken = default)
    {
        var state = GetState(transferToken) ?? throw new InvalidOperationException("This Quick Transfer status is no longer available.");
        var cancelledBeforeStart = false;
        lock (state.SyncRoot)
        {
            if (state.Completed) return ToStatus(state);

            state.CancelRequested = true;
            if (!state.StartAttempted)
            {
                MarkCancelled(state, "Transfer cancelled before MOVEit started the Task. The prepared Task was retained.");
                cancelledBeforeStart = true;
            }
            PutState(state);
        }

        // Cancellation intent disqualifies this Task from automatic deletion, even if MOVEit
        // finishes successfully before the native Kill operation is observed.
        if (!state.PersistentTask && state.DeleteAfterSuccess)
            await _cleanupTracker.StopTrackingAsync(state.Token, CancellationToken.None);

        if (cancelledBeforeStart)
        {
            _logger.LogInformation("Quick Transfer cancelled before start. TaskId={TaskId}, TaskName={TaskName}.", state.TaskId, state.TaskName);
            return ToStatus(state);
        }

        // Once the cancellation intent is recorded, do not tie the actual MOVEit stop attempt to the
        // browser request lifetime. A dropped/aborted Cancel response must not abandon a requested stop.
        if (await TryCancelVisibleRunAsync(state, CancellationToken.None))
            return ToStatus(state);

        return CancellationPendingStatus(state,
            state.StartOutcomeUnknown
                ? "Cancellation requested. MAUI is waiting for MOVEit to expose the run identity so it can stop the transfer safely."
                : "Cancellation requested. Waiting for the running MOVEit Task instance to become visible so it can be stopped safely.");
    }

    public async Task<QuickTransferStatusResult> GetStatusAsync(string transferToken, CancellationToken cancellationToken = default)
    {
        var state = GetState(transferToken) ?? throw new InvalidOperationException("This Quick Transfer status is no longer available.");
        // Status polling is active ownership of this prepared/running transfer. Refresh the cache lifetime so
        // a legitimate long-running MOVEit Task cannot lose its MAUI status token while it is still monitored.
        PutState(state);
        if (state.Completed)
            return ToStatus(state);
        if (!state.Started && !state.StartAttempted)
            return new QuickTransferStatusResult(state.Token, state.TaskId, state.TaskName, "Prepared", "Task prepared and ready to run.", null, null, null, null, null, false, false);

        var runningTasks = await _rest.GetRunningTasksAsync(cancellationToken);
        if (state.Completed) return ToStatus(state);
        if (state.CancelRequested)
        {
            var cancellationRun = SelectRunningRun(runningTasks, state.TaskId, state.NominalStart, state.StartedUtc);
            if (cancellationRun is not null && await CancelRunAsync(state, cancellationRun))
                return ToStatus(state);
        }

        var running = SelectRunningRun(runningTasks, state.TaskId, state.NominalStart, state.StartedUtc);
        if (running is not null)
        {
            if (!state.Started)
            {
                state.Started = true;
                state.StartOutcomeUnknown = false;
                state.NominalStart = First(state.NominalStart, running.NominalStart);
            }

            int? percent = null;
            if (running.TotFileBytes is > 0 && running.CurFileBytes is >= 0)
                percent = (int)Math.Clamp(Math.Round(running.CurFileBytes.Value * 100d / running.TotFileBytes.Value), 0, 100);

            state.RunningObserved = true;
            state.LastPercentComplete = percent ?? state.LastPercentComplete;
            state.LastCurrentFileBytes = running.CurFileBytes ?? state.LastCurrentFileBytes;
            state.LastTotalFileBytes = running.TotFileBytes ?? state.LastTotalFileBytes;
            PutState(state);

            if (state.CancelRequested)
                return CancellationPendingStatus(state, "Cancellation requested. MOVEit still reports the Task as running; MAUI is waiting for a safely correlated run identity.");

            var message = First(running.LastErrorText, running.Status, "Transfer is running.");
            return new QuickTransferStatusResult(
                state.Token, state.TaskId, state.TaskName, "Running", message, percent,
                running.CurFileBytes ?? state.LastCurrentFileBytes, running.TotFileBytes ?? state.LastTotalFileBytes,
                state.FilesTransferred, state.TotalBytesTransferred, false, false);
        }

        var history = await _reports.GetTaskRunsAsync(state.TaskId, 25, null, 25, cancellationToken);
        if (state.Completed) return ToStatus(state);
        var runRecord = SelectRunRecord(history.Items, state.NominalStart, state.StartedUtc);

        if (runRecord is null)
        {
            if (state.CancelRequested)
                return CancellationPendingStatus(state, "Cancellation requested. MOVEit is not currently exposing a safely correlated running instance; MAUI will continue checking until the run can be stopped or its final outcome is known.");

            var elapsed = state.StartedUtc.HasValue ? DateTimeOffset.UtcNow - state.StartedUtc.Value : TimeSpan.Zero;
            var finalizing = state.RunningObserved || state.HistoryObserved;
            var message = state.StartOutcomeUnknown
                ? "MAUI has not yet confirmed whether MOVEit started this transfer. The start request will not be resubmitted automatically; continue monitoring or review the retained Task and MOVEit records."
                : finalizing
                    ? "MOVEit is no longer reporting the Task as running; waiting for its completed Task Run record."
                    : elapsed > TimeSpan.FromSeconds(30)
                        ? "MOVEit has accepted the transfer, but the completed Task Run is not visible yet."
                        : "Transfer is starting...";
            return new QuickTransferStatusResult(
                state.Token, state.TaskId, state.TaskName, finalizing ? "Finalizing" : "Starting", message, state.LastPercentComplete,
                state.LastCurrentFileBytes, state.LastTotalFileBytes, state.FilesTransferred, state.TotalBytesTransferred,
                false, false);
        }

        state.HistoryObserved = true;
        var status = TaskRunStatus.Normalize(runRecord.Status);
        if (status.Equals("Success", StringComparison.OrdinalIgnoreCase))
        {
            await state.Gate.WaitAsync(cancellationToken);
            try
            {
                if (state.Completed) return ToStatus(state);
                state.Completed = true;
                state.Success = true;
                state.FinalState = "Success";
                state.FinalMessage = string.IsNullOrWhiteSpace(runRecord.StatusMessage) ? "Transfer completed successfully." : runRecord.StatusMessage;
                if (state.CancelRequested)
                    state.FinalMessage = $"{state.FinalMessage} Cancellation was requested, but MOVEit completed the transfer before it could be stopped.";
                state.CancelRequested = false;
                state.FilesTransferred = runRecord.FilesSent ?? state.FilesTransferred;
                state.TotalBytesTransferred = runRecord.TotalBytesSent ?? state.TotalBytesTransferred;
                if (!state.PersistentTask && state.DeleteAfterSuccess)
                {
                    try
                    {
                        // This is the same safe, correlated cleanup path used by the independent worker.
                        // If the server has not yet confirmed the matching run, leave the record queued.
                        if (!await _cleanupTracker.TryCleanupAsync(state.Token, CancellationToken.None))
                            state.FinalMessage += " The temporary Task is queued for server-side cleanup after MOVEit verifies its successful run.";
                    }
                    catch (Exception ex)
                    {
                        state.FinalMessage += " The transfer succeeded, but automatic Task cleanup is pending; review Configuration > Logs if it remains.";
                        _logger.LogError(ex, "Quick Transfer {TaskId} cleanup was deferred after success.", state.TaskId);
                    }
                }
                PutState(state);
                return ToStatus(state);
            }
            finally
            {
                state.Gate.Release();
            }
        }

        if (status.Equals("Failed", StringComparison.OrdinalIgnoreCase) || status.Equals("No Transfers", StringComparison.OrdinalIgnoreCase))
        {
            await state.Gate.WaitAsync(cancellationToken);
            try
            {
                if (state.Completed) return ToStatus(state);
                state.Completed = true;
                state.Success = false;
                state.FinalState = status.Length == 0 ? "Failed" : status;
                state.FinalMessage = First(runRecord.StatusMessage,
                    status.Equals("No Transfers", StringComparison.OrdinalIgnoreCase)
                        ? "MOVEit completed the Task without transferring a file. The Task was retained for troubleshooting."
                        : "The transfer failed. The Task was retained for troubleshooting.");
                if (state.CancelRequested)
                    state.FinalMessage = $"{state.FinalMessage} Cancellation was requested, but MOVEit reached a final state before it could be stopped.";
                state.CancelRequested = false;
                state.FilesTransferred = runRecord.FilesSent ?? state.FilesTransferred;
                state.TotalBytesTransferred = runRecord.TotalBytesSent ?? state.TotalBytesTransferred;
                if (!state.PersistentTask && state.DeleteAfterSuccess)
                    await _cleanupTracker.StopTrackingAsync(state.Token, CancellationToken.None);
                PutState(state);
                return ToStatus(state);
            }
            finally
            {
                state.Gate.Release();
            }
        }

        state.FilesTransferred = runRecord.FilesSent ?? state.FilesTransferred;
        state.TotalBytesTransferred = runRecord.TotalBytesSent ?? state.TotalBytesTransferred;
        PutState(state);
        return new QuickTransferStatusResult(
            state.Token, state.TaskId, state.TaskName, First(status, "Running"),
            First(runRecord.StatusMessage, "MOVEit is processing the transfer."), state.LastPercentComplete,
            state.LastCurrentFileBytes, state.LastTotalFileBytes,
            state.FilesTransferred, state.TotalBytesTransferred, false, false);
    }

    public async Task<QuickTransferPreparationResult> PrepareAsync(QuickTransferRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var normalized = NormalizeRequest(request);
        if (normalized.SendTransferConfirmationEmail)
        {
            var status = _outboundEmail.GetConfigurationStatus();
            if (!status.IsConfigured) throw new InvalidOperationException("SMTP is not configured for transfer confirmation emails.");
            _ = OutboundReportRepository.NormalizeRecipients(normalized.ConfirmationEmailRecipients);
        }
        var fingerprint = Fingerprint(normalized);
        var existing = GetState(normalized.TransferToken);
        if (existing is not null && existing.Fingerprint.Equals(fingerprint, StringComparison.Ordinal))
        {
            return new QuickTransferPreparationResult(existing.Token, existing.TaskId, existing.TaskName, existing.PersistentTask, existing.DeleteAfterSuccess, true);
        }

        var context = await ValidateAndResolveAsync(normalized, cancellationToken);

        if (existing is not null && !existing.Started && !existing.StartAttempted)
        {
            if (existing.PersistentTask
                && normalized.CreateTraditionalTask
                && existing.TaskName.Equals(normalized.TaskName, StringComparison.OrdinalIgnoreCase))
            {
                await _taskCrud.UpdateTraditionalFreshAsync(existing.TaskId,
                    current => { ConfigureTask(current, context, normalized, existing.TaskId, existing.TaskName); return current; },
                    context.ProcessScript is null ? null : persisted => VerifyPersistedProcess(existing.TaskId, persisted, context, normalized),
                    cancellationToken: cancellationToken);
                _tasks.InvalidateCache();
                await RecordTaskModifiedAuditAsync(existing.TaskId);
                existing.Fingerprint = fingerprint;
                existing.SourceHostName = context.SourceHost.Name;
                existing.DestinationHostName = context.DestinationHost.Name;
                PutState(existing);
                _logger.LogInformation("Quick Transfer persistent Task {TaskId} ({TaskName}) was updated from the revised prepared request.", existing.TaskId, existing.TaskName);
                return new QuickTransferPreparationResult(existing.Token, existing.TaskId, existing.TaskName, true, false, true);
            }

            if (!existing.PersistentTask)
            {
                try
                {
                    await _taskCrud.DeleteAsync(existing.TaskId, CancellationToken.None);
                    _tasks.InvalidateCache();
                    await DeleteTaskMetadataAsync(existing.TaskId);
                }
                catch (Exception ex) { _logger.LogWarning(ex, "MAUI could not remove superseded Quick Transfer temporary Task {TaskId}.", existing.TaskId); }
                _cache.Remove(CacheKey(existing.Token));
            }
        }

        var taskName = normalized.CreateTraditionalTask
            ? normalized.TaskName
            : BuildTemporaryTaskName(context.SourceHost.Name, normalized.SourceFileSpec);

        TaskCreationResult? created = null;
        try
        {
            created = await CreateTaskWithCapacityAsync(taskName, context, normalized, cancellationToken);
            _tasks.InvalidateCache();
            // Every Quick Transfer Task participates in MAUI audit metadata, including temporary ADHOC Tasks.
            // RecordObjectCreatedAsync initializes both Created By/On and Modified By/On after the Task has
            // received its final Quick Transfer configuration. Temporary metadata is removed with the Task.
            await RecordTaskCreatedAuditAsync(created.Id, created.Name);

            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            var state = new QuickTransferState
            {
                Token = token,
                Fingerprint = fingerprint,
                TaskId = created.Id,
                TaskName = taskName,
                SourceHostName = context.SourceHost.Name,
                DestinationHostName = context.DestinationHost.Name,
                PersistentTask = normalized.CreateTraditionalTask,
                DeleteAfterSuccess = !normalized.CreateTraditionalTask && normalized.DeleteTemporaryTaskAfterSuccess
            };
            PutState(state);
            _logger.LogInformation("Quick Transfer Task prepared. TaskId={TaskId}, TaskName={TaskName}, Persistent={Persistent}.", state.TaskId, state.TaskName, state.PersistentTask);
            return new QuickTransferPreparationResult(token, created.Id, taskName, state.PersistentTask, state.DeleteAfterSuccess, false);
        }
        catch
        {
            if (created is not null)
            {
                try
                {
                    await _taskCrud.DeleteAsync(created.Id, CancellationToken.None);
                    _tasks.InvalidateCache();
                    await DeleteTaskMetadataAsync(created.Id);
                }
                catch (Exception cleanupEx) { _logger.LogError(cleanupEx, "Quick Transfer setup failed and MAUI could not remove incomplete Task {TaskId} ({TaskName}).", created.Id, created.Name); }
            }
            throw;
        }
    }

    private async Task RecordTaskCreatedAuditAsync(string taskId, string taskName)
    {
        try
        {
            var session = RequireSession();
            await _metadata.RecordObjectCreatedAsync(
                session.GetEnvironmentKey(), "Task", taskId, session.Username, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Quick Transfer Task {TaskId} ({TaskName}) was created and configured, but MAUI could not record its Created/Modified audit metadata.",
                taskId, taskName);
        }
    }

    private async Task RecordTaskModifiedAuditAsync(string taskId)
    {
        try
        {
            var session = RequireSession();
            await _metadata.RecordObjectConfigurationModifiedAsync(
                session.GetEnvironmentKey(), "Task", taskId, session.Username, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Quick Transfer Task {TaskId} was updated, but MAUI could not record its Modified audit metadata.",
                taskId);
        }
    }

    private async Task DeleteTaskMetadataAsync(string taskId)
    {
        try
        {
            var session = RequireSession();
            await _metadata.DeleteObjectsAsync(
                session.GetEnvironmentKey(), "Task", new[] { taskId }, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Quick Transfer Task {TaskId} was removed from MOVEit, but its MAUI metadata could not be removed.",
                taskId);
        }
    }

    private async Task<TaskCreationResult> CreateTaskWithCapacityAsync(
        string taskName, ResolvedContext context, QuickTransferRequest request, CancellationToken cancellationToken)
    {
        var session = RequireSession();
        using var creationLease = await MoveItLicensePolicy.AcquireTaskCreationAsync(session, cancellationToken);
        var live = (await _tasks.GetTaskSnapshotAsync(true, cancellationToken)).Tasks;
        if (live.Any(item => item.Name.Equals(taskName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"A MOVEit Task named '{taskName}' already exists. Review MOVEit before attempting another creation.");
        var capacityBlock = MoveItLicensePolicy.GetTaskCapacityBlock(session, live.Count, 1);
        if (!string.IsNullOrWhiteSpace(capacityBlock))
            throw new InvalidOperationException($"Quick Transfer requires one MOVEit Traditional Task. {capacityBlock}");

        var definition = new JsonObject();
        ConfigureTask(definition, context, request, string.Empty, taskName);
        // REST creates the identity. Unlike an update, POST must not send an empty ID.
        definition.Remove("ID");
        // No COM creation for Traditional Tasks, including temporary ADHOC Tasks.
        try
        {
            // Verification is owned by shared REST persistence. If MOVEit accepted POST
            // but readback fails, the outcome is indeterminate and must not be retried.
            return await _taskCrud.CreateTraditionalResolvedAsync(definition, cancellationToken,
                verifyPersisted: context.ProcessScript is null
                    ? null
                    : (id, persisted) => VerifyPersistedProcess(id, persisted, context, request));
        }
        catch (OperationCanceledException) { throw; }
        catch (TraditionalTaskCreationUnverifiedException)
        {
            _tasks.InvalidateCache();
            throw;
        }
        catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException)
        {
            _tasks.InvalidateCache();
            throw new InvalidOperationException(
                $"MOVEit REST creation of '{taskName}' could not be confirmed. Check MOVEit for that Task before retrying.", ex);
        }
    }

    private async Task<ResolvedContext> ValidateAndResolveAsync(QuickTransferRequest request, CancellationToken cancellationToken)
    {
        var session = RequireSession();
        if (request.SourceHostId.Length == 0) throw new InvalidOperationException("Select a Source Host.");
        if (request.SourceFileSpec.Length == 0) throw new InvalidOperationException("Source Filename / Filespec is required.");
        if (request.DestinationHostId.Length == 0) throw new InvalidOperationException("Select a Destination Host.");
        if (request.CreateTraditionalTask && request.TaskName.Length == 0) throw new InvalidOperationException("Task Name is required when Create Traditional Task is selected.");
        if (session.Info?.IsAdmin != true)
            throw new InvalidOperationException("Quick Transfer is available only to MOVEit administrators.");
        if (request.TaskName.Length > 254) throw new InvalidOperationException("Task Name cannot exceed 254 characters.");
        if (MayMatchMultipleFiles(request.SourceFileSpec)
            && request.DestinationFileName.Length > 0
            && !request.DestinationFileName.Contains("[OrigName]", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A wildcard or multi-file Source Filespec cannot use one fixed Destination Filename. Leave Destination Filename blank to preserve each source filename, or include [OrigName] in the destination name.");
        if (ContainsPathTraversal(request.SourcePath) || ContainsPathTraversal(request.DestinationPath))
            throw new InvalidOperationException("Relative parent traversal ('..') is not permitted in Quick Transfer paths. Use Browse or specify the intended absolute/host-relative folder.");

        if (request.SourceHostId.Equals("0", StringComparison.OrdinalIgnoreCase)
            || request.DestinationHostId.Equals("0", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The MOVEit (Default) Local File System Host is not available for Quick Transfer. Select a saved Host.");

        var hosts = (await _hosts.GetHostSnapshotAsync(true, cancellationToken)).Hosts
            .Where(host => !host.Id.Equals("0", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var source = hosts.FirstOrDefault(host => host.Id.Equals(request.SourceHostId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The selected Source Host no longer exists or is no longer visible to the current MOVEit user.");
        var destination = hosts.FirstOrDefault(host => host.Id.Equals(request.DestinationHostId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The selected Destination Host no longer exists or is no longer visible to the current MOVEit user.");
        var sourceProfile = _profiles.Require(source.TypeCode, "Source");
        var destinationProfile = _profiles.Require(destination.TypeCode, "Destination");
        if (sourceProfile.RootKind == TraditionalTaskLocationRootKind.None && request.SourcePath.Length == 0)
            throw new InvalidOperationException("Source Folder is required.");
        if (destinationProfile.RootKind == TraditionalTaskLocationRootKind.None && request.DestinationPath.Length == 0)
            throw new InvalidOperationException("Destination Folder is required.");

        AutomationScript? processScript = null;
        AutomationKeyCertificate? pgpKey = null;
        if (request.ProcessingMode is "encrypt" or "decrypt")
        {
            if (!MoveItLicensePolicy.GetCapabilities(session).PgpLicensed)
                throw new InvalidOperationException("PGP processing is not licensed on this MOVEit Automation server.");
            var scriptName = request.ProcessingMode == "encrypt" ? "PGP Encrypt Only" : "PGP Decrypt";
            processScript = (await _scripts.GetScriptsAsync(cancellationToken))
                .FirstOrDefault(script => script.IsBuiltIn && script.Name.Equals(scriptName, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"MOVEit's built-in '{scriptName}' Script is not available to the current user.");
        }
        if (request.ProcessingMode == "encrypt")
        {
            if (request.PgpKeyId.Length == 0) throw new InvalidOperationException("Select a PGP Key for encryption.");
            pgpKey = (await _keys.GetSnapshotAsync(true, cancellationToken)).Items
                .FirstOrDefault(item => IsSelectableQuickTransferPgpKey(item) && item.Id.Equals(request.PgpKeyId, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("The selected PGP Key no longer exists, is expired, or is no longer visible to the current MOVEit user.");
        }

        return new ResolvedContext(source, destination, sourceProfile, destinationProfile, processScript, pgpKey);
    }

    private void ConfigureTask(JsonObject task, ResolvedContext context, QuickTransferRequest request, string taskId, string taskName)
    {
        task["ID"] = taskId;
        var steps = new JsonArray { WrapStep("Source", BuildSourceBody(context.SourceHost, context.SourceProfile, request)) };
        if (context.ProcessScript is not null) steps.Add(BuildProcess(context.ProcessScript, context.PgpKey, request.ProcessingMode));
        steps.Add(WrapStep("Destination", BuildDestinationBody(context.DestinationHost, context.DestinationProfile, request)));
        _composer.ApplyRootDefinition(task, new TraditionalTaskRootDefinition(
            taskName, false,
            request.CreateTraditionalTask ? "Created from MAUI Quick Transfer." : "Temporary Task created by MAUI Quick Transfer.",
            "Created by MAUI Quick Transfer.",
            new JsonObject { ["Schedule"] = new JsonArray() }, steps,
            NextActions: new JsonArray()));
    }

    private JsonObject BuildSourceBody(AutomationHost host, TraditionalTaskHostProfile profile, QuickTransferRequest request)
    {
        var source = new JsonObject
        {
            ["HostID"] = host.Id,
            ["Type"] = profile.TypeCode,
            ["FileMask"] = request.SourceFileSpec,
            ["DeleteOrig"] = request.DeleteSourceFileAfterSuccess ? 1 : 0,
            ["DelRename"] = 0,
            ["SearchSubdirs"] = 0,
            ["NewFilesOnly"] = 0,
            ["UDMxFi"] = 1,
            ["UDMxBy"] = 1
        };
        return _composer.BuildStepBody(new TraditionalTaskStepDefinition(
            TraditionalTaskStepRole.Source, profile, source,
            request.SourcePath, request.SourceRootName, request.SourceRootId));
    }

    private JsonObject BuildDestinationBody(AutomationHost host, TraditionalTaskHostProfile profile, QuickTransferRequest request)
    {
        var useOriginal = request.DestinationFileName.Length == 0
            || request.DestinationFileName.Equals("[OrigName]", StringComparison.OrdinalIgnoreCase);
        var destination = new JsonObject
        {
            ["HostID"] = host.Id,
            ["Type"] = profile.TypeCode,
            ["FileName"] = useOriginal ? "[OrigName]" : request.DestinationFileName,
            ["UseOrigName"] = useOriginal ? 1 : 0,
            ["OverwriteOrig"] = request.OverwriteExistingFile ? 1 : 0,
            ["UseRelativeSubdirs"] = 1,
            ["Zip"] = 0
        };
        if (profile.DestinationSupportsForceDir) destination["ForceDir"] = 1;
        return _composer.BuildStepBody(new TraditionalTaskStepDefinition(
            TraditionalTaskStepRole.Destination, profile, destination,
            request.DestinationPath, request.DestinationRootName, request.DestinationRootId));
    }

    private JsonObject BuildProcess(AutomationScript script, AutomationKeyCertificate? pgpKey, string mode)
    {
        JsonArray? parameters = null;
        if (mode == "encrypt")
        {
            if (pgpKey is null) throw new InvalidOperationException("A PGP Key is required for encryption.");
            // Native Task PUT requires Parameters.Parameter[] with a lowercase value
            // and numeric Secure; MAUI's shared serializer is the single owner.
            parameters = new JsonArray
            {
                new JsonObject
                {
                    ["Name"] = "PGPRecipientKey",
                    ["value"] = pgpKey.Id,
                    ["Secure"] = 0
                }
            };
        }
        return _composer.BuildWrappedProcess(new TraditionalTaskProcessDefinition(
            JsonValue.Create(script.Id)!, parameters));
    }

    private void VerifyPersistedProcess(
        string taskId,
        JsonObject persisted,
        ResolvedContext context,
        QuickTransferRequest request)
    {
        if (context.ProcessScript is null) return;
        var process = FindProcessStep(persisted, context.ProcessScript.Id);
        if (process is null)
        {
            _logger.LogError(
                "Quick Transfer Task {TaskId} did not preserve Process Script {ScriptId} ({ScriptName}) after MOVEit Task PUT.",
                taskId, context.ProcessScript.Id, context.ProcessScript.Name);
            throw new InvalidOperationException(
                "MOVEit did not preserve the requested Quick Transfer processing step. The transfer was not started; review Configuration > Logs for details.");
        }

        if (request.ProcessingMode != "encrypt" || context.PgpKey is null) return;

        var recipient = ReadProcessParameter(process, "PGPRecipientKey");
        if (recipient.Equals(context.PgpKey.Id, StringComparison.OrdinalIgnoreCase)) return;

        _logger.LogError(
            "Quick Transfer Task {TaskId} did not preserve the selected PGP recipient key after MOVEit Task PUT. ExpectedKeyId={ExpectedKeyId}, PersistedValuePresent={PersistedValuePresent}.",
            taskId, context.PgpKey.Id, recipient.Length > 0);
        throw new InvalidOperationException(
            "MOVEit did not preserve the selected PGP recipient key in the generated Task. The transfer was not started; review Configuration > Logs for details.");
    }

    private static JsonObject? FindProcessStep(JsonObject task, string scriptId)
    {
        if (GetNodeIgnoreCase(task, "steps") is not JsonArray steps) return null;
        foreach (var step in steps.OfType<JsonObject>())
        {
            if (GetNodeIgnoreCase(step, "Process") is not JsonObject process) continue;
            var persistedScriptId = NodeText(GetNodeIgnoreCase(process, "ScriptID"));
            if (scriptId.Length == 0 || persistedScriptId.Equals(scriptId, StringComparison.OrdinalIgnoreCase)) return process;
        }
        return null;
    }

    private static string ReadProcessParameter(JsonObject process, string parameterName)
    {
        if (GetNodeIgnoreCase(process, "Parameters") is not JsonObject parameters) return string.Empty;

        var nativeValue = ReadNamedParameter(GetNodeIgnoreCase(parameters, "Parameter"), parameterName);
        if (nativeValue.Length > 0) return nativeValue;

        var restValue = ReadNamedParameter(GetNodeIgnoreCase(parameters, "any"), parameterName);
        if (restValue.Length > 0) return restValue;

        if (GetNodeIgnoreCase(parameters, parameterName) is JsonObject keyedParameter)
            return NodeText(GetNodeIgnoreCase(keyedParameter, "value"));

        return string.Empty;
    }

    private static string ReadNamedParameter(JsonNode? node, string parameterName)
    {
        if (node is JsonObject single)
            return ParameterValue(single, parameterName);
        if (node is not JsonArray array) return string.Empty;

        foreach (var item in array.OfType<JsonObject>())
        {
            var value = ParameterValue(item, parameterName);
            if (value.Length > 0) return value;
        }
        return string.Empty;
    }

    private static string ParameterValue(JsonObject parameter, string parameterName)
    {
        var name = NodeText(GetNodeIgnoreCase(parameter, "Name"));
        return name.Equals(parameterName, StringComparison.OrdinalIgnoreCase)
            ? NodeText(GetNodeIgnoreCase(parameter, "value"))
            : string.Empty;
    }

    private static JsonNode? GetNodeIgnoreCase(JsonObject obj, string name)
    {
        foreach (var pair in obj)
            if (pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)) return pair.Value;
        return null;
    }

    private static string NodeText(JsonNode? node)
    {
        if (node is null) return string.Empty;
        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var text)) return text?.Trim() ?? string.Empty;
            return value.ToJsonString().Trim('"').Trim();
        }
        return string.Empty;
    }

    private static JsonObject WrapStep(string name, JsonObject body) => new() { [name] = body };

    private QuickTransferState? GetState(string? token)
    {
        var normalized = (token ?? string.Empty).Trim();
        if (normalized.Length == 0) return null;
        return _cache.TryGetValue<QuickTransferState>(CacheKey(normalized), out var state) ? state : null;
    }

    private void PutState(QuickTransferState state) => _cache.Set(CacheKey(state.Token), state, PreparedLifetime);

    private string CacheKey(string token)
    {
        var session = RequireSession();
        return $"{CachePrefix}{session.GetEnvironmentKey()}:{session.Username}:{token}";
    }

    private MoveItSessionState RequireSession()
        => _session.Get() ?? throw new MoveItAuthenticationException("Your MOVEit session has expired. Sign in again.");


    private QuickTransferHostOption ToHostOption(AutomationHost host)
    {
        var type = _profiles.NormalizeType(host.TypeCode);
        var profile = _profiles.Find(type)
            ?? throw new InvalidOperationException($"MOVEit Host type '{type}' does not have a verified Quick Transfer profile.");
        var browseSupported = profile.BrowseSupported && _browse.SupportsHostType(type);
        return new QuickTransferHostOption(
            host.Id,
            host.Name,
            type,
            host.Type,
            browseSupported,
            profile.RootKind == TraditionalTaskLocationRootKind.None ? string.Empty : profile.RootKind.ToString());
    }

    internal static QuickTransferRequest NormalizeRequest(QuickTransferRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var processingMode = NormalizeMode(request.ProcessingMode);
        var persistentTask = request.CreateTraditionalTask;
        return new QuickTransferRequest
        {
            SourceHostId = (request.SourceHostId ?? string.Empty).Trim(),
            SourcePath = (request.SourcePath ?? string.Empty).Trim(),
            SourceFileSpec = (request.SourceFileSpec ?? string.Empty).Trim(),
            SourceRootName = (request.SourceRootName ?? string.Empty).Trim(),
            SourceRootId = (request.SourceRootId ?? string.Empty).Trim(),
            DeleteSourceFileAfterSuccess = request.DeleteSourceFileAfterSuccess,
            ProcessingMode = processingMode,
            PgpKeyId = processingMode == "encrypt" ? (request.PgpKeyId ?? string.Empty).Trim() : string.Empty,
            DestinationHostId = (request.DestinationHostId ?? string.Empty).Trim(),
            DestinationPath = (request.DestinationPath ?? string.Empty).Trim(),
            DestinationFileName = (request.DestinationFileName ?? string.Empty).Trim(),
            OverwriteExistingFile = request.OverwriteExistingFile,
            DestinationRootName = (request.DestinationRootName ?? string.Empty).Trim(),
            DestinationRootId = (request.DestinationRootId ?? string.Empty).Trim(),
            CreateTraditionalTask = persistentTask,
            TaskName = persistentTask ? (request.TaskName ?? string.Empty).Trim() : string.Empty,
            DeleteTemporaryTaskAfterSuccess = !persistentTask && request.DeleteTemporaryTaskAfterSuccess,
            SendTransferConfirmationEmail = request.SendTransferConfirmationEmail,
            ConfirmationEmailRecipients = request.SendTransferConfirmationEmail ? (request.ConfirmationEmailRecipients ?? string.Empty).Trim() : string.Empty,
            TransferToken = (request.TransferToken ?? string.Empty).Trim()
        };
    }

    private static string NormalizeMode(string? value)
    {
        var mode = (value ?? string.Empty).Trim().ToLowerInvariant();
        return mode switch { "" or "none" => "none", "encrypt" => "encrypt", "decrypt" => "decrypt", _ => throw new InvalidOperationException("Processing must be No Encryption, Encrypt, or Decrypt.") };
    }

    private static string Fingerprint(QuickTransferRequest request)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            request.SourceHostId,
            request.SourcePath,
            request.SourceFileSpec,
            request.SourceRootName,
            request.SourceRootId,
            request.DeleteSourceFileAfterSuccess,
            request.ProcessingMode,
            request.PgpKeyId,
            request.DestinationHostId,
            request.DestinationPath,
            request.DestinationFileName,
            request.OverwriteExistingFile,
            request.DestinationRootName,
            request.DestinationRootId,
            request.CreateTraditionalTask,
            request.TaskName,
            request.DeleteTemporaryTaskAfterSuccess,
            request.SendTransferConfirmationEmail,
            request.ConfirmationEmailRecipients
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string BuildTemporaryTaskName(string sourceHostName, string fileSpec)
    {
        var source = SanitizeTaskNameFragment(sourceHostName, 48);
        var file = SanitizeTaskNameFragment(fileSpec, 72);
        var guid = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var name = $"ADHOC_{source}_{file}_{guid}";
        return name.Length <= 254 ? name : name[..254];
    }

    private static string SanitizeTaskNameFragment(string value, int maxLength)
    {
        var builder = new StringBuilder();
        var lastUnderscore = false;
        foreach (var ch in (value ?? string.Empty).Trim())
        {
            var allowed = char.IsLetterOrDigit(ch) || ch is '-' or '.';
            var output = allowed ? ch : '_';
            if (output == '_' && lastUnderscore) continue;
            builder.Append(output);
            lastUnderscore = output == '_';
            if (builder.Length >= maxLength) break;
        }
        var result = builder.ToString().Trim('_');
        return result.Length == 0 ? "TRANSFER" : result;
    }

    internal static bool IsSelectableQuickTransferPgpKey(AutomationKeyCertificate item)
        => item.Kind == KeyCertificateKind.Pgp
            && item.Id.Length > 0
            && !KeyCertificateExpiration.IsExpired(item);

    internal static MoveItRunningTask? SelectRunningRun(
        IReadOnlyList<MoveItRunningTask> running,
        string taskId,
        string? nominalStart,
        DateTimeOffset? startedUtc)
    {
        var candidates = running
            .Where(item => item.TaskId.Equals(taskId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (candidates.Length == 0) return null;

        var expected = (nominalStart ?? string.Empty).Trim();
        if (expected.Length > 0)
        {
            var exact = candidates.FirstOrDefault(item => NominalStartsEquivalent(expected, item.NominalStart));
            if (exact is not null) return exact;

            // Once MOVEit has returned a NominalStart, never substitute a different non-empty run identity.
            // A candidate with a missing NominalStart can still be correlated by its start time for display,
            // but cancellation will not proceed until MOVEit exposes a killable run identity.
            candidates = candidates
                .Where(item => string.IsNullOrWhiteSpace(item.NominalStart))
                .ToArray();
            if (candidates.Length == 0) return null;
        }

        if (!startedUtc.HasValue) return candidates.Length == 1 ? candidates[0] : null;

        var earliestCurrentRun = startedUtc.Value - RunCorrelationEarlyTolerance;
        var latestCurrentRun = startedUtc.Value + RunCorrelationLateTolerance;
        var plausible = candidates
            .Where(item => TryParseRunningStart(item, out var runStart)
                && runStart >= earliestCurrentRun
                && runStart <= latestCurrentRun)
            .ToArray();
        return plausible.Length == 1 ? plausible[0] : null;
    }

    private async Task<bool> TryCancelVisibleRunAsync(QuickTransferState state, CancellationToken cancellationToken)
    {
        if (state.Completed) return state.Cancelled;

        var running = await _rest.GetRunningTasksAsync(cancellationToken);
        var run = SelectRunningRun(running, state.TaskId, state.NominalStart, state.StartedUtc);
        return run is not null && await CancelRunAsync(state, run);
    }

    private async Task<bool> CancelRunAsync(QuickTransferState state, MoveItRunningTask run)
    {
        if (string.IsNullOrWhiteSpace(run.NominalStart)) return false;

        var result = await _tasks.KillTaskAsync(state.TaskId, new[] { run.NominalStart }, CancellationToken.None);
        if (result.Killed == 0)
        {
            if (result.Failed > 0)
            {
                var message = result.Items.FirstOrDefault(item => !item.Success)?.Message;
                lock (state.SyncRoot)
                {
                    state.CancelRequested = false;
                    PutState(state);
                }
                throw new InvalidOperationException(First(message ?? string.Empty, "MOVEit could not cancel the running transfer."));
            }

            // The exact run ended between correlation and Kill. Do not call that a cancellation; keep the
            // request pending so Task Run history can report the real terminal outcome.
            return false;
        }

        lock (state.SyncRoot)
        {
            if (!state.Completed)
            {
                if (run.TotFileBytes is > 0 && run.CurFileBytes is >= 0)
                    state.LastPercentComplete = (int)Math.Clamp(Math.Round(run.CurFileBytes.Value * 100d / run.TotFileBytes.Value), 0, 100);
                state.LastCurrentFileBytes = run.CurFileBytes ?? state.LastCurrentFileBytes;
                state.LastTotalFileBytes = run.TotFileBytes ?? state.LastTotalFileBytes;
                state.NominalStart = First(state.NominalStart, run.NominalStart);
                MarkCancelled(state, "Transfer cancelled. The generated Task was retained for review.");
                PutState(state);
            }
        }
        _logger.LogInformation(
            "Quick Transfer cancelled. TaskId={TaskId}, TaskName={TaskName}, NominalStart={NominalStart}.",
            state.TaskId, state.TaskName, run.NominalStart);
        return state.Cancelled;
    }

    private static QuickTransferStatusResult CancellationPendingStatus(QuickTransferState state, string message)
        => new(
            state.Token, state.TaskId, state.TaskName, "Cancelling", message, state.LastPercentComplete,
            state.LastCurrentFileBytes, state.LastTotalFileBytes, state.FilesTransferred, state.TotalBytesTransferred,
            false, false);

    private static void MarkCancelled(QuickTransferState state, string message)
    {
        state.CancelRequested = false;
        state.Cancelled = true;
        state.Completed = true;
        state.Success = false;
        state.FinalState = "Cancelled";
        state.FinalMessage = message;
    }

    private static bool TryParseRunningStart(MoveItRunningTask run, out DateTimeOffset timestamp)
        => DateTimeOffset.TryParse((run.TimeStarted ?? string.Empty).Trim(), out timestamp)
            || DateTimeOffset.TryParse((run.NominalStart ?? string.Empty).Trim(), out timestamp);

    internal static TaskRunReportRecord? SelectRunRecord(
        IReadOnlyList<TaskRunReportRecord> history,
        string? nominalStart,
        DateTimeOffset? startedUtc)
    {
        if (history.Count == 0) return null;

        var expectedNominalStart = (nominalStart ?? string.Empty).Trim();
        IReadOnlyList<TaskRunReportRecord> fallbackRows = history;
        if (expectedNominalStart.Length > 0)
        {
            var exact = history.FirstOrDefault(item => NominalStartsEquivalent(expectedNominalStart, item.NominalStart));
            if (exact is not null) return exact;

            // Some MOVEit Task Run responses omit NominalStart during propagation. Allow a conservative
            // time-based fallback only for rows that omit the identity; an explicit different NominalStart
            // belongs to another run and must never be substituted for this Quick Transfer.
            fallbackRows = history
                .Where(item => string.IsNullOrWhiteSpace(item.NominalStart))
                .ToArray();
            if (fallbackRows.Count == 0) return null;
        }

        if (fallbackRows.Count == 1)
        {
            var only = fallbackRows[0];
            if (!startedUtc.HasValue) return only;
            if (!only.StartTime.HasValue) return null;
            var singleRunEarliest = startedUtc.Value - RunCorrelationEarlyTolerance;
            var singleRunLatest = startedUtc.Value + RunCorrelationLateTolerance;
            return only.StartTime.Value >= singleRunEarliest && only.StartTime.Value <= singleRunLatest ? only : null;
        }

        if (!startedUtc.HasValue) return null;

        var earliestCurrentRun = startedUtc.Value - RunCorrelationEarlyTolerance;
        var latestCurrentRun = startedUtc.Value + RunCorrelationLateTolerance;
        var candidates = fallbackRows
            .Where(item => item.StartTime.HasValue
                && item.StartTime.Value >= earliestCurrentRun
                && item.StartTime.Value <= latestCurrentRun)
            .ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }

    internal static bool NominalStartsEquivalent(string? expected, string? actual)
    {
        var left = (expected ?? string.Empty).Trim();
        var right = (actual ?? string.Empty).Trim();
        if (left.Length == 0 || right.Length == 0) return false;
        if (left.Equals(right, StringComparison.OrdinalIgnoreCase)) return true;

        if (DateTimeOffset.TryParse(left, out var leftTimestamp)
            && DateTimeOffset.TryParse(right, out var rightTimestamp))
        {
            return Math.Abs((leftTimestamp - rightTimestamp).TotalMilliseconds) < 1d;
        }

        return false;
    }

    private static bool MayMatchMultipleFiles(string fileSpec)
        => (fileSpec ?? string.Empty).IndexOfAny(new[] { '*', '?', ';' }) >= 0;

    private static bool ContainsPathTraversal(string path)
        => (path ?? string.Empty).Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment == "..");

    private static string First(params string[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static QuickTransferStatusResult ToStatus(QuickTransferState state)
        => new(
            state.Token, state.TaskId, state.TaskName, state.FinalState, state.FinalMessage,
            state.Success ? 100 : state.Cancelled ? state.LastPercentComplete : null,
            state.Cancelled ? state.LastCurrentFileBytes : null, state.Cancelled ? state.LastTotalFileBytes : null,
            state.FilesTransferred, state.TotalBytesTransferred,
            state.Completed, state.Success);

    private sealed record ResolvedContext(
        AutomationHost SourceHost,
        AutomationHost DestinationHost,
        TraditionalTaskHostProfile SourceProfile,
        TraditionalTaskHostProfile DestinationProfile,
        AutomationScript? ProcessScript,
        AutomationKeyCertificate? PgpKey);

    private sealed class QuickTransferState
    {
        public string Token { get; init; } = string.Empty;
        public string Fingerprint { get; set; } = string.Empty;
        public string TaskId { get; init; } = string.Empty;
        public string TaskName { get; init; } = string.Empty;
        public string SourceHostName { get; set; } = string.Empty;
        public string DestinationHostName { get; set; } = string.Empty;
        public bool PersistentTask { get; init; }
        public bool DeleteAfterSuccess { get; init; }
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public object SyncRoot { get; } = new();
        public bool CancelRequested { get; set; }
        public bool Cancelled { get; set; }
        public bool StartAttempted { get; set; }
        public bool StartOutcomeUnknown { get; set; }
        public bool Started { get; set; }
        public DateTimeOffset? StartedUtc { get; set; }
        public string NominalStart { get; set; } = string.Empty;
        public bool RunningObserved { get; set; }
        public bool HistoryObserved { get; set; }
        public int? LastPercentComplete { get; set; }
        public long? LastCurrentFileBytes { get; set; }
        public long? LastTotalFileBytes { get; set; }
        public bool Completed { get; set; }
        public bool Success { get; set; }
        public string FinalState { get; set; } = string.Empty;
        public string FinalMessage { get; set; } = string.Empty;
        public int? FilesTransferred { get; set; }
        public double? TotalBytesTransferred { get; set; }
    }
}

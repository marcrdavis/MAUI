// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.Lifecycle;

namespace MoveItAutomationUI.Controllers;

// MOVEit Automation exposes Task Run, File Activity, and Audit through its Reports interfaces.
// MAUI routes those native history views under "Records" to distinguish them from MAUI-generated Reports.
[Authorize]
public sealed class RecordsController : Controller
{
    private static readonly HashSet<int> AllowedBatchSizes = new() { 100, 200, 500, 1000, 2000, 5000, 10000 };

    private readonly IAutomationReportService _reportService;
    private readonly UiOptions _uiOptions;
    private readonly GridBehaviorOptions _gridBehavior;
    private readonly ReportBehaviorOptions _reportBehavior;
    private readonly ILogger<RecordsController> _logger;

    // Purpose: Initializes the Records controller with the MOVEit report APIs and UI behavior it requires.
    public RecordsController(
        IAutomationReportService reportService,
        IOptionsSnapshot<UiOptions> uiOptions,
        IOptionsSnapshot<GridBehaviorOptions> gridBehavior,
        IOptionsSnapshot<ReportBehaviorOptions> reportBehavior,
        ILogger<RecordsController> logger)
    {
        _reportService = reportService;
        _uiOptions = uiOptions.Value;
        _gridBehavior = gridBehavior.Value;
        _reportBehavior = reportBehavior.Value;
        _logger = logger;
    }

    // Purpose: Renders the Task Run Records page shell. The first record batch is loaded asynchronously
    // while the shared Loading shell is visible, matching Tasks/Hosts/Upcoming Runs.
    [HttpGet]
    public IActionResult TaskRun(string? taskId = null, string? taskName = null, string? dashboardView = null, string? returnUrl = null, string? returnLabel = null)
    {
        var behavior = NormalizeBehavior(_reportBehavior.TaskRun, ReportBehaviorOptions.TaskRunDefaultBatchSize);
        var normalizedDashboardView = NormalizeTaskRunDashboardView(dashboardView);
        var title = normalizedDashboardView.Length > 0 ? $"Task Run / {TaskRunDashboardLabel(normalizedDashboardView)}" : "Task Run";
        var description = normalizedDashboardView switch
        {
            "failed" => "Failed task runs during the past 24 hours, matching the Dashboard criteria.",
            "successful" => "Successful task runs during the past 24 hours, matching the Dashboard criteria.",
            "no-transfers" => "Task runs with no transfers during the past 24 hours, matching the Dashboard criteria.",
            _ => "Review MOVEit task execution history, results, file counts, transfer volume, and run details."
        };
        var emptyBatch = new ReportBatch<TaskRunReportRecord>(Array.Empty<TaskRunReportRecord>(), false, string.Empty);
        return View(BuildPage(
            title,
            description,
            normalizedDashboardView.Length > 0 ? string.Empty : BuildTaskContext(taskId, taskName),
            Url.Action(nameof(TaskRun)) ?? Url.Content("~/Records/TaskRun"),
            BuildTaskRunGrid(Array.Empty<TaskRunReportRecord>(), 0, false, behavior.BatchSize),
            behavior,
            Url.Action(nameof(TaskRunBatch)) ?? Url.Content("~/Records/TaskRunBatch"),
            taskId,
            taskName,
            null,
            null,
            null,
            emptyBatch,
            returnUrl: returnUrl,
            returnLabel: returnLabel,
            reportPreset: normalizedDashboardView));
    }

    // Purpose: Renders the File Activity Records page shell. The first record batch is loaded asynchronously
    // while the shared Loading shell is visible, matching Tasks/Hosts/Upcoming Runs.
    [HttpGet]
    public IActionResult FileActivity(string? taskId = null, string? taskName = null, string? runId = null, string? nominalStart = null, string? dashboardView = null, string? returnUrl = null, string? returnLabel = null)
    {
        var behavior = NormalizeBehavior(_reportBehavior.FileActivity, ReportBehaviorOptions.FileActivityDefaultBatchSize);
        var normalizedDashboardView = NormalizeFileActivityDashboardView(dashboardView);
        var title = normalizedDashboardView.Length > 0
            ? $"File Activity / {(normalizedDashboardView == "failed" ? "Failed — Past 24 Hours" : "Successful — Past 24 Hours")}"
            : "File Activity";
        var description = normalizedDashboardView switch
        {
            "failed" => "Failed file activity during the past 24 hours, up to 1,000 records using MOVEit's Dashboard filter.",
            "successful" => "Successful file activity during the past 24 hours, up to 1,000 records using MOVEit's Dashboard filter.",
            _ => "Review individual file activity recorded during MOVEit task runs."
        };
        var emptyBatch = new ReportBatch<FileActivityReportRecord>(Array.Empty<FileActivityReportRecord>(), false, string.Empty);
        return View(BuildPage(
            title,
            description,
            normalizedDashboardView.Length > 0 ? string.Empty : BuildTaskContext(taskId, taskName),
            Url.Action(nameof(FileActivity)) ?? Url.Content("~/Records/FileActivity"),
            BuildFileActivityGrid(Array.Empty<FileActivityReportRecord>(), 0, false, behavior.BatchSize),
            behavior,
            Url.Action(nameof(FileActivityBatch)) ?? Url.Content("~/Records/FileActivityBatch"),
            taskId,
            taskName,
            null,
            null,
            null,
            emptyBatch,
            runId,
            nominalStart,
            returnUrl,
            returnLabel,
            normalizedDashboardView,
            normalizedDashboardView.Length > 0 ? 1000 : null));
    }

    // Purpose: Renders the Audit Records page shell. The first record batch is loaded asynchronously
    // while the shared Loading shell is visible, matching Tasks/Hosts/Upcoming Runs.
    [HttpGet]
    public IActionResult Audit(string? targetType = null, string? targetId = null, string? targetName = null, string? returnUrl = null, string? returnLabel = null)
    {
        var behavior = NormalizeBehavior(_reportBehavior.Audit, ReportBehaviorOptions.AuditDefaultBatchSize);
        var emptyBatch = new ReportBatch<AuditReportRecord>(Array.Empty<AuditReportRecord>(), false, string.Empty);
        return View(BuildPage(
            "Audit",
            "Review MOVEit commands, configuration changes, users, results, and affected objects.",
            BuildAuditContext(targetType, targetId, targetName),
            Url.Action(nameof(Audit)) ?? Url.Content("~/Records/Audit"),
            BuildAuditGrid(Array.Empty<AuditReportRecord>(), 0, false, behavior.BatchSize),
            behavior,
            Url.Action(nameof(AuditBatch)) ?? Url.Content("~/Records/AuditBatch"),
            null,
            null,
            targetType,
            targetId,
            targetName,
            emptyBatch,
            returnUrl: returnUrl,
            returnLabel: returnLabel));
    }

    // Purpose: Returns one Task Run record batch for client-side paging or refresh.
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> TaskRunBatch(string? taskId = null, string? taskName = null, string? dashboardView = null, string? cursor = null, int offset = 0, CancellationToken cancellationToken = default)
    {
        var behavior = NormalizeBehavior(_reportBehavior.TaskRun, ReportBehaviorOptions.TaskRunDefaultBatchSize);
        var normalizedDashboardView = NormalizeTaskRunDashboardView(dashboardView);
        if (normalizedDashboardView.Length > 0)
        {
            try
            {
                var preset = await _reportService.GetTaskRunDashboardViewAsync(normalizedDashboardView, cancellationToken);
                var rows = BuildTaskRunGrid(preset.Items, preset.Items.Count, false, preset.MaximumRows).Rows;
                return Json(new
                {
                    rows = rows.Select(ToClientGridRow),
                    offset = 0,
                    end = rows.Count,
                    totalCount = rows.Count,
                    hasMore = false,
                    currentCursor = string.Empty,
                    nextCursor = string.Empty,
                    nextCount = 0,
                    batchSize = preset.MaximumRows,
                    refreshedUtc = DateTimeOffset.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unable to refresh MOVEit Task Run dashboard view {DashboardView}.", normalizedDashboardView);
                return StatusCode(StatusCodes.Status502BadGateway, new { message = ex.Message });
            }
        }

        return await LoadBatchJsonAsync(
            offset,
            behavior,
            cursor,
            async remaining =>
            {
                var batch = await _reportService.GetTaskRunsAsync(taskId, behavior.BatchSize, cursor, remaining, cancellationToken);
                return (BuildTaskRunGrid(batch.Items, BuildTotalCount(offset, batch.Items.Count, batch.HasMore), batch.HasMore, behavior.BatchSize).Rows, batch.HasMore && !string.IsNullOrWhiteSpace(batch.NextCursor), batch.NextCursor);
            });
    }

    // Purpose: Returns one File Activity record batch for client-side paging or refresh.
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> FileActivityBatch(string? taskId = null, string? taskName = null, string? runId = null, string? nominalStart = null, string? dashboardView = null, string? cursor = null, int offset = 0, CancellationToken cancellationToken = default)
    {
        var behavior = NormalizeBehavior(_reportBehavior.FileActivity, ReportBehaviorOptions.FileActivityDefaultBatchSize);
        var normalizedDashboardView = NormalizeFileActivityDashboardView(dashboardView);
        if (normalizedDashboardView.Length > 0)
        {
            try
            {
                var preset = await _reportService.GetFileActivityDashboardViewAsync(normalizedDashboardView, cancellationToken);
                var rows = BuildFileActivityGrid(preset.Items, preset.Items.Count, false, preset.MaximumRows).Rows;
                return Json(new
                {
                    rows = rows.Select(ToClientGridRow),
                    offset = 0,
                    end = rows.Count,
                    totalCount = rows.Count,
                    hasMore = false,
                    currentCursor = string.Empty,
                    nextCursor = string.Empty,
                    nextCount = 0,
                    batchSize = preset.MaximumRows,
                    refreshedUtc = DateTimeOffset.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unable to refresh native MOVEit File Activity dashboard view {DashboardView}.", normalizedDashboardView);
                return StatusCode(StatusCodes.Status502BadGateway, new { message = ex.Message });
            }
        }

        return await LoadBatchJsonAsync(
            offset,
            behavior,
            cursor,
            async remaining =>
            {
                var batch = await _reportService.GetFileActivityAsync(taskId, nominalStart, behavior.BatchSize, cursor, remaining, cancellationToken);
                return (BuildFileActivityGrid(batch.Items, BuildTotalCount(offset, batch.Items.Count, batch.HasMore), batch.HasMore, behavior.BatchSize).Rows, batch.HasMore && !string.IsNullOrWhiteSpace(batch.NextCursor), batch.NextCursor);
            });
    }

    // Purpose: Returns one Audit record batch for client-side paging or refresh.
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> AuditBatch(string? targetType = null, string? targetId = null, string? targetName = null, string? cursor = null, int offset = 0, CancellationToken cancellationToken = default)
    {
        var behavior = NormalizeBehavior(_reportBehavior.Audit, ReportBehaviorOptions.AuditDefaultBatchSize);
        return await LoadBatchJsonAsync(
            offset,
            behavior,
            cursor,
            async remaining =>
            {
                var batch = await _reportService.GetAuditAsync(targetType, targetId, behavior.BatchSize, cursor, remaining, cancellationToken);
                return (BuildAuditGrid(batch.Items, BuildTotalCount(offset, batch.Items.Count, batch.HasMore), batch.HasMore, behavior.BatchSize).Rows, batch.HasMore && !string.IsNullOrWhiteSpace(batch.NextCursor), batch.NextCursor);
            });
    }

    // Purpose: Loads batch JSON.
    private async Task<IActionResult> LoadBatchJsonAsync(
        int requestedOffset,
        NormalizedReportBehavior behavior,
        string? currentCursor,
        Func<int, Task<(IReadOnlyList<GridRow> Rows, bool HasMore, string NextCursor)>> loader)
    {
        try
        {
            var offset = Math.Clamp(requestedOffset, 0, ReportBehaviorOptions.MaximumEntriesPerReport);
            var remaining = ReportBehaviorOptions.MaximumEntriesPerReport - offset;
            var result = await loader(remaining);
            var end = offset + result.Rows.Count;
            var totalCount = result.HasMore ? ReportBehaviorOptions.MaximumEntriesPerReport : end;
            var nextCount = result.HasMore ? Math.Min(behavior.BatchSize, ReportBehaviorOptions.MaximumEntriesPerReport - end) : 0;
            return Json(new
            {
                rows = result.Rows.Select(ToClientGridRow),
                offset,
                end,
                totalCount,
                hasMore = result.HasMore && nextCount > 0,
                currentCursor = currentCursor ?? string.Empty,
                nextCursor = result.NextCursor,
                nextCount,
                batchSize = behavior.BatchSize,
                refreshedUtc = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to refresh MOVEit Records batch.");
            return StatusCode(StatusCodes.Status502BadGateway, new { message = ex.Message });
        }
    }

    private ReportPageViewModel BuildPage<T>(
        string title,
        string description,
        string contextLabel,
        string clearFilterUrl,
        GridViewModel grid,
        NormalizedReportBehavior behavior,
        string batchUrl,
        string? taskId,
        string? taskName,
        string? targetType,
        string? targetId,
        string? targetName,
        ReportBatch<T> batch,
        string? runId = null,
        string? runNominalStart = null,
        string? returnUrl = null,
        string? returnLabel = null,
        string? reportPreset = null,
        int? maximumEntries = null)
    {
        var nextCount = batch.HasMore ? Math.Min(behavior.BatchSize, ReportBehaviorOptions.MaximumEntriesPerReport - batch.Items.Count) : 0;
        return new ReportPageViewModel
        {
            Title = title,
            Description = description,
            ContextLabel = contextLabel,
            ClearFilterUrl = clearFilterUrl,
            Grid = grid,
            BatchUrl = batchUrl,
            BatchSize = behavior.BatchSize,
            UiRefreshIntervalSeconds = behavior.UiRefreshSeconds,
            MaximumEntries = maximumEntries ?? ReportBehaviorOptions.MaximumEntriesPerReport,
            CurrentOffset = 0,
            CurrentCursor = string.Empty,
            NextCursor = batch.NextCursor,
            HasMore = batch.HasMore && !string.IsNullOrWhiteSpace(batch.NextCursor) && nextCount > 0,
            NextCount = nextCount,
            TaskId = taskId ?? string.Empty,
            TaskName = taskName ?? string.Empty,
            RunId = runId ?? string.Empty,
            RunNominalStart = runNominalStart ?? string.Empty,
            TargetType = targetType ?? string.Empty,
            TargetId = targetId ?? string.Empty,
            TargetName = targetName ?? string.Empty,
            ReturnUrl = NormalizeReturnUrl(returnUrl),
            ReturnLabel = NormalizeReturnLabel(returnLabel),
            ReportPreset = reportPreset ?? string.Empty,
            LastRefreshUtc = null
        };
    }

    // Purpose: Keeps Records back-navigation within the current IIS application.
    private string NormalizeReturnUrl(string? value)
    {
        var candidate = (value ?? string.Empty).Trim();
        if (candidate.Length == 0 || candidate.Length > 2048) return string.Empty;
        return MauiApplicationReturnUrls.IsWithinApplication(candidate, Request.PathBase)
            ? candidate
            : string.Empty;
    }

    // Purpose: Normalizes return label.
    private static string NormalizeReturnLabel(string? value)
    {
        var candidate = (value ?? string.Empty).Trim();
        return candidate.Length > 80 ? candidate[..80] : candidate;
    }

    // Purpose: Builds task run grid.
    private GridViewModel BuildTaskRunGrid(IReadOnlyList<TaskRunReportRecord> records, int totalCount, bool hasMore, int batchSize)
    {
        var columns = new List<GridColumn>
        {
            new() { Key="StartTime", Label="Start Time", Type="datetime", DefaultWidth=160 }, new() { Key="TaskName", Label="Task Name", DefaultWidth=350 }, new() { Key="Duration", Label="Duration", DefaultWidth=120 },
            new() { Key="Status", Label="Status", Type="status", DefaultWidth=150 }, new() { Key="FilesSent", Label="Files Sent", Type="number", DefaultWidth=120 }, new() { Key="TotalBytesSent", Label="Total Bytes Sent", Type="number" }, new() { Key="StartedBy", Label="Started By" },
            new() { Key="TaskID", Label="Task ID" }, new() { Key="EndTime", Label="End Time", Type="datetime" }, new() { Key="ScheduledTime", Label="Scheduled Time", Type="datetime" }, new() { Key="RunID", Label="Run ID", Type="number" },
            new() { Key="Node", Label="Node", Type="number" }, new() { Key="NominalStart", Label="Nominal Start" }, new() { Key="StatusCode", Label="Status Code", Type="number" }, new() { Key="StatusMessage", Label="Status Message" }, new() { Key="LogStamp", Label="Log Time", Type="datetime" },
            new() { Key="RecordType", Label="Record Type" }, new() { Key="QueryType", Label="Query Type" }
        };
        var rows = records.Select(record => new GridRow
        {
            Id = record.RowId,
            Values = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
            {
                ["StartTime"]=FormatDate(record.StartTime), ["TaskName"]=record.TaskName, ["Duration"]=FormatDuration(record.DurationSeconds), ["Status"]=record.Status, ["FilesSent"]=record.FilesSent?.ToString(CultureInfo.InvariantCulture)??string.Empty,
                ["TotalBytesSent"]=FormatByteSize(record.TotalBytesSent), ["StartedBy"]=record.StartedBy, ["TaskID"]=record.TaskId, ["EndTime"]=FormatDate(record.EndTime), ["ScheduledTime"]=FormatDate(record.ScheduledTime), ["RunID"]=record.RunId,
                ["Node"]=record.Node, ["NominalStart"]=record.NominalStart, ["StatusCode"]=record.StatusCode, ["StatusMessage"]=record.StatusMessage, ["LogStamp"]=FormatDate(record.LogStamp), ["RecordType"]=record.RecordType, ["QueryType"]=record.QueryType
            },
            RawValues = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Selectable"]="0", ["TaskID"]=record.TaskId, ["StartTime"]=record.StartTime?.ToString("O")??string.Empty, ["Duration"]=record.DurationSeconds?.ToString(CultureInfo.InvariantCulture)??string.Empty,
                ["FilesSent"]=record.FilesSent?.ToString(CultureInfo.InvariantCulture)??string.Empty, ["TotalBytesSent"]=record.TotalBytesSent?.ToString("R",CultureInfo.InvariantCulture)??string.Empty, ["EndTime"]=record.EndTime?.ToString("O")??string.Empty,
                ["ScheduledTime"]=record.ScheduledTime?.ToString("O")??string.Empty, ["LogStamp"]=record.LogStamp?.ToString("O")??string.Empty
            }
        }).ToArray();
        return BuildRecordGrid("taskruns", "Task Runs", "Search task runs", columns, rows,
            new[] { "StartTime", "TaskName", "Duration", "Status", "FilesSent", "TotalBytesSent", "StartedBy" },
            new[] { "TaskName", "Status", "StartedBy", "StatusMessage" }, totalCount, hasMore, batchSize);
    }

    // Purpose: Builds file activity grid.
    private GridViewModel BuildFileActivityGrid(IReadOnlyList<FileActivityReportRecord> records, int totalCount, bool hasMore, int batchSize)
    {
        var columns = new List<GridColumn>
        {
            new() { Key="LogTime", Label="Log Time", Type="datetime", DefaultWidth=160 }, new() { Key="TaskName", Label="Task Name", DefaultWidth=350 }, new() { Key="SourceFilename", Label="Source Filename" }, new() { Key="Action", Label="Action", DefaultWidth=110 },
            new() { Key="Status", Label="Status", Type="status", DefaultWidth=130 }, new() { Key="Size", Label="Size", Type="number", DefaultWidth=110 }, new() { Key="SourcePath", Label="Source Path" }, new() { Key="DestinationPath", Label="Destination Path" },
            new() { Key="LogID", Label="Log ID", Type="number" }, new() { Key="TaskID", Label="Task ID" }, new() { Key="SourceHost", Label="Source Host" }, new() { Key="DestinationHost", Label="Destination Host" }, new() { Key="DestinationFilename", Label="Destination Filename" },
            new() { Key="SourceBytes", Label="Source Bytes", Type="number" }, new() { Key="DestinationBytes", Label="Destination Bytes", Type="number" }, new() { Key="TransferBytes", Label="Transfer Bytes", Type="number" },
            new() { Key="SourceDuration", Label="Source Duration", Type="number" }, new() { Key="DestinationDuration", Label="Destination Duration", Type="number" }, new() { Key="SourceFileID", Label="Source File ID" }, new() { Key="DestinationFileID", Label="Destination File ID" },
            new() { Key="SourceStamp", Label="Source Stamp" }, new() { Key="StartTime", Label="Start Time", Type="datetime" }, new() { Key="ScheduledTime", Label="Scheduled Time", Type="datetime" }, new() { Key="NominalStart", Label="Nominal Start" },
            new() { Key="Node", Label="Node", Type="number" }, new() { Key="StatusCode", Label="Status Code", Type="number" }, new() { Key="StatusMessage", Label="Status Message" }, new() { Key="SourceASxMessageID", Label="Source ASx Message ID" },
            new() { Key="SourceASxMDN", Label="Source ASx MDN" }, new() { Key="DestinationASxMessageID", Label="Destination ASx Message ID" }, new() { Key="DestinationASxMDN", Label="Destination ASx MDN" }, new() { Key="QueryType", Label="Query Type" }
        };
        var rows = records.Select(record => new GridRow
        {
            Id = record.RowId,
            Values = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
            {
                ["LogTime"]=FormatDate(record.LogTime), ["TaskName"]=record.TaskName, ["SourceFilename"]=record.SourceFile, ["Action"]=record.Action, ["Status"]=record.Status, ["Size"]=FormatByteSize(record.Size), ["SourcePath"]=record.SourcePath, ["DestinationPath"]=record.DestinationPath,
                ["LogID"]=record.LogId, ["TaskID"]=record.TaskId, ["SourceHost"]=record.SourceHost, ["DestinationHost"]=record.DestinationHost, ["DestinationFilename"]=record.DestinationFile, ["SourceBytes"]=FormatByteSize(record.SourceBytes), ["DestinationBytes"]=FormatByteSize(record.DestinationBytes), ["TransferBytes"]=FormatByteSize(record.TransferBytes),
                ["SourceDuration"]=FormatSeconds(record.SourceDuration), ["DestinationDuration"]=FormatSeconds(record.DestinationDuration), ["SourceFileID"]=record.SourceFileId, ["DestinationFileID"]=record.DestinationFileId, ["SourceStamp"]=record.SourceStamp, ["StartTime"]=FormatDate(record.StartTime), ["ScheduledTime"]=FormatDate(record.ScheduledTime),
                ["NominalStart"]=record.NominalStart, ["Node"]=record.Node, ["StatusCode"]=record.StatusCode, ["StatusMessage"]=record.StatusMessage, ["SourceASxMessageID"]=record.SourceAsxMessageId, ["SourceASxMDN"]=record.SourceAsxMdn, ["DestinationASxMessageID"]=record.DestinationAsxMessageId, ["DestinationASxMDN"]=record.DestinationAsxMdn, ["QueryType"]=record.QueryType
            },
            RawValues = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Selectable"]="0", ["TaskID"]=record.TaskId, ["LogTime"]=record.LogTime?.ToString("O")??string.Empty, ["Size"]=record.Size?.ToString("R",CultureInfo.InvariantCulture)??string.Empty, ["SourceBytes"]=record.SourceBytes?.ToString("R",CultureInfo.InvariantCulture)??string.Empty,
                ["DestinationBytes"]=record.DestinationBytes?.ToString("R",CultureInfo.InvariantCulture)??string.Empty, ["TransferBytes"]=record.TransferBytes?.ToString("R",CultureInfo.InvariantCulture)??string.Empty, ["SourceDuration"]=record.SourceDuration?.ToString("R",CultureInfo.InvariantCulture)??string.Empty,
                ["DestinationDuration"]=record.DestinationDuration?.ToString("R",CultureInfo.InvariantCulture)??string.Empty, ["StartTime"]=record.StartTime?.ToString("O")??string.Empty, ["ScheduledTime"]=record.ScheduledTime?.ToString("O")??string.Empty
            }
        }).ToArray();
        return BuildRecordGrid("fileactivity", "File Activities", "Search file activity", columns, rows,
            new[] { "LogTime", "TaskName", "SourceFilename", "Action", "Status", "Size", "SourcePath", "DestinationPath" },
            new[] { "TaskName", "SourceFilename", "Action", "Status", "SourcePath", "DestinationPath", "SourceHost", "DestinationHost", "DestinationFilename", "StatusMessage" }, totalCount, hasMore, batchSize);
    }

    // Purpose: Builds audit grid.
    private GridViewModel BuildAuditGrid(IReadOnlyList<AuditReportRecord> records, int totalCount, bool hasMore, int batchSize)
    {
        var columns = new List<GridColumn>
        {
            new() { Key="LogTime", Label="Log Time", Type="datetime", DefaultWidth=160 }, new() { Key="Action", Label="Action", DefaultWidth=150 }, new() { Key="TargetType", Label="Target Type" }, new() { Key="TargetName", Label="Target Name" },
            new() { Key="Status", Label="Status", Type="status", DefaultWidth=150 }, new() { Key="Username", Label="Username", DefaultWidth=200 }, new() { Key="IPAddress", Label="IP Address" },
            new() { Key="LogID", Label="Log ID", Type="number" }, new() { Key="TargetID", Label="Target ID" }, new() { Key="TaskName", Label="Task Name", DefaultWidth=350 }, new() { Key="ClientIP", Label="Client IP" }, new() { Key="Node", Label="Node", Type="number" },
            new() { Key="StatusCode", Label="Status Code", Type="number" }, new() { Key="StatusMessage", Label="Status Message" }, new() { Key="AgentBrand", Label="Agent Brand" }, new() { Key="AgentVersion", Label="Agent Version" }, new() { Key="CentralVersion", Label="Automation Version" },
            new() { Key="QueryType", Label="Query Type" }, new() { Key="Hash", Label="Hash" }
        };
        var rows = records.Select(record => new GridRow
        {
            Id = record.RowId,
            Values = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
            {
                ["LogTime"]=FormatDate(record.LogTime), ["Action"]=record.Action, ["TargetType"]=record.TargetType, ["TargetName"]=record.TargetName, ["Status"]=record.Status, ["Username"]=record.Username, ["IPAddress"]=record.IpAddress,
                ["LogID"]=record.LogId, ["TargetID"]=record.TargetId, ["TaskName"]=record.TaskName, ["ClientIP"]=record.ClientIp, ["Node"]=record.Node, ["StatusCode"]=record.StatusCode, ["StatusMessage"]=record.StatusMessage, ["AgentBrand"]=record.AgentBrand,
                ["AgentVersion"]=record.AgentVersion, ["CentralVersion"]=record.CentralVersion, ["QueryType"]=record.QueryType, ["Hash"]=record.Hash,
                ["HasAuditDiff"]=record.HasAuditDiff ? "1" : "0"
            },
            RawValues = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Selectable"]="0", ["LogTime"]=record.LogTime?.ToString("O")??string.Empty,
                ["TaskID"]=record.TargetType.Contains("task", StringComparison.OrdinalIgnoreCase) ? record.TargetId : string.Empty
            }
        }).ToArray();
        return BuildRecordGrid("audit", "Audit Records", "Search audit records", columns, rows,
            new[] { "LogTime", "Action", "TargetType", "TargetName", "Status", "Username", "IPAddress" },
            new[] { "Action", "TargetType", "TargetName", "Status", "Username", "IPAddress", "TargetID", "TaskName", "StatusMessage" }, totalCount, hasMore, batchSize);
    }

    // Purpose: Builds one shared grid for the Records workspace.
    private GridViewModel BuildRecordGrid(
        string gridId,
        string entityName,
        string searchPlaceholder,
        IReadOnlyList<GridColumn> columns,
        IReadOnlyList<GridRow> rows,
        IReadOnlyList<string> fallbackDefaultColumns,
        IReadOnlyList<string> fallbackSearchColumns,
        int totalCount,
        bool hasMore,
        int batchSize)
    {
        var options = _uiOptions.Grids.TryGetValue(gridId, out var configured) ? configured : new GridOptions
        {
            DefaultColumns = fallbackDefaultColumns.ToList(),
            AvailableColumns = columns.Select(column => column.Key).ToList(),
            SearchColumns = fallbackSearchColumns.ToList()
        };
        return new GridViewModel
        {
            GridId = gridId,
            EntityName = entityName,
            SearchPlaceholder = searchPlaceholder,
            Columns = columns,
            Rows = rows,
            Options = options,
            SearchColumns = options.SearchColumns,
            FitColumnsToGrid = _gridBehavior.FitColumnsToGrid.Value,
            SelectRowsBeyondGridSize = false,
            DefaultPageSize = _gridBehavior.DefaultRowSize.Value,
            PageSizeOptions = _gridBehavior.DefaultRowSize.PossibleValues,
            TotalCountOverride = totalCount,
            DisplayedCountOverride = rows.Count,
            CountActionText = hasMore ? $"Fetch Next {Math.Min(batchSize, Math.Max(0, ReportBehaviorOptions.MaximumEntriesPerReport - rows.Count))}" : string.Empty,
            EnableRowSelection = false,
            ShowBulkActions = false
        };
    }

    // Purpose: Converts the current value to client grid row.
    private static object ToClientGridRow(GridRow row) => new
    {
        id = row.Id,
        infoActionUrl = row.InfoActionUrl,
        values = row.Values,
        rawValues = row.RawValues
    };

    // Purpose: Builds total count.
    private static int BuildTotalCount(int offset, int batchCount, bool hasMore)
        => hasMore ? ReportBehaviorOptions.MaximumEntriesPerReport : Math.Min(ReportBehaviorOptions.MaximumEntriesPerReport, offset + batchCount);

    // Purpose: Normalizes behavior.
    private static NormalizedReportBehavior NormalizeBehavior(ReportTypeBehaviorOptions options, int defaultBatchSize)
    {
        var fallbackBatchSize = AllowedBatchSizes.Contains(defaultBatchSize) ? defaultBatchSize : 1000;
        var batchSize = AllowedBatchSizes.Contains(options.BatchResultLimit.Value) ? options.BatchResultLimit.Value : fallbackBatchSize;
        return new NormalizedReportBehavior(batchSize, Math.Clamp(options.UiRefreshIntervalSeconds.Value, 0, 86400));
    }

    // Purpose: Normalizes task run dashboard view.
    private static string NormalizeTaskRunDashboardView(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "failed" => "failed",
            "successful" or "success" => "successful",
            "no-transfer" or "no-transfers" or "notransfer" => "no-transfers",
            _ => string.Empty
        };
    }

    // Purpose: Returns the display label for a Task Run Dashboard filter.
    private static string TaskRunDashboardLabel(string value)
        => value switch
        {
            "failed" => "Failed — Past 24 Hours",
            "successful" => "Successful — Past 24 Hours",
            "no-transfers" => "No Transfer — Past 24 Hours",
            _ => "Past 24 Hours"
        };

    // Purpose: Normalizes file activity dashboard view.
    private static string NormalizeFileActivityDashboardView(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is "failed" or "successful" ? normalized : string.Empty;
    }

    // Purpose: Builds task context.
    private static string BuildTaskContext(string? taskId, string? taskName)
    {
        if (string.IsNullOrWhiteSpace(taskId)) return string.Empty;
        var name = string.IsNullOrWhiteSpace(taskName) ? taskId.Trim() : taskName.Trim();
        return $"Filtered for Task: {name}";
    }

    // Purpose: Builds audit context.
    private static string BuildAuditContext(string? targetType, string? targetId, string? targetName)
    {
        if (string.IsNullOrWhiteSpace(targetId)) return string.Empty;
        var name = string.IsNullOrWhiteSpace(targetName) ? targetId.Trim() : targetName.Trim();
        var type = string.IsNullOrWhiteSpace(targetType) ? "Resource" : targetType.Trim();
        return $"Filtered for {type}: {name}";
    }

    // Purpose: Formats date.
    private static string FormatDate(DateTimeOffset? value) => value?.LocalDateTime.ToString("M/d/yyyy h:mm:ss tt", CultureInfo.CurrentCulture) ?? string.Empty;

    // Purpose: Formats duration.
    private static string FormatDuration(long? seconds)
    {
        if (!seconds.HasValue || seconds.Value < 0) return string.Empty;
        var span = TimeSpan.FromSeconds(seconds.Value);
        return span.TotalHours >= 1 ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}" : $"{(int)span.TotalMinutes}:{span.Seconds:00}";
    }

    // Purpose: Formats seconds.
    private static string FormatSeconds(double? seconds) => seconds.HasValue ? $"{seconds.Value:0.###} sec" : string.Empty;

    // Purpose: Formats byte size.
    private static string FormatByteSize(double? bytes)
    {
        if (!bytes.HasValue || bytes.Value < 0) return string.Empty;
        var value = bytes.Value;
        var units = new[] { "B", "KB", "MB", "GB", "TB", "PB" };
        var index = 0;
        while (value >= 1024d && index < units.Length - 1) { value /= 1024d; index++; }
        return index == 0 ? $"{value:0} {units[index]}" : $"{value:0.##} {units[index]}";
    }

    private sealed record NormalizedReportBehavior(int BatchSize, int UiRefreshSeconds);
}

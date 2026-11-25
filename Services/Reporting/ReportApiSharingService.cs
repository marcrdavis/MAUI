using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.Security;

namespace MoveItAutomationUI.Services.Reporting;

public sealed class ReportApiConflictException : Exception
{
    public ReportApiConflictException() : base("API access changed. Reopen the Report and try again.") { }
}

public sealed class ReportApiBusyException : Exception { }
public sealed class ReportDataUnavailableException(string message) : InvalidOperationException(message) { }

public sealed class ReportApiSharingService(
    MetadataDbContext db, IDataProtectionProvider protection, IOutboundReportService reports,
    IOutboundReportRepository repository, IMauiSecurityService security, ReportApiRuntime runtime,
    ILogger<ReportApiSharingService> logger)
{
    private readonly IDataProtector _protector = protection.CreateProtector("MAUI.ReportingAPI.ShareGuid.v1");
    private readonly IDataProtector _draftProtector = protection.CreateProtector("MAUI.ReportingAPI.LinkDraft.v1");

    public ReportApiAccessState PrepareCreateLink(string username, string environment)
    {
        var guid = Guid.NewGuid().ToString("D");
        var draft = _draftProtector.Protect(System.Text.Json.JsonSerializer.Serialize(new[] {
            CreateDraftOwner(username), MoveItEnvironmentKey.Normalize(environment), guid }));
        return new ReportApiAccessState(false, "", "/api/reports/" + guid + "/csv", null, draft);
    }

    // Save the definition and its exact prepared link together. Email is an independent
    // operation after commit, so a delivery failure cannot hide an already-saved Report.
    public async Task<CreateOutboundReportResult> CreateAsync(
        CreateOutboundReportRequest request, string username, string environment, CancellationToken token)
    {
        var access = request.ApiAccess ?? throw new ArgumentException("API access details are required.");
        if (!access.Enabled) return await reports.CreateAsync(request, token);
        if (!string.IsNullOrEmpty(access.Revision) || string.IsNullOrEmpty(access.LinkDraft))
            throw new ArgumentException("Prepare the API link before saving the Report.");
        var guid = ReadDraft(access.LinkDraft, CreateDraftOwner(username), MoveItEnvironmentKey.Normalize(environment));
        var hash = Hash(guid);
        if (await db.OutboundReportApiShares.AnyAsync(s => s.ShareGuidHash == hash, token))
            throw new ArgumentException("This API link has already been saved. Regenerate the link before creating another Report.");
        var sendNow = string.Equals(request.Schedule?.DeliveryMode, OutboundReportDeliveryModes.Now, StringComparison.OrdinalIgnoreCase);
        if (sendNow)
        {
            _ = OutboundReportRepository.NormalizeRecipients(request.Recipients);
            if (request.Snapshot is null) throw new ArgumentException("The current grid snapshot is required when sending a report now.");
        }
        var definition = sendNow ? new CreateOutboundReportRequest {
            GridKey = request.GridKey, Title = request.Title, Description = request.Description,
            ViewState = request.ViewState, SourceContext = request.SourceContext,
            Schedule = new() { DeliveryMode = OutboundReportDeliveryModes.None }
        } : request;
        CreateOutboundReportResult saved;
        await using (var transaction = await db.Database.BeginTransactionAsync(token))
        {
            saved = await reports.CreateAsync(definition, token);
            if (!saved.Saved || string.IsNullOrEmpty(saved.Id))
                throw new InvalidOperationException("The saved Report could not be confirmed.");
            var share = new OutboundReportApiShareEntity {
                ReportId = saved.Id, Enabled = true, CreatedUtc = DateTimeOffset.UtcNow
            };
            NewLink(share, guid);
            db.OutboundReportApiShares.Add(share);
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
        }
        if (!sendNow) return saved;
        try
        {
            var sent = await reports.CreateAsync(request, token);
            return saved with { Sent = sent.Sent, RowCount = sent.RowCount, Message = "Report saved and sent. API access is enabled." };
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.Net.Mail.SmtpException)
        {
            logger.LogWarning(ex, "Report {ReportId} was saved with API access, but immediate email delivery failed.", saved.Id);
            return saved with { Warning = "API access is enabled, but email delivery failed: " + ex.Message };
        }
    }

    private static string CreateDraftOwner(string username) => "create:" + username.Trim().ToUpperInvariant();

    public async Task<ReportApiAccessState> GetAsync(string id, string username, bool admin, CancellationToken token)
    {
        var report = await AuthorizedReportAsync(id, username, admin, token);
        var share = await db.OutboundReportApiShares.AsNoTracking().SingleOrDefaultAsync(s => s.ReportId == report.Id, token);
        return State(share);
    }

    // Preparing a link does not write sharing state or make its public URL usable.
    // The protected draft binds the exact URL to this Report and its saved revision.
    public async Task<ReportApiAccessState> PrepareLinkAsync(
        RegenerateReportApiRequest request, string username, bool admin, CancellationToken token)
    {
        var report = await AuthorizedReportAsync(request.Id, username, admin, token);
        var share = await db.OutboundReportApiShares.AsNoTracking().SingleOrDefaultAsync(s => s.ReportId == report.Id, token);
        CheckRevision(share, request.Revision);
        var guid = Guid.NewGuid().ToString("D");
        var draft = _draftProtector.Protect(System.Text.Json.JsonSerializer.Serialize(new[] { report.Id, request.Revision, guid }));
        return new ReportApiAccessState(share?.Enabled == true, share?.Revision ?? "", "/api/reports/" + guid + "/csv",
            share?.LastAccessUtc, draft);
    }

    public async Task<OutboundReportSummary> UpdateAsync(
        UpdateOutboundReportRequest request, string username, bool admin, CancellationToken token)
    {
        var report = await AuthorizedReportAsync(request.Id, username, admin, token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var share = await db.OutboundReportApiShares.SingleOrDefaultAsync(s => s.ReportId == report.Id, token);
        var access = request.ApiAccess ?? throw new ArgumentException("API access details are required.");
        CheckRevision(share, access.Revision);
        var preparedGuid = access.Enabled && access.LinkDraft is not null
            ? ReadDraft(access.LinkDraft, report.Id, access.Revision) : null;
        var result = await reports.UpdateAsync(request, username, admin, token);
        if (share is null && access.Enabled)
        {
            share = new OutboundReportApiShareEntity { ReportId = report.Id, CreatedUtc = DateTimeOffset.UtcNow };
            NewLink(share, preparedGuid);
            db.OutboundReportApiShares.Add(share);
        }
        else if (share is not null && preparedGuid is not null) NewLink(share, preparedGuid);
        if (share is not null)
        {
            share.Enabled = access.Enabled;
            share.Revision = Guid.NewGuid().ToString("D");
            share.ModifiedUtc = DateTimeOffset.UtcNow;
        }
        try
        {
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
        }
        catch (DbUpdateException) { throw new ReportApiConflictException(); }
        return result;
    }

    public async Task<ReportApiAccessState> RegenerateAsync(
        RegenerateReportApiRequest request, string username, bool admin, CancellationToken token)
    {
        var report = await AuthorizedReportAsync(request.Id, username, admin, token);
        var share = await db.OutboundReportApiShares.SingleOrDefaultAsync(s => s.ReportId == report.Id, token);
        CheckRevision(share, request.Revision);
        if (share is null || !share.Enabled) throw new ArgumentException("Save API access before regenerating its link.");
        NewLink(share);
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException) { throw new ReportApiConflictException(); }
        return State(share);
    }

    public async Task<ReportApiCsv?> CsvAsync(Guid publicGuid, bool head, CancellationToken cancellationToken)
    {
        var hash = Hash(publicGuid.ToString("D"));
        var share = await db.OutboundReportApiShares.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Enabled && s.ShareGuidHash == hash, cancellationToken);
        if (share is null) return null;
        var report = await db.OutboundReports.AsNoTracking().SingleOrDefaultAsync(r => r.Id == share.ReportId, cancellationToken);
        if (report is null || !OutboundReportSourcePolicy.IsAllowed(report.GridKey, security.IsAdmin(report.OwnerUsername)))
            return null;
        var definition = DefinitionRevision(report);
        if (head) return new ReportApiCsv([], DateTimeOffset.UtcNow, 0, definition, null, false);
        var key = report.Id + ":" + share.Revision + ":" + definition;
        if (runtime.TryGet(key, out var cached) && cached is not null)
        {
            if (!await StillCurrentAsync(share, definition, cancellationToken)) return null;
            await AccessedAsync(report.Id, cancellationToken);
            return cached;
        }
        if (!runtime.TryBegin(report.Id)) throw new ReportApiBusyException();
        var started = DateTimeOffset.UtcNow;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        try
        {
            var data = await reports.GenerateAsync(report, timeout.Token);
            var csv = new ReportApiCsv(Encoding.UTF8.GetBytes(ReportCsvWriter.Write(data)),
                DateTimeOffset.UtcNow, data.Rows.Count, definition, data.RetrievalLimit, data.SourceLimitReached);
            if (!await StillCurrentAsync(share, definition, timeout.Token)) return null;
            await repository.RecordExecutionAsync(report, started,
                new OutboundReportExecutionResult(true, data.Rows.Count, "API CSV generated.", csv.GeneratedUtc),
                report.NextRunUtc, report.Enabled, 0, updateScheduleState: false, cancellationToken: timeout.Token);
            // Recheck after the history write because link changes may have completed while saving it.
            if (!await StillCurrentAsync(share, definition, timeout.Token)) return null;
            await AccessedAsync(report.Id, timeout.Token);
            runtime.Store(key, csv);
            return csv;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "API CSV generation failed for Report {ReportId}.", report.Id);
            await repository.RecordExecutionAsync(report, started,
                new OutboundReportExecutionResult(false, 0, "API CSV generation failed.", DateTimeOffset.UtcNow),
                report.NextRunUtc, report.Enabled, 0, updateScheduleState: false, cancellationToken: cancellationToken);
            throw;
        }
        finally { runtime.End(report.Id); }
    }

    private async Task<OutboundReportEntity> AuthorizedReportAsync(string id, string username, bool admin, CancellationToken token)
    {
        var report = await repository.GetAsync(id, username, admin, token)
            ?? throw new ArgumentException("The Report no longer exists or you do not have access to it.");
        OutboundReportSourcePolicy.EnsureAllowed(report.GridKey, admin);
        return report;
    }

    private async Task<bool> StillCurrentAsync(OutboundReportApiShareEntity share, string definition, CancellationToken token)
    {
        if (!await db.OutboundReportApiShares.AsNoTracking().AnyAsync(
            s => s.ReportId == share.ReportId && s.Enabled && s.Revision == share.Revision, token)) return false;
        var current = await db.OutboundReports.AsNoTracking().SingleOrDefaultAsync(r => r.Id == share.ReportId, token);
        return current is not null && DefinitionRevision(current) == definition
            && OutboundReportSourcePolicy.IsAllowed(current.GridKey, security.IsAdmin(current.OwnerUsername));
    }

    private Task<int> AccessedAsync(string id, CancellationToken token)
    {
        logger.LogInformation("API CSV accessed for Report {ReportId}.", id);
        var now = (DateTimeOffset?)DateTimeOffset.UtcNow;
        return db.OutboundReportApiShares.Where(s => s.ReportId == id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.LastAccessUtc, now), token);
    }

    private ReportApiAccessState State(OutboundReportApiShareEntity? share)
    {
        string? path = null;
        if (share?.Enabled == true)
        {
            try { path = "/api/reports/" + _protector.Unprotect(share.ProtectedShareGuid) + "/csv"; }
            catch (CryptographicException) { /* Regenerate Link restores copying after lost protection keys. */ }
        }
        return new ReportApiAccessState(share?.Enabled == true, share?.Revision ?? "", path, share?.LastAccessUtc);
    }

    private string ReadDraft(string draft, string reportId, string revision)
    {
        try
        {
            var parts = System.Text.Json.JsonSerializer.Deserialize<string[]>(_draftProtector.Unprotect(draft));
            if (parts is { Length: 3 } && parts[0] == reportId && parts[1] == revision
                && Guid.TryParseExact(parts[2], "D", out var guid)) return guid.ToString("D");
        }
        catch (Exception ex) when (ex is CryptographicException or System.Text.Json.JsonException or FormatException) { }
        throw new ArgumentException("The prepared API link is invalid. Regenerate the link and save again.");
    }

    private void NewLink(OutboundReportApiShareEntity share, string? preparedGuid = null)
    {
        var guid = preparedGuid ?? Guid.NewGuid().ToString("D");
        share.ShareGuidHash = Hash(guid);
        share.ProtectedShareGuid = _protector.Protect(guid);
        share.Revision = Guid.NewGuid().ToString("D");
        share.ModifiedUtc = DateTimeOffset.UtcNow;
    }

    private static void CheckRevision(OutboundReportApiShareEntity? share, string revision)
    {
        if (!string.Equals(share?.Revision ?? "", revision, StringComparison.Ordinal))
            throw new ReportApiConflictException();
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static string DefinitionRevision(OutboundReportEntity report)
        => Hash(System.Text.Json.JsonSerializer.Serialize(new[] { report.EnvironmentKey, report.OwnerUsername,
            report.GridKey, report.Title, report.Description, report.ViewStateJson, report.SourceContextJson }));
}

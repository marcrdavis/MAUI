// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Performs immediate SMTP credential operations that intentionally remain outside the common Settings save.

using System.Text.Json.Nodes;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Configuration;

namespace MoveItAutomationUI.Services.Reporting;

public interface IReportConfigurationStore
{
    Task<string> ClearSmtpPasswordAsync(CancellationToken cancellationToken = default);
}

public sealed class ReportConfigurationStore : IReportConfigurationStore
{
    private readonly IAppSettingsDocumentStore _appSettings;
    private readonly AppSettingsWriteCoordinator _writeCoordinator;
    private readonly IAppSettingsConfigurationSnapshotReader _snapshots;
    private readonly ILogger<ReportConfigurationStore> _logger;

    public ReportConfigurationStore(
        IAppSettingsDocumentStore appSettings,
        AppSettingsWriteCoordinator writeCoordinator,
        IAppSettingsConfigurationSnapshotReader snapshots,
        ILogger<ReportConfigurationStore> logger)
    {
        _appSettings = appSettings;
        _writeCoordinator = writeCoordinator;
        _snapshots = snapshots;
        _logger = logger;
    }


    // Clears only the saved SMTP password. The remaining SMTP server settings are preserved.
    public async Task<string> ClearSmtpPasswordAsync(CancellationToken cancellationToken = default)
    {
        await _writeCoordinator.Gate.WaitAsync(cancellationToken);
        try
        {
            var root = await _appSettings.ReadLiveAsync(cancellationToken);
            var templateRoot = _appSettings.ReadTemplate();
            var smtp = AppSettingsJson.GetOrCreateSection(root, SmtpOptions.SectionName, templateRoot);

            smtp[nameof(SmtpOptions.ProtectedPassword)] = string.Empty;
            await _appSettings.WriteLiveAsync(root, cancellationToken);
            _logger.LogInformation("MAUI SMTP password was cleared.");
            return _snapshots.GetRevision(root);
        }
        finally
        {
            _writeCoordinator.Gate.Release();
        }
    }

}

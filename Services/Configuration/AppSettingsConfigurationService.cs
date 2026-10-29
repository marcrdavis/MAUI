// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Coordinates Configuration > Settings reads, validation, mutation, and persistence.

using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Security;

namespace MoveItAutomationUI.Services.Configuration;

public interface IAppSettingsConfigurationService
{
    // Purpose: Returns the current editable MAUI configuration settings.
    ConfigurationSettingsViewModel GetSettings();

    // Purpose: Validates and saves editable MAUI configuration plus database-backed Security settings.
    Task<ConfigurationSaveResult> SaveAsync(
        ConfigurationSettingsSaveRequest request,
        string modifiedBy,
        CancellationToken cancellationToken = default);
}

public sealed class ConfigurationRevisionConflictException : Exception
{
    public ConfigurationRevisionConflictException()
        : base("Configuration changed since this Settings page was opened. Reload Settings and review the current values before saving; no changes were written.") { }
}

public sealed class AppSettingsConfigurationService : IAppSettingsConfigurationService
{
    private readonly IConfigurationSettingsViewModelBuilder _viewModelBuilder;
    private readonly IConfigurationSettingsValidator _validator;
    private readonly IConfigurationSettingsMutator _mutator;
    private readonly IMetadataFieldDeletionService _metadataFieldDeletionService;
    private readonly IConfigurationRestartDetector _restartDetector;
    private readonly IAppSettingsDocumentStore _appSettings;
    private readonly AppSettingsWriteCoordinator _writeCoordinator;
    private readonly IMauiSecurityService _securityService;
    private readonly IAppSettingsConfigurationSnapshotReader _snapshots;
    private readonly ILogger<AppSettingsConfigurationService> _logger;

    // Purpose: Initializes the Configuration > Settings coordinator with focused infrastructure services.
    public AppSettingsConfigurationService(
        IConfigurationSettingsViewModelBuilder viewModelBuilder,
        IConfigurationSettingsValidator validator,
        IConfigurationSettingsMutator mutator,
        IMetadataFieldDeletionService metadataFieldDeletionService,
        IConfigurationRestartDetector restartDetector,
        IAppSettingsDocumentStore appSettings,
        AppSettingsWriteCoordinator writeCoordinator,
        IMauiSecurityService securityService,
        IAppSettingsConfigurationSnapshotReader snapshots,
        ILogger<AppSettingsConfigurationService> logger)
    {
        _viewModelBuilder = viewModelBuilder;
        _validator = validator;
        _mutator = mutator;
        _metadataFieldDeletionService = metadataFieldDeletionService;
        _restartDetector = restartDetector;
        _appSettings = appSettings;
        _writeCoordinator = writeCoordinator;
        _securityService = securityService;
        _snapshots = snapshots;
        _logger = logger;
    }

    // Purpose: Returns the current editable MAUI configuration settings.
    public ConfigurationSettingsViewModel GetSettings() => _viewModelBuilder.Build();

    // Purpose: Validates and saves editable MAUI configuration plus database-backed Security settings.
    public async Task<ConfigurationSaveResult> SaveAsync(
        ConfigurationSettingsSaveRequest request,
        string modifiedBy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        modifiedBy = string.IsNullOrWhiteSpace(modifiedBy) ? "unknown" : modifiedBy.Trim();

        try
        {
            await _writeCoordinator.Gate.WaitAsync(cancellationToken);
            try
            {
                var root = await _appSettings.ReadLiveAsync(cancellationToken);
                var snapshot = _snapshots.FromDocument(root);
                if (string.IsNullOrWhiteSpace(request.ConfigurationRevision)
                    || !string.Equals(request.ConfigurationRevision, snapshot.Revision, StringComparison.Ordinal))
                    throw new ConfigurationRevisionConflictException();

                // Validate against the SAME configuration revision that will be mutated.
                await _validator.ValidateAndNormalizeAsync(request, snapshot.Configuration, cancellationToken);
                var originalRoot = root.DeepClone().AsObject();
                var metadataDeletionPlan = _metadataFieldDeletionService.Prepare(originalRoot, request, modifiedBy);
                var mutation = _mutator.Apply(root, request);
                var smtpSection = AppSettingsJson.RequiredSection(root, SmtpOptions.SectionName);
                var smtpProtectedPassword = smtpSection[nameof(SmtpOptions.ProtectedPassword)]?.GetValue<string>() ?? string.Empty;
                MetadataFieldDeletionExecution? metadataDeletionExecution = null;

                await _appSettings.WriteLiveAsync(root, cancellationToken);
                try
                {
                    // Persist destructive Metadata field cleanup only after the replacement appsettings document
                    // is safely written. A recovery snapshot is captured before any stored values are removed.
                    metadataDeletionExecution = await _metadataFieldDeletionService.ApplyAsync(metadataDeletionPlan, cancellationToken);

                    await _securityService.SaveUsersAsync(
                        request.SecurityUsers,
                        request.SecurityOriginalUsernames,
                        modifiedBy,
                        cancellationToken);
                }
                catch
                {
                    // Database-backed Settings operations must not leave appsettings.json partially committed.
                    // Restore both the file and any metadata values deleted by this save attempt.
                    try
                    {
                        await _appSettings.WriteLiveAsync(originalRoot, CancellationToken.None);
                    }
                    catch (Exception rollbackException)
                    {
                        _logger.LogCritical(rollbackException,
                            "MAUI Settings persistence failed and appsettings.json rollback also failed. Manual configuration review is required.");
                    }

                    if (metadataDeletionExecution is not null)
                    {
                        try
                        {
                            await _metadataFieldDeletionService.RollbackAsync(metadataDeletionExecution, CancellationToken.None);
                        }
                        catch (Exception rollbackException)
                        {
                            _logger.LogCritical(rollbackException,
                                "MAUI Settings persistence failed and deleted Metadata values could not be restored automatically. Manual metadata review is required.");
                        }
                    }

                    throw;
                }

                _logger.LogInformation(
                    "MAUI application configuration, SMTP configuration, database-backed Security settings, and {DeletedMetadataFieldCount} Metadata field deletion(s) were updated successfully.",
                    metadataDeletionExecution?.Plan.Fields.Count ?? 0);

                var reasons = _restartDetector.GetRestartReasons(mutation.MoveIt, mutation.Database);
                return new ConfigurationSaveResult
                {
                    RestartRequired = reasons.Count > 0,
                    RestartReasons = reasons,
                    SmtpPasswordConfigured = !string.IsNullOrWhiteSpace(smtpProtectedPassword),
                    ClearedDataAccessServerKeys = mutation.ClearedDataAccessServerKeys,
                    ConfigurationRevision = _snapshots.GetRevision(root)
                };
            }
            finally
            {
                _writeCoordinator.Gate.Release();
            }
        }
        finally
        {
            // Never retain a plaintext SMTP password on the request object beyond this save attempt.
            if (request.Smtp is not null) request.Smtp.Password = string.Empty;
        }
    }
}

// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// First-run configuration workflow for the minimum settings required to bring MAUI online.

using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.Upgrade;

namespace MoveItAutomationUI.Services.Configuration;

public interface IInitialSetupService
{
    // Purpose: Builds the current Initial Setup page model from template and startup configuration.
    InitialSetupViewModel GetViewModel();
    // Purpose: Tests database without changing the active runtime configuration.
    Task<DatabaseConnectionTestResult> TestDatabaseAsync(SetupDatabaseRequest request, CancellationToken cancellationToken = default);
    // Purpose: Tests MOVEit without changing the active runtime configuration.
    Task<string> TestMoveItAsync(SetupMoveItRequest request, CancellationToken cancellationToken = default);
    // Purpose: Provisions the metadata database and updates the pre-created installation configuration.
    Task<InitialSetupCompletionResult> CompleteAsync(InitialSetupRequest request, CancellationToken cancellationToken = default);
}

public sealed class InitialSetupService : IInitialSetupService
{
    private readonly IConfiguration _configuration;
    private readonly IDatabaseConnectionTester _databaseTester;
    private readonly IAppSettingsDocumentStore _appSettings;
    private readonly AppSettingsWriteCoordinator _writeCoordinator;
    private readonly MauiSetupState _setupState;
    private readonly ConfigurationStartupState _runtimeConfiguration;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMauiUpgradeManifestProvider _upgradeManifestProvider;
    private readonly IMetadataDatabaseProvisioner _databaseProvisioner;
    private readonly IInitialTagSeedService _initialTagSeedService;
    private readonly ILogger<InitialSetupService> _logger;

    // Purpose: Initializes initial setup service with its required dependencies.
    public InitialSetupService(
        IConfiguration configuration,
        IDatabaseConnectionTester databaseTester,
        IAppSettingsDocumentStore appSettings,
        AppSettingsWriteCoordinator writeCoordinator,
        MauiSetupState setupState,
        ConfigurationStartupState runtimeConfiguration,
        IServiceScopeFactory scopeFactory,
        IMauiUpgradeManifestProvider upgradeManifestProvider,
        IMetadataDatabaseProvisioner databaseProvisioner,
        IInitialTagSeedService initialTagSeedService,
        ILogger<InitialSetupService> logger)
    {
        _configuration = configuration;
        _databaseTester = databaseTester;
        _appSettings = appSettings;
        _writeCoordinator = writeCoordinator;
        _setupState = setupState;
        _runtimeConfiguration = runtimeConfiguration;
        _scopeFactory = scopeFactory;
        _upgradeManifestProvider = upgradeManifestProvider;
        _databaseProvisioner = databaseProvisioner;
        _initialTagSeedService = initialTagSeedService;
        _logger = logger;
    }

    // Purpose: Builds the current Initial Setup page model from template and startup configuration.
    public InitialSetupViewModel GetViewModel()
    {
        var database = _configuration.GetSection(MetadataDatabaseOptions.SectionName).Get<MetadataDatabaseOptions>() ?? new MetadataDatabaseOptions();
        var moveIt = _configuration.GetSection(MoveItOptions.SectionName).Get<MoveItOptions>() ?? new MoveItOptions();
        var setupUi = ReadSetupUiOptions();
        var provider = MetadataDatabaseProvider.Normalize(database.Provider.Value);
        var sqliteFile = @"Data\maui.db";

        if (provider.Equals(SetupDatabaseProviders.SQLite, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var builder = new SqliteConnectionStringBuilder(database.ConnectionString.Value);
                if (!string.IsNullOrWhiteSpace(builder.DataSource)) sqliteFile = builder.DataSource;
            }
            catch
            {
                // Keep the known-safe default on a malformed first-run value. Completion validation still
                // rejects malformed configuration rather than silently accepting it.
            }
        }

        return new InitialSetupViewModel
        {
            ApplicationName = setupUi.ApplicationName,
            ApplicationLogo = setupUi.ApplicationLogo,
            ApplicationVersion = setupUi.Version,
            DatabaseProvider = provider,
            SqliteDatabaseFile = sqliteFile,
            AllowInvalidServerCertificates = moveIt.AllowInvalidServerCertificates.Value,
            TargetSchemaVersion = _upgradeManifestProvider.Manifest.RequiredSchemaVersion
        };
    }

    // Initial Setup runs before appsettings.json is configured, so its branding/version must come
    // from the shipped template rather than the option defaults compiled into UiOptions.
    private UiOptions ReadSetupUiOptions()
    {
        var fallback = _configuration.GetSection(UiOptions.SectionName).Get<UiOptions>() ?? new UiOptions();
        if (!File.Exists(_appSettings.TemplatePath)) return fallback;

        try
        {
            var root = _appSettings.ReadTemplate();
            return root[UiOptions.SectionName]?.Deserialize<UiOptions>() ?? fallback;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Unable to read Setup branding/version from appsettings.template.json. Falling back to configured UI defaults.");
            return fallback;
        }
    }

    // Purpose: Tests database without changing the active runtime configuration.
    public async Task<DatabaseConnectionTestResult> TestDatabaseAsync(
        SetupDatabaseRequest request,
        CancellationToken cancellationToken = default)
    {
        var (provider, connectionString) = BuildDatabaseConfiguration(request);
        var result = await _databaseTester.TestAsync(provider, connectionString, cancellationToken);
        MetadataDatabaseTargetGuard.EnsureSafeForMauiInitialization(result);
        return result;
    }

    // Purpose: Tests MOVEit without changing the active runtime configuration.
    public async Task<string> TestMoveItAsync(
        SetupMoveItRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateMoveIt(request);
        using var handler = new HttpClientHandler();
        if (request.AllowInvalidServerCertificates)
        {
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        var baseUrl = request.WebAdminUrl.Trim().TrimEnd('/');
        var probeUrl = $"{baseUrl}/api/v0/info";
        using var response = await client.GetAsync(probeUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        // /api/v0/info is an existing MAUI integration endpoint. Authentication is intentionally not
        // available during OOB setup, so 401/403 are valid connectivity results; 404 and 5xx indicate
        // that the configured base URL does not expose a usable MOVEit Automation API endpoint.
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound || (int)response.StatusCode >= 500)
        {
            throw new InvalidOperationException(
                $"The configured MOVEit API endpoint responded with HTTP {(int)response.StatusCode} ({response.StatusCode}). Verify the Web Admin URL.");
        }

        VerifyMoveItComApiInstallation();
        return "Connection successful. MOVEit Automation COM API detected and activatable.";
    }

    // Purpose: Verifies the local MOVEit Automation Windows COM API installation without MOVEit credentials.
    // This checks the same ProgID used by MAUI's COM-backed features and creates/releases the COM object,
    // which catches missing registration, process-bitness mismatches, and activation failures before Setup completes.
    private void VerifyMoveItComApiInstallation()
    {
        const string progId = "MICentralAPICOM.MICentralAPI";
        if (!OperatingSystem.IsWindows())
        {
            throw new InvalidOperationException(
                "The MOVEit Automation Windows COM API can only be validated on Windows. MAUI must be hosted on a Windows server with the MOVEit Automation API components installed.");
        }

        object? comObject = null;
        try
        {
            var apiType = Type.GetTypeFromProgID(progId);
            if (apiType is null)
            {
                throw new InvalidOperationException(
                    $"MOVEit Web Admin is reachable, but the MOVEit Automation Windows COM API ({progId}) is not registered on this MAUI server. Install/register the MOVEit Automation API components and run the server test again.");
            }

            comObject = Activator.CreateInstance(apiType);
            if (comObject is null)
            {
                throw new InvalidOperationException(
                    $"MOVEit Web Admin is reachable and {progId} is registered, but MAUI could not create the COM API object. Verify the MOVEit Automation API installation and IIS application-pool architecture.");
            }

            _logger.LogInformation("Initial Setup verified local MOVEit Automation COM API registration and activation using ProgID {ProgId}.", progId);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (COMException ex)
        {
            _logger.LogWarning(ex, "Initial Setup could not activate the MOVEit Automation COM API using ProgID {ProgId}. HRESULT=0x{HResult:X8}.", progId, ex.HResult);
            throw new InvalidOperationException(
                $"MOVEit Web Admin is reachable, but the MOVEit Automation Windows COM API could not be activated (COM 0x{ex.HResult:X8}). Verify that the MOVEit Automation API components are installed/registered and match the IIS application-pool architecture.", ex);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Initial Setup could not verify the MOVEit Automation COM API using ProgID {ProgId}.", progId);
            throw new InvalidOperationException(
                $"MOVEit Web Admin is reachable, but MAUI could not verify the MOVEit Automation Windows COM API ({progId}): {ex.Message}", ex);
        }
        finally
        {
            if (comObject is not null && Marshal.IsComObject(comObject))
            {
                try { Marshal.FinalReleaseComObject(comObject); }
                catch (Exception ex) { _logger.LogDebug(ex, "Initial Setup could not release the MOVEit Automation COM API test object cleanly."); }
            }
        }
    }

    // Purpose: Provisions the metadata database and updates the pre-created installation configuration.
    public async Task<InitialSetupCompletionResult> CompleteAsync(
        InitialSetupRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_setupState.IsConfiguredAtStartup)
            throw new InvalidOperationException("Initial setup has already been completed for this MAUI installation.");

        ValidateMoveIt(request.MoveIt);
        var environmentKey = MoveItEnvironmentKey.Normalize(request.MoveIt.EnvironmentKey);
        var (databaseProvider, databaseConnectionString) = BuildDatabaseConfiguration(request.Database);

        // Re-run both validations on completion. Client-side Test buttons are convenience only and are
        // never trusted as proof that the final submitted values are valid.
        var databaseTest = await _databaseTester.TestAsync(databaseProvider, databaseConnectionString, cancellationToken);
        MetadataDatabaseTargetGuard.EnsureSafeForMauiInitialization(databaseTest);
        await TestMoveItAsync(request.MoveIt, cancellationToken);

        var serverKey = GenerateServerKey();
        await _writeCoordinator.Gate.WaitAsync(cancellationToken);
        try
        {
            // Serialize completion so two concurrent first-run requests cannot both initialize the same
            // database or overwrite each other's generated server key/configuration.
            if (_setupState.IsConfiguredAtStartup)
                throw new InvalidOperationException("Initial setup has already been completed for this MAUI installation.");

            // A restricted IIS deployment must provision appsettings.json as a valid empty JSON
            // object ({}) before first startup. Validate existence, write permission, and the
            // release template BEFORE making any database/schema/Tag changes. Existing manual
            // settings are overlaid on the template without discarding administrator values.
            var root = await _appSettings.ReadSeedAsync(cancellationToken);

            // Database provisioning follows only after configuration-file preflight succeeds.
            // It uses the same cumulative upgrade.json migrations as subsequent upgrades.
            await _databaseProvisioner.ProvisionRequiredSchemaAsync(databaseProvider, databaseConnectionString, cancellationToken);
            await _initialTagSeedService.SeedDefaultsIfPristineAsync(
                databaseProvider,
                databaseConnectionString,
                environmentKey,
                cancellationToken);

            var moveIt = root[MoveItOptions.SectionName]?.AsObject()
                ?? throw new InvalidOperationException("The MoveIt configuration section is missing.");
            moveIt["Servers"] = JsonSerializer.SerializeToNode(new[]
            {
                new MoveItServerDefinition
                {
                    Key = serverKey,
                    EnvironmentKey = environmentKey,
                    DisplayName = request.MoveIt.DisplayName.Trim(),
                    WebAdminUrl = request.MoveIt.WebAdminUrl.Trim().TrimEnd('/'),
                    ServerHost = string.Empty
                }
            });
            AppSettingsJson.SetSettingValue(root, MoveItOptions.SectionName, "AllowInvalidServerCertificates", request.MoveIt.AllowInvalidServerCertificates);

            AppSettingsJson.SetSettingValue(root, MetadataDatabaseOptions.SectionName, "Provider", databaseProvider);
            AppSettingsJson.SetSettingValue(root, MetadataDatabaseOptions.SectionName, "ConnectionString", databaseConnectionString);

            var ui = AppSettingsJson.RequiredSection(root, UiOptions.SectionName);
            ui["DefaultServer"] = serverKey;

            root["Setup"] = new JsonObject
            {
                ["Completed"] = true,
                ["CompletedUtc"] = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
            };

            await _appSettings.WriteLiveAsync(root, cancellationToken);

            // Activate the freshly persisted installation in the same IIS process. Database and TLS
            // settings become the effective startup-sensitive baseline exactly once, during Setup.
            // Ordinary Settings changes still require restart when they change these values.
            // Activation must complete after the durable write, even if the browser disconnects.
            // If activation fails, keep Setup gated and allow the administrator to retry in-process.
            try
            {
                _runtimeConfiguration.ActivateAfterInitialSetup(_configuration);

                // Reuse startup upgrade inspection with a NEW scope, so EF Core binds to the
                // newly activated metadata database rather than the initial unconfigured options.
                using var scope = _scopeFactory.CreateScope();
                var startup = scope.ServiceProvider.GetRequiredService<MauiUpgradeStartupService>();
                await startup.InitializeAsync(CancellationToken.None, afterInitialSetup: true);

                // Last step: open the Setup gate. Concurrent requests cannot reach Login or
                // scheduler work before configuration and upgrade/database state are ready.
                _setupState.MarkConfigured();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Initial Setup saved appsettings.json, but in-process runtime activation failed.");
                throw new InvalidOperationException(
                    "Setup configuration was saved, but runtime initialization did not complete. Retry Complete Setup; an IIS restart should not be necessary.", ex);
            }
            _logger.LogInformation(
                "Initial MAUI setup activated without IIS restart. MOVEit server key {ServerKey}, environment {EnvironmentKey}, database provider {DatabaseProvider}.",
                serverKey,
                environmentKey,
                databaseProvider);
        }
        finally
        {
            _writeCoordinator.Gate.Release();
        }

        return new InitialSetupCompletionResult
        {
            ServerKey = serverKey,
            EnvironmentKey = environmentKey,
            DatabaseProvider = databaseProvider,
            RestartRequired = false
        };
    }


    // Purpose: Builds database configuration.
    private static (string Provider, string ConnectionString) BuildDatabaseConfiguration(SetupDatabaseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var provider = MetadataDatabaseProvider.Normalize(request.Provider);

        if (provider.Equals(SetupDatabaseProviders.SQLite, StringComparison.OrdinalIgnoreCase))
        {
            var file = request.SqliteDatabaseFile?.Trim() ?? string.Empty;
            if (file.Length == 0) throw new ArgumentException("SQLite Database File is required.");
            var builder = new SqliteConnectionStringBuilder { DataSource = file };
            return (SetupDatabaseProviders.SQLite, builder.ConnectionString);
        }

        var server = request.SqlServer?.Trim() ?? string.Empty;
        var database = request.SqlDatabase?.Trim() ?? string.Empty;
        var authentication = request.SqlAuthentication?.Trim() ?? string.Empty;
        if (server.Length == 0) throw new ArgumentException("SQL Server name is required.");
        if (database.Length == 0) throw new ArgumentException("SQL Server database name is required.");
        if (!authentication.Equals(SetupSqlAuthenticationModes.Integrated, StringComparison.OrdinalIgnoreCase)
            && !authentication.Equals(SetupSqlAuthenticationModes.Sql, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("SQL authentication must be Integrated or SQL Server authentication.");
        }

        var builderSql = new SqlConnectionStringBuilder
        {
            DataSource = server,
            InitialCatalog = database,
            IntegratedSecurity = authentication.Equals(SetupSqlAuthenticationModes.Integrated, StringComparison.OrdinalIgnoreCase),
            PersistSecurityInfo = false,
            ConnectTimeout = 15,
            ApplicationName = "MAUI"
        };
        builderSql["Encrypt"] = request.SqlEncrypt;
        builderSql["TrustServerCertificate"] = request.SqlTrustServerCertificate;

        if (!builderSql.IntegratedSecurity)
        {
            var username = request.SqlUsername?.Trim() ?? string.Empty;
            if (username.Length == 0) throw new ArgumentException("SQL Username is required when SQL Server authentication is selected.");
            if (string.IsNullOrEmpty(request.SqlPassword)) throw new ArgumentException("SQL Password is required when SQL Server authentication is selected.");
            builderSql.UserID = username;
            builderSql.Password = request.SqlPassword;
        }

        return (SetupDatabaseProviders.SqlServer, builderSql.ConnectionString);
    }

    // Purpose: Validates move it.
    private static void ValidateMoveIt(SetupMoveItRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.DisplayName = request.DisplayName?.Trim() ?? string.Empty;
        request.EnvironmentKey = request.EnvironmentKey?.Trim() ?? string.Empty;
        request.WebAdminUrl = request.WebAdminUrl?.Trim() ?? string.Empty;

        if (request.DisplayName.Length == 0) throw new ArgumentException("MOVEit Display Name is required.");
        MoveItEnvironmentKey.Normalize(request.EnvironmentKey);
        if (!Uri.TryCreate(request.WebAdminUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException("MOVEit Web Admin URL must be a valid HTTP or HTTPS URL.");
        }
    }

    // Purpose: Generates the stable internal Key for the first configured MOVEit server.
    private static string GenerateServerKey()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        return $"srv-{suffix}";
    }

}

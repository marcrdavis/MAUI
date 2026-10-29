// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services;
using MoveItAutomationUI.Services.Authentication;
using MoveItAutomationUI.Services.Backup;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.Lifecycle;
using MoveItAutomationUI.Services.Logging;
using MoveItAutomationUI.Services.Configuration;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Tasks;
using MoveItAutomationUI.Features.Editors;
using MoveItAutomationUI.Services.MoveIt.TraditionalTasks;
using MoveItAutomationUI.Services.Security;
using MoveItAutomationUI.Services.Upgrade;
using MoveItAutomationUI.Services.Reporting;
using MoveItAutomationUI.Services.SystemJobs;

var builder = WebApplication.CreateBuilder(args);

// Recover a committed Factory Reset before Data Protection, databases, or hosted jobs open Data.
MauiFactoryResetRecovery.CompletePendingReset(builder.Environment.ContentRootPath);

builder.Services.AddScoped<IMoveItObjectAccessService, MoveItObjectAccessService>();
builder.Services.AddScoped<MoveItObjectPermissionFilter>();
builder.Services.AddControllersWithViews(options => options.Filters.AddService<MoveItObjectPermissionFilter>());

// Keep ASP.NET authentication, antiforgery, and TempData keys independent of the IIS app-pool
// user profile. The installation-owned Data directory is writable even when the app root is not.
// On Windows keys are encrypted at rest with local-machine DPAPI; non-Windows development
// uses the platform's default key repository instead of saving plaintext key XML.
MauiDataProtectionConfiguration.AddMauiDataProtection(builder.Services, builder.Environment.ContentRootPath);

// Gives the login antiforgery cookie a stable MAUI-specific name. The Account controller validates
// the login POST explicitly so stale tokens can be recovered gracefully instead of returning a generic 400.
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = MauiAuthenticationDefaults.AntiforgeryCookieName;
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

// Keeps MOVEit bearer/refresh tokens in this server-side cache and reference them by an opaque ID in the
// normal ASP.NET authentication cookie. DistributedMemoryCache supports the current single-process deployment model;
// a distributed provider can replace it without changing the authentication/session service contract.
builder.Services.AddDistributedMemoryCache();
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();

var moveItOptions = builder.Configuration.GetSection(MoveItOptions.SectionName).Get<MoveItOptions>() ?? new MoveItOptions();
var setupState = MauiSetupState.Capture(builder.Configuration);
var configurationStartupState = ConfigurationStartupState.Capture(builder.Configuration);
var applicationSessionLifetime = MauiAuthenticationDefaults.GetSessionLifetime(moveItOptions);

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = MauiAuthenticationDefaults.AuthenticationCookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.LoginPath = MauiLifecyclePaths.Login;
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = applicationSessionLifetime;
        options.SlidingExpiration = true;
        options.Events = new CookieAuthenticationEvents
        {
            // Normal application cookies are valid only while their referenced server-side MOVEit session
            // remains live. Upgrade authorization is intentionally exempt because it is target-version-specific
            // and must survive the redirect/restart boundary independently of transient MOVEit token state.
            OnValidatePrincipal = context =>
            {
                var principal = context.Principal;
                var upgradeTarget = principal?.FindFirst(MauiAuthenticationDefaults.UpgradeTargetVersionClaimType)?.Value;
                if (!string.IsNullOrWhiteSpace(upgradeTarget)) return Task.CompletedTask;

                var sessionService = context.HttpContext.RequestServices.GetRequiredService<IMoveItSessionService>();
                if (principal is null || !sessionService.HasLiveBrowserSession(principal))
                {
                    context.RejectPrincipal();
                    context.Response.Cookies.Delete(MauiAuthenticationDefaults.AuthenticationCookieName, new CookieOptions { Path = context.Request.PathBase.HasValue ? context.Request.PathBase.Value! : "/" });
                }
                return Task.CompletedTask;
            },

            // Browser fetch/XHR calls must receive a real 401 when authentication has expired.
            // Returning the normal cookie-auth 302 causes fetch() to follow the redirect and
            // hand the Login page HTML to grid code that expects JSON. Full document navigation
            // continues to use the standard Login redirect behavior.
            OnRedirectToLogin = context =>
            {
                var request = context.Request;
                var fetchMode = request.Headers["Sec-Fetch-Mode"].ToString();
                var fetchDestination = request.Headers["Sec-Fetch-Dest"].ToString();
                var acceptsHtml = request.Headers["Accept"].Any(value =>
                    value?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true);
                var isDocumentNavigation =
                    fetchMode.Equals("navigate", StringComparison.OrdinalIgnoreCase)
                    || fetchDestination.Equals("document", StringComparison.OrdinalIgnoreCase)
                    || ((HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)) && acceptsHtml);

                if (!isDocumentNavigation)
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Response.Headers["Cache-Control"] = "no-store";
                    return Task.CompletedTask;
                }

                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            }
        };
    });

// Protects every MVC endpoint by default. Login explicitly opts out with [AllowAnonymous].
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
    options.AddPolicy(MauiSecurityPolicies.Admin, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.AddRequirements(new MauiAdminRequirement());
    });
    // Shared templates can be saved by either MAUI or MOVEit administrators.
    // Other MAUI administration remains governed by the existing MAUI-only policy.
    options.AddPolicy(MauiSecurityPolicies.TemplateAdmin, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.AddRequirements(new MauiAdminRequirement(allowMoveItAdmin: true));
    });
});

MoveItAutomationUI.Features.EnvironmentCopy.EnvironmentCopyRegistration.AddEnvironmentCopy(builder.Services, builder.Configuration);
builder.Services.Configure<UiOptions>(builder.Configuration.GetSection(UiOptions.SectionName));
builder.Services.Configure<GridBehaviorOptions>(builder.Configuration.GetSection(GridBehaviorOptions.SectionName));
builder.Services.Configure<MoveItOptions>(builder.Configuration.GetSection(MoveItOptions.SectionName));
builder.Services.Configure<TaskBehaviorOptions>(builder.Configuration.GetSection(TaskBehaviorOptions.SectionName));
builder.Services.Configure<HostBehaviorOptions>(builder.Configuration.GetSection(HostBehaviorOptions.SectionName));
builder.Services.Configure<SshAlgorithmOptions>(builder.Configuration.GetSection(SshAlgorithmOptions.SectionName));
builder.Services.Configure<ScriptBehaviorOptions>(builder.Configuration.GetSection(ScriptBehaviorOptions.SectionName));
builder.Services.Configure<DateListBehaviorOptions>(builder.Configuration.GetSection(DateListBehaviorOptions.SectionName));
builder.Services.Configure<GlobalParameterBehaviorOptions>(builder.Configuration.GetSection(GlobalParameterBehaviorOptions.SectionName));
builder.Services.Configure<KeyCertificateBehaviorOptions>(builder.Configuration.GetSection(KeyCertificateBehaviorOptions.SectionName));
builder.Services.Configure<ReportBehaviorOptions>(builder.Configuration.GetSection(ReportBehaviorOptions.SectionName));
builder.Services.Configure<MetadataDatabaseOptions>(builder.Configuration.GetSection(MetadataDatabaseOptions.SectionName));
builder.Services.Configure<MetadataUiOptions>(builder.Configuration.GetSection(MetadataUiOptions.SectionName));
builder.Services.Configure<RecycleBinOptions>(builder.Configuration.GetSection(RecycleBinOptions.SectionName));
builder.Services.Configure<UserPreferencesOptions>(builder.Configuration.GetSection(UserPreferencesOptions.SectionName));
builder.Services.Configure<ApplicationLogOptions>(builder.Configuration.GetSection(ApplicationLogOptions.SectionName));
builder.Services.Configure<JobHistoryOptions>(builder.Configuration.GetSection(JobHistoryOptions.SectionName));
builder.Services.Configure<BackupOptions>(builder.Configuration.GetSection(BackupOptions.SectionName));
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection(SmtpOptions.SectionName));
builder.Services.Configure<SystemMessageOptions>(builder.Configuration.GetSection(SystemMessageOptions.SectionName));
builder.Services.Configure<KeepAliveOptions>(builder.Configuration.GetSection(KeepAliveOptions.SectionName));
builder.Services.AddSingleton<KeepAliveTarget>();
builder.Services.AddSingleton<IKeepAlivePinger, KeepAlivePinger>();
builder.Services.AddHostedService<KeepAliveService>();

builder.Services.AddTransient<MoveItGridConnectionHandler>();
builder.Services.AddHttpClient("MoveItAutomation")
    .AddHttpMessageHandler<MoveItGridConnectionHandler>()
    .ConfigurePrimaryHttpMessageHandler(() =>
    {
        var handler = new HttpClientHandler();
        if (configurationStartupState.AllowInvalidServerCertificates)
        {
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }
        return handler;
    });

// DbContextOptions are scoped. Read the *effective* database target for each new scope:
// Setup activates it once after provisioning, while later Settings edits stay staged until a restart.
builder.Services.AddDbContext<MetadataDbContext>(options =>
{
    MetadataDatabaseProvider.Configure(
        options,
        configurationStartupState.DatabaseProvider,
        configurationStartupState.DatabaseConnectionString,
        builder.Environment.ContentRootPath,
        builder.Environment.WebRootPath,
        ensureSqliteDirectory: setupState.IsConfiguredAtStartup);
});

builder.Services.AddScoped<IMoveItSessionService, MoveItSessionService>();
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<IObjectEditLeaseTransport, ObjectEditLeaseTransport>();
builder.Services.AddSingleton<IObjectEditLeaseJournal, ObjectEditLeaseJournal>();
builder.Services.AddSingleton<ObjectEditLeaseService>();
builder.Services.AddSingleton<IObjectEditLeaseService>(sp => sp.GetRequiredService<ObjectEditLeaseService>());
builder.Services.AddHostedService<ObjectEditLeaseService>(sp => sp.GetRequiredService<ObjectEditLeaseService>());
builder.Services.AddScoped<IMauiApplicationSessionService, MauiApplicationSessionService>();
builder.Services.AddScoped<IMoveItRestClient, MoveItRestClient>();
builder.Services.AddScoped<IMoveItServerTimeService, MoveItServerTimeService>();
builder.Services.AddSingleton<ISmtpSecretProtector, SmtpSecretProtector>();
builder.Services.AddSingleton<IReportScheduleCalculator, ReportScheduleCalculator>();
builder.Services.AddScoped<IReportConfigurationStore, ReportConfigurationStore>();
builder.Services.AddScoped<IOutboundReportRepository, OutboundReportRepository>();
builder.Services.AddScoped<IOutboundReportDataSource, OutboundReportDataSource>();
builder.Services.AddScoped<IOutboundReportEmailService, OutboundReportEmailService>();
builder.Services.AddScoped<IOutboundReportService, OutboundReportService>();
builder.Services.AddScoped<IOutboundReportPortabilityService, OutboundReportPortabilityService>();
builder.Services.AddScoped<IMoveItComClient, MoveItComClient>();
builder.Services.AddScoped<IAutomationTaskService, AutomationTaskService>();
builder.Services.AddScoped<ITaskPreflightService, TaskPreflightService>();
builder.Services.AddScoped<IResourceGroupLookupService, ResourceGroupLookupService>();
builder.Services.AddScoped<IResourceGroupPermissionsService, ResourceGroupPermissionsService>();
builder.Services.AddScoped<IResourceGroupManagementService, ResourceGroupManagementService>();
builder.Services.AddScoped<IAutomationScheduleForecastService, AutomationScheduleForecastService>();
builder.Services.AddScoped<IAutomationTaskCalendarService, AutomationTaskCalendarService>();
builder.Services.AddScoped<ITaskPortabilityService, TaskPortabilityService>();
builder.Services.AddScoped<ITaskSpreadsheetImportService, TaskSpreadsheetImportService>();
builder.Services.AddScoped<IMauiNativePortabilityService, MauiNativePortabilityService>();
builder.Services.AddScoped<IResourcePortabilityService, ResourcePortabilityService>();
builder.Services.AddScoped<MoveItAutomationUI.Services.MoveIt.Hosts.IHostDefinitionSaveService, MoveItAutomationUI.Services.MoveIt.Hosts.HostDefinitionSaveService>();
builder.Services.AddScoped<MoveItAutomationUI.Services.MoveIt.Hosts.IHostCrudCoordinator, MoveItAutomationUI.Services.MoveIt.Hosts.HostCrudCoordinator>();
builder.Services.AddScoped<IAutomationHostService, AutomationHostService>();
builder.Services.AddScoped<MoveItAutomationUI.Services.MoveIt.Hosts.IHostSpreadsheetImportService, MoveItAutomationUI.Services.MoveIt.Hosts.HostSpreadsheetImportService>();
builder.Services.AddScoped<IHostConnectionTestService, HostConnectionTestService>();
// Phase 1 common native Traditional Task contracts: a single catalog instance backs
// both the RC1 file-transfer mappings and the RC2 editor capability metadata.
TraditionalTaskHostCapabilities.ValidateCatalog();
builder.Services.AddSingleton<TraditionalTaskHostProfileCatalog>();
builder.Services.AddSingleton<ITraditionalTaskHostProfileCatalog>(sp => sp.GetRequiredService<TraditionalTaskHostProfileCatalog>());
builder.Services.AddSingleton<ITraditionalTaskHostCapabilityCatalog>(sp => sp.GetRequiredService<TraditionalTaskHostProfileCatalog>());
builder.Services.AddSingleton<ITraditionalTaskDefinitionComposer, TraditionalTaskDefinitionComposer>();
builder.Services.AddScoped<ITaskDefinitionSaveService, TaskJsonPersistenceService>();
builder.Services.AddScoped<ITraditionalTaskPersistenceService, TraditionalTaskPersistenceService>();
// Central Task CRUD service. Empty Advanced/Sync Tasks use captured v0 REST creation; COM remains registered for native operations.
builder.Services.AddScoped<ITaskCrudStrategy, TraditionalTaskCrudStrategy>();
builder.Services.AddScoped<ITaskCrudStrategy>(sp => new RestV0TaskCrudStrategy(MoveItTaskKind.Advanced, sp.GetRequiredService<IMoveItRestClient>()));
builder.Services.AddScoped<ITaskCrudStrategy>(sp => new RestV0TaskCrudStrategy(MoveItTaskKind.Synchronization, sp.GetRequiredService<IMoveItRestClient>()));
builder.Services.AddScoped<ITaskCrudCoordinator, TaskCrudCoordinator>();
builder.Services.AddScoped<ITaskFilenameSearchService, TaskFilenameSearchService>();
builder.Services.AddScoped<ITaskJsonValueService, TaskJsonValueService>();
// Short-lived checkout intent shares the edit-lease cleanup token protection format.
builder.Services.AddSingleton<ShortCheckoutRecoveryService>();
builder.Services.AddSingleton<IShortCheckoutRecovery>(sp => sp.GetRequiredService<ShortCheckoutRecoveryService>());
builder.Services.AddHostedService<ShortCheckoutRecoveryService>(sp => sp.GetRequiredService<ShortCheckoutRecoveryService>());
builder.Services.AddSingleton<TaskRunAsOfProgress>();
builder.Services.AddSingleton<TaskRunAsOfRecoveryService>();
builder.Services.AddSingleton<ITaskRunAsOfRecovery>(sp => sp.GetRequiredService<TaskRunAsOfRecoveryService>());
builder.Services.AddHostedService<TaskRunAsOfRecoveryService>(sp => sp.GetRequiredService<TaskRunAsOfRecoveryService>());
builder.Services.AddScoped<ITaskRunAsOfService, TaskRunAsOfService>();
builder.Services.AddScoped<ITaskDefinitionPatchService, TaskDefinitionPatchService>();
builder.Services.AddScoped<ITraditionalTaskDefinitionPatchService, TraditionalTaskDefinitionPatchService>();
builder.Services.AddMauiEditorFeatures();
builder.Services.AddScoped<IAutomationHostBrowseService, AutomationHostBrowseService>();
builder.Services.AddScoped<IQuickTransferService, QuickTransferService>();
// Independent, crash-recoverable Quick Transfer completion cleanup (no credentials persisted).
builder.Services.AddSingleton<QuickTransferCleanupWorker>();
builder.Services.AddSingleton<IQuickTransferCleanupTracker>(sp => sp.GetRequiredService<QuickTransferCleanupWorker>());
builder.Services.AddHostedService<QuickTransferCleanupWorker>(sp => sp.GetRequiredService<QuickTransferCleanupWorker>());
builder.Services.AddScoped<IHostHealthRepository, HostHealthRepository>();
builder.Services.AddScoped<IHostHealthService, HostHealthService>();
builder.Services.AddScoped<ISystemJobRepository, SystemJobRepository>();
builder.Services.AddScoped<IReportsSystemJob, ReportsSystemJob>();
builder.Services.AddScoped<BackupSystemJob>();
builder.Services.AddScoped<IBackupSystemJob>(serviceProvider => serviceProvider.GetRequiredService<BackupSystemJob>());
builder.Services.AddScoped<IExternallyManagedSystemJob>(serviceProvider => serviceProvider.GetRequiredService<BackupSystemJob>());
builder.Services.AddScoped<IBackupCleanupJob, BackupCleanupJob>();
builder.Services.AddScoped<IJobHistoryCleanupJob, JobHistoryCleanupJob>();
builder.Services.AddScoped<IHostHealthCheckJob, HostHealthCheckJob>();
builder.Services.AddScoped<ISystemJobService, SystemJobService>();
builder.Services.AddSingleton<SystemJobScheduler>();
builder.Services.AddSingleton<ISystemJobRuntime>(serviceProvider => serviceProvider.GetRequiredService<SystemJobScheduler>());
builder.Services.AddHostedService<SystemJobScheduler>(serviceProvider => serviceProvider.GetRequiredService<SystemJobScheduler>());
builder.Services.AddScoped<IAutomationScriptService, AutomationScriptService>();
builder.Services.AddScoped<IAutomationDateListService, AutomationDateListService>();
builder.Services.AddScoped<IAutomationGlobalParameterService, AutomationGlobalParameterService>();
builder.Services.AddScoped<IAutomationKeyCertificateService, AutomationKeyCertificateService>();
builder.Services.AddScoped<IAutomationReportService, AutomationReportService>();
builder.Services.AddScoped<ITaskFailureAlertService, TaskFailureAlertService>();
builder.Services.AddScoped<IAutomationDashboardService, AutomationDashboardService>();
builder.Services.AddScoped<IAutomationConfigurationService, AutomationConfigurationService>();
builder.Services.AddScoped<IMetadataRepository, MetadataRepository>();
builder.Services.AddScoped<ITagRepository, TagRepository>();
builder.Services.AddScoped<IMetadataValueValidationService, MetadataValueValidationService>();
builder.Services.AddScoped<IMetadataWorkbookService, MetadataWorkbookService>();
builder.Services.AddScoped<IMauiMetadataPortabilityService, MauiMetadataPortabilityService>();
builder.Services.AddScoped<IConfigurationUndoRepository, ConfigurationUndoRepository>();
builder.Services.AddScoped<IConfigurationUndoWorkflow, ConfigurationUndoWorkflow>();
builder.Services.AddScoped<ITaskUndoService, TaskUndoService>();
builder.Services.AddScoped<IHostUndoService, HostUndoService>();
builder.Services.AddScoped<IScriptUndoService, ScriptUndoService>();
builder.Services.AddScoped<IDateListUndoService, DateListUndoService>();
builder.Services.AddScoped<INativeEditorUndoService, NativeEditorUndoService>();
builder.Services.AddScoped<IRecycleBinRepository, RecycleBinRepository>();
builder.Services.AddScoped<IRecycleBinService, RecycleBinService>();
builder.Services.AddScoped<IUserPreferencesService, UserPreferencesService>();
builder.Services.AddScoped<ISavedViewService, SavedViewService>();
builder.Services.AddScoped<ITaskNotificationTemplateService, TaskNotificationTemplateService>();
builder.Services.AddScoped<IApplicationLogRepository, ApplicationLogRepository>();
builder.Services.AddScoped<MauiDatabaseSchemaManager>();
builder.Services.AddSingleton<MauiDatabaseSchemaState>();
builder.Services.AddSingleton<IDatabaseConnectionTester, DatabaseConnectionTester>();
builder.Services.AddSingleton<IMetadataDatabaseProvisioner, MetadataDatabaseProvisioner>();
builder.Services.AddSingleton<IInitialTagSeedService, InitialTagSeedService>();
builder.Services.AddSingleton(setupState);
builder.Services.AddSingleton(configurationStartupState);
builder.Services.AddSingleton<AppSettingsWriteCoordinator>();
builder.Services.AddSingleton<IAppSettingsDocumentStore, AppSettingsDocumentStore>();
builder.Services.AddSingleton<IAppSettingsConfigurationSnapshotReader, AppSettingsConfigurationSnapshotReader>();
// MAUI Admin/User authorization is persisted in the metadata database. The singleton service opens
// short-lived scoped DbContexts so every authorization decision sees current SecurityUsers state.
builder.Services.AddSingleton<IMauiSecurityService, MauiSecurityService>();
builder.Services.AddScoped<IAuthorizationHandler, MauiAdminAuthorizationHandler>();
builder.Services.AddSingleton<IMoveItServerCatalog, MoveItServerCatalog>();
builder.Services.AddSingleton<IDataAccessCredentialProtector, DataAccessCredentialProtector>();
builder.Services.AddSingleton<IDataAccessCredentialService, DataAccessCredentialService>();
builder.Services.AddSingleton<IMauiMetadataDatabaseBackupService, MauiMetadataDatabaseBackupService>();
builder.Services.AddSingleton<IMauiBackupService, MauiBackupService>();
builder.Services.AddSingleton<IConfigurationRestartDetector, ConfigurationRestartDetector>();
builder.Services.AddSingleton<IConfigurationSettingsViewModelBuilder, ConfigurationSettingsViewModelBuilder>();
builder.Services.AddSingleton<IConfigurationSettingsValidator, ConfigurationSettingsValidator>();
builder.Services.AddSingleton<IConfigurationSettingsMutator, ConfigurationSettingsMutator>();
builder.Services.AddSingleton<IMetadataFieldDeletionService, MetadataFieldDeletionService>();
builder.Services.AddSingleton<IAppSettingsConfigurationService, AppSettingsConfigurationService>();
builder.Services.AddSingleton<IMauiUpgradeManifestProvider, MauiUpgradeManifestProvider>();
builder.Services.AddSingleton<MauiUpgradeState>();
builder.Services.AddScoped<MauiUpgradeStartupService>();
builder.Services.AddScoped<IMauiUpgradeService, MauiUpgradeService>();
builder.Services.AddSingleton<ApplicationRestartService>();
builder.Services.AddSingleton<IApplicationRestartService>(serviceProvider => serviceProvider.GetRequiredService<ApplicationRestartService>());
builder.Services.AddHostedService<ApplicationRestartService>(serviceProvider => serviceProvider.GetRequiredService<ApplicationRestartService>());
builder.Services.AddSingleton<IInitialSetupService, InitialSetupService>();
builder.Services.AddSingleton<MauiFactoryResetState>();
builder.Services.AddScoped<MauiFactoryResetService>();

// Captures MAUI-owned ILogger events into a bounded in-memory channel and persists them asynchronously
// to the metadata database. Framework/Microsoft categories continue using the normal configured providers only.
builder.Services.AddSingleton<MauiApplicationLogChannel>();
builder.Services.AddSingleton<ILoggerProvider, MauiDatabaseLoggerProvider>();
builder.Services.AddHostedService<MauiDatabaseLogWriter>();


var app = builder.Build();

// Read-only upgrade detection happens once per application start. Physical schema creation and
// changes are handled only by Initial Setup or the authenticated /Upgrade workflow.
using (var upgradeScope = app.Services.CreateScope())
{
    var upgradeStartup = upgradeScope.ServiceProvider.GetRequiredService<MauiUpgradeStartupService>();
    await upgradeStartup.InitializeAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// Once reset is committed, refuse new requests until shutdown completes.
app.UseMiddleware<MauiFactoryResetAccessMiddleware>();
// Local keep-alive neither authenticates users nor calls MOVEit/database services.
app.UseMiddleware<KeepAliveMiddleware>();
app.UseHttpsRedirection();
app.UseStaticFiles();

// A new installation exposes only the first-run Setup surface. Once MAUI starts configured,
// stale browser navigation to /Setup redirects to login while Setup POST/API routes remain unavailable.
app.UseMiddleware<InitialSetupAccessMiddleware>();

app.UseRouting();
app.UseAuthentication();

// Pending upgrades suspend normal access after authentication has populated the request principal.
// The middleware preserves login/logout access, requires a live MOVEit-backed Admin session for
// /Upgrade, redirects eligible Admins into the workflow, and makes /Upgrade nonexistent when current.
app.UseMiddleware<UpgradeAccessMiddleware>();

app.UseAuthorization();
app.UseMiddleware<ObjectEditLeaseMiddleware>();
app.UseMiddleware<MoveItGridConnectionMiddleware>();

// Setup and Upgrade both intentionally use short lifecycle URLs (/Setup and /Upgrade).
// The application's global conventional route defaults a missing action to Login, so these
// lifecycle controllers need explicit routes whose missing action defaults to Index. Without
// these routes, /Setup would resolve as SetupController.Login and return 404 on a clean install,
// and /Upgrade would fail the same way when an upgrade is pending.
app.MapControllerRoute(
    name: "setup",
    pattern: "Setup/{action=Index}/{id?}",
    defaults: new { controller = "Setup" });

app.MapControllerRoute(
    name: "upgrade",
    pattern: "Upgrade/{action=Index}/{id?}",
    defaults: new { controller = "Upgrade" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.Run();


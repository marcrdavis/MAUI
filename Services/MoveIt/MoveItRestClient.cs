// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using MoveItAutomationUI.Services.MoveIt.Hosts;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using MoveItAutomationUI.Services.Configuration;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Concurrency;

namespace MoveItAutomationUI.Services.MoveIt;

public interface IMoveItRestClient
{
    // Explicit, administrator-confirmed takeover. Normal checkout methods never set force.
    Task<JsonObject> ForceCheckoutObjectV0Async(string type, string id, CancellationToken cancellationToken = default);
    // Purpose: Authenticate a user against the selected MOVEit server.
    Task<MoveItSessionState> AuthenticateAsync(string serverKey, string username, string password, CancellationToken cancellationToken = default);
    // Purpose: Retrieve native MOVEit server/user/license information immediately after authentication.
    Task<MoveItInfo> GetInfoAsync(MoveItSessionState authenticatedState, CancellationToken cancellationToken = default);
    // Purpose: Sign out of MOVEit and clear the MAUI session.
    Task LogoutAsync(CancellationToken cancellationToken = default);
    // Purpose: Export selected native MOVEit configuration items through the Web Admin v0 portability endpoint.
    Task<string> ExportNativeItemsAsync(IReadOnlyList<NativeConfigurationExportItem> items, bool includeKeys, string? passphrase, CancellationToken cancellationToken = default);
    // Purpose: Decode and validate a native MOVEit import document before applying conflict resolutions.
    Task<JsonObject> EncodeNativeImportAsync(string fileContents, string? passphrase, CancellationToken cancellationToken = default);
    // Purpose: Commit a previously decoded native MOVEit import model. The raw response is intentionally discarded.
    Task ImportNativeConfigurationAsync(JsonObject imported, string? passphrase, CancellationToken cancellationToken = default);
    // Purpose: Export the complete MOVEit Automation config.xml using the documented v1 Config API.
    Task<string> ExportConfigurationAsync(string? passphrase, bool sanitize, CancellationToken cancellationToken = default);
    // Purpose: Export configuration using an explicitly authenticated state for unattended/background operations.
    Task<string> ExportConfigurationAsync(MoveItSessionState authenticatedState, string? passphrase, bool sanitize, CancellationToken cancellationToken = default);
    // Purpose: Import a complete configuration that was exported with a user-provided passphrase.
    Task ImportConfigurationWithPassphraseAsync(string configXml, string passphrase, CancellationToken cancellationToken = default);
    // Purpose: Import a complete configuration encrypted with this MOVEit server's server key.
    Task ImportConfigurationWithServerKeyAsync(string configXml, CancellationToken cancellationToken = default);
    // Purpose: Import a passphrase-protected configuration using an explicitly authenticated state.
    Task ImportConfigurationWithPassphraseAsync(MoveItSessionState authenticatedState, string configXml, string passphrase, CancellationToken cancellationToken = default);
    // Purpose: Import a Server-Key-protected configuration using an explicitly authenticated state.
    Task ImportConfigurationWithServerKeyAsync(MoveItSessionState authenticatedState, string configXml, CancellationToken cancellationToken = default);
    // Purpose: Retrieve tasks.
    Task<IReadOnlyList<JsonObject>> GetTasksAsync(CancellationToken cancellationToken = default);
    // Retrieves the Task inventory fields used by the main Tasks grids, including configured schedules.
    Task<IReadOnlyList<JsonObject>> GetTaskGridDefinitionsAsync(CancellationToken cancellationToken = default);
    // Purpose: Check whether another MOVEit task already uses the requested name.
    Task<bool> TaskNameExistsAsync(string taskName, CancellationToken cancellationToken = default);
    // Purpose: Check whether another MOVEit task already uses the requested renamed value.
    Task<bool> TaskNameExistsForOtherTaskAsync(string taskName, string taskId, CancellationToken cancellationToken = default);
    // Purpose: Retrieve task.
    Task<JsonObject> GetTaskAsync(string taskId, CancellationToken cancellationToken = default);
    // Creates one complete Traditional Task definition through the documented v1 task endpoint.
    Task<JsonObject> CreateTaskDefinitionAsync(JsonObject task, CancellationToken cancellationToken = default);
    // Purpose: Updates one existing Traditional Task using the documented GET/edit/PUT contract.
    Task UpdateTaskAsync(string taskId, JsonObject task, CancellationToken cancellationToken = default);
    Task<JsonObject> GetSynchronizationCompareV0Async(string taskId, CancellationToken cancellationToken = default);
    Task<JsonObject> ClearSynchronizationCompareV0Async(string taskId, CancellationToken cancellationToken = default);
    Task<JsonObject> UpdateTaskV0Async(string taskId, JsonObject task, CancellationToken cancellationToken = default);
    Task<JsonObject> CreateTaskV0Async(JsonObject task, CancellationToken cancellationToken = default);
    // Purpose: Retrieve task dependency definitions.
    Task<IReadOnlyList<JsonObject>> GetTaskDependencyDefinitionsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<JsonObject>> GetTaskFilenameDefinitionsAsync(CancellationToken cancellationToken = default);
    // Retrieves the documented schedule fields needed by the Upcoming Runs forecast.
    Task<IReadOnlyList<JsonObject>> GetTaskScheduleDefinitionsAsync(CancellationToken cancellationToken = default);
    // Purpose: Retrieve the current MOVEit Automation task-scheduler status.
    Task<string> GetSchedulerStatusAsync(CancellationToken cancellationToken = default);
    // Purpose: Retrieve MOVEit Automation failover/resiliency status from the native v1 endpoint.
    Task<MoveItFailoverStatus> GetFailoverStatusAsync(CancellationToken cancellationToken = default);
    // Purpose: Enable the MOVEit Automation task scheduler.
    Task StartSchedulerAsync(CancellationToken cancellationToken = default);
    // Purpose: Disable the MOVEit Automation task scheduler without terminating currently running tasks.
    Task StopSchedulerAsync(CancellationToken cancellationToken = default);
    // Purpose: Reset MOVEit Automation tamper-detection hashing through the native v0 server action.
    Task ResetTamperDetectionAsync(CancellationToken cancellationToken = default);
    // Whole-system settings use the native singleton Globals checkout/PUT contract.
    Task<JsonObject> CheckoutGlobalsV0Async(CancellationToken cancellationToken = default);
    Task<JsonObject> GetSystemLockoutPolicyV0Async(CancellationToken cancellationToken = default);
    Task<JsonObject> GetSystemLockoutsV1Async(string kind, CancellationToken cancellationToken = default);
    Task UnlockSystemLockoutsV1Async(string kind, IReadOnlyList<string> values, CancellationToken cancellationToken = default);
    Task UpdateSystemLockoutPolicyV0Async(JsonObject policy, CancellationToken cancellationToken = default);
    Task<JsonObject> GetSystemTrustedIPsV0Async(CancellationToken cancellationToken = default);
    Task AddSystemTrustedIPV0Async(string address, CancellationToken cancellationToken = default);
    Task RemoveSystemTrustedIPV0Async(string address, CancellationToken cancellationToken = default);
    Task<JsonObject> UpdateGlobalsV0Async(JsonObject globals, CancellationToken cancellationToken = default);
    Task ReleaseGlobalsCheckoutV0Async(CancellationToken cancellationToken = default);
    // Purpose: Delete task.
    Task DeleteTaskAsync(string taskId, CancellationToken cancellationToken = default);
    // Purpose: Start task.
    Task<string> StartTaskAsync(string taskId, CancellationToken cancellationToken = default);
    // Purpose: Kill task.
    Task KillTaskAsync(string taskId, string nominalStart, CancellationToken cancellationToken = default);
    // Purpose: Retrieve running tasks through the documented v1 endpoint used by kill/preflight operations.
    Task<IReadOnlyList<MoveItRunningTask>> GetRunningTasksAsync(CancellationToken cancellationToken = default);
    // Purpose: Retrieve task runs.
    Task<IReadOnlyList<MoveItTaskRunRecord>> GetTaskRunsAsync(CancellationToken cancellationToken = default);
    // Purpose: Retrieve report records through the documented MOVEit v1 reporting endpoints.
    Task<IReadOnlyList<JsonObject>> GetTaskRunReportAsync(string predicate, string orderBy, int maxCount, CancellationToken cancellationToken = default);
    // Purpose: Retrieves raw File Activity report rows through the documented v1 reporting endpoint.
    Task<IReadOnlyList<JsonObject>> GetFileActivityReportAsync(string predicate, string orderBy, int maxCount, CancellationToken cancellationToken = default);
    // Purpose: Retrieve the native Web Admin File Activity report view through the v0 endpoint.
    Task<IReadOnlyList<JsonObject>> GetFileActivityReportV0Async(string predicate, string orderBy, int maxCount, CancellationToken cancellationToken = default);
    // Purpose: Retrieves raw Audit report rows through the documented v1 reporting endpoint.
    Task<IReadOnlyList<JsonObject>> GetAuditReportAsync(string predicate, string orderBy, int maxCount, CancellationToken cancellationToken = default);
    // Reads one native 2026+ audit comparison on demand, using the current user's session (no checkout).
    Task<AuditChangeRecord> GetAuditChangesAsync(long logId, CancellationToken cancellationToken = default);
    // Purpose: Retrieve task logs.
    Task<IReadOnlyList<MoveItTaskLogRecord>> GetTaskLogsAsync(string taskId, CancellationToken cancellationToken = default);
    // Purpose: Retrieve task log text.
    Task<string> GetTaskLogTextAsync(string taskId, long taskLogId, CancellationToken cancellationToken = default);
    // Retrieves all custom Scripts visible to the current MOVEit user.
    Task<IReadOnlyList<JsonObject>> GetCustomScriptsAsync(CancellationToken cancellationToken = default);
    // Performs a live exact-name lookup before creating a custom Script.
    Task<bool> CustomScriptNameExistsAsync(string scriptName, CancellationToken cancellationToken = default);
    // Retrieves one custom Script by persistent Script ID.
    Task<JsonObject> GetCustomScriptAsync(string scriptId, CancellationToken cancellationToken = default);
    Task<JsonObject> CreateCustomScriptV0Async(JsonObject body, CancellationToken cancellationToken = default);
    Task<JsonObject> UpdateCustomScriptV0Async(string id, JsonObject body, CancellationToken cancellationToken = default);
    Task CheckoutCustomScriptV0Async(string id, CancellationToken cancellationToken = default);
    Task ReleaseCustomScriptCheckoutV0Async(string id, CancellationToken cancellationToken = default);
    // Retrieves all built-in/standard Scripts visible to the current MOVEit user.
    Task<IReadOnlyList<JsonObject>> GetStandardScriptsAsync(CancellationToken cancellationToken = default);
    // Performs a live exact-name lookup across built-in/standard Scripts.
    Task<bool> StandardScriptNameExistsAsync(string scriptName, CancellationToken cancellationToken = default);
    // Retrieves one built-in/standard Script by persistent Script ID.
    Task<JsonObject> GetStandardScriptAsync(string scriptId, CancellationToken cancellationToken = default);
    // Retrieves Task definitions used to find visible custom-Script dependencies.
    Task<IReadOnlyList<JsonObject>> GetTaskScriptReferenceDefinitionsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<JsonObject>> GetTaskGlobalParameterReferenceDefinitionsAsync(CancellationToken cancellationToken = default);
    // Retrieves all Date Lists visible to the current MOVEit user.
    Task<IReadOnlyList<JsonObject>> GetDateListsAsync(CancellationToken cancellationToken = default);
    // Purpose: Returns date list.
    Task<JsonObject> GetDateListAsync(string dateListId, CancellationToken cancellationToken = default);
    Task<JsonObject> CreateDateListV0Async(JsonObject body, CancellationToken cancellationToken = default);
    Task<JsonObject> UpdateDateListV0Async(string id, JsonObject body, CancellationToken cancellationToken = default);
    Task CheckoutDateListV0Async(string id, CancellationToken cancellationToken = default);
    Task ReleaseDateListCheckoutV0Async(string id, CancellationToken cancellationToken = default);

    // Retrieves Date Lists including Entries for schedule forecasting.
    Task<IReadOnlyList<JsonObject>> GetDateListsWithEntriesAsync(CancellationToken cancellationToken = default);
    // Purpose: Checks whether a Date List already uses the requested name.
    Task<bool> DateListNameExistsAsync(string name, CancellationToken cancellationToken = default);
    // Purpose: Returns task date list reference definitions.
    Task<IReadOnlyList<JsonObject>> GetTaskDateListReferenceDefinitionsAsync(CancellationToken cancellationToken = default);
    // Purpose: Retrieves all Global Parameters visible to the current MOVEit user.
    Task<IReadOnlyList<JsonObject>> GetGlobalParametersAsync(CancellationToken cancellationToken = default);
    // Purpose: Returns global parameter.
    Task<JsonObject> GetGlobalParameterAsync(string name, CancellationToken cancellationToken = default);
    // Purpose: Checks whether a Global Parameter already uses the requested name.
    Task<bool> GlobalParameterNameExistsAsync(string name, string? excludeName = null, CancellationToken cancellationToken = default);
    // Retrieves the three key/certificate inventories used by the combined Resources grid.
    Task<IReadOnlyList<JsonObject>> GetSshKeysAsync(CancellationToken cancellationToken = default);
    // Purpose: Returns PGP keys.
    Task<IReadOnlyList<JsonObject>> GetPgpKeysAsync(CancellationToken cancellationToken = default);
    // Purpose: Returns TLS certificates.
    Task<IReadOnlyList<JsonObject>> GetTlsCertificatesAsync(CancellationToken cancellationToken = default);
    // Purpose: Checks whether an SSH client key already uses the requested name.
    Task<bool> SshKeyNameExistsAsync(string name, CancellationToken cancellationToken = default);
    // MOVEit Web Admin 2025.x creates keys/certificates through internal v0 endpoints; keep those writes isolated here.
    Task CreateSshKeyV0Async(string name, string keyType, int keySize, CancellationToken cancellationToken = default, string? resourceGroup = null);
    // Purpose: Creates a TLS certificate through the native Web Admin v0 contract.
    Task CreateTlsCertificateV0Async(string subject, string expirationDate, int keySize, CancellationToken cancellationToken = default, string? resourceGroup = null);
    // Purpose: Creates a PGP keypair through the native Web Admin v0 contract.
    Task CreatePgpKeyV0Async(string uid, string keyType, int keyLength, string hashAlgorithm, int expiresAfter, string expireUnit, string passphrase, CancellationToken cancellationToken = default, string? resourceGroup = null);
    // Purpose: Imports an SSH client key through the native Web Admin v0 contract.
    Task ImportSshKeyV0Async(string name, string password, string privateKey, CancellationToken cancellationToken = default, string? resourceGroup = null);
    // Purpose: Imports TLS certificate after validating the package and target.
    Task ImportTlsCertificateV0Async(string fileName, byte[] certificateBytes, bool isPublicOnly, string password, CancellationToken cancellationToken = default, string? resourceGroup = null);
    // Purpose: Imports PGP key after validating the package and target.
    Task ImportPgpKeyV0Async(byte[] keyMaterial, string passphrase, CancellationToken cancellationToken = default, string? resourceGroup = null);
    Task<byte[]> ExportTlsCertificateV0Async(JsonObject request, CancellationToken cancellationToken = default);
    Task<byte[]> ExportPgpKeyV0Async(JsonObject request, CancellationToken cancellationToken = default);
    // Purpose: Deletes PGP key after applying the required guardrails.
    Task DeletePgpKeyV0Async(string id, CancellationToken cancellationToken = default);
    // Purpose: Deletes TLS certificate after applying the required guardrails.
    Task DeleteTlsCertificateV0Async(string identity, CancellationToken cancellationToken = default);
    // Purpose: Returns task key certificate reference definitions.
    Task<IReadOnlyList<JsonObject>> GetTaskKeyCertificateReferenceDefinitionsAsync(CancellationToken cancellationToken = default);
    // Purpose: Returns host key certificate reference definitions.
    Task<IReadOnlyList<JsonObject>> GetHostKeyCertificateReferenceDefinitionsAsync(CancellationToken cancellationToken = default);
    // Retrieves all Resource Groups visible to the current MOVEit user without taking the native v0 checkout.
    Task<IReadOnlyList<JsonObject>> GetResourceGroupsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<JsonObject>> GetResourceGroupDefinitionsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<JsonObject>> GetResourceGroupAclDefinitionsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<JsonObject>> GetUserGroupDefinitionsAsync(string type, CancellationToken cancellationToken = default);
    Task<JsonObject> CreateResourceGroupAclAsync(JsonObject acl, CancellationToken cancellationToken = default);
    Task DeleteResourceGroupAclAsync(string aclId, CancellationToken cancellationToken = default);
    Task UpdateResourceGroupAclAsync(string aclId, JsonObject acl, CancellationToken cancellationToken = default);
    // Checks out the complete Resource Group dataset through the native Web Admin v0 contract.
    Task<JsonObject> CheckoutResourceGroupsV0Async(CancellationToken cancellationToken = default);
    // Commits the complete checked-out Resource Group dataset through the native Web Admin v0 contract.
    Task<JsonObject> UpdateResourceGroupsV0Async(JsonObject resourceGroups, CancellationToken cancellationToken = default);
    // Retrieves all Hosts visible to the current MOVEit user.
    Task<IReadOnlyList<JsonObject>> GetHostsAsync(CancellationToken cancellationToken = default);
    // Performs a live exact-name lookup before creating a Host.
    Task<bool> HostNameExistsAsync(string hostName, CancellationToken cancellationToken = default);
    // RC2 verified native Web Admin operations; distinct from the public task/host v1 contracts.
    Task<JsonObject> CreateHostV0Async(JsonObject host, CancellationToken cancellationToken = default);
    Task<JsonObject> UpdateHostV0Async(string hostId, JsonObject host, CancellationToken cancellationToken = default);
    Task<JsonObject> GetTaskFailedInfoV0Async(string taskId, CancellationToken cancellationToken = default);
    Task<JsonObject> DeleteTaskFailedEntriesV0Async(string taskId, IReadOnlyCollection<string> exceptionIds, CancellationToken cancellationToken = default);
    Task<JsonObject> GetTaskNewFileTimestampsV0Async(string taskId, string hostId, CancellationToken cancellationToken = default);
    Task<JsonObject> UpdateTaskNewFileTimestampV0Async(string taskId, string hostId, string path, string mask, string timestamp, CancellationToken cancellationToken = default);
    // Retrieves one Host definition by persistent Host ID.
    Task<JsonObject> GetHostAsync(string hostId, CancellationToken cancellationToken = default);
    // Checks out one Host through the native Web Admin v0 contract and returns its full type-specific definition.
    Task<JsonObject> GetHostTlsCertificatesV0Async(CancellationToken cancellationToken = default);
    Task<JsonObject> GetSshEncryptionListV0Async(CancellationToken cancellationToken = default);
    Task<JsonObject> GetSshDefaultKexAlgorithmsV0Async(CancellationToken cancellationToken = default);
    Task<JsonObject> GetSshHostFingerprintV0Async(IReadOnlyDictionary<string, string> query, CancellationToken cancellationToken = default);
    Task<JsonObject> CheckoutHostV0Async(string hostId, CancellationToken cancellationToken = default);
    Task CheckoutTaskV0Async(string taskId, CancellationToken cancellationToken = default);
    Task ReleaseTaskCheckoutV0Async(string taskId, CancellationToken cancellationToken = default);
    // Releases a Host checkout previously acquired through the native Web Admin v0 contract.
    Task ReleaseHostCheckoutV0Async(string hostId, CancellationToken cancellationToken = default);
    // Tests one saved UNC Share Host through the same native file-system browse endpoint used by MOVEit Web Admin.
    Task<JsonObject> BrowseFileSystemHostV0Async(JsonObject request, CancellationToken cancellationToken = default);
    // Tests one saved SSH/SFTP Host through the same native browse endpoint used by MOVEit Web Admin.
    Task<JsonObject> BrowseSshHostV0Async(JsonObject request, CancellationToken cancellationToken = default);
    // Tests one saved FTP/FTPS Host through the same native browse endpoint used by MOVEit Web Admin.
    Task<JsonObject> BrowseFtpHostV0Async(JsonObject request, CancellationToken cancellationToken = default);
    // Tests one saved SharePoint Host through the same native test endpoint used by MOVEit Web Admin.
    Task<JsonObject> TestSharePointHostV0Async(JsonObject request, CancellationToken cancellationToken = default);
    // Tests one saved Azure Blob Host through the native test endpoint used by MOVEit Web Admin.
    Task<JsonObject> TestAzureBlobHostV0Async(JsonObject request, CancellationToken cancellationToken = default);
    // Tests one saved Google Storage Host through the native test endpoint used by MOVEit Web Admin.
    Task<JsonObject> TestGoogleStorageHostV0Async(JsonObject request, CancellationToken cancellationToken = default);
    // Tests one saved Amazon S3 Host through the native test endpoint used by MOVEit Web Admin.
    Task<JsonObject> TestS3HostV0Async(JsonObject request, CancellationToken cancellationToken = default);
    // Browses Amazon S3 buckets/folders through the captured native Web Admin contract.
    Task<JsonObject> BrowseS3HostV0Async(JsonObject request, CancellationToken cancellationToken = default);
    // Browses Azure Blob containers/folders through the captured native Web Admin contract.
    Task<JsonObject> BrowseAzureBlobHostV0Async(JsonObject request, CancellationToken cancellationToken = default);
    // Browses SharePoint document libraries/folders through the captured native Web Admin contract.
    Task<JsonObject> BrowseSharePointHostV0Async(JsonObject request, CancellationToken cancellationToken = default);
    Task<JsonObject> BrowseOneDriveHostV0Async(JsonObject request, CancellationToken cancellationToken = default);
    // Browses Google Storage buckets/folders through the captured native Web Admin contract.
    Task<JsonObject> BrowseGoogleStorageHostV0Async(JsonObject request, CancellationToken cancellationToken = default);
    // Tests one saved MOVEit Transfer Host through the native DMZ browse endpoint used by MOVEit Web Admin.
    Task<JsonObject> BrowseDmzHostV0Async(JsonObject request, CancellationToken cancellationToken = default);
    // Retrieves the Task fields shared by Host-reference analysis and Calendar classification.
    Task<IReadOnlyList<JsonObject>> GetTaskHostReferenceDefinitionsAsync(CancellationToken cancellationToken = default);
}

public sealed record MoveItRunningTask(
    string TaskId,
    string TaskName,
    string StartedBy,
    string Status,
    string TimeStarted,
    string NominalStart,
    long? CurFileBytes,
    long? TotFileBytes,
    string LastErrorType,
    string LastErrorText);
public sealed record MoveItTaskRunRecord(string TaskId, string TaskName, string Status, string StatusMessage, int? FilesSent, double? TotalBytesSent, DateTimeOffset? StartTime, DateTimeOffset? EndTime, string NominalStart);
public sealed record MoveItTaskLogRecord(long Id, string TaskId, string ExitStatus, string LogPath, string StartTime);

public class MoveItApiException : Exception
{
    public HttpStatusCode? StatusCode { get; }

    // Initializes a MOVEit API exception with the optional HTTP status and inner exception.
    public MoveItApiException(string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}

public sealed class MoveItAuthenticationException : MoveItApiException
{
    // Initializes a MOVEit authentication exception with the optional HTTP status and inner exception.
    public MoveItAuthenticationException(string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
        : base(message, statusCode, innerException)
    {
    }
}

/// <summary>
/// Implements MOVEit Automation REST authentication, task reads, running state, and task-run reporting here.
/// Keeps configuration edits in MoveItComClient because REST write coverage differs by task type.
/// </summary>
public sealed class MoveItRestClient : IMoveItRestClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        // JsonNode.ToJsonString(options) can require an explicit resolver for customized JsonValue nodes on .NET 8.
        // PGP import's populated keymaterial JsonArray is one such shape. Configure the shared REST serializer once
        // so all JSON request paths are safe rather than special-casing individual endpoints.
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    // Serializes MOVEit refresh-token renewal because Task-grid refreshes issue several REST calls concurrently and token
    // renewal must be serialized per MOVEit user/server or two requests can attempt to use the same
    // refresh token and the later request will receive "invalid token" after the first one rotates it.
    private static readonly KeyedAsyncLock TokenRenewalLocks = new(StringComparer.OrdinalIgnoreCase);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMoveItSessionService _sessionService;
    private readonly ConfigurationStartupState _configuration;
    private readonly IMoveItServerCatalog _servers;
    private readonly ILogger<MoveItRestClient> _logger;

    // Initializes the MOVEit REST client with HTTP, session, configuration, and logging dependencies.
    public MoveItRestClient(
        IHttpClientFactory httpClientFactory,
        IMoveItSessionService sessionService,
        ConfigurationStartupState configuration,
        IMoveItServerCatalog servers,
        ILogger<MoveItRestClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _sessionService = sessionService;
        _configuration = configuration;
        _servers = servers;
        _logger = logger;
    }

    // Authenticates against the exact Web Admin node selected by the user. A failed token request is treated as
    // login failure (bad credentials and/or a passive HA node); no secondary failover-status request is performed.
    // Purpose: Authenticate against the selected MOVEit Web Admin node.
    public async Task<MoveItSessionState> AuthenticateAsync(string serverKey, string username, string password, CancellationToken cancellationToken = default)
    {
        // Apply the same identity spelling to interactive and unattended logins, tokens and session state.
        username = MauiUsername.Normalize(username);
        var server = ResolveServer(serverKey);
        var baseUrl = NormalizeBaseUrl(server.WebAdminUrl);
        string environmentKey;
        try
        {
            environmentKey = MoveItEnvironmentKey.Resolve(server);
        }
        catch (ArgumentException ex)
        {
            throw new MoveItAuthenticationException($"The selected MOVEit server has an invalid EnvironmentKey configuration: {ex.Message}", null, ex);
        }

        _logger.LogInformation(
            "Authenticating MOVEit user {Username} against configured server {ServerKey} ({WebAdminUrl}).",
            username, server.Key, baseUrl);

        var token = await RequestPasswordTokenAsync(baseUrl, username, password, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        var state = new MoveItSessionState
        {
            ServerKey = server.Key,
            EnvironmentKey = environmentKey,
            ServerDisplayName = string.IsNullOrWhiteSpace(server.DisplayName) ? server.Key : server.DisplayName,
            WebAdminUrl = baseUrl,
            ServerHost = server.ServerHost,
            Username = username,
            Password = password,
            AccessToken = token.AccessToken,
            RefreshToken = token.RefreshToken,
            AccessTokenExpiresUtc = now.AddSeconds(Math.Max(30, token.ExpiresInSeconds))
        };

        _logger.LogInformation("MOVEit REST authentication succeeded for user {Username} on {ServerKey}.", username, server.Key);
        return state;
    }

    // Reads the native Web Admin session/server information once immediately after authentication. This overload
    // intentionally accepts the just-authenticated state because the MAUI browser session has not been created yet.
    public async Task<MoveItInfo> GetInfoAsync(MoveItSessionState authenticatedState, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authenticatedState);
        using var response = await SendAsync(authenticatedState, HttpMethod.Get, "/api/v0/info", null, cancellationToken);
        var root = await ParseJsonResponseAsync(response, cancellationToken);
        return ParseMoveItInfo(root);
    }


    // Exports selected Tasks and their native dependencies through the same v0 endpoint used by MOVEit Web Admin.
    // Host-only portable exports are created by MAUI through a temporary disabled Task carrier, so SectionType remains Task.
    public Task<string> ExportNativeItemsAsync(
        IReadOnlyList<NativeConfigurationExportItem> items,
        bool includeKeys,
        string? passphrase,
        CancellationToken cancellationToken = default)
    {
        if (items is null || items.Count == 0) throw new ArgumentException("At least one native configuration item is required.", nameof(items));
        var body = BuildNativeExportRequest(items, includeKeys, passphrase);
        return SendForTextAsync(HttpMethod.Post, "/api/v0/config/export", body, cancellationToken);
    }

    // Decodes MOVEit's native export into the structured import model used by the Web Admin import preview.
    public async Task<JsonObject> EncodeNativeImportAsync(string fileContents, string? passphrase, CancellationToken cancellationToken = default)
    {
        var body = BuildNativeEncodeRequest(fileContents, passphrase);

        // Use the common authenticated request path so an expired access token receives the same one-time
        // refresh/retry behavior as every other MAUI REST operation.
        var root = await SendForJsonObjectAsync(HttpMethod.Put, "/api/v0/config/encode", body, cancellationToken);
        var import = root["Import"] as JsonObject
            ?? throw new MoveItApiException("MOVEit did not return an import model for the selected package.");
        return (JsonObject)import.DeepClone();
    }

    // Commits the structured import model. MOVEit can return its broader Settings configuration here; MAUI deliberately
    // does not read, parse, log, or return that successful response body. Error responses still flow through the common
    // typed REST error handling so administrators receive a useful failure without exposing configuration data.
    public Task ImportNativeConfigurationAsync(JsonObject imported, string? passphrase, CancellationToken cancellationToken = default)
    {
        var body = BuildNativeImportRequest(imported, passphrase);
        return SendAndDiscardSuccessfulBodyAsync(HttpMethod.Put, "/api/v0/config/import", body, cancellationToken);
    }

    // These builders intentionally mirror the Web Admin request shapes captured from MOVEit Automation.
    // Keeping them isolated and testable protects the undocumented v0 portability contract from accidental drift.
    internal static JsonObject BuildNativeExportRequest(IReadOnlyList<NativeConfigurationExportItem> items, bool includeKeys, string? passphrase)
    {
        if (items is null || items.Count == 0) throw new ArgumentException("At least one native configuration item is required.", nameof(items));
        var nativeItems = new JsonArray();
        foreach (var item in items)
        {
            var id = (item.ID ?? string.Empty).Trim();
            var sectionType = (item.SectionType ?? string.Empty).Trim();
            if (id.Length == 0 || sectionType.Length == 0) throw new ArgumentException("Native export items require ID and SectionType.", nameof(items));
            nativeItems.Add(new JsonObject { ["ID"] = id, ["SectionType"] = sectionType });
        }

        var body = new JsonObject
        {
            ["Item"] = nativeItems,
            ["doNotExportKeys"] = !includeKeys
        };
        if (!string.IsNullOrWhiteSpace(passphrase)) body["passphrase"] = passphrase;
        return body;
    }

    internal static JsonObject BuildNativeEncodeRequest(string fileContents, string? passphrase)
    {
        if (string.IsNullOrWhiteSpace(fileContents)) throw new ArgumentException("Native MOVEit export XML is required.", nameof(fileContents));
        var body = new JsonObject { ["fileContents"] = fileContents };
        if (!string.IsNullOrWhiteSpace(passphrase)) body["passphrase"] = passphrase;
        return body;
    }

    internal static JsonObject BuildNativeImportRequest(JsonObject imported, string? passphrase)
    {
        ArgumentNullException.ThrowIfNull(imported);

        // MOVEit Web Admin's native /api/v0/config/import contract expects the portable
        // decryption passphrase inside the Imported object. Keep MOVEit's decoded import
        // model otherwise unchanged so protected Host/key material can be decrypted by
        // MOVEit during the single native import operation.
        var importedBody = (JsonObject)imported.DeepClone();
        if (!string.IsNullOrWhiteSpace(passphrase)) importedBody["passphrase"] = passphrase;

        return new JsonObject { ["Imported"] = importedBody };
    }

    // Exports config.xml exactly as MOVEit returns it. sanitize=true takes precedence over passphrase per the API contract.
    public Task<string> ExportConfigurationAsync(string? passphrase, bool sanitize, CancellationToken cancellationToken = default)
    {
        var body = BuildConfigurationExportBody(passphrase, sanitize);
        return SendForTextAsync(HttpMethod.Post, "/api/v1/config", body, cancellationToken);
    }

    // Exports config.xml using an explicitly authenticated state so hosted/background services do not depend on a browser session.
    public async Task<string> ExportConfigurationAsync(MoveItSessionState authenticatedState, string? passphrase, bool sanitize, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authenticatedState);
        var body = BuildConfigurationExportBody(passphrase, sanitize);
        using var response = await SendAsync(authenticatedState, HttpMethod.Post, "/api/v1/config", body, cancellationToken, acceptJson: false);
        return await ParseTextResponseAsync(response, cancellationToken);
    }

    private static JsonObject BuildConfigurationExportBody(string? passphrase, bool sanitize)
    {
        var body = new JsonObject();
        if (sanitize)
            body["sanitize"] = true;
        else if (!string.IsNullOrWhiteSpace(passphrase))
            body["passphrase"] = passphrase;
        return body;
    }

    // Imports a portable configuration using the passphrase that protected the export.
    public async Task ImportConfigurationWithPassphraseAsync(string configXml, string passphrase, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(configXml)) throw new ArgumentException("Configuration XML is required.", nameof(configXml));
        if (string.IsNullOrWhiteSpace(passphrase)) throw new ArgumentException("Configuration passphrase is required.", nameof(passphrase));

        var body = new JsonObject
        {
            ["configXml"] = configXml,
            ["passphrase"] = passphrase
        };
        // Whole-configuration import does not need MOVEit's successful response body. Discard it so any
        // configuration-bearing response can never flow into MAUI application state or logs.
        await SendAndDiscardSuccessfulBodyAsync(HttpMethod.Post, "/api/v1/config/import", body, cancellationToken);
    }

    // Imports a configuration encrypted with MOVEit's server key. MOVEit's PUT /api/v1/config contract
    // requires the configuration document itself as application/xml, not a JSON-encoded string.
    public async Task ImportConfigurationWithServerKeyAsync(string configXml, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(configXml)) throw new ArgumentException("Configuration XML is required.", nameof(configXml));
        await SendXmlAndDiscardSuccessfulBodyAsync(HttpMethod.Put, "/api/v1/config", configXml, cancellationToken);
    }

    // Imports a portable configuration using an explicitly authenticated state.
    public async Task ImportConfigurationWithPassphraseAsync(MoveItSessionState authenticatedState, string configXml, string passphrase, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authenticatedState);
        if (string.IsNullOrWhiteSpace(configXml)) throw new ArgumentException("Configuration XML is required.", nameof(configXml));
        if (string.IsNullOrWhiteSpace(passphrase)) throw new ArgumentException("Configuration passphrase is required.", nameof(passphrase));

        var body = new JsonObject
        {
            ["configXml"] = configXml,
            ["passphrase"] = passphrase
        };
        using var response = await SendAsync(authenticatedState, HttpMethod.Post, "/api/v1/config/import", body, cancellationToken, acceptJson: false);
        await EnsureSuccessWithoutReadingBodyAsync(response, cancellationToken);
    }

    // Imports a Server-Key-protected configuration using an explicitly authenticated state.
    public async Task ImportConfigurationWithServerKeyAsync(MoveItSessionState authenticatedState, string configXml, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authenticatedState);
        if (string.IsNullOrWhiteSpace(configXml)) throw new ArgumentException("Configuration XML is required.", nameof(configXml));
        using var response = await SendXmlAsync(authenticatedState, HttpMethod.Put, "/api/v1/config", configXml, cancellationToken);
        await EnsureSuccessWithoutReadingBodyAsync(response, cancellationToken);
    }

    // Revokes the current token when possible and clear all in-memory MOVEIt state, including the retained password.
    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        var state = _sessionService.Get();
        if (state is null)
        {
            _sessionService.Clear();
            return;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(state.AccessToken))
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(state.WebAdminUrl, "/api/v1/token/revoke"))
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = state.AccessToken })
                };
                await MoveItGridConnectionHandler.SendBufferedAsync(CreateClient(), request, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "MOVEit token revocation failed during logout; local state will still be cleared.");
        }
        finally
        {
            _sessionService.Clear();
        }
    }

    // Retrieves the lightweight Task inventory used by counts and lookup surfaces.
    public async Task<IReadOnlyList<JsonObject>> GetTasksAsync(CancellationToken cancellationToken = default)
    {
        const string fields = "ID,Name,Info,Active,TT,Scheduled,Group,xAddEditDelete,xAlter,xRun";
        var root = await SendForJsonObjectAsync(HttpMethod.Get, $"/api/v1/tasks?fields={Uri.EscapeDataString(fields)}", null, cancellationToken);
        return GetObjectArray(root, "items");
    }

    // Retrieves the normal Task-grid inventory in one request. Schedules are included here so displaying the
    // human-readable Schedule column does not add a second MOVEit call to each authoritative Task refresh.
    public async Task<IReadOnlyList<JsonObject>> GetTaskGridDefinitionsAsync(CancellationToken cancellationToken = default)
    {
        const string fields = "ID,Name,Info,Active,TT,Scheduled,Schedules,Group,xAddEditDelete,xAlter,xRun";
        var root = await SendForJsonObjectAsync(HttpMethod.Get, $"/api/v1/tasks?fields={Uri.EscapeDataString(fields)}", null, cancellationToken);
        return GetObjectArray(root, "items");
    }

    // Retrieves only the documented Task fields needed to calculate Upcoming Runs.
    public async Task<IReadOnlyList<JsonObject>> GetTaskScheduleDefinitionsAsync(CancellationToken cancellationToken = default)
    {
        const string fields = "ID,Name,Info,Active,Scheduled,Schedules";
        var root = await SendForJsonObjectAsync(HttpMethod.Get, $"/api/v1/tasks?fields={Uri.EscapeDataString(fields)}", null, cancellationToken);
        return GetObjectArray(root, "items");
    }

    // Reads MOVEit's authoritative scheduler state. This is intentionally not cached because the scheduler
    // can also be changed outside MAUI through Web Admin or another API client.
    public async Task<string> GetSchedulerStatusAsync(CancellationToken cancellationToken = default)
    {
        var root = await SendForJsonObjectAsync(HttpMethod.Get, "/api/v1/tasks/scheduler", null, cancellationToken);
        var status = GetString(root, "status").Trim();
        return status.Length == 0 ? "SCHEDULER_STATUS_UNKNOWN" : status;
    }

    // Reads MOVEit's native failover/resiliency status. No state is inferred from status text: node health
    // comes only from NodeHasError, and primary/secondary status text is preserved exactly as returned.
    public async Task<MoveItFailoverStatus> GetFailoverStatusAsync(CancellationToken cancellationToken = default)
    {
        var root = await SendForJsonObjectAsync(HttpMethod.Get, "/api/v1/failoverstatus", null, cancellationToken);
        return ParseFailoverStatus(root);
    }

    // Enables the task scheduler through the documented REST scheduler-control endpoint.
    public async Task StartSchedulerAsync(CancellationToken cancellationToken = default)
    {
        await SendForTextAsync(HttpMethod.Put, "/api/v1/tasks/scheduler/start", null, cancellationToken);
    }

    // Disables new scheduled launches through the documented REST scheduler-control endpoint. MOVEit allows
    // already-running tasks to complete; this call does not stop the Automation service.
    public async Task StopSchedulerAsync(CancellationToken cancellationToken = default)
    {
        await SendForTextAsync(HttpMethod.Put, "/api/v1/tasks/scheduler/stop", null, cancellationToken);
    }

    // Native Web Admin sends this action without a body. The id/status/errorcode envelope
    // is the response acknowledgement, not input to the reset operation.
    public async Task ResetTamperDetectionAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendForJsonObjectAsync(HttpMethod.Post, "/api/v0/actions/resethashing", null, cancellationToken);
        var status = GetString(response, "status").Trim();
        var errorCode = GetNullableInt(response, "errorcode");
        if (!status.Equals("success", StringComparison.OrdinalIgnoreCase) || errorCode != 0)
        {
            var detail = GetString(response, "message").Trim();
            throw new MoveItApiException(detail.Length > 0
                ? $"MOVEit could not reset tamper detection: {detail}"
                : "MOVEit could not reset tamper detection.");
        }
    }

    public async Task<JsonObject> CheckoutGlobalsV0Async(CancellationToken cancellationToken = default)
    {
        var response = await SendCheckoutDefinitionAsync("/api/v0/globals/checkout", null, cancellationToken);
        ValidateCheckoutAcquisition(response);
        return response;
    }

    public Task<JsonObject> UpdateGlobalsV0Async(JsonObject globals, CancellationToken cancellationToken = default)
        => SendForJsonObjectAsync(HttpMethod.Put, "/api/v0/globals", globals, cancellationToken);

    // Lockout operations are v1 reads/deletes, with no Globals or object checkout.
    public Task<JsonObject> GetSystemLockoutsV1Async(string kind, CancellationToken cancellationToken = default)
        => SendForJsonObjectAsync(HttpMethod.Get, SystemLockoutsPath(kind), null, cancellationToken);

    public async Task UnlockSystemLockoutsV1Async(string kind, IReadOnlyList<string> values, CancellationToken cancellationToken = default)
    {
        var path = SystemLockoutsPath(kind);
        if (values is null || values.Count == 0 || values.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Select one or more lockouts to unlock.");
        var body = new JsonArray(values.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());
        // The native contract is a bare string array. Accept empty success bodies; the service reads back.
        _ = await SendForTextAsync(HttpMethod.Delete, path, body, cancellationToken);
    }

    private static string SystemLockoutsPath(string kind) => kind switch
    {
        "ip" => "/api/v1/lockouts/ip", "user" => "/api/v1/lockouts/user",
        _ => throw new ArgumentException("Select IP addresses or users to unlock.")
    };

    public Task<JsonObject> GetSystemLockoutPolicyV0Async(CancellationToken cancellationToken = default)
        => SendForJsonObjectAsync(HttpMethod.Get, "/api/v0/systemSettings/iplockout", null, cancellationToken);

    // Native POST returns an empty successful body. The service verifies via a fresh GET.
    public async Task UpdateSystemLockoutPolicyV0Async(JsonObject policy, CancellationToken cancellationToken = default)
        => _ = await SendForTextAsync(HttpMethod.Post, "/api/v0/systemSettings/iplockout", policy, cancellationToken);

    public Task<JsonObject> GetSystemTrustedIPsV0Async(CancellationToken cancellationToken = default)
        => SendForJsonObjectAsync(HttpMethod.Get, "/api/v0/systemSettings/trustedip", null, cancellationToken);

    public async Task AddSystemTrustedIPV0Async(string address, CancellationToken cancellationToken = default)
        => _ = await SendForTextAsync(HttpMethod.Post, "/api/v0/systemSettings/trustedip", TrustedIPBody(address), cancellationToken);

    public async Task RemoveSystemTrustedIPV0Async(string address, CancellationToken cancellationToken = default)
        => _ = await SendForTextAsync(HttpMethod.Delete, "/api/v0/systemSettings/trustedip", TrustedIPBody(address), cancellationToken);

    private static JsonObject TrustedIPBody(string address) => new() { ["TrustedIP"] = new JsonArray(JsonValue.Create(address)) };

    // Reuse normal checkout cleanup, including explicit native already-unlocked acknowledgements.
    public async Task ReleaseGlobalsCheckoutV0Async(CancellationToken cancellationToken = default)
        => _ = await SendForTextAsync(HttpMethod.Delete, "/api/v0/globals/checkout", null, cancellationToken, checkoutRelease: true);

    // Performs a live, case-insensitive exact-name lookup before COM creates a persistent Task.
    // MOVEit treats a name without wildcard characters as an exact search; an explicit comparison is
    // still performed so MAUI never mistakes a broader/unexpected server response for a duplicate.
    // Purpose: Perform the live exact-name duplicate check used before task creation.
    public async Task<bool> TaskNameExistsAsync(string taskName, CancellationToken cancellationToken = default)
    {
        var normalizedName = (taskName ?? string.Empty).Trim();
        if (normalizedName.Length == 0) return false;

        var matches = await GetTasksByExactNameAsync(normalizedName, cancellationToken);
        return matches.Count > 0;
    }

    // Excludes the current Task ID during rename validation so an unchanged
    // name (or a case-only rename) is allowed while a collision with any other Task is rejected.
    // Purpose: Perform the live duplicate-name check used while renaming an existing task.
    public async Task<bool> TaskNameExistsForOtherTaskAsync(
        string taskName,
        string taskId,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = (taskName ?? string.Empty).Trim();
        var normalizedTaskId = (taskId ?? string.Empty).Trim();
        if (normalizedName.Length == 0) return false;
        if (normalizedTaskId.Length == 0) throw new ArgumentException("Task ID is required.", nameof(taskId));

        var matches = await GetTasksByExactNameAsync(normalizedName, cancellationToken);
        return matches.Any(task =>
            !string.Equals(GetString(task, "ID").Trim(), normalizedTaskId, StringComparison.OrdinalIgnoreCase));
    }

    // Purpose: Retrieve tasks by exact name.
    private async Task<IReadOnlyList<JsonObject>> GetTasksByExactNameAsync(
        string normalizedName,
        CancellationToken cancellationToken)
    {
        const string fields = "ID,Name";
        var relativeUrl = $"/api/v1/tasks?fields={Uri.EscapeDataString(fields)}&name={Uri.EscapeDataString(normalizedName)}";
        var root = await SendForJsonObjectAsync(HttpMethod.Get, relativeUrl, null, cancellationToken);
        return GetObjectArray(root, "items")
            .Where(task => string.Equals(GetString(task, "Name").Trim(), normalizedName, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    // Retrieves the complete custom Script inventory visible to the current MOVEit user.
    public Task<IReadOnlyList<JsonObject>> GetCustomScriptsAsync(CancellationToken cancellationToken = default)
    {
        const string fields = "ID,Name,Description,Lang,Group,xAddEditDelete";
        return SendForJsonObjectArrayAsync(HttpMethod.Get, $"/api/v1/customscripts?fields={Uri.EscapeDataString(fields)}", null, cancellationToken);
    }

    // Performs a live, case-insensitive exact-name check immediately before creation.
    public async Task<bool> CustomScriptNameExistsAsync(string scriptName, CancellationToken cancellationToken = default)
    {
        var normalizedName = (scriptName ?? string.Empty).Trim();
        if (normalizedName.Length == 0) return false;

        const string fields = "ID,Name";
        var relativeUrl = $"/api/v1/customscripts?fields={Uri.EscapeDataString(fields)}&name={Uri.EscapeDataString(normalizedName)}";
        var scripts = await SendForJsonObjectArrayAsync(HttpMethod.Get, relativeUrl, null, cancellationToken);
        return scripts.Any(item =>
            string.Equals(GetString(item, "Name").Trim(), normalizedName, StringComparison.OrdinalIgnoreCase));
    }

    // Retrieves one custom Script definition by persistent ID.
    public Task<JsonObject> GetCustomScriptAsync(string scriptId, CancellationToken cancellationToken = default)
    {
        var normalizedScriptId = (scriptId ?? string.Empty).Trim();
        if (normalizedScriptId.Length == 0) throw new ArgumentException("Script ID is required.", nameof(scriptId));
        return SendForJsonObjectAsync(HttpMethod.Get, $"/api/v1/customscripts/{Uri.EscapeDataString(normalizedScriptId)}", null, cancellationToken);
    }

    // Script writes use the supplied native Web Admin v0 envelope. Checkout follows
    // the shared native object lifecycle; failed acquisition never permits an unlocked PUT.
    public Task<JsonObject> CreateCustomScriptV0Async(JsonObject body, CancellationToken cancellationToken = default)
        => SendForJsonObjectAsync(HttpMethod.Post, "/api/v0/customscripts", body, cancellationToken);
    public Task<JsonObject> UpdateCustomScriptV0Async(string id, JsonObject body, CancellationToken cancellationToken = default)
        => SendForJsonObjectAsync(HttpMethod.Put, $"/api/v0/customscripts/{Resources.Scripts.ScriptConfiguration.Id(id)}", body, cancellationToken);
    public async Task CheckoutCustomScriptV0Async(string id, CancellationToken cancellationToken = default)
    {
        var response = await SendCheckoutDefinitionAsync($"/api/v0/customscripts/{Resources.Scripts.ScriptConfiguration.Id(id)}/checkout", null, cancellationToken);
        ValidateCheckoutAcquisition(response);
    }
    public async Task ReleaseCustomScriptCheckoutV0Async(string id, CancellationToken cancellationToken = default)
        => _ = await SendForTextAsync(HttpMethod.Delete, $"/api/v0/customscripts/{Resources.Scripts.ScriptConfiguration.Id(id)}/checkout", null, cancellationToken, checkoutRelease: true);

    // Date List writes use the supplied native Web Admin v0 envelope. Checkout follows
    // the shared native object lifecycle; failed acquisition never permits an unlocked PUT.
    public Task<JsonObject> CreateDateListV0Async(JsonObject body, CancellationToken cancellationToken = default)
        => SendForJsonObjectAsync(HttpMethod.Post, "/api/v0/datelists", body, cancellationToken);
    public Task<JsonObject> UpdateDateListV0Async(string id, JsonObject body, CancellationToken cancellationToken = default)
        => SendForJsonObjectAsync(HttpMethod.Put, $"/api/v0/datelists/{Resources.DateLists.DateListConfiguration.Id(id)}", body, cancellationToken);
    public async Task CheckoutDateListV0Async(string id, CancellationToken cancellationToken = default)
    {
        var response = await SendCheckoutDefinitionAsync($"/api/v0/datelists/{Resources.DateLists.DateListConfiguration.Id(id)}/checkout", null, cancellationToken);
        ValidateCheckoutAcquisition(response);
    }
    public async Task ReleaseDateListCheckoutV0Async(string id, CancellationToken cancellationToken = default)
        => _ = await SendForTextAsync(HttpMethod.Delete, $"/api/v0/datelists/{Resources.DateLists.DateListConfiguration.Id(id)}/checkout", null, cancellationToken, checkoutRelease: true);

    // Retrieves the built-in/standard Script inventory. Standard scripts are read-only in MAUI.
    public Task<IReadOnlyList<JsonObject>> GetStandardScriptsAsync(CancellationToken cancellationToken = default)
    {
        // The published Standard Script model does not document a language field even though Web Admin
        // distinguishes script engines. Request the documented display fields plus the language/type
        // field names used by MOVEit models across releases. Unknown field names are ignored by the API,
        // keeping the recurring inventory response small while allowing installed versions to expose a hint.
        const string fields = "ID,Name,Description,Group,xAddEditDelete,ProcessDesc,Lang,Language,ScriptLanguage,ScriptType,Type";
        return SendForJsonObjectArrayAsync(HttpMethod.Get, $"/api/v1/standardscripts?fields={Uri.EscapeDataString(fields)}", null, cancellationToken);
    }

    // Performs a live, case-insensitive exact-name check against built-in/standard Scripts.
    public async Task<bool> StandardScriptNameExistsAsync(string scriptName, CancellationToken cancellationToken = default)
    {
        var normalizedName = (scriptName ?? string.Empty).Trim();
        if (normalizedName.Length == 0) return false;

        const string fields = "ID,Name";
        var relativeUrl = $"/api/v1/standardscripts?fields={Uri.EscapeDataString(fields)}&name={Uri.EscapeDataString(normalizedName)}";
        var scripts = await SendForJsonObjectArrayAsync(HttpMethod.Get, relativeUrl, null, cancellationToken);
        return scripts.Any(item =>
            string.Equals(GetString(item, "Name").Trim(), normalizedName, StringComparison.OrdinalIgnoreCase));
    }

    // Retrieves one built-in/standard Script definition by persistent ID.
    public Task<JsonObject> GetStandardScriptAsync(string scriptId, CancellationToken cancellationToken = default)
    {
        var normalizedScriptId = (scriptId ?? string.Empty).Trim();
        if (normalizedScriptId.Length == 0) throw new ArgumentException("Script ID is required.", nameof(scriptId));
        return SendForJsonObjectAsync(HttpMethod.Get, $"/api/v1/standardscripts/{Uri.EscapeDataString(normalizedScriptId)}", null, cancellationToken);
    }

    // Retrieves only the Task fields needed to locate ScriptID references for Delete guardrails.
    public async Task<IReadOnlyList<JsonObject>> GetTaskScriptReferenceDefinitionsAsync(CancellationToken cancellationToken = default)
    {
        const string fields = "ID,Name,steps,NextActions";
        var root = await SendForJsonObjectAsync(HttpMethod.Get, $"/api/v1/tasks?fields={Uri.EscapeDataString(fields)}", null, cancellationToken);
        return GetObjectArray(root, "items");
    }

    // Macro references can occur in task/process parameters as well as transfer and next-action fields.
    public async Task<IReadOnlyList<JsonObject>> GetTaskGlobalParameterReferenceDefinitionsAsync(CancellationToken cancellationToken = default)
    {
        const string fields = "ID,Name,Parameters,steps,NextActions";
        var root = await SendForJsonObjectAsync(HttpMethod.Get, $"/api/v1/tasks?fields={Uri.EscapeDataString(fields)}", null, cancellationToken);
        return GetObjectArray(root, "items");
    }

    // Date List inventory and dependency reads. Entries are omitted from the recurring list call.
    public Task<IReadOnlyList<JsonObject>> GetDateListsAsync(CancellationToken cancellationToken = default)
        => SendForJsonObjectArrayAsync(HttpMethod.Get, $"/api/v1/datelists?fields={Uri.EscapeDataString("ID,Name,Description,Group,xAddEditDelete")}", null, cancellationToken);

    // Retrieves Date Lists with their entries so schedule include/exclude references can be evaluated locally.
    public Task<IReadOnlyList<JsonObject>> GetDateListsWithEntriesAsync(CancellationToken cancellationToken = default)
        => SendForJsonObjectArrayAsync(HttpMethod.Get, $"/api/v1/datelists?fields={Uri.EscapeDataString("ID,Name,Entries")}", null, cancellationToken);

    // Purpose: Returns date list.
    public Task<JsonObject> GetDateListAsync(string dateListId, CancellationToken cancellationToken = default)
    {
        var id = (dateListId ?? string.Empty).Trim();
        if (id.Length == 0) throw new ArgumentException("Date List ID is required.", nameof(dateListId));
        return SendForJsonObjectAsync(HttpMethod.Get, $"/api/v1/datelists/{Uri.EscapeDataString(id)}", null, cancellationToken);
    }

    // Purpose: Checks whether a Date List already uses the requested name.
    public async Task<bool> DateListNameExistsAsync(string name, CancellationToken cancellationToken = default)
    {
        var value = (name ?? string.Empty).Trim();
        if (value.Length == 0) return false;
        var items = await SendForJsonObjectArrayAsync(HttpMethod.Get, $"/api/v1/datelists?fields=ID%2CName&name={Uri.EscapeDataString(value)}", null, cancellationToken);
        return items.Any(item => string.Equals(GetString(item, "Name"), value, StringComparison.OrdinalIgnoreCase));
    }

    // Purpose: Returns task date list reference definitions.
    public async Task<IReadOnlyList<JsonObject>> GetTaskDateListReferenceDefinitionsAsync(CancellationToken cancellationToken = default)
    {
        const string fields = "ID,Name,Schedules";
        var root = await SendForJsonObjectAsync(HttpMethod.Get, $"/api/v1/tasks?fields={Uri.EscapeDataString(fields)}", null, cancellationToken);
        return GetObjectArray(root, "items");
    }

    // Global Parameters are exposed as a list-result rather than individual ID-based configuration objects.
    public async Task<IReadOnlyList<JsonObject>> GetGlobalParametersAsync(CancellationToken cancellationToken = default)
    {
        var root = await SendForJsonObjectAsync(HttpMethod.Get, "/api/v1/globalparameters", null, cancellationToken);
        return GetObjectArray(root, "items");
    }

    // Purpose: Returns global parameter.
    public Task<JsonObject> GetGlobalParameterAsync(string name, CancellationToken cancellationToken = default)
    {
        var normalized = (name ?? string.Empty).Trim();
        if (normalized.Length == 0) throw new ArgumentException("Global Parameter Name is required.", nameof(name));
        return SendForJsonObjectAsync(HttpMethod.Get, $"/api/v1/globalparameters/{Uri.EscapeDataString(normalized)}", null, cancellationToken);
    }

    // Purpose: Checks whether a Global Parameter already uses the requested name.
    public async Task<bool> GlobalParameterNameExistsAsync(string name, string? excludeName = null, CancellationToken cancellationToken = default)
    {
        var normalized = (name ?? string.Empty).Trim();
        if (normalized.Length == 0) return false;
        var excluded = (excludeName ?? string.Empty).Trim();
        var parameters = await GetGlobalParametersAsync(cancellationToken);
        return parameters.Any(item =>
        {
            var itemName = GetString(item, "name").Trim();
            if (itemName.Length == 0) itemName = GetString(item, "Name").Trim();
            if (excluded.Length > 0 && itemName.Equals(excluded, StringComparison.OrdinalIgnoreCase)) return false;
            return itemName.Equals(normalized, StringComparison.OrdinalIgnoreCase);
        });
    }

    // Keys and Certificates inventory uses the documented v1 read endpoints.
    public Task<IReadOnlyList<JsonObject>> GetSshKeysAsync(CancellationToken cancellationToken = default)
    {
        const string fields = "ID,Name,Fingerprint,PublicKeyOpenSSH,PublicKeySSH,Group,xAddEditDelete";
        return SendForJsonObjectArrayAsync(HttpMethod.Get, $"/api/v1/sshkeys?fields={Uri.EscapeDataString(fields)}", null, cancellationToken);
    }

    // Purpose: Returns PGP keys.
    public Task<IReadOnlyList<JsonObject>> GetPgpKeysAsync(CancellationToken cancellationToken = default)
    {
        const string fields = "ID,Name,uid,Fingerprint,KeyLength,KeyType,PubPriv,SymAlg,Created,Expires,Expired,Disabled,Revoked,Status,Group,xAddEditDelete";
        return SendForJsonObjectArrayAsync(HttpMethod.Get, $"/api/v1/pgpkeys?fields={Uri.EscapeDataString(fields)}", null, cancellationToken);
    }

    public Task<JsonObject> GetSshEncryptionListV0Async(CancellationToken cancellationToken = default)
        => SendForJsonObjectAsync(HttpMethod.Get, "/api/v0/ssh/encryptionlist", null, cancellationToken);

    public Task<JsonObject> GetSshDefaultKexAlgorithmsV0Async(CancellationToken cancellationToken = default)
        => SendForJsonObjectAsync(HttpMethod.Get, "/api/v0/sshdefaultkexalgs", null, cancellationToken);

    // Purpose: Returns TLS certificates.
    public Task<JsonObject> GetHostTlsCertificatesV0Async(CancellationToken cancellationToken = default)
        => SendForJsonObjectAsync(HttpMethod.Get, "/api/v0/sslcerts", null, cancellationToken);

    public Task<IReadOnlyList<JsonObject>> GetTlsCertificatesAsync(CancellationToken cancellationToken = default)
    {
        // TLS certificate responses have varied slightly in property casing across MOVEit 2025.x builds.
        // Request the complete objects here rather than depending on a case-sensitive fields projection; the
        // combined resource service normalizes the returned property names before building grid rows.
        return SendForJsonObjectArrayAsync(HttpMethod.Get, "/api/v1/sslcerts", null, cancellationToken);
    }

    // Native exports return attachment bytes, not the JSON objects used by normal CRUD calls.
    public Task<byte[]> ExportTlsCertificateV0Async(JsonObject request, CancellationToken cancellationToken = default)
        => SendKeyExportAsync("/api/v0/sslcerts/export", request, cancellationToken);
    public Task<byte[]> ExportPgpKeyV0Async(JsonObject request, CancellationToken cancellationToken = default)
        => SendKeyExportAsync("/api/v0/pgpkeys/export", request, cancellationToken);

    private async Task<byte[]> SendKeyExportAsync(string path, JsonObject body, CancellationToken cancellationToken)
    {
        try
        {
            var state = await GetValidSessionAsync(cancellationToken);
            using var first = await SendAsync(state, HttpMethod.Post, path, body, cancellationToken, acceptJson: false);
            if (first.StatusCode == HttpStatusCode.Unauthorized)
            {
                state = await RenewSessionAsync(state, cancellationToken, forceRenewal: true);
                using var retry = await SendAsync(state, HttpMethod.Post, path, body, cancellationToken, acceptJson: false);
                return await ReadKeyExportAsync(retry, cancellationToken);
            }
            return await ReadKeyExportAsync(first, cancellationToken);
        }
        catch (MoveItAuthenticationException) { _sessionService.Clear(); throw; }
    }

    private static async Task<byte[]> ReadKeyExportAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        // Never include export response bodies in exceptions: they can contain private key material.
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new MoveItAuthenticationException("MOVEit session expired while exporting the key or certificate.", response.StatusCode);
        if (!response.IsSuccessStatusCode)
            throw new MoveItApiException($"MOVEit could not export the key or certificate (HTTP {(int)response.StatusCode}). Check export permissions, private-key availability and the export password.", response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length == 0) throw new MoveItApiException("MOVEit returned an empty key or certificate export.");
        return bytes;
    }

    // Purpose: Checks whether an SSH client key already uses the requested name.
    public async Task<bool> SshKeyNameExistsAsync(string name, CancellationToken cancellationToken = default)
    {
        var normalized = (name ?? string.Empty).Trim();
        if (normalized.Length == 0) return false;
        const string fields = "ID,Name";
        var items = await SendForJsonObjectArrayAsync(HttpMethod.Get, $"/api/v1/sshkeys?fields={Uri.EscapeDataString(fields)}&name={Uri.EscapeDataString(normalized)}", null, cancellationToken);
        return items.Any(item => GetString(item, "Name").Trim().Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    // The native 2025.x Web Admin posts this payload to /api/v0/sshkeys. This is intentionally
    // kept separate from the documented v1 read surface so later MOVEit versions can replace it cleanly.
    public async Task CreateSshKeyV0Async(string name, string keyType, int keySize, CancellationToken cancellationToken = default, string? resourceGroup = null)
    {
        var body = new JsonObject
        {
            ["name"] = name,
            ["keytype"] = keyType,
            ["keysize"] = keySize,
            ["Group"] = string.IsNullOrWhiteSpace(resourceGroup) ? new JsonArray() : new JsonArray(resourceGroup.Trim())
        };
        await SendForTextAsync(HttpMethod.Post, "/api/v0/sshkeys", body, cancellationToken);
    }

    // The native 2025.x Web Admin creates TLS certificates through /api/v0/sslcerts.
    // The payload mirrors the browser request captured from the native UI.
    public async Task CreateTlsCertificateV0Async(string subject, string expirationDate, int keySize, CancellationToken cancellationToken = default, string? resourceGroup = null)
    {
        var normalizedSubject = (subject ?? string.Empty).Trim();
        var normalizedExpirationDate = (expirationDate ?? string.Empty).Trim();
        if (normalizedSubject.Length == 0) throw new ArgumentException("TLS Certificate subject is required.", nameof(subject));
        if (normalizedExpirationDate.Length == 0) throw new ArgumentException("TLS Certificate expiration date is required.", nameof(expirationDate));

        var body = new JsonObject
        {
            ["subject"] = normalizedSubject,
            ["expdate"] = normalizedExpirationDate,
            ["keysize"] = keySize,
            ["Group"] = string.IsNullOrWhiteSpace(resourceGroup) ? new JsonArray() : new JsonArray(resourceGroup.Trim())
        };
        await SendForTextAsync(HttpMethod.Post, "/api/v0/sslcerts", body, cancellationToken);
    }

    // The native 2025.x Web Admin creates PGP keypairs through /api/v0/pgpkeys.
    // Keep the captured v0 payload contract isolated from the public v1 inventory surface.
    public async Task CreatePgpKeyV0Async(
        string uid,
        string keyType,
        int keyLength,
        string hashAlgorithm,
        int expiresAfter,
        string expireUnit,
        string passphrase,
        CancellationToken cancellationToken = default, string? resourceGroup = null)
    {
        var normalizedUid = (uid ?? string.Empty).Trim();
        if (normalizedUid.Length == 0) throw new ArgumentException("PGP Key UID is required.", nameof(uid));

        var body = new JsonObject
        {
            ["uid"] = normalizedUid,
            ["keyLength"] = keyLength,
            ["keyType"] = keyType,
            ["hashAlg"] = hashAlgorithm,
            ["expiresin"] = expiresAfter,
            ["expireunit"] = expireUnit,
            ["passphrase"] = passphrase ?? string.Empty,
            ["Group"] = string.IsNullOrWhiteSpace(resourceGroup) ? new JsonArray() : new JsonArray(resourceGroup.Trim())
        };

        await SendForTextAsync(HttpMethod.Post, "/api/v0/pgpkeys", body, cancellationToken);
    }

    // The native Web Admin import surfaces use internal v0 contracts. Keep the three payloads
    // separate because TLS is multipart/binary while SSH and PGP are JSON. Sensitive key material
    // and passwords are never logged by this client.
    public async Task ImportSshKeyV0Async(
        string name,
        string password,
        string privateKey,
        CancellationToken cancellationToken = default, string? resourceGroup = null)
    {
        var normalizedName = (name ?? string.Empty).Trim();
        if (normalizedName.Length == 0) throw new ArgumentException("SSH Key Name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(privateKey)) throw new ArgumentException("SSH private key material is required.", nameof(privateKey));

        var body = new JsonObject
        {
            ["Name"] = normalizedName,
            ["Group"] = string.IsNullOrWhiteSpace(resourceGroup) ? new JsonArray() : new JsonArray(resourceGroup.Trim()),
            ["password"] = password ?? string.Empty,
            ["privatekey"] = privateKey
        };

        // This endpoint intentionally matches the Web Admin request captured from MOVEit 2025.x.
        await SendForTextAsync(HttpMethod.Post, "/api/v0/sslcerts/import", body, cancellationToken);
    }

    // Purpose: Imports TLS certificate after validating the package and target.
    public async Task ImportTlsCertificateV0Async(
        string fileName,
        byte[] certificateBytes,
        bool isPublicOnly,
        string password,
        CancellationToken cancellationToken = default, string? resourceGroup = null)
    {
        var safeFileName = Path.GetFileName(fileName ?? string.Empty);
        if (safeFileName.Length == 0) throw new ArgumentException("TLS certificate file name is required.", nameof(fileName));
        if (certificateBytes is null || certificateBytes.Length == 0) throw new ArgumentException("TLS certificate file is empty.", nameof(certificateBytes));

        try
        {
            var state = await GetValidSessionAsync(cancellationToken);
            using var firstResponse = await SendTlsImportAsync(state, safeFileName, certificateBytes, isPublicOnly, password ?? string.Empty, cancellationToken, resourceGroup);
            if (firstResponse.StatusCode == HttpStatusCode.Unauthorized)
            {
                state = await RenewSessionAsync(state, cancellationToken, forceRenewal: true);
                using var retryResponse = await SendTlsImportAsync(state, safeFileName, certificateBytes, isPublicOnly, password ?? string.Empty, cancellationToken, resourceGroup);
                await ParseTextResponseAsync(retryResponse, cancellationToken);
                return;
            }

            await ParseTextResponseAsync(firstResponse, cancellationToken);
        }
        catch (MoveItAuthenticationException)
        {
            _sessionService.Clear();
            throw;
        }
    }

    // Purpose: Imports PGP key after validating the package and target.
    public async Task ImportPgpKeyV0Async(byte[] keyMaterial, string passphrase, CancellationToken cancellationToken = default, string? resourceGroup = null)
    {
        if (keyMaterial is null || keyMaterial.Length == 0) throw new ArgumentException("PGP key material is required.", nameof(keyMaterial));

        var material = new JsonArray();
        foreach (var value in keyMaterial) material.Add((int)value);

        var body = new JsonObject
        {
            ["Group"] = string.IsNullOrWhiteSpace(resourceGroup) ? new JsonArray() : new JsonArray(resourceGroup.Trim()),
            ["passphrase"] = passphrase ?? string.Empty,
            ["keymaterial"] = material
        };

        await SendForTextAsync(HttpMethod.Post, "/api/v0/pgpkeys/import", body, cancellationToken);
    }

    // Purpose: Sends the multipart TLS certificate import request to the native Web Admin endpoint.
    private async Task<HttpResponseMessage> SendTlsImportAsync(
        MoveItSessionState state,
        string fileName,
        byte[] certificateBytes,
        bool isPublicOnly,
        string password,
        CancellationToken cancellationToken, string? resourceGroup)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(state.WebAdminUrl, "/api/v0/sslcerts/import"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", state.AccessToken);

        var form = new MultipartFormDataContent();
        var certContent = new ByteArrayContent(certificateBytes);
        certContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(certContent, "cert", fileName);
        form.Add(new StringContent(isPublicOnly ? "true" : "false", Encoding.UTF8), "ispubliconly");
        form.Add(new StringContent(password ?? string.Empty, Encoding.UTF8), "password");
        form.Add(new StringContent((string.IsNullOrWhiteSpace(resourceGroup) ? new JsonArray() : new JsonArray(resourceGroup.Trim())).ToJsonString(), Encoding.UTF8), "Group");
        request.Content = form;

        return await CreateClient().SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    // Purpose: Deletes PGP key after applying the required guardrails.
    public async Task DeletePgpKeyV0Async(string id, CancellationToken cancellationToken = default)
    {
        var normalized = (id ?? string.Empty).Trim();
        if (normalized.Length == 0) throw new ArgumentException("PGP Key ID is required.", nameof(id));
        await SendForTextAsync(HttpMethod.Delete, $"/api/v0/pgpkeys/{Uri.EscapeDataString(normalized)}", null, cancellationToken);
    }

    // Purpose: Deletes TLS certificate after applying the required guardrails.
    public async Task DeleteTlsCertificateV0Async(string identity, CancellationToken cancellationToken = default)
    {
        var normalized = (identity ?? string.Empty).Trim();
        if (normalized.Length == 0) throw new ArgumentException("TLS Certificate identity is required.", nameof(identity));
        await SendForTextAsync(HttpMethod.Delete, $"/api/v0/sslcerts/{Uri.EscapeDataString(normalized)}", null, cancellationToken);
    }

    // Purpose: Returns task key certificate reference definitions.
    public async Task<IReadOnlyList<JsonObject>> GetTaskKeyCertificateReferenceDefinitionsAsync(CancellationToken cancellationToken = default)
    {
        // Dependency checks are destructive-operation guardrails. Request complete visible Task definitions
        // here rather than guessing which task shapes may contain key/certificate references. This path is
        // used only for Information/deletion preflight, not the recurring grid refresh.
        var root = await SendForJsonObjectAsync(HttpMethod.Get, "/api/v1/tasks", null, cancellationToken);
        return GetObjectArray(root, "items");
    }

    // Purpose: Returns host key certificate reference definitions.
    public Task<IReadOnlyList<JsonObject>> GetHostKeyCertificateReferenceDefinitionsAsync(CancellationToken cancellationToken = default)
        => SendForJsonObjectArrayAsync(HttpMethod.Get, "/api/v1/hosts", null, cancellationToken);

    // Retrieves Resource Groups without taking the native v0 edit checkout.
    public Task<IReadOnlyList<JsonObject>> GetResourceGroupsAsync(CancellationToken cancellationToken = default)
        => SendForJsonObjectArrayAsync(HttpMethod.Get, "/api/v1/resourcegroups?fields=Name%2CNotes", null, cancellationToken);

    // Read the complete v1 inventory without acquiring the dataset's edit checkout.
    public Task<IReadOnlyList<JsonObject>> GetResourceGroupDefinitionsAsync(CancellationToken cancellationToken = default)
        => ReadResourceGroupPagesAsync("/api/v1/resourcegroups", cancellationToken);

    public Task<IReadOnlyList<JsonObject>> GetResourceGroupAclDefinitionsAsync(CancellationToken cancellationToken = default)
        => ReadResourceGroupPagesAsync("/api/v1/resourcegroups/acls", cancellationToken);

    // Local/domain group discovery shares the complete, validated paging reader with Resource Groups/ACLs.
    public Task<IReadOnlyList<JsonObject>> GetUserGroupDefinitionsAsync(string type, CancellationToken cancellationToken = default)
    {
        if (type is not ("local" or "remote")) throw new ArgumentException("User Group Type must be Local or Domain.", nameof(type));
        return ReadResourceGroupPagesAsync("/api/v1/usergroups?type=" + type, cancellationToken);
    }

    public Task<JsonObject> CreateResourceGroupAclAsync(JsonObject acl, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(acl);
        return SendForJsonObjectAsync(HttpMethod.Post, "/api/v1/resourcegroups/acls", acl, cancellationToken);
    }

    public async Task DeleteResourceGroupAclAsync(string aclId, CancellationToken cancellationToken = default)
    {
        if (!ulong.TryParse(aclId, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _))
            throw new ArgumentException("A valid ACL ID is required.", nameof(aclId));
        await SendForTextAsync(HttpMethod.Delete, "/api/v1/resourcegroups/acls/" + aclId, null, cancellationToken);
    }

    public async Task UpdateResourceGroupAclAsync(string aclId, JsonObject acl, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(acl);
        if (!ulong.TryParse(aclId, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _)
            || acl["ACLID"]?.ToString() != aclId)
            throw new ArgumentException("A matching ACL ID is required.", nameof(aclId));
        // Native PUT acknowledgements need not contain JSON (204 is also a successful update).
        await SendForTextAsync(HttpMethod.Put, "/api/v1/resourcegroups/acls/" + aclId, acl, cancellationToken);
    }

    private async Task<IReadOnlyList<JsonObject>> ReadResourceGroupPagesAsync(string endpoint, CancellationToken cancellationToken)
    {
        var items = new List<JsonObject>();
        int? expectedTotal = null;
        for (var page = 1; page <= 10000; page++)
        {
            var root = await SendForJsonObjectAsync(HttpMethod.Get, $"{endpoint}{(endpoint.Contains('?') ? '&' : '?')}page={page}&perPage=100", null, cancellationToken);
            if (root["items"] is not JsonArray rows || rows.Any(row => row is not JsonObject))
                throw new InvalidOperationException("MOVEit returned an invalid Resource Group inventory. Refresh to try again.");
            items.AddRange(rows.Cast<JsonObject>());
            // A server without pagination metadata is an unpaged response. Never guess at missing pages.
            if (root["paging"] is null) return items;
            if (root["paging"] is not JsonObject paging)
                throw new InvalidOperationException("MOVEit returned invalid Resource Group paging metadata. Refresh to try again.");
            if (!int.TryParse(paging["page"]?.ToString(), out var current) || current != page
                || !int.TryParse(paging["totalPages"]?.ToString(), out var totalPages) || totalPages < 0
                || !int.TryParse(paging["totalItems"]?.ToString(), out var totalItems) || totalItems < 0
                || (expectedTotal.HasValue && expectedTotal.Value != totalItems))
                throw new InvalidOperationException("The Resource Group inventory changed while loading. Refresh to load a complete view.");
            expectedTotal = totalItems;
            if (page >= totalPages)
            {
                if (items.Count != totalItems)
                    throw new InvalidOperationException("MOVEit returned an incomplete Resource Group inventory. Refresh to try again.");
                return items;
            }
            if (rows.Count == 0) break;
        }
        throw new InvalidOperationException("MOVEit could not provide a complete Resource Group inventory. Refresh to try again.");
    }

    // Checks out the complete Resource Group dataset. The v0 checkout contract binds to
    // ObjectCheckoutInput, so the request body must be a JSON object ({}), not an array ([]).
    public Task<JsonObject> CheckoutResourceGroupsV0Async(CancellationToken cancellationToken = default)
        => SendCheckoutDefinitionAsync("/api/v0/resourcegroups/checkout", new JsonObject(), cancellationToken);

    // Commits the complete checked-out Resource Group dataset. Partial Resource Group PUTs are not used.
    public Task<JsonObject> UpdateResourceGroupsV0Async(JsonObject resourceGroups, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resourceGroups);
        return SendForJsonObjectAsync(HttpMethod.Put, "/api/v0/resourcegroups", resourceGroups, cancellationToken);
    }

    // Retrieves the complete Host inventory visible to the current MOVEit user.
    public Task<IReadOnlyList<JsonObject>> GetHostsAsync(CancellationToken cancellationToken = default)
        => SendForJsonObjectArrayAsync(HttpMethod.Get, "/api/v1/hosts", null, cancellationToken);

    // Performs a live, case-insensitive exact-name check before shared Host creation.
    public async Task<bool> HostNameExistsAsync(string hostName, CancellationToken cancellationToken = default)
    {
        var normalizedName = (hostName ?? string.Empty).Trim();
        if (normalizedName.Length == 0) return false;

        var relativeUrl = $"/api/v1/hosts?name={Uri.EscapeDataString(normalizedName)}";
        var hosts = await SendForJsonObjectArrayAsync(HttpMethod.Get, relativeUrl, null, cancellationToken);
        return hosts.Any(item =>
            string.Equals(GetPolymorphicHostName(item), normalizedName, StringComparison.OrdinalIgnoreCase));
    }

    // Retrieves one Host definition by persistent Host ID.
    public Task<JsonObject> GetHostAsync(string hostId, CancellationToken cancellationToken = default)
    {
        var normalizedHostId = (hostId ?? string.Empty).Trim();
        if (normalizedHostId.Length == 0) throw new ArgumentException("Host ID is required.", nameof(hostId));
        return SendForJsonObjectAsync(HttpMethod.Get, $"/api/v1/hosts/{Uri.EscapeDataString(normalizedHostId)}", null, cancellationToken);
    }

    // Creates one New Host shell using the captured typed Web Admin POST envelope.
    public Task<JsonObject> CreateHostV0Async(JsonObject host, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        MoveItFeatureSupport.RequireHost(host, _sessionService.Get()?.Info);
        return SendForJsonObjectAsync(HttpMethod.Post, "/api/v0/hosts", host, cancellationToken);
    }

    // Updates one existing Host using the captured native Web Admin PUT /api/v0/hosts/{hostid} contract.
    public Task<JsonObject> UpdateHostV0Async(string hostId, JsonObject host, CancellationToken cancellationToken = default)
    {
        var normalizedHostId = (hostId ?? string.Empty).Trim();
        if (normalizedHostId.Length == 0) throw new ArgumentException("Host ID is required.", nameof(hostId));
        ArgumentNullException.ThrowIfNull(host);
        MoveItFeatureSupport.RequireHost(host, _sessionService.Get()?.Info);
        return SendForJsonObjectAsync(HttpMethod.Put, $"/api/v0/hosts/{Uri.EscapeDataString(normalizedHostId)}", host, cancellationToken);
    }

    // Checks out one Host through the same v0 endpoint used by the native Host editor.
    public Task<JsonObject> GetSshHostFingerprintV0Async(IReadOnlyDictionary<string, string> query, CancellationToken cancellationToken = default)
    {
        var parameters = string.Join("&", query.Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
        return SendForJsonObjectAsync(HttpMethod.Get, "/api/v0/sshhostfingerprint?" + parameters, null, cancellationToken);
    }

    public async Task<JsonObject> CheckoutHostV0Async(string hostId, CancellationToken cancellationToken = default)
    {
        var normalizedHostId = (hostId ?? string.Empty).Trim();
        if (normalizedHostId.Length == 0) throw new ArgumentException("Host ID is required.", nameof(hostId));
        var response = await SendCheckoutDefinitionAsync($"/api/v0/hosts/{Uri.EscapeDataString(normalizedHostId)}/checkout", null, cancellationToken);
        ValidateCheckoutAcquisition(response);
        return response;
    }

    // Native v0 object checkout; v1 JSON remains the Traditional Task write transport.
    // Never request a forced checkout, and never fall back to an unlocked write.
    public async Task CheckoutTaskV0Async(string taskId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(taskId)) throw new ArgumentException("Task ID is required.", nameof(taskId));
        var response = await SendCheckoutAsync($"/api/v0/tasks/{Uri.EscapeDataString(taskId.Trim())}/checkout", null, cancellationToken);
        // Keep support for native empty/text acknowledgements and returned definitions.
        // An explicit JSON error must never be mistaken for acquired ownership.
        JsonNode? parsed = null;
        try { parsed = JsonNode.Parse(response); }
        catch (JsonException) { }
        if (parsed is JsonObject acknowledgement) ValidateCheckoutAcquisition(acknowledgement);
    }

    public async Task ReleaseTaskCheckoutV0Async(string taskId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(taskId)) throw new ArgumentException("Task ID is required.", nameof(taskId));
        _ = await SendForTextAsync(HttpMethod.Delete, $"/api/v0/tasks/{Uri.EscapeDataString(taskId.Trim())}/checkout", null, cancellationToken, checkoutRelease: true);
    }

    // The captured override POST requires {"force":true}; repeating an ordinary checkout cannot override it.
    public Task<JsonObject> ForceCheckoutObjectV0Async(string type, string id, CancellationToken cancellationToken = default)
    {
        var session = _sessionService.Get() ?? throw new MoveItAuthenticationException("MOVEit session expired. Please sign in again.");
        if (session.Info?.IsAdmin != true)
            throw new MoveItApiException("Only a MOVEit administrator can override checkout.", HttpStatusCode.Forbidden);
        var url = type switch
        {
            "Task" when long.TryParse(id, out var task) && task > 0 => $"/api/v0/tasks/{Uri.EscapeDataString(id.Trim())}/checkout",
            "Host" when long.TryParse(id, out var host) && host >= 0 => $"/api/v0/hosts/{Uri.EscapeDataString(id.Trim())}/checkout",
            "Script" => $"/api/v0/customscripts/{Resources.Scripts.ScriptConfiguration.Id(id)}/checkout",
            "DateList" => $"/api/v0/datelists/{Resources.DateLists.DateListConfiguration.Id(id)}/checkout",
            "MOVEit Settings" => "/api/v0/globals/checkout",
            "Resource Groups" => "/api/v0/resourcegroups/checkout",
            _ => throw new ArgumentException("A supported checkout type and valid object ID are required.")
        };
        return SendCheckoutDefinitionAsync(url, new JsonObject { ["force"] = true }, cancellationToken);
    }

    // Normal acquisition never requests force or retries a checkout after native rejection.
    // Failed acquisitions never trigger DELETE or an automatic retry/forced takeover.
    private async Task<string> SendCheckoutAsync(string url, JsonNode? body, CancellationToken ct, bool acceptJson = false)
    {
        try
        {
            var text = await SendForTextAsync(HttpMethod.Post, url, body, ct, acceptJson: acceptJson);
            JsonObject? response = null;
            try { response = JsonNode.Parse(text) as JsonObject; } catch (JsonException) { }
            if (response is not null) ValidateCheckoutAcquisition(response);
            return text;
        }
        catch (MoveItApiException ex) when (ex is not (MoveItAuthenticationException or ObjectCheckoutRejectedException)
            && ex.StatusCode is { } code && (int)code is >= 400 and < 500 and not 408 and not 429
            && ObjectCheckoutRejectedException.DescribesLock(ex.Message))
        { throw new ObjectCheckoutRejectedException(ex.Message); }
    }
    private async Task<JsonObject> SendCheckoutDefinitionAsync(string url, JsonNode? body, CancellationToken ct)
        => JsonNode.Parse(await SendCheckoutAsync(url, body, ct, acceptJson: true)) as JsonObject
            ?? throw new MoveItApiException("MOVEit did not return a complete checkout definition.");

    private static void ValidateCheckoutAcquisition(JsonObject response)
    {
        string Field(string name) => response.FirstOrDefault(pair => pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value?.ToString().Trim() ?? "";
        var code = Field("errorcode");
        if (Field("status").Equals("error", StringComparison.OrdinalIgnoreCase) || code.Length > 0 && code != "0")
            {
            var details = Field("message");
            if (details.Length == 0 && response["errors"] is JsonArray errors)
                details = string.Join(" ", errors.OfType<JsonObject>().Select(error => GetString(error, "message")).Where(value => !string.IsNullOrWhiteSpace(value)));
            throw new ObjectCheckoutRejectedException(details.Length > 0 ? details : "MOVEit rejected the checkout" + (int.TryParse(code, out var number) ? $" (native error {number})" : "") + ". No changes were submitted.");
        }
    }

    // Releases a native Host editor checkout, accepting documented already-unlocked cleanup.
    public async Task ReleaseHostCheckoutV0Async(string hostId, CancellationToken cancellationToken = default)
    {
        var normalizedHostId = (hostId ?? string.Empty).Trim();
        if (normalizedHostId.Length == 0) throw new ArgumentException("Host ID is required.", nameof(hostId));
        // The native diagnostic may still be completing after its caller cancelled. Do not
        // apply the ordinary inventory timeout to the queued checkout DELETE.
        using var cleanup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cleanup.CancelAfter(HostOperationTimeout.Maximum);
        _ = await SendForTextAsync(HttpMethod.Delete, $"/api/v0/hosts/{Uri.EscapeDataString(normalizedHostId)}/checkout", null,
            cleanup.Token, checkoutRelease: true, timeout: HostOperationTimeout.Maximum);
    }

    // Sends the verified native UNC Share browse payload used to validate saved Host connectivity.
    public Task<JsonObject> BrowseFileSystemHostV0Async(JsonObject request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendHostOperationAsync("/api/v0/hosts/fs/browse", request, cancellationToken);
    }

    // Sends the verified native SSH/SFTP browse payload used to validate saved Host connectivity.
    public Task<JsonObject> BrowseSshHostV0Async(JsonObject request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendHostOperationAsync("/api/v0/hosts/ssh/browse", request, cancellationToken);
    }

    // Sends the verified native FTP/FTPS browse payload used to validate saved Host connectivity.
    public Task<JsonObject> BrowseFtpHostV0Async(JsonObject request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendHostOperationAsync("/api/v0/hosts/ftp/browse", request, cancellationToken);
    }

    // Sends the verified native SharePoint test payload used to validate saved Host connectivity.
    public Task<JsonObject> TestSharePointHostV0Async(JsonObject request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendHostOperationAsync("/api/v0/hosts/sharepoint/test", request, cancellationToken);
    }

    // Sends the captured native Azure Blob test payload used to validate saved Host connectivity.
    public Task<JsonObject> TestAzureBlobHostV0Async(JsonObject request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendHostOperationAsync("/api/v0/hosts/azureblob/test", request, cancellationToken);
    }

    // Sends the captured native Google Storage test payload used to validate saved Host connectivity.
    public Task<JsonObject> TestGoogleStorageHostV0Async(JsonObject request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendHostOperationAsync("/api/v0/hosts/googlestorage/test", request, cancellationToken);
    }

    // Sends the captured native Amazon S3 test payload used to validate saved Host connectivity.
    public Task<JsonObject> TestS3HostV0Async(JsonObject request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (HostCloudAuthenticationSettings.S3Method(request) != 0) MoveItFeatureSupport.Require(_sessionService.Get()?.Info, MoveItFeature.AmazonS3IamAuthentication);
        return SendHostOperationAsync("/api/v0/hosts/s3/test", request, cancellationToken);
    }

    public Task<JsonObject> BrowseS3HostV0Async(JsonObject request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (HostCloudAuthenticationSettings.S3Method(new JsonObject { ["AuthMethod"] = request["authmethod"]?.DeepClone() ?? JsonValue.Create(0) }) != 0) MoveItFeatureSupport.Require(_sessionService.Get()?.Info, MoveItFeature.AmazonS3IamAuthentication);
        return SendHostOperationAsync("/api/v0/hosts/s3/browse", request, cancellationToken);
    }

    public Task<JsonObject> BrowseAzureBlobHostV0Async(JsonObject request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendHostOperationAsync("/api/v0/hosts/azureblob/browse", request, cancellationToken);
    }

    public Task<JsonObject> BrowseSharePointHostV0Async(JsonObject request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendHostOperationAsync("/api/v0/hosts/sharepoint/browse", request, cancellationToken);
    }

    public Task<JsonObject> BrowseOneDriveHostV0Async(JsonObject request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        MoveItFeatureSupport.Require(_sessionService.Get()?.Info, MoveItFeature.OneDriveHosts);
        return SendHostOperationAsync("/api/v0/hosts/onedrive/browse", request, cancellationToken);
    }

    public Task<JsonObject> BrowseGoogleStorageHostV0Async(JsonObject request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendHostOperationAsync("/api/v0/hosts/googlestorage/browse", request, cancellationToken);
    }

    // Sends the captured native MOVEit Transfer browse payload used to validate saved Host connectivity.
    public Task<JsonObject> BrowseDmzHostV0Async(JsonObject request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendHostOperationAsync("/api/v0/hosts/dmz/browse", request, cancellationToken);
    }

    // Retrieves the task fields needed to derive Host references plus the common classification
    // data reused by the Task Calendar (description and Resource Group membership).
    public async Task<IReadOnlyList<JsonObject>> GetTaskHostReferenceDefinitionsAsync(CancellationToken cancellationToken = default)
    {
        const string fields = "ID,Name,Info,Group,steps,NextActions";
        var root = await SendForJsonObjectAsync(HttpMethod.Get, $"/api/v1/tasks?fields={Uri.EscapeDataString(fields)}", null, cancellationToken);
        return GetObjectArray(root, "items");
    }

    // Extracts Name from MOVEit's polymorphic Host response wrapper without assuming one Host type.
    private static string GetPolymorphicHostName(JsonObject wrapper)
    {
        if (!string.IsNullOrWhiteSpace(GetString(wrapper, "Name"))) return GetString(wrapper, "Name").Trim();
        foreach (var pair in wrapper)
        {
            if (pair.Value is not JsonObject typedHost) continue;
            var name = GetString(typedHost, "Name").Trim();
            if (name.Length > 0) return name;
        }
        return string.Empty;
    }

    // Retrieves one task payload used by the metadata/details modal. REST is used for reading all task types.
    public Task<JsonObject> GetTaskAsync(string taskId, CancellationToken cancellationToken = default)
        => SendForJsonObjectAsync(HttpMethod.Get, $"/api/v1/tasks/{Uri.EscapeDataString(taskId)}", null, cancellationToken);

    // Creates one complete Traditional Task definition. This is used by spreadsheet import because Resource Group
    // membership is part of the proven create payload and cannot be reliably reassigned by the normal REST update path.
    public Task<JsonObject> CreateTaskDefinitionAsync(JsonObject task, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        return SendForJsonObjectAsync(HttpMethod.Post, "/api/v1/tasks", task, cancellationToken);
    }

    // Retrieves the native Web Admin transfer-exception state for one Task.
    public Task<JsonObject> GetTaskFailedInfoV0Async(string taskId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(taskId)) throw new ArgumentException("Task ID is required.", nameof(taskId));
        return SendForJsonObjectAsync(
            HttpMethod.Get,
            $"/api/v0/tasks/{Uri.EscapeDataString(taskId.Trim())}/failedinfo",
            null,
            cancellationToken);
    }

    // Removes the selected native Web Admin transfer-exception entries for one Task.
    public Task<JsonObject> DeleteTaskFailedEntriesV0Async(
        string taskId,
        IReadOnlyCollection<string> exceptionIds,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(taskId)) throw new ArgumentException("Task ID is required.", nameof(taskId));
        var ids = (exceptionIds ?? Array.Empty<string>())
            .Select(value => (value ?? string.Empty).Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (ids.Count == 0) throw new ArgumentException("At least one transfer exception is required.", nameof(exceptionIds));

        var body = new JsonArray(ids.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray());
        return SendForJsonObjectAsync(
            HttpMethod.Post,
            $"/api/v0/tasks/{Uri.EscapeDataString(taskId.Trim())}/deletefailedentries",
            body,
            cancellationToken);
    }

    // Retrieves the native Web Admin Source Timestamp state for one Task/source Host. The host ID is
    // supplied as the same query-string argument emitted by MOVEit's own Task Source Timestamps dialog.
    public Task<JsonObject> GetTaskNewFileTimestampsV0Async(
        string taskId,
        string hostId,
        CancellationToken cancellationToken = default)
    {
        var normalizedTaskId = (taskId ?? string.Empty).Trim();
        var normalizedHostId = (hostId ?? string.Empty).Trim();
        if (normalizedTaskId.Length == 0) throw new ArgumentException("Task ID is required.", nameof(taskId));
        if (normalizedHostId.Length == 0) throw new ArgumentException("Host ID is required.", nameof(hostId));

        return SendForJsonObjectAsync(
            HttpMethod.Get,
            $"/api/v0/tasks/{Uri.EscapeDataString(normalizedTaskId)}/newfiletimestamps?hostid={Uri.EscapeDataString(normalizedHostId)}",
            null,
            cancellationToken);
    }

    // Adds, edits, or clears one Source Timestamp through the exact native Web Admin PUT contract.
    public Task<JsonObject> UpdateTaskNewFileTimestampV0Async(
        string taskId,
        string hostId,
        string path,
        string mask,
        string timestamp,
        CancellationToken cancellationToken = default)
    {
        var normalizedTaskId = (taskId ?? string.Empty).Trim();
        var normalizedHostId = (hostId ?? string.Empty).Trim();
        var normalizedPath = (path ?? string.Empty).Trim();
        var normalizedMask = (mask ?? string.Empty).Trim();
        if (normalizedTaskId.Length == 0) throw new ArgumentException("Task ID is required.", nameof(taskId));
        if (normalizedHostId.Length == 0) throw new ArgumentException("Host ID is required.", nameof(hostId));
        if (normalizedPath.Length == 0) throw new ArgumentException("Path is required.", nameof(path));
        if (normalizedMask.Length == 0) throw new ArgumentException("File mask is required.", nameof(mask));

        var body = new JsonObject
        {
            ["hostId"] = normalizedHostId,
            ["path"] = normalizedPath,
            ["mask"] = normalizedMask,
            ["timestamp"] = timestamp ?? string.Empty
        };

        return SendForJsonObjectAsync(
            HttpMethod.Put,
            $"/api/v0/tasks/{Uri.EscapeDataString(normalizedTaskId)}/newfiletimestamps",
            body,
            cancellationToken);
    }


    // Updates one existing Traditional Task using MOVEit's documented full-task PUT contract.
    public async Task UpdateTaskAsync(string taskId, JsonObject task, CancellationToken cancellationToken = default)
    {
        var normalizedTaskId = (taskId ?? string.Empty).Trim();
        if (normalizedTaskId.Length == 0) throw new ArgumentException("Task ID is required.", nameof(taskId));
        ArgumentNullException.ThrowIfNull(task);
        await SendForTextAsync(HttpMethod.Put, $"/api/v1/tasks/{Uri.EscapeDataString(normalizedTaskId)}", task, cancellationToken);
    }

    // Captured native Sync comparison subresource. DELETE has no request body.
    public Task<JsonObject> GetSynchronizationCompareV0Async(string taskId, CancellationToken cancellationToken = default)
        => SendForJsonObjectAsync(HttpMethod.Get, SyncCompareUrl(taskId), null, cancellationToken, TimeSpan.FromMinutes(5));

    public Task<JsonObject> ClearSynchronizationCompareV0Async(string taskId, CancellationToken cancellationToken = default)
        => SendForJsonObjectAsync(HttpMethod.Delete, SyncCompareUrl(taskId), null, cancellationToken);

    private static string SyncCompareUrl(string taskId)
    {
        if (string.IsNullOrWhiteSpace(taskId)) throw new ArgumentException("Task ID is required.", nameof(taskId));
        return $"/api/v0/tasks/{Uri.EscapeDataString(taskId.Trim())}/synccompare";
    }

    // Captured empty Advanced/Sync creation. Authentication renewal may retry only
    // a definitive 401 rejection; transport/acknowledgement failures are never replayed.
    public Task<JsonObject> CreateTaskV0Async(JsonObject task, CancellationToken cancellationToken = default)
        => SendForJsonObjectAsync(HttpMethod.Post, "/api/v0/tasks", BuildTaskV0Body(task), cancellationToken);

    // Captured Web Admin Sync/Advanced PUT. Keep the common v0 Task envelope explicit.
    public Task<JsonObject> UpdateTaskV0Async(string taskId, JsonObject task, CancellationToken cancellationToken = default)
    {
        var id = (taskId ?? string.Empty).Trim();
        if (id.Length == 0) throw new ArgumentException("Task ID is required.", nameof(taskId));
        return SendForJsonObjectAsync(HttpMethod.Put, $"/api/v0/tasks/{Uri.EscapeDataString(id)}",
            BuildTaskV0Body(task), cancellationToken);
    }

    internal static JsonObject BuildTaskV0Body(JsonObject task)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (task["TT"]?.ToString() is not ("Sy" or "COS"))
            throw new InvalidOperationException("The v0 Task write requires TT=Sy or TT=COS.");
        return new JsonObject { ["Task"] = task.DeepClone() };
    }

    // Filename search uses the established bulk field projection, only when explicitly requested.
    // Reject incomplete lists rather than treating missing/malformed steps data as no matches.
    public async Task<IReadOnlyList<JsonObject>> GetTaskFilenameDefinitionsAsync(CancellationToken cancellationToken = default)
    {
        const string fields = "ID,steps";
        var root = await SendForJsonObjectAsync(HttpMethod.Get, $"/api/v1/tasks?fields={Uri.EscapeDataString(fields)}", null, cancellationToken);
        if (root["items"] is not JsonArray items || items.Any(item => item is not JsonObject))
            throw new InvalidOperationException("MOVEit returned an incomplete Task filename list.");
        return items.Cast<JsonObject>().ToArray();
    }

    // Retrieves only the documented task-reference fields needed for safe delete preflight.
    public async Task<IReadOnlyList<JsonObject>> GetTaskDependencyDefinitionsAsync(CancellationToken cancellationToken = default)
    {
        const string fields = "ID,Name,NextActions,steps";
        var root = await SendForJsonObjectAsync(HttpMethod.Get, $"/api/v1/tasks?fields={Uri.EscapeDataString(fields)}", null, cancellationToken);
        return GetObjectArray(root, "items");
    }

    // Deletes one Task through MOVEit's documented REST endpoint.
    public async Task DeleteTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var normalizedTaskId = (taskId ?? string.Empty).Trim();
        if (normalizedTaskId.Length == 0) throw new ArgumentException("Task ID is required.", nameof(taskId));
        await SendForTextAsync(HttpMethod.Delete, $"/api/v1/tasks/{Uri.EscapeDataString(normalizedTaskId)}", null, cancellationToken);
    }

    // Starts one task immediately through MOVEit's documented REST endpoint. The response's
    // nominalStart value uniquely identifies the resulting run when MOVEit returns it.
    // Purpose: Start one task immediately through the MOVEit REST API.
    public async Task<string> StartTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var normalizedTaskId = (taskId ?? string.Empty).Trim();
        if (normalizedTaskId.Length == 0) throw new ArgumentException("Task ID is required.", nameof(taskId));

        var response = await SendForJsonObjectAsync(
            HttpMethod.Post,
            $"/api/v1/tasks/{Uri.EscapeDataString(normalizedTaskId)}/start",
            new JsonObject(),
            cancellationToken);

        var nominalStart = GetString(response, "nominalStart");
        return string.IsNullOrWhiteSpace(nominalStart) ? GetString(response, "NominalStart") : nominalStart;
    }

    // Immediately terminates one specific running instance. MOVEit requires the exact nominalStart
    // value returned by the running-task API; MAUI intentionally preserves that string unchanged.
    // Purpose: Kill one exact running task instance identified by NominalStart.
    public async Task KillTaskAsync(string taskId, string nominalStart, CancellationToken cancellationToken = default)
    {
        var normalizedTaskId = (taskId ?? string.Empty).Trim();
        var normalizedNominalStart = (nominalStart ?? string.Empty).Trim();
        if (normalizedTaskId.Length == 0) throw new ArgumentException("Task ID is required.", nameof(taskId));
        if (normalizedNominalStart.Length == 0) throw new ArgumentException("Nominal start is required to kill a running task.", nameof(nominalStart));

        var body = new JsonObject { ["nominalStart"] = normalizedNominalStart };
        await SendForTextAsync(
            HttpMethod.Post,
            $"/api/v1/tasks/{Uri.EscapeDataString(normalizedTaskId)}/kill",
            body,
            cancellationToken);
    }

    // Retrieves currently running tasks so the grid can identify live activity independently of historical reports.
    public async Task<IReadOnlyList<MoveItRunningTask>> GetRunningTasksAsync(CancellationToken cancellationToken = default)
    {
        var root = await SendForJsonObjectAsync(HttpMethod.Get, "/api/v1/tasks/running", null, cancellationToken);
        return GetObjectArray(root, "items")
            .Select(item => new MoveItRunningTask(
                GetString(item, "TaskID"),
                GetString(item, "TaskName"),
                GetString(item, "StartedBy"),
                GetString(item, "Status"),
                GetString(item, "TimeStarted"),
                GetString(item, "NominalStart"),
                GetNullableLong(item, "CurFileBytes"),
                GetNullableLong(item, "TotFileBytes"),
                GetString(item, "LastErrorType"),
                GetString(item, "LastErrorText")))
            .Where(item => !string.IsNullOrWhiteSpace(item.TaskId))
            .ToList();
    }

    // Retrieves recent task-run history used to derive each task's latest Success/Failure/No Transfers status.
    public async Task<IReadOnlyList<MoveItTaskRunRecord>> GetTaskRunsAsync(CancellationToken cancellationToken = default)
    {
        var requestBody = new JsonObject
        {
            ["predicate"] = "TaskID>0",
            ["orderBy"] = "!StartTime",
            ["maxCount"] = Math.Clamp(_configuration.TaskRunHistoryMaxCount, 1, 100000)
        };
        var root = await SendForJsonObjectAsync(HttpMethod.Post, "/api/v1/reports/taskruns", requestBody, cancellationToken);
        return GetObjectArray(root, "items")
            .Select(item => new MoveItTaskRunRecord(
                GetString(item, "TaskID"),
                GetString(item, "TaskName"),
                GetString(item, "Status"),
                GetString(item, "StatusMsg"),
                GetNullableInt(item, "FilesSent"),
                GetNullableDouble(item, "TotalBytesSent"),
                GetDateTimeOffset(item, "StartTime"),
                GetDateTimeOffset(item, "EndTime"),
                GetString(item, "NominalStart")))
            .Where(item => !string.IsNullOrWhiteSpace(item.TaskId))
            .ToList();
    }

    // Retrieves raw Task Run report rows so MAUI can expose the full documented reporting model.
    public Task<IReadOnlyList<JsonObject>> GetTaskRunReportAsync(string predicate, string orderBy, int maxCount, CancellationToken cancellationToken = default)
        => GetReportItemsAsync("/api/v1/reports/taskruns", predicate, orderBy, maxCount, cancellationToken);

    // Retrieves raw File Activity report rows so optional report columns remain available to the reusable grid.
    public Task<IReadOnlyList<JsonObject>> GetFileActivityReportAsync(string predicate, string orderBy, int maxCount, CancellationToken cancellationToken = default)
        => GetReportItemsAsync("/api/v1/reports/fileactivity", predicate, orderBy, maxCount, cancellationToken);

    // Retrieves the exact v0 File Activity report shape used by native Web Admin dashboard drill-downs.
    // v0 returns { Records: { Record: [...] } } rather than the v1 { items: [...] } envelope.
    public async Task<IReadOnlyList<JsonObject>> GetFileActivityReportV0Async(
        string predicate,
        string orderBy,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        var requestBody = new JsonObject
        {
            ["predicate"] = predicate ?? string.Empty,
            ["orderBy"] = orderBy ?? string.Empty,
            ["maxCount"] = Math.Clamp(maxCount, 1, 100000)
        };

        var root = await SendForJsonObjectAsync(HttpMethod.Post, "/api/v0/reports/fileactivity", requestBody, cancellationToken);
        if (root["Records"] is not JsonObject records) return Array.Empty<JsonObject>();
        if (records["Record"] is JsonArray array) return array.OfType<JsonObject>().ToList();
        if (records["Record"] is JsonObject single) return new[] { single };
        return Array.Empty<JsonObject>();
    }

    // Retrieves raw Audit report rows. MOVEit has historically returned a few fields beyond some published schema tables,
    // so the report service intentionally parses this response defensively.
    public Task<IReadOnlyList<JsonObject>> GetAuditReportAsync(string predicate, string orderBy, int maxCount, CancellationToken cancellationToken = default)
        => GetReportItemsAsync("/api/v1/reports/audit", predicate, orderBy, maxCount, cancellationToken);

    public async Task<AuditChangeRecord> GetAuditChangesAsync(long logId, CancellationToken cancellationToken = default)
    {
        if (logId <= 0) throw new ArgumentOutOfRangeException(nameof(logId));
        MoveItFeatureSupport.Require(_sessionService.Get()?.Info, MoveItFeature.AuditConfigurationChanges);
        var root = await SendForJsonObjectAsync(HttpMethod.Get,
            "/api/v0/reports/audit/diff/" + logId.ToString(System.Globalization.CultureInfo.InvariantCulture), null, cancellationToken);
        if (root["Record"] is not JsonObject record)
            throw new MoveItApiException("Configuration changes are not available for this audit record.");
        if (!record.ContainsKey("PreChange") && !record.ContainsKey("PostChange"))
            throw new MoveItApiException("Configuration changes are not available for this audit record.");
        string Read(string name)
        {
            if (record[name] is null) return string.Empty; // Creates/deletes may have an empty side.
            if (record[name] is JsonValue value && value.TryGetValue<string>(out var text)) return text ?? string.Empty;
            throw new MoveItApiException("MOVEit returned an invalid audit comparison.");
        }
        return new(Read("PreChange"), Read("PostChange"));
    }

    // Purpose: Returns report items.
    private async Task<IReadOnlyList<JsonObject>> GetReportItemsAsync(string endpoint, string predicate, string orderBy, int maxCount, CancellationToken cancellationToken)
    {
        var requestBody = new JsonObject
        {
            ["predicate"] = string.IsNullOrWhiteSpace(predicate) ? "LogID>0" : predicate,
            ["orderBy"] = orderBy ?? string.Empty,
            ["maxCount"] = Math.Clamp(maxCount, 1, 100000)
        };
        var root = await SendForJsonObjectAsync(HttpMethod.Post, endpoint, requestBody, cancellationToken);
        return GetObjectArray(root, "items");
    }

    // Retrieves the retained task-log list for one task. MOVEit 2025.x installations have been observed
    // returning either the documented { items: [...] } object or a direct JSON array, so both shapes are accepted.
    // Purpose: Retrieve the retained task-log list for one task.
    public async Task<IReadOnlyList<MoveItTaskLogRecord>> GetTaskLogsAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(taskId)) throw new ArgumentException("Task ID is required.", nameof(taskId));

        var body = await SendForTextAsync(
            HttpMethod.Get,
            $"/api/v1/tasks/{Uri.EscapeDataString(taskId)}/log",
            null,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(body)) return Array.Empty<MoveItTaskLogRecord>();

        JsonNode? rootNode;
        try
        {
            rootNode = JsonNode.Parse(body);
        }
        catch (Exception ex)
        {
            throw new MoveItApiException("MOVEit returned an invalid task-log list response.", null, ex);
        }

        JsonArray? items = rootNode as JsonArray;
        if (items is null && rootNode is JsonObject rootObject)
        {
            items = rootObject["items"] as JsonArray;
        }

        if (items is null) return Array.Empty<MoveItTaskLogRecord>();

        var logs = new List<MoveItTaskLogRecord>();
        foreach (var item in items.OfType<JsonObject>())
        {
            var idText = GetString(item, "id");
            if (string.IsNullOrWhiteSpace(idText)) idText = GetString(item, "ID");
            if (!long.TryParse(idText, out var logId) || logId <= 0) continue;

            var itemTaskId = GetString(item, "taskId");
            if (string.IsNullOrWhiteSpace(itemTaskId)) itemTaskId = GetString(item, "TaskID");
            if (string.IsNullOrWhiteSpace(itemTaskId)) itemTaskId = taskId;

            var exitStatus = GetString(item, "exitStatus");
            if (string.IsNullOrWhiteSpace(exitStatus)) exitStatus = GetString(item, "ExitStatus");

            var logPath = GetString(item, "logPath");
            if (string.IsNullOrWhiteSpace(logPath)) logPath = GetString(item, "LogPath");

            var startTime = GetString(item, "startTime");
            if (string.IsNullOrWhiteSpace(startTime)) startTime = GetString(item, "StartTime");

            logs.Add(new MoveItTaskLogRecord(logId, itemTaskId, exitStatus, logPath, startTime));
        }

        return logs;
    }

    // Retrieves one task log as text. The REST endpoint returns the log body rather than a JSON model.
    public Task<string> GetTaskLogTextAsync(string taskId, long taskLogId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(taskId)) throw new ArgumentException("Task ID is required.", nameof(taskId));
        if (taskLogId <= 0) throw new ArgumentOutOfRangeException(nameof(taskLogId));

        return SendForTextAsync(
            HttpMethod.Get,
            $"/api/v1/tasks/{Uri.EscapeDataString(taskId)}/log/{taskLogId}",
            null,
            cancellationToken);
    }

    // All Host diagnostics/listings share native-aware timing, independent of the general
    // 60-second inventory limit. Linked cancellation also bounds slow response-body reads.
    private async Task<JsonObject> SendHostOperationAsync(string relativeUrl, JsonObject body, CancellationToken cancellationToken)
    {
        var timeout = HostOperationTimeout.ForRequest(body);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        operation.CancelAfter(timeout);
        try
        {
            return await SendForJsonObjectAsync(HttpMethod.Post, relativeUrl, body, operation.Token, timeout);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MoveItApiException("The Host operation timed out. Check the Host connection and timeout settings, then try again.", HttpStatusCode.RequestTimeout, ex);
        }
    }

    // Sends a bearer-authenticated JSON request. One 401 triggers token renewal and a single retry.
    private async Task<JsonObject> SendForJsonObjectAsync(HttpMethod method, string relativeUrl, JsonNode? body, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        try
        {
            var state = await GetValidSessionAsync(cancellationToken);
            using var firstResponse = await SendAsync(state, method, relativeUrl, body, cancellationToken, timeout: timeout);
            if (firstResponse.StatusCode == HttpStatusCode.Unauthorized)
            {
                state = await RenewSessionAsync(state, cancellationToken, forceRenewal: true);
                using var retryResponse = await SendAsync(state, method, relativeUrl, body, cancellationToken, timeout: timeout);
                return await ParseJsonResponseAsync(retryResponse, cancellationToken);
            }

            return await ParseJsonResponseAsync(firstResponse, cancellationToken);
        }
        catch (MoveItAuthenticationException)
        {
            _sessionService.Clear();
            throw;
        }
    }


    // Sends a bearer-authenticated request whose successful JSON body is an array of objects.
    // The Hosts list endpoint uses a top-level array rather than the { items: [...] } list-result contract used by Tasks.
    private async Task<IReadOnlyList<JsonObject>> SendForJsonObjectArrayAsync(
        HttpMethod method,
        string relativeUrl,
        JsonNode? body,
        CancellationToken cancellationToken)
    {
        try
        {
            var state = await GetValidSessionAsync(cancellationToken);
            using var firstResponse = await SendAsync(state, method, relativeUrl, body, cancellationToken);
            if (firstResponse.StatusCode == HttpStatusCode.Unauthorized)
            {
                state = await RenewSessionAsync(state, cancellationToken, forceRenewal: true);
                using var retryResponse = await SendAsync(state, method, relativeUrl, body, cancellationToken);
                return await ParseJsonObjectArrayResponseAsync(retryResponse, cancellationToken);
            }

            return await ParseJsonObjectArrayResponseAsync(firstResponse, cancellationToken);
        }
        catch (MoveItAuthenticationException)
        {
            _sessionService.Clear();
            throw;
        }
    }

    // Sends a bearer-authenticated request while intentionally discarding a successful response body.
    // This is used by native configuration import because MOVEit may return broad Settings/configuration data
    // that MAUI does not need and must not retain. One 401 still receives the standard token-renewal retry.
    private async Task SendAndDiscardSuccessfulBodyAsync(HttpMethod method, string relativeUrl, JsonNode? body, CancellationToken cancellationToken)
    {
        try
        {
            var state = await GetValidSessionAsync(cancellationToken);
            using var firstResponse = await SendAsync(state, method, relativeUrl, body, cancellationToken, acceptJson: false);
            if (firstResponse.StatusCode == HttpStatusCode.Unauthorized)
            {
                state = await RenewSessionAsync(state, cancellationToken, forceRenewal: true);
                using var retryResponse = await SendAsync(state, method, relativeUrl, body, cancellationToken, acceptJson: false);
                await EnsureSuccessWithoutReadingBodyAsync(retryResponse, cancellationToken);
                return;
            }

            await EnsureSuccessWithoutReadingBodyAsync(firstResponse, cancellationToken);
        }
        catch (MoveItAuthenticationException)
        {
            _sessionService.Clear();
            throw;
        }
    }

    private static async Task EnsureSuccessWithoutReadingBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var message = await BuildSafeNativeImportErrorMessageAsync(response, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized) throw new MoveItAuthenticationException(message, response.StatusCode);
        throw new MoveItApiException(message, response.StatusCode);
    }

    // MOVEit configuration-import endpoints can return broad Settings/configuration data on success. On failure,
    // expose only conservatively sanitized structured/plain-text error detail; never fall back to arbitrary raw
    // configuration-bearing response content.
    private static async Task<string> BuildSafeNativeImportErrorMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await MoveItGridConnectionHandler.ReadBodyAsync(response, cancellationToken);
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                if (JsonNode.Parse(body) is JsonObject node)
                {
                    var detail = GetString(node, "error_description");
                    if (string.IsNullOrWhiteSpace(detail)) detail = GetString(node, "detail");
                    if (string.IsNullOrWhiteSpace(detail)) detail = GetString(node, "message");
                    if (string.IsNullOrWhiteSpace(detail)) detail = GetString(node, "title");
                    var safeDetail = SanitizeNativeImportPlainTextError(detail);
                    if (!string.IsNullOrWhiteSpace(safeDetail))
                        return $"MOVEit import failed: {safeDetail}";
                }
            }
            catch
            {
                // MOVEit also returns useful validation failures as plain text. Fall through to the conservative
                // plain-text sanitizer below instead of discarding them solely because they are not JSON.
            }

            var safePlainText = SanitizeNativeImportPlainTextError(body);
            if (!string.IsNullOrWhiteSpace(safePlainText))
                return $"MOVEit import failed: {safePlainText}";
        }

        return $"MOVEit import failed: {(int)response.StatusCode} {response.ReasonPhrase}.";
    }

    private static string SanitizeNativeImportPlainTextError(string body)
    {
        var text = (body ?? string.Empty).Trim();
        if (text.Length == 0 || text.Length > 4096) return string.Empty;

        // Do not surface anything that resembles a native configuration document or JSON payload. Error text
        // returned by MOVEit is normally short prose; configuration-bearing bodies are deliberately suppressed.
        if (text.StartsWith("<", StringComparison.Ordinal)
            || text.StartsWith("{", StringComparison.Ordinal)
            || text.StartsWith("[", StringComparison.Ordinal)
            || text.StartsWith("\"", StringComparison.Ordinal)
            || text.Contains("<Exported", StringComparison.OrdinalIgnoreCase)
            || text.Contains("<Settings", StringComparison.OrdinalIgnoreCase)
            || text.Contains("\"Imported\"", StringComparison.OrdinalIgnoreCase)
            || text.Contains("DefPassword", StringComparison.OrdinalIgnoreCase)
            || text.Contains("privatekey", StringComparison.OrdinalIgnoreCase)
            || text.Contains("keymaterial", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        var normalized = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length > 1024) normalized = normalized[..1024];

        // Redact long opaque tokens (encrypted/base64 material, certificates, etc.) while preserving the surrounding
        // validation message. IDs and ordinary field names remain visible because they are useful diagnostics.
        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index];
            if (token.Length >= 64 || token.StartsWith("4gAAAA", StringComparison.OrdinalIgnoreCase))
                tokens[index] = "[redacted]";
        }

        return string.Join(" ", tokens);
    }

    // Sends raw XML using the exact application/xml media type required by MOVEit's whole-configuration PUT endpoint.
    // Successful import bodies are deliberately discarded because MAUI does not need them and MOVEit may return
    // broad configuration data. Failures use the same conservative import-error sanitizer as native portability.
    private async Task SendXmlAndDiscardSuccessfulBodyAsync(HttpMethod method, string relativeUrl, string xmlBody, CancellationToken cancellationToken)
    {
        try
        {
            var state = await GetValidSessionAsync(cancellationToken);
            using var firstResponse = await SendXmlAsync(state, method, relativeUrl, xmlBody, cancellationToken);
            if (firstResponse.StatusCode == HttpStatusCode.Unauthorized)
            {
                state = await RenewSessionAsync(state, cancellationToken, forceRenewal: true);
                using var retryResponse = await SendXmlAsync(state, method, relativeUrl, xmlBody, cancellationToken);
                await EnsureSuccessWithoutReadingBodyAsync(retryResponse, cancellationToken);
                return;
            }

            await EnsureSuccessWithoutReadingBodyAsync(firstResponse, cancellationToken);
        }
        catch (MoveItAuthenticationException)
        {
            _sessionService.Clear();
            throw;
        }
    }

    // Creates one bearer-authenticated request with a raw UTF-8 XML body. ByteArrayContent is used so the
    // Content-Type header is exactly application/xml rather than application/xml plus an added charset parameter.
    private async Task<HttpResponseMessage> SendXmlAsync(
        MoveItSessionState state,
        HttpMethod method,
        string relativeUrl,
        string xmlBody,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, BuildUri(state.WebAdminUrl, relativeUrl));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", state.AccessToken);
        request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(xmlBody));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/xml");
        return await CreateClient().SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    // Sends a bearer-authenticated request whose successful body is not necessarily JSON.
    private async Task<string> SendForTextAsync(HttpMethod method, string relativeUrl, JsonNode? body, CancellationToken cancellationToken, bool checkoutRelease = false, bool acceptJson = false, TimeSpan? timeout = null)
    {
        try
        {
            var state = await GetValidSessionAsync(cancellationToken);
            using var firstResponse = await SendAsync(state, method, relativeUrl, body, cancellationToken, acceptJson: acceptJson, timeout: timeout);
            if (firstResponse.StatusCode == HttpStatusCode.Unauthorized)
            {
                state = await RenewSessionAsync(state, cancellationToken, forceRenewal: true);
                using var retryResponse = await SendAsync(state, method, relativeUrl, body, cancellationToken, acceptJson: acceptJson, timeout: timeout);
                return await ParseTextResponseAsync(retryResponse, cancellationToken, checkoutRelease);
            }

            return await ParseTextResponseAsync(firstResponse, cancellationToken, checkoutRelease);
        }
        catch (MoveItAuthenticationException)
        {
            _sessionService.Clear();
            throw;
        }
    }

    // Creates and send one bearer-authenticated request to the selected MOVEit Web Admin node.
    private async Task<HttpResponseMessage> SendAsync(
        MoveItSessionState state,
        HttpMethod method,
        string relativeUrl,
        JsonNode? body,
        CancellationToken cancellationToken,
        bool acceptJson = true, TimeSpan? timeout = null)
    {
        using var request = new HttpRequestMessage(method, BuildUri(state.WebAdminUrl, relativeUrl));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(acceptJson ? "application/json" : "*/*"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", state.AccessToken);
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(JsonOptions), Encoding.UTF8, "application/json");
        }
        var client = CreateClient();
        if (timeout.HasValue) client.Timeout = timeout.Value;
        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    // Proactively renews access tokens just before expiry so normal UI navigation does not see avoidable 401 responses.
    private async Task<MoveItSessionState> GetValidSessionAsync(CancellationToken cancellationToken)
    {
        var state = _sessionService.Get() ?? throw new MoveItAuthenticationException("MOVEit session is not authenticated.");
        if (state.AccessTokenExpiresUtc <= DateTimeOffset.UtcNow.AddSeconds(30))
        {
            state = await RenewSessionAsync(state, cancellationToken);
        }
        return state;
    }

    // Serializes token renewal per user/server. MOVEit refresh tokens are rotated, so concurrent use of the
    // same refresh token can legitimately make the second request fail with "invalid token". After acquiring
    // Re-reads the cached session after acquiring the gate because another request may already have renewed it.
    // Purpose: Serialize token renewal and reuse any session another request already refreshed.
    private async Task<MoveItSessionState> RenewSessionAsync(
        MoveItSessionState requestedState,
        CancellationToken cancellationToken,
        bool forceRenewal = false)
    {
        var renewalKey = $"{requestedState.ServerKey}\u001f{requestedState.Username}";
        using var renewalLease = await TokenRenewalLocks.AcquireAsync(renewalKey, cancellationToken);

        var state = _sessionService.Get()
            ?? throw new MoveItAuthenticationException("MOVEit session is not authenticated.");

        // Reuses a session that another concurrent request refreshed while this request was waiting.
        // The newer state is always used if the access token changed instead of renewing again.
        if (!string.Equals(state.AccessToken, requestedState.AccessToken, StringComparison.Ordinal))
        {
            return state;
        }

        if (!forceRenewal && state.AccessTokenExpiresUtc > DateTimeOffset.UtcNow.AddSeconds(30))
        {
            return state;
        }

        // Does not infer refresh-token expiry because MOVEit Web Admin configures its TTL and the token API does not return it.
        // Does not guess its expiry locally; Tries it when present and fall back cleanly if MOVEit rejects it.
        if (!string.IsNullOrWhiteSpace(state.RefreshToken))
        {
            try
            {
                return await RefreshTokenAsync(state, cancellationToken);
            }
            catch (MoveItAuthenticationException ex)
            {
                // Recovers from an expired or rotated refresh token with the password retained for the active MAUI session.
                // Keeps this recovery path out of normal warning/error logs unless detailed operational logging is enabled later.
                _logger.LogDebug(ex,
                    "MOVEit refresh-token renewal was rejected for {Username}; attempting password reauthentication.",
                    state.Username);
            }
        }

        if (!string.IsNullOrWhiteSpace(state.Password))
        {
            try
            {
                var token = await RequestPasswordTokenAsync(state.WebAdminUrl, state.Username, state.Password, cancellationToken);
                ApplyTokenResult(state, token);
                _sessionService.UpdateTokens(state.AccessToken, state.RefreshToken, state.AccessTokenExpiresUtc);
                _logger.LogDebug("MOVEit password reauthentication succeeded for {Username}.", state.Username);
                return state;
            }
            catch (MoveItAuthenticationException ex)
            {
                _logger.LogDebug(ex, "MOVEit password reauthentication failed for {Username}.", state.Username);
            }
        }

        throw new MoveItAuthenticationException("MOVEit session expired. Please sign in again.");
    }

    // Exchanges a MOVEit refresh token for a new access token and retains the in-memory password for COM access.
    private async Task<MoveItSessionState> RefreshTokenAsync(MoveItSessionState state, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(state.WebAdminUrl, "/api/v1/token"))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = state.RefreshToken
            })
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await MoveItGridConnectionHandler.SendBufferedAsync(CreateClient(), request, cancellationToken);
        var responseBody = await MoveItGridConnectionHandler.ReadBodyAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new MoveItAuthenticationException(
                BuildErrorMessage("MOVEit session refresh failed", response.StatusCode, response.ReasonPhrase, responseBody),
                response.StatusCode);
        }

        var tokenObject = ParseJsonObject(responseBody, "MOVEit returned an invalid token refresh response.");
        var accessToken = GetString(tokenObject, "access_token");
        var refreshToken = GetString(tokenObject, "refresh_token");
        var expiresIn = GetNullableInt(tokenObject, "expires_in") ?? 900;
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new MoveItAuthenticationException("MOVEit returned a successful refresh response, but no access_token was present.");

        state.AccessToken = accessToken;

        // Expects MOVEit refresh to return a replacement refresh token, so Does not retain the token that was just
        // consumed if the server omits the replacement; clearing it forces the safe password-grant fallback later.
        state.RefreshToken = refreshToken;
        state.AccessTokenExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, expiresIn));
        _sessionService.UpdateTokens(state.AccessToken, state.RefreshToken, state.AccessTokenExpiresUtc);
        return state;
    }

    // Applies a newly issued password-grant token pair to the current server-side session.
    private static void ApplyTokenResult(MoveItSessionState state, TokenResult token)
    {
        var now = DateTimeOffset.UtcNow;
        state.AccessToken = token.AccessToken;
        state.RefreshToken = token.RefreshToken;
        state.AccessTokenExpiresUtc = now.AddSeconds(Math.Max(30, token.ExpiresInSeconds));
    }

    // Performs the MOVEit password grant used by MAUI authentication.
    private async Task<TokenResult> RequestPasswordTokenAsync(string baseUrl, string username, string password, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(baseUrl, "/api/v1/token"))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["username"] = username,
                ["password"] = password
            })
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await MoveItGridConnectionHandler.SendBufferedAsync(CreateClient(), request, cancellationToken);
        var responseBody = await MoveItGridConnectionHandler.ReadBodyAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new MoveItAuthenticationException(
                BuildErrorMessage(
                    "MOVEit login failed. Verify the username/password and confirm the selected server is the active Automation node",
                    response.StatusCode,
                    response.ReasonPhrase,
                    responseBody),
                response.StatusCode);
        }

        var tokenObject = ParseJsonObject(responseBody, "MOVEit login failed because the token response was not valid JSON.");
        var accessToken = GetString(tokenObject, "access_token");
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new MoveItAuthenticationException("MOVEit login failed because no access token was returned.");

        return new TokenResult(
            accessToken,
            GetString(tokenObject, "refresh_token"),
            GetNullableInt(tokenObject, "expires_in") ?? 900);
    }


    // Returns a successful response body unchanged, or convert MOVEit's error response to the same typed exceptions used elsewhere.
    private static async Task<string> ParseTextResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken, bool checkoutRelease = false)
    {
        if (checkoutRelease) return await ParseCheckoutReleaseResponseAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var message = await BuildErrorMessageAsync("MOVEit REST request failed", response, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized) throw new MoveItAuthenticationException(message, response.StatusCode);
            throw new MoveItApiException(message, response.StatusCode);
        }

        return await MoveItGridConnectionHandler.ReadBodyAsync(response, cancellationToken);
    }

    // Release is idempotent only when MOVEit explicitly reports native 4015 (already unlocked).
    // Do not infer this from a status, a task ID, or text containing the digits 4015.
    private static async Task<string> ParseCheckoutReleaseResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await MoveItGridConnectionHandler.ReadBodyAsync(response, cancellationToken);
        JsonObject? result = null;
        try { result = JsonNode.Parse(body) as JsonObject; }
        catch (JsonException) { } // Native 204/empty responses do not carry an acknowledgement body.
        string Field(string name) => result?.FirstOrDefault(pair => pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value?.ToString().Trim() ?? "";
        int? code = int.TryParse(Field("errorcode"), out var value) ? value : null;
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new MoveItAuthenticationException("MOVEit session expired during checkout cleanup.", response.StatusCode);
        var canReportNativeRelease = response.IsSuccessStatusCode || response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict or HttpStatusCode.InternalServerError;
        if (canReportNativeRelease && ObjectCheckout.IsReleased(false, code)) return body;
        if (!response.IsSuccessStatusCode || code is not null and not 0 || Field("status").Equals("error", StringComparison.OrdinalIgnoreCase))
            throw new MoveItApiException($"MOVEit checkout cleanup failed (HTTP {(int)response.StatusCode}{(code.HasValue ? $", native error {code}" : "")}).", response.StatusCode);
        return body;
    }

    // Parses a successful JSON response or throws an error containing MOVEit's response body.
    private static async Task<JsonObject> ParseJsonResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var message = await BuildErrorMessageAsync("MOVEit REST request failed", response, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized) throw new MoveItAuthenticationException(message, response.StatusCode);
            throw new MoveItApiException(message, response.StatusCode);
        }

        var text = await MoveItGridConnectionHandler.ReadBodyAsync(response, cancellationToken);
        if (string.IsNullOrWhiteSpace(text)) return new JsonObject();
        return JsonNode.Parse(text) as JsonObject
            ?? throw new MoveItApiException("MOVEit returned JSON that was not an object.");
    }

    // Parses a successful JSON array response or throws an error containing MOVEit's response body.
    // An { items: [...] } wrapper is also accepted defensively for version-to-version compatibility.
    private static async Task<IReadOnlyList<JsonObject>> ParseJsonObjectArrayResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var message = await BuildErrorMessageAsync("MOVEit REST request failed", response, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new MoveItAuthenticationException(message, response.StatusCode);
            throw new MoveItApiException(message, response.StatusCode);
        }

        var text = await MoveItGridConnectionHandler.ReadBodyAsync(response, cancellationToken);
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<JsonObject>();

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(text);
        }
        catch (Exception ex)
        {
            throw new MoveItApiException("MOVEit returned an invalid JSON array response.", null, ex);
        }

        JsonArray? array = node as JsonArray;
        if (array is null && node is JsonObject root)
            array = root["items"] as JsonArray;

        if (array is null)
            throw new MoveItApiException("MOVEit returned JSON that was not an object array.");

        return array.OfType<JsonObject>().ToList();
    }

    // Purpose: Build error message.
    private static async Task<string> BuildErrorMessageAsync(string prefix, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await MoveItGridConnectionHandler.ReadBodyAsync(response, cancellationToken);
        return BuildErrorMessage(prefix, response.StatusCode, response.ReasonPhrase, body);
    }

    // Purpose: Build error message.
    private static string BuildErrorMessage(string prefix, HttpStatusCode statusCode, string? reasonPhrase, string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return $"{prefix}: {(int)statusCode} {reasonPhrase}.";

        try
        {
            var node = JsonNode.Parse(body) as JsonObject;
            var detail = GetString(node, "error_description");
            if (string.IsNullOrWhiteSpace(detail)) detail = GetString(node, "detail");
            if (string.IsNullOrWhiteSpace(detail)) detail = GetString(node, "title");
            if (string.IsNullOrWhiteSpace(detail)) detail = GetString(node, "message");
            if (string.IsNullOrWhiteSpace(detail) && node?["errors"] is JsonArray errors)
                detail = string.Join(" ", errors.OfType<JsonObject>().Select(error => GetString(error, "message")).Where(value => !string.IsNullOrWhiteSpace(value)));
            if (!string.IsNullOrWhiteSpace(detail))
            {
                var checkout = ObjectCheckoutRejectedException.NormalizeMessage(detail);
                return checkout != detail ? checkout : $"{prefix}: {detail}";
            }
        }
        catch
        {
            // Falls through to a compact raw response when the server does not return JSON.
        }

        var compact = body.Length > 600 ? body[..600] + "…" : body;
        return $"{prefix}: {(int)statusCode} {reasonPhrase}. {compact}";
    }

    // Purpose: Parse json object.
    private static JsonObject ParseJsonObject(string body, string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(body)) throw new MoveItAuthenticationException(errorMessage + " The response body was empty.");
        try
        {
            return JsonNode.Parse(body) as JsonObject ?? throw new MoveItAuthenticationException(errorMessage + " The response was not a JSON object.");
        }
        catch (MoveItAuthenticationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var compact = body.Length > 300 ? body[..300] + "…" : body;
            throw new MoveItAuthenticationException($"{errorMessage} Response: {compact}", null, ex);
        }
    }

    // Purpose: Create client.
    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient("MoveItAutomation");
        client.Timeout = TimeSpan.FromSeconds(Math.Clamp(_configuration.RequestTimeoutSeconds, 5, 300));
        return client;
    }

    // Purpose: Resolve server.
    private MoveItServerDefinition ResolveServer(string serverKey)
    {
        var server = _servers.FindForLogin(serverKey);
        if (server is null) throw new MoveItAuthenticationException("The selected MOVEit Automation server is not configured.");
        if (string.IsNullOrWhiteSpace(server.WebAdminUrl)) throw new MoveItAuthenticationException("The selected MOVEit server does not have a WebAdminUrl configured.");
        return server;
    }

    // Purpose: Build uri.
    private static Uri BuildUri(string baseUrl, string relativeUrl)
        => new($"{NormalizeBaseUrl(baseUrl)}{(relativeUrl.StartsWith('/') ? relativeUrl : "/" + relativeUrl)}", UriKind.Absolute);

    // Purpose: Normalize base url.
    private static string NormalizeBaseUrl(string baseUrl) => baseUrl.Trim().TrimEnd('/');

    // Purpose: Retrieve object array.
    private static IReadOnlyList<JsonObject> GetObjectArray(JsonObject root, string propertyName)
        => root[propertyName] is JsonArray array ? array.OfType<JsonObject>().ToList() : new List<JsonObject>();

    // Purpose: Parses move it info.
    private static MoveItInfo ParseMoveItInfo(JsonObject root)
    {
        var license = root["license"] as JsonObject ?? new JsonObject();
        var timezone = root["serverTimezoneInfo"] as JsonObject ?? new JsonObject();

        return new MoveItInfo
        {
            CentralHost = GetString(root, "centralHost"),
            Username = GetString(root, "username"),
            IsAdmin = IsJsonTrue(root, "isAdmin"),
            Domain = GetString(root, "domain"),
            AppVersion = GetString(root, "appVersion"),
            AppBuildNumber = GetString(root, "appBuildNumber"),
            AppBuildTimestamp = GetString(root, "appBuildTimestamp"),
            CentralVersion = GetString(root, "centralVersion"),
            CentralApiVersion = GetString(root, "centralApiVersion"),
            LicenseInformationAvailable = root["license"] is JsonObject,
            ServerCertificateDaysValid = GetNullableInt(root, "serverCertificateDaysValid"),
            MaxInactiveSessionInterval = GetNullableInt(root, "maxInactiveSessionInterval"),
            SftpFipsModeEnabled = IsJsonTrue(root, "sftpFipsModeEnabled"),
            CustomScriptingDisabled = IsJsonTrue(root, "customScriptingDisabled"),
            ServerTimezoneInfo = new MoveItServerTimezoneInfo
            {
                TimeZoneName = GetString(timezone, "TimeZoneName"),
                TimeZoneBias = GetString(timezone, "TimeZoneBias"),
                DaylightBias = GetString(timezone, "DaylightBias")
            },
            License = new MoveItLicenseInfo
            {
                Version = GetString(license, "Version"),
                ResilEnabled = IsJsonTrue(license, "ResilEnabled"),
                ResilRole = GetString(license, "ResilRole"),
                MaxHosts = GetNullableInt(license, "MaxHosts"),
                MaxTasks = GetNullableInt(license, "MaxTasks"),
                As2Licensed = IsJsonTrue(license, "AS2Licensed"),
                BackupHostsLicensed = IsJsonTrue(license, "BackupHostsLicensed"),
                AccountManagementLicensed = IsJsonTrue(license, "AcctMgtLicensed"),
                AdvancedTasksLicensed = IsJsonTrue(license, "AdvTasksLicensed"),
                CustomScriptsLicensed = IsJsonTrue(license, "CustomScriptsLicensed"),
                ApiLicensed = IsJsonTrue(license, "APILicensed"),
                PgpLicensed = IsJsonTrue(license, "PGPLicensed")
            }
        };
    }

    // Converts the captured /api/v1/failoverstatus payload into MAUI's small read-only model.
    internal static MoveItFailoverStatus ParseFailoverStatus(JsonObject root)
    {
        var nodeRoot = root["Nodes"] as JsonObject;
        var nodes = new List<MoveItFailoverNode>();
        if (nodeRoot?["Node"] is JsonArray nodeArray)
        {
            foreach (var node in nodeArray.OfType<JsonObject>())
            {
                nodes.Add(new MoveItFailoverNode
                {
                    NodeNum = GetNullableInt(node, "NodeNum") ?? 0,
                    Hostname = GetString(node, "Hostname").Trim(),
                    ResilRole = GetString(node, "ResilRole").Trim(),
                    NodeHasError = IsJsonTrue(node, "NodeHasError")
                });
            }
        }
        else if (nodeRoot?["Node"] is JsonObject singleNode)
        {
            // Defensive compatibility: MOVEit normally returns an array, but accepting a single object keeps
            // diagnostics available if an incomplete/degraded failover configuration returns one node only.
            nodes.Add(new MoveItFailoverNode
            {
                NodeNum = GetNullableInt(singleNode, "NodeNum") ?? 0,
                Hostname = GetString(singleNode, "Hostname").Trim(),
                ResilRole = GetString(singleNode, "ResilRole").Trim(),
                NodeHasError = IsJsonTrue(singleNode, "NodeHasError")
            });
        }

        return new MoveItFailoverStatus
        {
            ResilEnabled = IsJsonTrue(root, "ResilEnabled"),
            PrimaryStatus = GetString(root, "ResilStatus"),
            PrimaryStatusStamp = GetString(root, "ResilStatusStamp"),
            SecondaryStatus = GetString(root, "ResilStatusSec"),
            SecondaryStatusStamp = GetString(root, "ResilStatusStampSec"),
            ThisNode = GetNullableInt(root, "ThisNode") ?? 0,
            Nodes = nodes
        };
    }

    // Purpose: Retrieve string.
    internal static string GetString(JsonObject? obj, string propertyName)
    {
        if (obj is null || obj[propertyName] is null) return string.Empty;
        var node = obj[propertyName];
        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var text)) return text ?? string.Empty;
            if (value.TryGetValue<int>(out var intValue)) return intValue.ToString();
            if (value.TryGetValue<long>(out var longValue)) return longValue.ToString();
            if (value.TryGetValue<bool>(out var boolValue)) return boolValue ? "true" : "false";
        }
        return node?.ToJsonString() ?? string.Empty;
    }

    // Purpose: Retrieve nullable int.
    internal static int? GetNullableInt(JsonObject obj, string propertyName)
        => int.TryParse(GetString(obj, propertyName), out var value) ? value : null;

    // Purpose: Returns nullable long.
    private static long? GetNullableLong(JsonObject obj, string propertyName)
        => long.TryParse(GetString(obj, propertyName), out var value) ? value : null;

    // Purpose: Returns whether JSON true.
    private static bool IsJsonTrue(JsonObject obj, string propertyName)
    {
        var value = GetString(obj, propertyName);
        return value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    // Purpose: Retrieve nullable double.
    private static double? GetNullableDouble(JsonObject obj, string propertyName)
        => double.TryParse(
            GetString(obj, propertyName),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;

    // Purpose: Retrieve date time offset.
    private static DateTimeOffset? GetDateTimeOffset(JsonObject obj, string propertyName)
        => DateTimeOffset.TryParse(GetString(obj, propertyName), out var value) ? value : null;

    private sealed record TokenResult(string AccessToken, string RefreshToken, int ExpiresInSeconds);
}


// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using System.Globalization;
using System.Text.Json.Nodes;
using MoveItAutomationUI.Services.MoveIt.Hosts;

namespace MoveItAutomationUI.Services.MoveIt;

public interface IHostConnectionTestService
{
    // Returns whether MAUI has a verified native MOVEit connection-test pattern for this Host type.
    bool SupportsHostType(string? typeCode);
    // Tests one saved Host by following the same checkout/test/release lifecycle used by MOVEit Web Admin.
    Task<HostConnectionTestResult> TestHostAsync(string hostId, CancellationToken cancellationToken = default);
    // Uses the verified native browse pattern for supported file-oriented Host types to validate a configured Task directory/file mask.
    Task<HostConnectionTestResult> TestHostPathAsync(string hostId, string directory, string fileMask = "", CancellationToken cancellationToken = default);
}

public sealed record HostConnectionTestResult(
    string HostId,
    string HostName,
    string HostTypeCode,
    string Status,
    string Message,
    string CurrentDirectory,
    int ItemCount,
    int FileCount = 0);

/// <summary>
/// Reproduces MOVEit Web Admin Host tests through verified native v0 endpoint patterns.
/// Only Host types with an explicitly implemented and observed pattern are considered supported.
/// </summary>
public sealed class HostConnectionTestService : IHostConnectionTestService
{
    public const string StatusNotTested = "Not tested";
    public const string StatusUnsupported = "Unsupported";
    public const string StatusIncomplete = "Incomplete";
    public const string StatusTesting = "Testing";
    public const string StatusPassed = "Passed";
    public const string StatusFailed = "Failed";

    private const string FileSystemShareHostTypeCode = "Share";
    private const string MoveItTransferHostTypeCode = "siLock";
    private const string FtpHostTypeCode = "FTP";
    private const string SshHostTypeCode = "SSHFTP";
    private const string S3HostTypeCode = "S3";
    private const string AzureBlobHostTypeCode = "AzureBlob";
    private const string SharePointHostTypeCode = "SharePoint";
    private const string GoogleStorageHostTypeCode = "GoogleStorage";
    private readonly IMoveItRestClient _restClient;
    private readonly IMoveItSessionService? _sessions;
    private readonly ILogger<HostConnectionTestService> _logger;

    public HostConnectionTestService(IMoveItRestClient restClient, ILogger<HostConnectionTestService> logger, IMoveItSessionService? sessions = null)
    {
        _restClient = restClient;
        _sessions = sessions;
        _logger = logger;
    }

    public bool SupportsHostType(string? typeCode)
    {
        var normalized = typeCode?.Trim();
        if (string.Equals(normalized, "OneDrive", StringComparison.OrdinalIgnoreCase)) return MoveItFeatureSupport.Supports(_sessions?.Get()?.Info, MoveItFeature.OneDriveHosts);
        return string.Equals(normalized, FileSystemShareHostTypeCode, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, MoveItTransferHostTypeCode, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, SshHostTypeCode, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, FtpHostTypeCode, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, S3HostTypeCode, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, AzureBlobHostTypeCode, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, SharePointHostTypeCode, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, GoogleStorageHostTypeCode, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<HostConnectionTestResult> TestHostAsync(string hostId, CancellationToken cancellationToken = default)
    {
        var normalizedHostId = (hostId ?? string.Empty).Trim();
        if (normalizedHostId.Length == 0)
            throw new ArgumentException("Host ID is required.", nameof(hostId));

        JsonObject? checkout = null;
        using var server = _sessions?.Get() is { } session ? ObjectCheckout.ForServer(session.WebAdminUrl) : null;
        HostConnectionTestResult? result = null;

        try
        {
            result = await HostBrowseCheckout.ReadAsync(_restClient, normalizedHostId, async (definitionWrapper, token) =>
            {
                checkout = definitionWrapper;
                var (typeCode, definition) = GetDefinition(checkout);
                var hostName = GetString(definition, "Name");

                if (!SupportsHostType(typeCode) || definition is null)
                {
                    result = new HostConnectionTestResult(
                        normalizedHostId,
                        hostName,
                        typeCode,
                        StatusUnsupported,
                        string.IsNullOrWhiteSpace(typeCode)
                            ? "MOVEit did not return a supported Host definition."
                            : $"MAUI does not currently have a connection-test pattern for Host type '{typeCode}'.",
                        string.Empty,
                        0);
                }
                else if (typeCode.Equals(FileSystemShareHostTypeCode, StringComparison.OrdinalIgnoreCase))
                {
                    result = await TestFileSystemShareHostAsync(normalizedHostId, definition, token);
                }
                else if (typeCode.Equals(MoveItTransferHostTypeCode, StringComparison.OrdinalIgnoreCase))
                {
                    result = await TestMoveItTransferHostAsync(normalizedHostId, definition, token);
                }
                else if (typeCode.Equals(SshHostTypeCode, StringComparison.OrdinalIgnoreCase))
                {
                    result = await TestSshHostAsync(normalizedHostId, definition, token);
                }
                else if (typeCode.Equals(FtpHostTypeCode, StringComparison.OrdinalIgnoreCase))
                {
                    result = await TestFtpHostAsync(normalizedHostId, definition, token);
                }
                else if (typeCode.Equals("OneDrive", StringComparison.OrdinalIgnoreCase))
                {
                    result = await TestOneDriveHostAsync(normalizedHostId, definition, token);
                }
                else if (typeCode.Equals(S3HostTypeCode, StringComparison.OrdinalIgnoreCase))
                {
                    result = await TestS3HostAsync(normalizedHostId, definition, token);
                }
                else if (typeCode.Equals(AzureBlobHostTypeCode, StringComparison.OrdinalIgnoreCase))
                {
                    result = await TestAzureBlobHostAsync(normalizedHostId, definition, token);
                }
                else if (typeCode.Equals(SharePointHostTypeCode, StringComparison.OrdinalIgnoreCase))
                {
                    result = await TestSharePointHostAsync(normalizedHostId, definition, token);
                }
                else if (typeCode.Equals(GoogleStorageHostTypeCode, StringComparison.OrdinalIgnoreCase))
                {
                    result = await TestGoogleStorageHostAsync(normalizedHostId, definition, token);
                }
                // Protocol adapters return displayable failures. Preserve native ownership
                // conflicts as exceptions until ObjectCheckout has retired its release intent.
                if (result?.Status == StatusFailed && ObjectCheckoutRejectedException.DescribesLock(result.Message))
                    throw new ObjectCheckoutRejectedException(result.Message);
                return result!;
            }, cancellationToken);
        }
        catch (MoveItAuthenticationException)
        {
            throw;
        }
        catch (MoveItApiException ex)
        {
            var (typeCode, definition) = GetDefinition(checkout);
            result = new HostConnectionTestResult(
                normalizedHostId,
                GetString(definition, "Name"),
                typeCode,
                StatusFailed,
                CleanMoveItError(ex.Message),
                string.Empty,
                0);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TimeoutException
            || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            var (typeCode, definition) = GetDefinition(checkout);
            result = new HostConnectionTestResult(normalizedHostId, GetString(definition, "Name"), typeCode, StatusFailed,
                ex is OperationCanceledException or TimeoutException
                    ? "The Host test timed out. Check the Host connection and timeout settings, then try again."
                    : "MAUI could not complete the Host test because the connection to MOVEit failed. Try again.",
                string.Empty, 0);
            _logger.LogWarning(ex, "Host test transport failed for Host {HostId}.", normalizedHostId);
        }
        catch (ObjectCheckoutReleaseException ex)
        {
            _logger.LogWarning(ex, "MAUI could not release MOVEIt Host checkout for Host {HostId} after connection testing.", normalizedHostId);
            result = new HostConnectionTestResult(
                result?.HostId ?? normalizedHostId,
                result?.HostName ?? string.Empty,
                result?.HostTypeCode ?? string.Empty,
                StatusFailed,
                $"{(result?.Status == StatusPassed ? "The connection test succeeded, but " : string.Empty)}MAUI could not release the MOVEit Host checkout. Reload the Host in MAUI before testing again.",
                result?.CurrentDirectory ?? string.Empty,
                result?.ItemCount ?? 0);
        }

        return result ?? new HostConnectionTestResult(
            normalizedHostId,
            string.Empty,
            string.Empty,
            StatusFailed,
            "The Host connection test did not return a result.",
            string.Empty,
            0);
    }



    public async Task<HostConnectionTestResult> TestHostPathAsync(
        string hostId,
        string directory,
        string fileMask = "",
        CancellationToken cancellationToken = default)
    {
        var normalizedHostId = (hostId ?? string.Empty).Trim();
        var normalizedDirectory = (directory ?? string.Empty).Trim();
        var normalizedMask = (fileMask ?? string.Empty).Trim();
        if (normalizedHostId.Length == 0)
            throw new ArgumentException("Host ID is required.", nameof(hostId));
        if (normalizedDirectory.Length == 0)
            throw new ArgumentException("Directory is required for a Host path test.", nameof(directory));

        JsonObject? checkout = null;
        using var server = _sessions?.Get() is { } session ? ObjectCheckout.ForServer(session.WebAdminUrl) : null;
        HostConnectionTestResult? result = null;

        try
        {
            result = await HostBrowseCheckout.ReadAsync(_restClient, normalizedHostId, async (definitionWrapper, token) =>
            {
                checkout = definitionWrapper;
                var (typeCode, definition) = GetDefinition(checkout);
                var hostName = GetString(definition, "Name");

                if (definition is null)
                {
                    result = new HostConnectionTestResult(
                        normalizedHostId,
                        hostName,
                        typeCode,
                        StatusUnsupported,
                        "MOVEit did not return a Host definition that MAUI can browse.",
                        string.Empty,
                        0);
                }
                else if (typeCode.Equals(FileSystemShareHostTypeCode, StringComparison.OrdinalIgnoreCase))
                {
                    result = await TestFileSystemShareHostAsync(normalizedHostId, definition, token, normalizedDirectory, normalizedMask);
                }
                else if (typeCode.Equals(MoveItTransferHostTypeCode, StringComparison.OrdinalIgnoreCase))
                {
                    result = await TestMoveItTransferHostAsync(normalizedHostId, definition, token, normalizedDirectory, normalizedMask);
                }
                else if (typeCode.Equals(SshHostTypeCode, StringComparison.OrdinalIgnoreCase))
                {
                    result = await TestSshHostAsync(normalizedHostId, definition, token, normalizedDirectory, normalizedMask);
                }
                else if (typeCode.Equals(FtpHostTypeCode, StringComparison.OrdinalIgnoreCase))
                {
                    result = await TestFtpHostAsync(normalizedHostId, definition, token, normalizedDirectory, normalizedMask);
                }
                else
                {
                    result = new HostConnectionTestResult(
                        normalizedHostId,
                        hostName,
                        typeCode,
                        StatusUnsupported,
                        $"MAUI has a verified connection test for Host type '{typeCode}', but not a verified directory-browse pattern for Task Preflight.",
                        normalizedDirectory,
                        0);
                }
                if (result?.Status == StatusFailed && ObjectCheckoutRejectedException.DescribesLock(result.Message))
                    throw new ObjectCheckoutRejectedException(result.Message);
                return result!;
            }, cancellationToken);
        }
        catch (MoveItAuthenticationException)
        {
            throw;
        }
        catch (MoveItApiException ex)
        {
            var (typeCode, definition) = GetDefinition(checkout);
            result = new HostConnectionTestResult(
                normalizedHostId,
                GetString(definition, "Name"),
                typeCode,
                StatusFailed,
                CleanMoveItError(ex.Message),
                normalizedDirectory,
                0);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TimeoutException
            || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            var (typeCode, definition) = GetDefinition(checkout);
            result = new HostConnectionTestResult(normalizedHostId, GetString(definition, "Name"), typeCode, StatusFailed,
                ex is OperationCanceledException or TimeoutException
                    ? "The Host test timed out. Check the Host connection and timeout settings, then try again."
                    : "MAUI could not complete the Host test because the connection to MOVEit failed. Try again.",
                string.Empty, 0);
            _logger.LogWarning(ex, "Host test transport failed for Host {HostId}.", normalizedHostId);
        }
        catch (ObjectCheckoutReleaseException ex)
        {
            _logger.LogWarning(ex, "MAUI could not release MOVEit Host checkout for Host {HostId} after Task Preflight path testing.", normalizedHostId);
            result = new HostConnectionTestResult(
                result?.HostId ?? normalizedHostId,
                result?.HostName ?? string.Empty,
                result?.HostTypeCode ?? string.Empty,
                StatusFailed,
                $"{(result?.Status == StatusPassed ? "The path browse succeeded, but " : string.Empty)}MAUI could not release the MOVEit Host checkout. Reload the Host in MAUI before testing again.",
                result?.CurrentDirectory ?? normalizedDirectory,
                result?.ItemCount ?? 0,
                result?.FileCount ?? 0);
        }

        return result ?? new HostConnectionTestResult(
            normalizedHostId,
            string.Empty,
            string.Empty,
            StatusFailed,
            "The Host path test did not return a result.",
            normalizedDirectory,
            0);
    }

    private async Task<HostConnectionTestResult> TestFileSystemShareHostAsync(string hostId, JsonObject definition, CancellationToken cancellationToken, string? directory = null, string? fileMask = null)
    {
        var effective = await EnrichDefinitionAsync(
            hostId,
            FileSystemShareHostTypeCode,
            definition,
            cancellationToken,
            "Name",
            "UNC",
            "DefPreserveFileTimestamp");

        var hostName = GetString(effective, "Name");
        var shareName = GetString(effective, "UNC");

        if (string.IsNullOrWhiteSpace(shareName))
            return Incomplete(hostId, hostName, FileSystemShareHostTypeCode, "UNC Share", new[] { "UNC Path" });

        // Match the verified native UNC browse request. Task Preflight optionally substitutes the configured
        // Task directory/file mask while retaining the saved Host identity and credentials.
        var browseDirectory = ResolveFileSystemBrowseDirectory(shareName, directory);
        var request = new JsonObject
        {
            ["showfiles"] = true,
            ["mask"] = (fileMask ?? string.Empty).Trim(),
            ["hostid"] = hostId,
            ["username"] = string.Empty,
            ["password"] = string.Empty,
            ["dirname"] = browseDirectory,
            ["sharename"] = shareName,
            ["firstbrowse"] = true
        };
        AddBoolIfPresent(request, "preservefiletimestamp", effective, "DefPreserveFileTimestamp");

        try
        {
            var response = await _restClient.BrowseFileSystemHostV0Async(request, cancellationToken);
            return new HostConnectionTestResult(
                hostId,
                hostName,
                FileSystemShareHostTypeCode,
                StatusPassed,
                "Test Successful",
                FirstNonEmpty(GetString(response, "CurrentDir"), browseDirectory),
                CountBrowseItems(response),
                CountCollection(response, "Files", "File"));
        }
        catch (MoveItAuthenticationException)
        {
            throw;
        }
        catch (MoveItApiException ex)
        {
            return Failed(hostId, hostName, FileSystemShareHostTypeCode, ex);
        }
    }

    private async Task<HostConnectionTestResult> TestMoveItTransferHostAsync(string hostId, JsonObject definition, CancellationToken cancellationToken, string? directory = null, string? fileMask = null)
    {
        var effective = await EnrichDefinitionAsync(
            hostId,
            MoveItTransferHostTypeCode,
            definition,
            cancellationToken,
            "Name",
            "Host",
            "DefUsername",
            "Port",
            "ScriptPath",
            "Secure",
            "IgnoreCertProbs",
            "DefClientCertStore",
            "DefClientCertSubject",
            "DefClientCertSHA1",
            "DefConnTimeoutSecs",
            "DefDataTimeoutSecs");

        var hostName = GetString(effective, "Name");
        var server = GetString(effective, "Host");
        var username = GetString(effective, "DefUsername");
        var port = GetInt(effective, "Port");
        var serverPort = port.GetValueOrDefault();

        // Host/port/user identify the saved MOVEit Transfer connection. Other values are effective/defaultable
        // settings and are sent only when MOVEit supplied them; hostid lets the native browse apply saved defaults.
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(server)) missing.Add("Host");
        if (serverPort <= 0) missing.Add("Port");
        if (string.IsNullOrWhiteSpace(username)) missing.Add("Username");

        if (missing.Count > 0)
            return Incomplete(hostId, hostName, MoveItTransferHostTypeCode, "MOVEit Transfer", missing);

        var loginInfo = new JsonObject
        {
            ["serverip"] = server,
            ["serverport"] = serverPort,
            ["username"] = username,
            // MOVEit resolves the saved protected Host password internally when hostid is supplied.
            ["password"] = null
        };

        AddStringIfPresent(loginInfo, "scriptpath", effective, "ScriptPath");
        AddBoolIfPresent(loginInfo, "secure", effective, "Secure");
        AddBoolIfPresent(loginInfo, "ignorecertprobs", effective, "IgnoreCertProbs");
        AddStringIfPresent(loginInfo, "clientcertstore", effective, "DefClientCertStore");
        AddStringIfPresent(loginInfo, "clientcertsubject", effective, "DefClientCertSubject");
        AddStringIfPresent(loginInfo, "clientcertsha1", effective, "DefClientCertSHA1");
        AddIntIfPresent(loginInfo, "conntimeoutsecs", effective, "DefConnTimeoutSecs");
        AddIntIfPresent(loginInfo, "datatimeoutsecs", effective, "DefDataTimeoutSecs");

        var pathBrowse = !string.IsNullOrWhiteSpace(directory) || !string.IsNullOrWhiteSpace(fileMask);
        var browseDirectory = string.IsNullOrWhiteSpace(directory) ? "/" : directory!.Trim();
        var request = new JsonObject
        {
            ["showfiles"] = pathBrowse,
            ["mask"] = (fileMask ?? string.Empty).Trim(),
            ["hostid"] = hostId,
            ["logininfo"] = loginInfo,
            ["folderid"] = -1,
            ["folderpath"] = browseDirectory,
            ["taskid"] = null,
            ["stepid"] = null
        };

        try
        {
            var response = await _restClient.BrowseDmzHostV0Async(request, cancellationToken);
            return new HostConnectionTestResult(
                hostId,
                hostName,
                MoveItTransferHostTypeCode,
                StatusPassed,
                "Test Successful",
                FirstNonEmpty(GetString(response, "CurrentDir"), browseDirectory),
                CountCollection(response, "Folders", "Folder") + CountCollection(response, "Files", "File"),
                CountCollection(response, "Files", "File"));
        }
        catch (MoveItAuthenticationException)
        {
            throw;
        }
        catch (MoveItApiException ex)
        {
            return Failed(hostId, hostName, MoveItTransferHostTypeCode, ex);
        }
    }

    private async Task<HostConnectionTestResult> TestSshHostAsync(string hostId, JsonObject definition, CancellationToken cancellationToken, string? directory = null, string? fileMask = null)
    {
        var effective = await EnrichDefinitionAsync(
            hostId,
            SshHostTypeCode,
            definition,
            cancellationToken,
            "Name",
            "Host",
            "DefUsername",
            "Port",
            "DefConnTimeoutSecs",
            "EncList",
            "IgnoreCertProbs",
            "DefClientKeyID",
            "SSHFingerprint",
            "ProxyType",
            "OptimizeSSHBuf",
            "DefBlindDL",
            "DefPreserveFileTimestamp");

        var hostName = GetString(effective, "Name");
        var server = GetString(effective, "Host");
        var username = GetString(effective, "DefUsername");
        var port = GetInt(effective, "Port");
        var serverPort = port.GetValueOrDefault();
        var proxyType = NullIfWhiteSpace(GetString(effective, "ProxyType"));

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(server)) missing.Add("Host");
        if (serverPort <= 0) missing.Add("Port");
        if (string.IsNullOrWhiteSpace(username)) missing.Add("Username");

        if (missing.Count > 0)
            return Incomplete(hostId, hostName, SshHostTypeCode, "SSH/SFTP", missing);

        if (!string.IsNullOrWhiteSpace(directory) && GetNullableBool(effective, "DefBlindDL") == true)
            return new HostConnectionTestResult(hostId, hostName, SshHostTypeCode, StatusUnsupported,
                "The saved SSH/SFTP Host uses Blind Downloads, so MOVEit does not provide a directory listing for path validation.",
                directory.Trim(), 0);

        // The captured native browse pattern is verified only for explicitly configured direct SSH/SFTP Hosts.
        if (!string.IsNullOrWhiteSpace(proxyType) && !proxyType.Equals("None", StringComparison.OrdinalIgnoreCase))
            return UnsupportedProxy(hostId, hostName, SshHostTypeCode, "SSH/SFTP", proxyType);

        var loginInfo = new JsonObject
        {
            ["serverip"] = server,
            ["serverport"] = serverPort,
            ["username"] = username,
            // MOVEit resolves the saved protected Host password internally when hostid is supplied.
            ["password"] = null,
            ["customdir"] = 0,
            ["customdircoldate"] = 0,
            ["customdircolfilename"] = 0,
            ["customdirskipbottom"] = 0,
            ["customdirskiptop"] = 0,
            ["dirscriptid"] = string.Empty
        };

        // Optional/effective SSH values are sent only when MOVEit supplied them from checkout or v1.
        // In particular, absent IgnoreCertProbs must never become false because that changes host-key trust.
        AddBoolIfPresent(loginInfo, "ignorecertprobs", effective, "IgnoreCertProbs");
        AddStringIfPresent(loginInfo, "clientkeyid", effective, "DefClientKeyID");
        AddStringIfPresent(loginInfo, "hostfingerprint", effective, "SSHFingerprint");
        AddIntIfPresent(loginInfo, "conntimeoutsecs", effective, "DefConnTimeoutSecs");
        AddBoolIfPresent(loginInfo, "optimizesshbuf", effective, "OptimizeSSHBuf");
        if (!string.IsNullOrWhiteSpace(proxyType))
            loginInfo["proxytype"] = proxyType;
        AddStringIfPresent(loginInfo, "encryptionlist", effective, "EncList");
        AddBoolIfPresent(loginInfo, "blinddownloads", effective, "DefBlindDL");
        AddBoolIfPresent(loginInfo, "preservefiletimestamp", effective, "DefPreserveFileTimestamp");

        var request = BuildBrowseRequest(hostId, loginInfo, directory, fileMask);

        try
        {
            var response = await _restClient.BrowseSshHostV0Async(request, cancellationToken);
            return PassedBrowse(hostId, hostName, SshHostTypeCode, response);
        }
        catch (MoveItAuthenticationException)
        {
            throw;
        }
        catch (MoveItApiException ex)
        {
            return Failed(hostId, hostName, SshHostTypeCode, ex);
        }
    }

    private async Task<HostConnectionTestResult> TestFtpHostAsync(string hostId, JsonObject definition, CancellationToken cancellationToken, string? directory = null, string? fileMask = null)
    {
        var effective = await EnrichDefinitionAsync(
            hostId,
            FtpHostTypeCode,
            definition,
            cancellationToken,
            "Name",
            "Host",
            "DefUsername",
            "Port",
            "Secure",
            "IgnoreCertProbs",
            "DefPassive",
            "DefAccount",
            "DefClientCertStore",
            "DefClientCertSubject",
            "DefClientCertSHA1",
            "DefConnTimeoutSecs",
            "DefDataTimeoutSecs",
            "DefReuseSSL",
            "NATUseServerIPForData",
            "NATClientIP",
            "AddCom",
            "ProxyType",
            "DefBlindDL");

        var hostName = GetString(effective, "Name");
        var server = GetString(effective, "Host");
        var username = GetString(effective, "DefUsername");
        var port = GetInt(effective, "Port");
        var serverPort = port.GetValueOrDefault();
        var proxyType = NullIfWhiteSpace(GetString(effective, "ProxyType"));

        // Do not reject a saved FTP/FTPS Host because checkout omitted effective/default timeout or TLS values.
        // Let MOVEit's native browse apply those saved defaults when MAUI cannot retrieve them from v1 either.
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(server)) missing.Add("Host");
        if (serverPort <= 0) missing.Add("Port");
        if (string.IsNullOrWhiteSpace(username)) missing.Add("Username");

        if (missing.Count > 0)
            return Incomplete(hostId, hostName, FtpHostTypeCode, "FTP/FTPS", missing);

        if (!string.IsNullOrWhiteSpace(directory) && GetNullableBool(effective, "DefBlindDL") == true)
            return new HostConnectionTestResult(hostId, hostName, FtpHostTypeCode, StatusUnsupported,
                "The saved FTP/FTPS Host uses Blind Downloads, so MOVEit does not provide a directory listing for path validation.",
                directory.Trim(), 0);

        // The captured native browse pattern is verified only for explicitly configured direct FTP/FTPS Hosts.
        if (!string.IsNullOrWhiteSpace(proxyType) && !proxyType.Equals("None", StringComparison.OrdinalIgnoreCase))
            return UnsupportedProxy(hostId, hostName, FtpHostTypeCode, "FTP/FTPS", proxyType);

        var loginInfo = new JsonObject
        {
            ["serverip"] = server,
            ["serverport"] = serverPort,
            ["username"] = username,
            // MOVEit resolves the saved protected Host password internally when hostid is supplied.
            ["password"] = null,
            ["customdir"] = 0,
            ["customdircoldate"] = 0,
            ["customdircolfilename"] = 0,
            ["customdirskipbottom"] = 0,
            ["customdirskiptop"] = 0,
            ["dirscriptid"] = string.Empty
        };

        AddStringIfPresent(loginInfo, "secure", effective, "Secure");
        AddBoolIfPresent(loginInfo, "ignorecertprobs", effective, "IgnoreCertProbs");
        AddBoolIfPresent(loginInfo, "passive", effective, "DefPassive");
        if (HasValue(effective, "DefAccount"))
            loginInfo["account"] = GetNullableString(effective, "DefAccount");
        AddStringIfPresent(loginInfo, "clientcertstore", effective, "DefClientCertStore");
        AddStringIfPresent(loginInfo, "clientcertsubject", effective, "DefClientCertSubject");
        AddStringIfPresent(loginInfo, "clientcertsha1", effective, "DefClientCertSHA1");
        AddIntIfPresent(loginInfo, "conntimeoutsecs", effective, "DefConnTimeoutSecs");
        AddIntIfPresent(loginInfo, "datatimeoutsecs", effective, "DefDataTimeoutSecs");
        AddBoolIfPresent(loginInfo, "reuseSSLsession", effective, "DefReuseSSL");
        AddIntIfPresent(loginInfo, "natuseserveripfordata", effective, "NATUseServerIPForData");
        AddStringIfPresent(loginInfo, "natclientip", effective, "NATClientIP");
        AddStringIfPresent(loginInfo, "addcom", effective, "AddCom");
        if (!string.IsNullOrWhiteSpace(proxyType))
            loginInfo["proxytype"] = proxyType;
        AddBoolIfPresent(loginInfo, "blinddownloads", effective, "DefBlindDL");

        var request = BuildBrowseRequest(hostId, loginInfo, directory, fileMask);

        try
        {
            var response = await _restClient.BrowseFtpHostV0Async(request, cancellationToken);
            return PassedBrowse(hostId, hostName, FtpHostTypeCode, response);
        }
        catch (MoveItAuthenticationException)
        {
            throw;
        }
        catch (MoveItApiException ex)
        {
            return Failed(hostId, hostName, FtpHostTypeCode, ex);
        }
    }

    private async Task<HostConnectionTestResult> TestOneDriveHostAsync(string hostId, JsonObject definition, CancellationToken cancellationToken)
    {
        MoveItFeatureSupport.Require(_sessions?.Get()?.Info, MoveItFeature.OneDriveHosts);
        var effective = await EnrichDefinitionAsync(hostId, "OneDrive", definition, cancellationToken,
            "Name", "DefUsername", "OAuthClientID", "TenantID", "DefConnTimeoutSecs", "DefDataTimeoutSecs", "ProxyType",
            "DefClientCertSHA1", "DefClientCertStore", "DefClientCertSubject", "OAuthClientSecret");
        var required = new[] { "TenantID", "DefUsername", "OAuthClientID" }
            .Concat(HostCloudAuthenticationSettings.OneDriveMode(effective) == "certificate" ? HostResourceContracts.CertificateFields : Array.Empty<string>());
        var missing = required.Where(key => GetString(effective, key).Length == 0).ToList();
        if (missing.Count > 0) return Incomplete(hostId, GetString(effective, "Name"), "OneDrive", "Microsoft OneDrive", missing);
        var response = await _restClient.BrowseOneDriveHostV0Async(HostCloudAuthenticationSettings.OneDriveBrowse(hostId, effective), cancellationToken);
        return PassedBrowse(hostId, GetString(effective, "Name"), "OneDrive", response);
    }

    private async Task<HostConnectionTestResult> TestS3HostAsync(string hostId, JsonObject definition, CancellationToken cancellationToken)
    {
        var effective = await EnrichDefinitionAsync(
            hostId,
            S3HostTypeCode,
            definition,
            cancellationToken,
            CloudCommonFields.Concat(new[] { "DefDLInteg", "DefBucket", "AuthMethod", "IsNonAmazonEndPoint", "S3AlternativeEndpoint" }).ToArray());

        var hostName = GetString(effective, "Name");
        var proxyType = NullIfWhiteSpace(GetString(effective, "ProxyType"));
        if (!string.IsNullOrWhiteSpace(proxyType) && !proxyType.Equals("None", StringComparison.OrdinalIgnoreCase))
            return UnsupportedProxy(hostId, hostName, S3HostTypeCode, "Amazon S3", proxyType);

        var request = BuildCloudTestRequest(hostId, effective, proxyType);
        AddStringIfPresent(request, "DefDLInteg", effective, "DefDLInteg");
        AddStringIfPresent(request, "DefBucket", effective, "DefBucket");
        var method = HostCloudAuthenticationSettings.S3Method(effective);
        if (method != 0) MoveItFeatureSupport.Require(_sessions?.Get()?.Info, MoveItFeature.AmazonS3IamAuthentication);
        if (HostDefinitionJson.GetNode(effective, "AuthMethod") is not null || MoveItFeatureSupport.Supports(_sessions?.Get()?.Info, MoveItFeature.AmazonS3IamAuthentication)) request["AuthMethod"] = method;
        if (method != 0) request.Remove("DefPassword");
        if (method == 2) request.Remove("DefUsername");
        if (method == 0)
        {
            AddIntIfPresent(request, "IsNonAmazonEndPoint", effective, "IsNonAmazonEndPoint");
            AddStringIfPresent(request, "S3AlternativeEndpoint", effective, "S3AlternativeEndpoint");
        }
        request["Group"] = new JsonArray();

        return await ExecuteStatusTestAsync(
            hostId,
            hostName,
            S3HostTypeCode,
            "Amazon S3",
            request,
            _restClient.TestS3HostV0Async,
            cancellationToken);
    }

    private async Task<HostConnectionTestResult> TestAzureBlobHostAsync(string hostId, JsonObject definition, CancellationToken cancellationToken)
    {
        var effective = await EnrichDefinitionAsync(
            hostId,
            AzureBlobHostTypeCode,
            definition,
            cancellationToken,
            CloudCommonFields.Append("DefDLInteg").Append("DefContainer").ToArray());

        var hostName = GetString(effective, "Name");
        var proxyType = NullIfWhiteSpace(GetString(effective, "ProxyType"));
        if (!string.IsNullOrWhiteSpace(proxyType) && !proxyType.Equals("None", StringComparison.OrdinalIgnoreCase))
            return UnsupportedProxy(hostId, hostName, AzureBlobHostTypeCode, "Azure Blob", proxyType);

        var request = BuildCloudTestRequest(hostId, effective, proxyType);
        AddStringIfPresent(request, "DefDLInteg", effective, "DefDLInteg");
        AddStringIfPresent(request, "DefContainer", effective, "DefContainer");
        request["Group"] = new JsonArray();

        return await ExecuteStatusTestAsync(
            hostId,
            hostName,
            AzureBlobHostTypeCode,
            "Azure Blob",
            request,
            _restClient.TestAzureBlobHostV0Async,
            cancellationToken);
    }

    private async Task<HostConnectionTestResult> TestGoogleStorageHostAsync(string hostId, JsonObject definition, CancellationToken cancellationToken)
    {
        var effective = await EnrichDefinitionAsync(
            hostId,
            GoogleStorageHostTypeCode,
            definition,
            cancellationToken,
            CloudCommonFields.Concat(new[] { "DefBucket", "UseOAuth" })
                .Concat(!HasValue(definition, "UseOAuth") || HostBrowseSettings.Flag(definition, "UseOAuth")
                    ? new[] { "ProjectID", "OAuthClientID" } : Array.Empty<string>()).ToArray());

        var hostName = GetString(effective, "Name");
        var proxyType = NullIfWhiteSpace(GetString(effective, "ProxyType"));
        if (!string.IsNullOrWhiteSpace(proxyType) && !proxyType.Equals("None", StringComparison.OrdinalIgnoreCase))
            return UnsupportedProxy(hostId, hostName, GoogleStorageHostTypeCode, "Google Storage", proxyType);

        var request = BuildCloudTestRequest(hostId, effective, proxyType);
        AddStringIfPresent(request, "DefBucket", effective, "DefBucket");
        var useOAuth = HostBrowseSettings.Flag(effective, "UseOAuth");
        request["UseOAuth"] = useOAuth ? 1 : 0;
        if (useOAuth)
        {
            var missing = new List<string>();
            if (GetString(effective, "ProjectID").Length == 0) missing.Add("Project ID");
            if (GetString(effective, "OAuthClientID").Length == 0) missing.Add("OAuth Client ID");
            if (missing.Count > 0) return Incomplete(hostId, hostName, GoogleStorageHostTypeCode, "Google Storage", missing);
            request.Remove("DefUsername");
            request.Remove("DefPassword");
            request["ProjectID"] = GetString(effective, "ProjectID");
            request["OAuthClientID"] = GetString(effective, "OAuthClientID");
            // Tests use a saved Host ID. Null retains its protected private key; never forward GET ciphertext.
            request["OAuthClientSecret"] = null;
        }
        request["Group"] = new JsonArray();

        return await ExecuteStatusTestAsync(
            hostId,
            hostName,
            GoogleStorageHostTypeCode,
            "Google Storage",
            request,
            _restClient.TestGoogleStorageHostV0Async,
            cancellationToken);
    }

    private static readonly string[] CloudCommonFields =
    {
        "Name",
        "Desc",
        "DefRetryCount",
        "DefRetryTimeoutSecs",
        "UsedByCount",
        "OverridesTotal",
        "OverridesNoPerm",
        "DelOldStateDays",
        "UseDefStateCaching",
        "AdjDST",
        "DefUsername",
        "DefConnTimeoutSecs",
        "DefDataTimeoutSecs",
        "DefMxBy",
        "DefMxFi",
        "DefRescanSecs",
        "ProxyType",
        "Secure"
    };

    private static JsonObject BuildCloudTestRequest(string hostId, JsonObject definition, string? proxyType)
    {
        var request = new JsonObject
        {
            ["Name"] = GetString(definition, "Name"),
            ["ID"] = hostId,
            // MOVEit resolves the saved protected cloud credential internally for the checked-out Host.
            ["DefPassword"] = null
        };

        AddStringIfPresent(request, "Desc", definition, "Desc");
        AddIntIfPresent(request, "DefRetryCount", definition, "DefRetryCount");
        AddIntIfPresent(request, "DefRetryTimeoutSecs", definition, "DefRetryTimeoutSecs");
        AddIntIfPresent(request, "UsedByCount", definition, "UsedByCount");
        AddIntIfPresent(request, "OverridesTotal", definition, "OverridesTotal");
        AddBoolIfPresent(request, "OverridesNoPerm", definition, "OverridesNoPerm");
        AddIntIfPresent(request, "DelOldStateDays", definition, "DelOldStateDays");
        AddIntIfPresent(request, "UseDefStateCaching", definition, "UseDefStateCaching");
        AddIntIfPresent(request, "AdjDST", definition, "AdjDST");
        AddStringIfPresent(request, "DefUsername", definition, "DefUsername");
        AddIntIfPresent(request, "DefConnTimeoutSecs", definition, "DefConnTimeoutSecs");
        AddIntIfPresent(request, "DefDataTimeoutSecs", definition, "DefDataTimeoutSecs");
        AddIntIfPresent(request, "DefMxBy", definition, "DefMxBy");
        AddIntIfPresent(request, "DefMxFi", definition, "DefMxFi");
        AddIntIfPresent(request, "DefRescanSecs", definition, "DefRescanSecs");
        if (!string.IsNullOrWhiteSpace(proxyType))
            request["ProxyType"] = proxyType;
        AddIntIfPresent(request, "Secure", definition, "Secure");
        return request;
    }

    private async Task<HostConnectionTestResult> ExecuteStatusTestAsync(
        string hostId,
        string hostName,
        string typeCode,
        string displayType,
        JsonObject request,
        Func<JsonObject, CancellationToken, Task<JsonObject>> execute,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await execute(request, cancellationToken);
            if (GetString(response, "status").Equals("success", StringComparison.OrdinalIgnoreCase))
            {
                return new HostConnectionTestResult(
                    hostId,
                    hostName,
                    typeCode,
                    StatusPassed,
                    "Test Successful",
                    string.Empty,
                    0);
            }

            return new HostConnectionTestResult(
                hostId,
                hostName,
                typeCode,
                StatusFailed,
                GetResponseErrorMessage(response, $"MOVEit reported that the {displayType} Host test failed."),
                string.Empty,
                0);
        }
        catch (MoveItAuthenticationException)
        {
            throw;
        }
        catch (MoveItApiException ex)
        {
            return Failed(hostId, hostName, typeCode, ex);
        }
    }

    private async Task<HostConnectionTestResult> TestSharePointHostAsync(string hostId, JsonObject definition, CancellationToken cancellationToken)
    {
        var effective = await EnrichDefinitionAsync(
            hostId,
            SharePointHostTypeCode,
            definition,
            cancellationToken,
            CloudCommonFields.Where(key => key != "DefUsername")
                .Concat(new[] { "TenantName", "SiteUrlPrefix", "Site", "UsingAppAuthentication", "ClientID", "DefDocumentLibrary", "DefDocumentLibraryGuid" })
                .Concat(GetInt(definition, "UsingAppAuthentication") switch
                {
                    1 => new[] { "RealmID" },
                    2 => new[] { "TenantID", "DefClientCertSHA1", "DefClientCertStore", "DefClientCertSubject" },
                    _ => new[] { "RealmID", "TenantID", "DefClientCertSHA1", "DefClientCertStore", "DefClientCertSubject" }
                }).ToArray());

        var hostName = GetString(effective, "Name");
        var proxyType = NullIfWhiteSpace(GetString(effective, "ProxyType"));
        var authenticationMode = GetInt(effective, "UsingAppAuthentication");

        if (authenticationMode.HasValue && authenticationMode is not (1 or 2))
        {
            return new HostConnectionTestResult(hostId, hostName, SharePointHostTypeCode, StatusUnsupported,
                "SharePoint Test Host requires App Access or Certificate Access.", string.Empty, 0);
        }

        // The supplied native test captures share common fields but have distinct authentication fields.
        // A blank Site selects the default site, as in the shared SharePoint browser and save contract.
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(hostName)) missing.Add("Name");
        if (string.IsNullOrWhiteSpace(GetString(effective, "TenantName"))) missing.Add("Tenant Name");
        if (string.IsNullOrWhiteSpace(GetString(effective, "SiteUrlPrefix"))) missing.Add("Site URL Prefix");
        if (!authenticationMode.HasValue) missing.Add("Authentication Mode");
        if (string.IsNullOrWhiteSpace(GetString(effective, "ClientID"))) missing.Add("Client ID");
        if (string.IsNullOrWhiteSpace(GetString(effective, "DefDocumentLibrary"))) missing.Add("Document Library");
        if (authenticationMode == 1)
        {
            if (GetString(effective, "RealmID").Length == 0) missing.Add("Realm ID");
        }
        else if (authenticationMode == 2)
        {
            if (GetString(effective, "TenantID").Length == 0) missing.Add("Tenant ID");
            if (GetString(effective, "DefClientCertStore").Length == 0) missing.Add("Client Certificate Store");
            if (GetString(effective, "DefClientCertSubject").Length == 0) missing.Add("Client Certificate Subject");
            if (GetString(effective, "DefClientCertSHA1").Length == 0) missing.Add("Client Certificate SHA1");
        }

        if (missing.Count > 0)
            return Incomplete(hostId, hostName, SharePointHostTypeCode, "SharePoint", missing);

        // The captured native SharePoint test pattern is verified only for explicitly configured direct Hosts.
        if (!string.IsNullOrWhiteSpace(proxyType) && !proxyType.Equals("None", StringComparison.OrdinalIgnoreCase))
            return UnsupportedProxy(hostId, hostName, SharePointHostTypeCode, "SharePoint", proxyType);

        // Match the native SharePoint test request, excluding fields from the inactive authentication mode.
        var request = new JsonObject
        {
            ["Name"] = hostName,
            ["ID"] = hostId,
            ["TenantName"] = GetString(effective, "TenantName"),
            ["SiteUrlPrefix"] = GetString(effective, "SiteUrlPrefix"),
            ["Site"] = GetString(effective, "Site"),
            ["UsingAppAuthentication"] = authenticationMode.GetValueOrDefault(),
            ["ClientID"] = GetString(effective, "ClientID"),
            ["DefDocumentLibrary"] = GetString(effective, "DefDocumentLibrary"),
            ["Group"] = new JsonArray()
        };

        if (authenticationMode == 1)
        {
            request["RealmID"] = GetString(effective, "RealmID");
            // Resolve the saved app secret using ID; never echo protected Host GET/checkout values.
            request["ClientSecret"] = null;
        }
        else
        {
            foreach (var key in new[] { "TenantID", "DefClientCertSHA1", "DefClientCertStore", "DefClientCertSubject" })
                request[key] = GetString(effective, key);
        }

        AddStringIfPresent(request, "Desc", effective, "Desc");
        AddIntIfPresent(request, "DefRetryCount", effective, "DefRetryCount");
        AddIntIfPresent(request, "DefRetryTimeoutSecs", effective, "DefRetryTimeoutSecs");
        AddIntIfPresent(request, "UsedByCount", effective, "UsedByCount");
        AddIntIfPresent(request, "OverridesTotal", effective, "OverridesTotal");
        AddBoolIfPresent(request, "OverridesNoPerm", effective, "OverridesNoPerm");
        AddIntIfPresent(request, "DelOldStateDays", effective, "DelOldStateDays");
        AddIntIfPresent(request, "UseDefStateCaching", effective, "UseDefStateCaching");
        AddIntIfPresent(request, "AdjDST", effective, "AdjDST");
        AddIntIfPresent(request, "DefConnTimeoutSecs", effective, "DefConnTimeoutSecs");
        AddIntIfPresent(request, "DefDataTimeoutSecs", effective, "DefDataTimeoutSecs");
        AddIntIfPresent(request, "DefMxBy", effective, "DefMxBy");
        AddIntIfPresent(request, "DefMxFi", effective, "DefMxFi");
        AddIntIfPresent(request, "DefRescanSecs", effective, "DefRescanSecs");
        if (!string.IsNullOrWhiteSpace(proxyType))
            request["ProxyType"] = proxyType;
        AddIntIfPresent(request, "Secure", effective, "Secure");
        AddStringIfPresent(request, "DefDocumentLibraryGuid", effective, "DefDocumentLibraryGuid");

        try
        {
            var response = await _restClient.TestSharePointHostV0Async(request, cancellationToken);
            var status = GetString(response, "status");
            var errorCode = GetInt(response, "errorcode");
            if (status.Equals("success", StringComparison.OrdinalIgnoreCase) && errorCode.GetValueOrDefault() == 0)
            {
                return new HostConnectionTestResult(
                    hostId,
                    hostName,
                    SharePointHostTypeCode,
                    StatusPassed,
                    "Test Successful",
                    string.Empty,
                    0);
            }

            var responseMessage = FirstNonEmpty(
                GetString(response, "message"),
                GetString(response, "error"),
                GetString(response, "errormsg"),
                "MOVEit reported that the SharePoint Host test failed.");
            return new HostConnectionTestResult(
                hostId,
                hostName,
                SharePointHostTypeCode,
                StatusFailed,
                responseMessage,
                string.Empty,
                0);
        }
        catch (MoveItAuthenticationException)
        {
            throw;
        }
        catch (MoveItApiException ex)
        {
            return Failed(hostId, hostName, SharePointHostTypeCode, ex);
        }
    }

    private static string GetResponseErrorMessage(JsonObject response, string fallback)
    {
        var direct = FirstNonEmpty(
            GetString(response, "message"),
            GetString(response, "error"),
            GetString(response, "errormsg"));
        if (!string.IsNullOrWhiteSpace(direct)) return direct;

        if (response["errors"] is JsonArray errors)
        {
            var messages = errors
                .OfType<JsonObject>()
                .Select(error => FirstNonEmpty(
                    GetString(error, "message"),
                    GetString(error, "detail"),
                    GetString(error, "error")))
                .Where(message => !string.IsNullOrWhiteSpace(message))
                .ToArray();
            if (messages.Length > 0) return string.Join(" ", messages);
        }

        return fallback;
    }

    private static JsonObject BuildBrowseRequest(string hostId, JsonObject loginInfo, string? directory = null, string? fileMask = null)
    {
        var pathBrowse = !string.IsNullOrWhiteSpace(directory) || !string.IsNullOrWhiteSpace(fileMask);
        return new JsonObject
        {
            ["showfiles"] = pathBrowse,
            ["mask"] = (fileMask ?? string.Empty).Trim(),
            ["hostid"] = hostId,
            ["logininfo"] = loginInfo,
            ["dirname"] = (directory ?? string.Empty).Trim(),
            ["taskid"] = null,
            ["stepid"] = null
        };
    }

    private static string ResolveFileSystemBrowseDirectory(string shareName, string? directory)
    {
        var requested = (directory ?? string.Empty).Trim();
        if (requested.Length == 0) return shareName;
        if (requested.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase)
            || requested.StartsWith(shareName, StringComparison.OrdinalIgnoreCase))
            return requested;

        return shareName.TrimEnd('\\', '/') + "\\" + requested.TrimStart('\\', '/');
    }

    private static HostConnectionTestResult PassedBrowse(string hostId, string hostName, string typeCode, JsonObject response)
        => new(
            hostId,
            hostName,
            typeCode,
            StatusPassed,
            "Test Successful",
            GetString(response, "CurrentDir"),
            CountBrowseItems(response),
            CountCollection(response, "Files", "File"));

    private static HostConnectionTestResult Failed(string hostId, string hostName, string typeCode, MoveItApiException ex)
        => new(
            hostId,
            hostName,
            typeCode,
            StatusFailed,
            CleanMoveItError(ex.Message),
            string.Empty,
            0);

    private static HostConnectionTestResult Incomplete(string hostId, string hostName, string typeCode, string displayType, IReadOnlyCollection<string> missing)
        => new(
            hostId,
            hostName,
            typeCode,
            StatusIncomplete,
            $"The saved {displayType} Host is incomplete. Missing: {string.Join(", ", missing)}.",
            string.Empty,
            0);

    private static HostConnectionTestResult UnsupportedProxy(string hostId, string hostName, string typeCode, string displayType, string proxyType)
        => new(
            hostId,
            hostName,
            typeCode,
            StatusUnsupported,
            $"{displayType} Host testing with proxy type '{proxyType}' has not been verified yet.",
            string.Empty,
            0);

    private static (string TypeCode, JsonObject? Definition) GetDefinition(JsonObject? wrapper)
    {
        if (wrapper is null) return (string.Empty, null);

        // MOVEit returns polymorphic Host wrappers for most v0/v1 reads, but some v1 Host responses
        // can be flat. Prefer known Host-type keys so unrelated nested objects are never mistaken for
        // the Host definition during enrichment.
        foreach (var key in new[]
        {
            "FileSystem", "Share", "siLock", "SiLock", "FTP", "SSHFTP", "POP3", "SMTP",
            "S3", "AzureBlob", "SharePoint", "GoogleStorage", "OneDrive", "AS1", "AS2", "AS3"
        })
        {
            if (TryGetPropertyValue(wrapper, key, out var node) && node is JsonObject typedHost)
                return (NormalizeHostTypeCode(key), typedHost);
        }

        if (!string.IsNullOrWhiteSpace(GetString(wrapper, "ID")))
            return (NormalizeHostTypeCode(GetString(wrapper, "Type")), wrapper);

        var firstObject = wrapper.FirstOrDefault(pair => pair.Value is JsonObject);
        return firstObject.Value is JsonObject fallback
            ? (NormalizeHostTypeCode(firstObject.Key), fallback)
            : (string.Empty, null);
    }

    private static string NormalizeHostTypeCode(string? typeCode)
    {
        var value = (typeCode ?? string.Empty).Trim();
        if (value.Equals("SiLock", StringComparison.OrdinalIgnoreCase) || value.Equals("silock", StringComparison.OrdinalIgnoreCase))
            return MoveItTransferHostTypeCode;

        foreach (var known in new[]
        {
            "FileSystem", FileSystemShareHostTypeCode, FtpHostTypeCode, SshHostTypeCode, "POP3", "SMTP",
            S3HostTypeCode, AzureBlobHostTypeCode, SharePointHostTypeCode, GoogleStorageHostTypeCode, "AS1", "AS2", "AS3"
        })
        {
            if (value.Equals(known, StringComparison.OrdinalIgnoreCase)) return known;
        }

        return value;
    }

    private static int CountBrowseItems(JsonObject response)
    {
        return CountCollection(response, "Dirs", "Dir") + CountCollection(response, "Files", "File");
    }

    private static int CountCollection(JsonObject response, string containerName, string itemName)
    {
        if (response[containerName] is not JsonObject container || container[itemName] is null) return 0;
        return container[itemName] switch
        {
            JsonArray array => array.Count,
            JsonObject => 1,
            _ => 0
        };
    }

    private async Task<JsonObject> EnrichDefinitionAsync(
        string hostId,
        string expectedTypeCode,
        JsonObject definition,
        CancellationToken cancellationToken,
        params string[] fieldNames)
    {
        var missingFields = fieldNames
            .Where(name => !HasValue(definition, name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (missingFields.Length == 0)
            return definition;

        try
        {
            var liveHost = await _restClient.GetHostAsync(hostId, cancellationToken);
            var (liveTypeCode, liveDefinition) = GetDefinition(liveHost);
            if (liveDefinition is null || !liveTypeCode.Equals(expectedTypeCode, StringComparison.OrdinalIgnoreCase))
                return definition;

            var effective = (JsonObject)definition.DeepClone();
            foreach (var fieldName in missingFields)
            {
                if (TryGetPropertyValue(liveDefinition, fieldName, out var liveValue) && liveValue is not null)
                    effective[fieldName] = liveValue.DeepClone();
            }

            return effective;
        }
        catch (MoveItAuthenticationException)
        {
            throw;
        }
        catch (MoveItApiException ex)
        {
            // A v1 enrichment failure must not replace the native v0 checkout/test result. The test below
            // will use the values MOVEit did expose and let the native endpoint report any real problem.
            _logger.LogDebug(
                ex,
                "MAUI could not enrich optional {HostType} Host test settings for Host {HostId} from the v1 Host definition.",
                expectedTypeCode,
                hostId);
            return definition;
        }
    }

    private static void AddStringIfPresent(JsonObject target, string targetName, JsonObject source, string sourceName)
    {
        if (HasValue(source, sourceName))
            target[targetName] = GetString(source, sourceName);
    }

    private static void AddIntIfPresent(JsonObject target, string targetName, JsonObject source, string sourceName)
    {
        var value = GetInt(source, sourceName);
        if (value.HasValue)
            target[targetName] = value.Value;
    }

    private static void AddBoolIfPresent(JsonObject target, string targetName, JsonObject source, string sourceName)
    {
        var value = GetNullableBool(source, sourceName);
        if (value.HasValue)
            target[targetName] = value.Value;
    }

    private static string GetString(JsonObject? source, string name)
    {
        if (source is null || !TryGetPropertyValue(source, name, out var node) || node is null) return string.Empty;
        if (node is JsonValue value && value.TryGetValue<string>(out var text)) return text?.Trim() ?? string.Empty;
        return node.ToString().Trim();
    }

    private static string? GetNullableString(JsonObject? source, string name)
    {
        if (source is null || !TryGetPropertyValue(source, name, out var node) || node is null) return null;
        var value = GetString(source, name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static int? GetInt(JsonObject? source, string name)
    {
        if (source is null || !TryGetPropertyValue(source, name, out var node) || node is null) return null;
        if (node is JsonValue value)
        {
            if (value.TryGetValue<int>(out var integer)) return integer;
            if (value.TryGetValue<long>(out var longValue) && longValue is >= int.MinValue and <= int.MaxValue) return (int)longValue;
            if (value.TryGetValue<string>(out var text) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out integer)) return integer;
        }
        return int.TryParse(node.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static bool? GetNullableBool(JsonObject? source, string name)
    {
        if (source is null || !TryGetPropertyValue(source, name, out var node) || node is null) return null;
        if (node is JsonValue value)
        {
            if (value.TryGetValue<bool>(out var boolean)) return boolean;
            if (value.TryGetValue<int>(out var integer)) return integer != 0;
            if (value.TryGetValue<long>(out var longValue)) return longValue != 0;
            if (value.TryGetValue<string>(out var text))
            {
                if (bool.TryParse(text, out boolean)) return boolean;
                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out integer)) return integer != 0;
            }
        }
        return null;
    }

    private static bool HasValue(JsonObject? source, string name)
        => source is not null && TryGetPropertyValue(source, name, out var node) && node is not null;

    private static bool TryGetPropertyValue(JsonObject source, string name, out JsonNode? node)
    {
        if (source.TryGetPropertyValue(name, out node)) return true;
        foreach (var pair in source)
        {
            if (!pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            node = pair.Value;
            return true;
        }
        node = null;
        return false;
    }

    private static string? NullIfWhiteSpace(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string FirstNonEmpty(params string[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static string CleanMoveItError(string message)
    {
        var normalized = (message ?? string.Empty).Trim();
        if (normalized.Length == 0) return "MOVEit reported that the Host connection test failed.";

        const string prefix = "MOVEit REST request failed:";
        if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            normalized = normalized[prefix.Length..].Trim();

        // MOVEit v0 test endpoints commonly return errors as { status, errors:[{ message }] }.
        // The REST client includes the raw body after the HTTP status for non-2xx responses, so extract
        // that native message here without changing error formatting for unrelated API calls.
        var jsonStart = normalized.IndexOf('{');
        if (jsonStart >= 0)
        {
            try
            {
                if (JsonNode.Parse(normalized[jsonStart..]) is JsonObject errorResponse)
                {
                    var detail = GetResponseErrorMessage(errorResponse, string.Empty);
                    if (!string.IsNullOrWhiteSpace(detail)) return detail;
                }
            }
            catch
            {
                // Keep the original compact MOVEit error when the trailing body is not valid JSON.
            }
        }

        return normalized;
    }
}

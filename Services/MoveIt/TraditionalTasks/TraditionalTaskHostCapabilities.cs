// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.


namespace MoveItAutomationUI.Services.MoveIt.TraditionalTasks;

// Shared native Traditional Task Host capability catalog (originally verified by RC2 editor). The catalog intentionally
// distinguishes "known/readable" host types from host patterns whose Source/Destination write and browse
// contracts have actually been verified. New host support is enabled by adding a proven profile here and
// implementing it at the feature-specific save/browse seams; unknown or unverified types fail closed.
public static class TraditionalTaskHostCapabilities
{
    public const string FileSystemWriteProfile = "filesystem-v1";
    public const string FileSystemBrowseProfile = "filesystem-v1";
    public const string ShareWriteProfile = "share-v1";
    public const string ShareBrowseProfile = "share-v1";
    public const string MoveItTransferWriteProfile = "silock-v1";
    public const string MoveItTransferBrowseProfile = "silock-v1";
    public const string FtpWriteProfile = "ftp-v1";
    public const string FtpBrowseProfile = "ftp-v1";
    public const string SftpWriteProfile = "sshftp-v1";
    public const string SftpBrowseProfile = "sshftp-v1";
    public const string S3WriteProfile = "s3-v1";
    public const string S3BrowseProfile = "s3-v1";
    public const string AzureBlobWriteProfile = "azureblob-v1";
    public const string AzureBlobBrowseProfile = "azureblob-v1";
    public const string SharePointWriteProfile = "sharepoint-v1";
    public const string SharePointBrowseProfile = "sharepoint-v1";
    public const string GoogleStorageWriteProfile = "googlestorage-v1";
    public const string GoogleStorageBrowseProfile = "googlestorage-v1";
    public const string Pop3WriteProfile = "pop3-v1";
    public const string SmtpWriteProfile = "smtp-v1";
    public const string FileSourceChangeHostProfile = "file-source-v1";
    public const string FileDestinationChangeHostProfile = "file-destination-v1";

    private static readonly IReadOnlyList<TraditionalTaskHostCapability> Capabilities = new[]
    {
        Capability("FileSystem", "Local File System", source: true, destination: true, sourceTimestamps: true,
            sourceWritable: true, destinationWritable: true, sourceBrowsable: true, destinationBrowsable: true,
            sourceWriteProfile: FileSystemWriteProfile, destinationWriteProfile: FileSystemWriteProfile,
            sourceBrowseProfile: FileSystemBrowseProfile, destinationBrowseProfile: FileSystemBrowseProfile,
            sourceChangeHostProfile: FileSourceChangeHostProfile, destinationChangeHostProfile: FileDestinationChangeHostProfile),
        Capability("Share", "UNC Share", source: true, destination: true, sourceTimestamps: true,
            sourceWritable: true, destinationWritable: true,
            sourceBrowsable: true, destinationBrowsable: true,
            sourceWriteProfile: ShareWriteProfile, destinationWriteProfile: ShareWriteProfile,
            sourceBrowseProfile: ShareBrowseProfile, destinationBrowseProfile: ShareBrowseProfile,
            sourceChangeHostProfile: FileSourceChangeHostProfile, destinationChangeHostProfile: FileDestinationChangeHostProfile),
        Capability("siLock", "MOVEit Transfer", source: true, destination: true, sourceTimestamps: true,
            sourceWritable: true, destinationWritable: true,
            sourceBrowsable: true, destinationBrowsable: true,
            sourceWriteProfile: MoveItTransferWriteProfile, destinationWriteProfile: MoveItTransferWriteProfile,
            sourceBrowseProfile: MoveItTransferBrowseProfile, destinationBrowseProfile: MoveItTransferBrowseProfile,
            sourceChangeHostProfile: FileSourceChangeHostProfile, destinationChangeHostProfile: FileDestinationChangeHostProfile),
        Capability("FTP", "FTP/FTPS", source: true, destination: true, sourceTimestamps: true,
            sourceWritable: true, destinationWritable: true,
            sourceBrowsable: true, destinationBrowsable: true,
            sourceWriteProfile: FtpWriteProfile, destinationWriteProfile: FtpWriteProfile,
            sourceBrowseProfile: FtpBrowseProfile, destinationBrowseProfile: FtpBrowseProfile,
            sourceChangeHostProfile: FileSourceChangeHostProfile, destinationChangeHostProfile: FileDestinationChangeHostProfile),
        Capability("SSHFTP", "SSH/SFTP", source: true, destination: true, sourceTimestamps: true,
            sourceWritable: true, destinationWritable: true,
            sourceBrowsable: true, destinationBrowsable: true,
            sourceWriteProfile: SftpWriteProfile, destinationWriteProfile: SftpWriteProfile,
            sourceBrowseProfile: SftpBrowseProfile, destinationBrowseProfile: SftpBrowseProfile,
            sourceChangeHostProfile: FileSourceChangeHostProfile, destinationChangeHostProfile: FileDestinationChangeHostProfile),
        Capability("POP3", "POP3 (Incoming Email)", source: true, destination: false,
            sourceWritable: true, sourceWriteProfile: Pop3WriteProfile),
        Capability("SMTP", "SMTP (Outgoing Email)", source: false, destination: true,
            destinationWritable: true, destinationWriteProfile: SmtpWriteProfile),
        Capability("S3", "Amazon S3", source: true, destination: true,
            sourceWritable: true, destinationWritable: true,
            sourceBrowsable: true, destinationBrowsable: true,
            sourceWriteProfile: S3WriteProfile, destinationWriteProfile: S3WriteProfile,
            sourceBrowseProfile: S3BrowseProfile, destinationBrowseProfile: S3BrowseProfile,
            sourceChangeHostProfile: FileSourceChangeHostProfile, destinationChangeHostProfile: FileDestinationChangeHostProfile),
        Capability("AzureBlob", "Azure Blob", source: true, destination: true,
            sourceWritable: true, destinationWritable: true,
            sourceBrowsable: true, destinationBrowsable: true,
            sourceWriteProfile: AzureBlobWriteProfile, destinationWriteProfile: AzureBlobWriteProfile,
            sourceBrowseProfile: AzureBlobBrowseProfile, destinationBrowseProfile: AzureBlobBrowseProfile,
            sourceChangeHostProfile: FileSourceChangeHostProfile, destinationChangeHostProfile: FileDestinationChangeHostProfile),
        Capability("SharePoint", "SharePoint", source: true, destination: true,
            sourceWritable: true, destinationWritable: true,
            sourceBrowsable: true, destinationBrowsable: true,
            sourceWriteProfile: SharePointWriteProfile, destinationWriteProfile: SharePointWriteProfile,
            sourceBrowseProfile: SharePointBrowseProfile, destinationBrowseProfile: SharePointBrowseProfile,
            sourceChangeHostProfile: FileSourceChangeHostProfile, destinationChangeHostProfile: FileDestinationChangeHostProfile),
        Capability("GoogleStorage", "Google Storage", source: true, destination: true,
            sourceWritable: true, destinationWritable: true,
            sourceBrowsable: true, destinationBrowsable: true,
            sourceWriteProfile: GoogleStorageWriteProfile, destinationWriteProfile: GoogleStorageWriteProfile,
            sourceBrowseProfile: GoogleStorageBrowseProfile, destinationBrowseProfile: GoogleStorageBrowseProfile,
            sourceChangeHostProfile: FileSourceChangeHostProfile, destinationChangeHostProfile: FileDestinationChangeHostProfile),
        Capability("AS1", "AS1", source: true, destination: true),
        Capability("AS2", "AS2", source: true, destination: true),
        Capability("AS3", "AS3", source: true, destination: true)
    };

    private static readonly IReadOnlyDictionary<string, TraditionalTaskHostCapability> ByType = Capabilities
        .ToDictionary(item => item.TypeCode, StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlySet<string> EmptyPatchFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    // Fields in these sets describe the host-neutral portion of a file Source/Destination. MOVEit's
    // native Change Host workflow retains these general behaviors while the selected Host, Folder(s),
    // and Host Override values are specific to the newly selected Host. Keep the common contract
    // separate from each host profile so verified Host profiles do not duplicate it.
    private static readonly IReadOnlySet<string> StepIdentityPatchFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "ID", "HostID", "Type"
    };

    private static readonly IReadOnlySet<string> SourceCommonBehaviorPatchFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "FileMask", "ExFo", "SearchSubdirs", "ExFile", "Criteria", "NewFilesOnly", "Unzip",
        "RetryIfNoFiles", "DeleteOrig", "RenameTo", "DelRename"
    };

    private static readonly IReadOnlySet<string> DestinationCommonBehaviorPatchFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "UseRelativeSubdirs", "ForceDir", "Zip", "UseOrigName", "FileName", "OverwriteOrig"
    };

    private static readonly IReadOnlySet<string> ShareSourceOverridePatchFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "UseDefRetryCount", "RetryCount", "UseDefRetryTimeoutSecs", "RetryTimeoutSecs",
        "UseDefRescanSecs", "RescanSecs", "UDMxFi", "MxFi", "UDMxBy", "MxBy",
        "UseDefWinCopyFileAPI", "UseWinCopyFileAPI"
    };

    // Local transfers have no account, network timeout or UNC Windows CopyFile override.
    private static readonly IReadOnlySet<string> FileSystemSourceOverridePatchFields = FieldSet(
        "UseDefRetryCount", "RetryCount", "UseDefRetryTimeoutSecs", "RetryTimeoutSecs",
        "UseDefRescanSecs", "RescanSecs", "UDMxFi", "MxFi", "UDMxBy", "MxBy");
    private static readonly IReadOnlySet<string> FileSystemDestinationOverridePatchFields = FieldSet(
        "UseDefRetryCount", "RetryCount", "UseDefRetryTimeoutSecs", "RetryTimeoutSecs",
        "UseDefPreserveFileTimestamp", "PreserveFileTimestamp");

    private static readonly IReadOnlySet<string> ShareDestinationOverridePatchFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "UseDefRetryCount", "RetryCount", "UseDefRetryTimeoutSecs", "RetryTimeoutSecs",
        "UseDefWinCopyFileAPI", "UseWinCopyFileAPI", "UseDefPreserveFileTimestamp", "PreserveFileTimestamp"
    };

    private static readonly IReadOnlySet<string> MoveItTransferSourceOverridePatchFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "UseDefUser", "Username", "Password",
        "UseDefClientCert", "ClientCertSHA1", "ClientCertStore", "ClientCertSubject",
        "UseDefSort", "Sort",
        "UseDefConnTimeoutSecs", "ConnTimeoutSecs", "UseDefDataTimeoutSecs", "DataTimeoutSecs",
        "UseDefRetryCount", "RetryCount", "UseDefRetryTimeoutSecs", "RetryTimeoutSecs",
        "UDMxFi", "MxFi", "UDMxBy", "MxBy"
    };

    private static readonly IReadOnlySet<string> MoveItTransferDestinationOverridePatchFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "UseDefUser", "Username", "Password",
        "UseDefClientCert", "ClientCertSHA1", "ClientCertStore", "ClientCertSubject",
        "UseDefConnTimeoutSecs", "ConnTimeoutSecs", "UseDefDataTimeoutSecs", "DataTimeoutSecs",
        "UseDefRetryCount", "RetryCount", "UseDefRetryTimeoutSecs", "RetryTimeoutSecs"
    };


    private static readonly IReadOnlySet<string> FtpSourceOverridePatchFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "UseDefUser", "Username", "Password", "Account",
        "UseDefClientCert", "ClientCertSHA1", "ClientCertStore", "ClientCertSubject",
        "UseDefConnTimeoutSecs", "ConnTimeoutSecs", "UseDefDataTimeoutSecs", "DataTimeoutSecs",
        "UseDefRetryCount", "RetryCount", "UseDefRetryTimeoutSecs", "RetryTimeoutSecs",
        "UseDefRescanSecs", "RescanSecs",
        "UseDefAB", "AB", "UseDefPassive", "Passive", "UseDefXS", "XS",
        "UseDefBlindDL", "BlindDL", "UseDefRsm", "Rsm", "UseDefReuseSSL", "ReuseSSL",
        "UDMxFi", "MxFi", "UDMxBy", "MxBy",
        "UseDefMD5Get", "MD5Get", "UseDefMD5File", "MD5File",
        "AddCom", "AddComAX"
    };

    private static readonly IReadOnlySet<string> FtpDestinationOverridePatchFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "UseDefUser", "Username", "Password", "Account",
        "UseDefClientCert", "ClientCertSHA1", "ClientCertStore", "ClientCertSubject",
        "UseDefConnTimeoutSecs", "ConnTimeoutSecs", "UseDefDataTimeoutSecs", "DataTimeoutSecs",
        "UseDefRetryCount", "RetryCount", "UseDefRetryTimeoutSecs", "RetryTimeoutSecs",
        "UseDefAB", "AB", "UseDefPassive", "Passive", "UseDefXS", "XS",
        "UseDefBlindUL", "BlindUL", "UseDefRsm", "Rsm", "UseDefReuseSSL", "ReuseSSL",
        "AddCom", "AddComAX"
    };

    private static readonly IReadOnlySet<string> SftpSourceOverridePatchFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "UseDefUser", "Username", "Password",
        "UseDefClientKey", "ClientKeyID",
        "UseDefConnTimeoutSecs", "ConnTimeoutSecs",
        "UseDefRetryCount", "RetryCount", "UseDefRetryTimeoutSecs", "RetryTimeoutSecs",
        "UseDefRescanSecs", "RescanSecs",
        "UseDefBlindDL", "BlindDL", "UseDefRsm", "Rsm",
        "UDMxFi", "MxFi", "UDMxBy", "MxBy",
        "UseDefMD5Get", "MD5Get", "UseDefMD5File", "MD5File"
    };

    private static readonly IReadOnlySet<string> SftpDestinationOverridePatchFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "UseDefUser", "Username", "Password",
        "UseDefClientKey", "ClientKeyID",
        "UseDefConnTimeoutSecs", "ConnTimeoutSecs",
        "UseDefRetryCount", "RetryCount", "UseDefRetryTimeoutSecs", "RetryTimeoutSecs",
        "UseDefRsm", "Rsm",
        "UseDefSetFileAttr", "SetFileAttr", "DestFileAttr",
        "UseDefPreserveFileTimestamp", "PreserveFileTimestamp"
    };

    private static readonly IReadOnlySet<string> S3SourceOverridePatchFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "UseDefBucket", "Bucket",
        "UseDefUser", "Username", "Password",
        "UseDefConnTimeoutSecs", "ConnTimeoutSecs", "UseDefDataTimeoutSecs", "DataTimeoutSecs",
        "UseDefRetryCount", "RetryCount", "UseDefRetryTimeoutSecs", "RetryTimeoutSecs",
        "UseDefRescanSecs", "RescanSecs",
        "UDMxFi", "MxFi", "UDMxBy", "MxBy",
        "UseDefDLInteg", "DLInteg"
    };

    private static readonly IReadOnlySet<string> S3DestinationOverridePatchFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "UseDefBucket", "Bucket",
        "UseDefUser", "Username", "Password",
        "UseDefConnTimeoutSecs", "ConnTimeoutSecs", "UseDefDataTimeoutSecs", "DataTimeoutSecs",
        "UseDefRetryCount", "RetryCount", "UseDefRetryTimeoutSecs", "RetryTimeoutSecs"
    };

    private static readonly IReadOnlySet<string> AzureBlobSourceOverridePatchFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "UseDefContainer", "Container",
        "UseDefUser", "Username", "Password",
        "UseDefConnTimeoutSecs", "ConnTimeoutSecs", "UseDefDataTimeoutSecs", "DataTimeoutSecs",
        "UseDefRetryCount", "RetryCount", "UseDefRetryTimeoutSecs", "RetryTimeoutSecs",
        "UseDefRescanSecs", "RescanSecs",
        "UDMxFi", "MxFi", "UDMxBy", "MxBy",
        "UseDefDLInteg", "DLInteg"
    };

    private static readonly IReadOnlySet<string> AzureBlobDestinationOverridePatchFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "UseDefContainer", "Container",
        "UseDefUser", "Username", "Password",
        "UseDefConnTimeoutSecs", "ConnTimeoutSecs", "UseDefDataTimeoutSecs", "DataTimeoutSecs",
        "UseDefRetryCount", "RetryCount", "UseDefRetryTimeoutSecs", "RetryTimeoutSecs"
    };

    private static readonly IReadOnlySet<string> GoogleStorageSourceOverridePatchFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "UseDefBucket", "Bucket",
        "UseDefUser", "UseOAuth", "Username", "Password", "ProjectID", "OAuthClientID", "OAuthClientSecret",
        "UseDefConnTimeoutSecs", "ConnTimeoutSecs", "UseDefDataTimeoutSecs", "DataTimeoutSecs",
        "UseDefRetryCount", "RetryCount", "UseDefRetryTimeoutSecs", "RetryTimeoutSecs",
        "UseDefRescanSecs", "RescanSecs",
        "UDMxFi", "MxFi", "UDMxBy", "MxBy"
    };

    private static readonly IReadOnlySet<string> GoogleStorageDestinationOverridePatchFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "UseDefBucket", "Bucket",
        "UseDefUser", "UseOAuth", "Username", "Password", "ProjectID", "OAuthClientID", "OAuthClientSecret",
        "UseDefConnTimeoutSecs", "ConnTimeoutSecs", "UseDefDataTimeoutSecs", "DataTimeoutSecs",
        "UseDefRetryCount", "RetryCount", "UseDefRetryTimeoutSecs", "RetryTimeoutSecs"
    };

    private static readonly IReadOnlySet<string> Pop3SourcePatchFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "FileMask", "ExFile", "NewFilesOnly", "Unzip", "RetryIfNoFiles", "DeleteOrig",
        "UseDefUser", "UseOAuth", "Username", "Password",
        "OAuthTokenURL", "OAuthClientID", "OAuthClientSecret", "OAuthScope",
        "UseDefConnTimeoutSecs", "ConnTimeoutSecs", "UseDefDataTimeoutSecs", "DataTimeoutSecs",
        "UseDefRetryCount", "RetryCount", "UseDefRetryTimeoutSecs", "RetryTimeoutSecs"
    };

    private static readonly IReadOnlySet<string> SmtpDestinationPatchFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "AddressTo", "Subject", "Message", "IsHTML", "Zip", "UseOrigName", "FileName",
        "UseDefUser", "UseOAuth", "Username", "Password",
        "OAuthTokenURL", "OAuthClientID", "OAuthClientSecret", "OAuthScope",
        "UseDefConnTimeoutSecs", "ConnTimeoutSecs", "UseDefDataTimeoutSecs", "DataTimeoutSecs",
        "UseDefRetryCount", "RetryCount", "UseDefRetryTimeoutSecs", "RetryTimeoutSecs"
    };

    private static readonly IReadOnlySet<string> SharePointSourceOverridePatchFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "UseDefDocumentLibrary", "DocumentLibrary", "DocumentLibraryGuid",
        "UseDefUser", "UsingAppAuthentication", "ClientID", "TenantID", "RealmID", "ClientSecret",
        "UseDefClientCert", "ClientCertSHA1", "ClientCertStore", "ClientCertSubject",
        "UseDefConnTimeoutSecs", "ConnTimeoutSecs", "UseDefDataTimeoutSecs", "DataTimeoutSecs",
        "UseDefRetryCount", "RetryCount", "UseDefRetryTimeoutSecs", "RetryTimeoutSecs",
        "UseDefRescanSecs", "RescanSecs",
        "UDMxFi", "MxFi", "UDMxBy", "MxBy"
    };

    private static readonly IReadOnlySet<string> SharePointDestinationOverridePatchFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "UseDefDocumentLibrary", "DocumentLibrary", "DocumentLibraryGuid",
        "UseDefUser", "UsingAppAuthentication", "ClientID", "TenantID", "RealmID", "ClientSecret",
        "UseDefClientCert", "ClientCertSHA1", "ClientCertStore", "ClientCertSubject",
        "UseDefConnTimeoutSecs", "ConnTimeoutSecs", "UseDefDataTimeoutSecs", "DataTimeoutSecs",
        "UseDefRetryCount", "RetryCount", "UseDefRetryTimeoutSecs", "RetryTimeoutSecs"
    };

    private sealed record HostPatchProfile(IReadOnlySet<string> SourceFields, IReadOnlySet<string> DestinationFields);

    // One profile registry is the server-side authority for profile-owned patch fields. Keeping the
    // profile-to-field mapping here prevents IsPatchFieldAllowed from growing another parallel chain
    // every time a verified Host profile is added.
    private static readonly IReadOnlyDictionary<string, HostPatchProfile> PatchFieldsByWriteProfile =
        new Dictionary<string, HostPatchProfile>(StringComparer.OrdinalIgnoreCase)
        {
            [FileSystemWriteProfile] = new(FileSystemSourceOverridePatchFields, FileSystemDestinationOverridePatchFields),
            [ShareWriteProfile] = new(ShareSourceOverridePatchFields, ShareDestinationOverridePatchFields),
            [MoveItTransferWriteProfile] = new(MoveItTransferSourceOverridePatchFields, MoveItTransferDestinationOverridePatchFields),
            [FtpWriteProfile] = new(FtpSourceOverridePatchFields, FtpDestinationOverridePatchFields),
            [SftpWriteProfile] = new(SftpSourceOverridePatchFields, SftpDestinationOverridePatchFields),
            [S3WriteProfile] = new(S3SourceOverridePatchFields, S3DestinationOverridePatchFields),
            [AzureBlobWriteProfile] = new(AzureBlobSourceOverridePatchFields, AzureBlobDestinationOverridePatchFields),
            [SharePointWriteProfile] = new(SharePointSourceOverridePatchFields, SharePointDestinationOverridePatchFields),
            [GoogleStorageWriteProfile] = new(GoogleStorageSourceOverridePatchFields, GoogleStorageDestinationOverridePatchFields),
            [Pop3WriteProfile] = new(Pop3SourcePatchFields, EmptyPatchFields),
            [SmtpWriteProfile] = new(EmptyPatchFields, SmtpDestinationPatchFields)
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> LocationFieldsByWriteProfile =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [FileSystemWriteProfile] = FieldSet("Path"),
            [ShareWriteProfile] = FieldSet("Path"),
            [MoveItTransferWriteProfile] = FieldSet("FolderName", "FolderID"),
            [FtpWriteProfile] = FieldSet("Path"),
            [SftpWriteProfile] = FieldSet("Path"),
            [S3WriteProfile] = FieldSet("Path"),
            [AzureBlobWriteProfile] = FieldSet("Path"),
            [SharePointWriteProfile] = FieldSet("Path"),
            [GoogleStorageWriteProfile] = FieldSet("Path")
        };

    private static readonly IReadOnlySet<string> CloudObjectWriteProfiles = FieldSet(
        S3WriteProfile, AzureBlobWriteProfile, GoogleStorageWriteProfile);

    private static readonly IReadOnlySet<string> ImplementedBrowseProfiles = FieldSet(
        FileSystemBrowseProfile, ShareBrowseProfile, MoveItTransferBrowseProfile, FtpBrowseProfile, SftpBrowseProfile,
        S3BrowseProfile, AzureBlobBrowseProfile, SharePointBrowseProfile, GoogleStorageBrowseProfile);

    // Server-side conversion whitelist. Folder/Path, HostID/Type, and every Host Override field are
    // intentionally excluded. When the Host changes, the save service rebuilds the Step from only
    // these values before applying the new host profile patch, which prevents old host-specific state
    // from leaking into a different MOVEit host type.
    private static readonly IReadOnlySet<string> SourceRetainedOnHostChange = new HashSet<string>(
        SourceCommonBehaviorPatchFields.Concat(new[] { "ID" }), StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlySet<string> DestinationRetainedOnHostChange = new HashSet<string>(
        DestinationCommonBehaviorPatchFields.Concat(new[] { "ID" }), StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<TraditionalTaskHostCapability> ClientCapabilities => Capabilities;

    // The RC1 file-transfer workflows retain their established narrower Host allow-list.
    // This catalog describes RC2 editor capabilities; it does not enable a workflow by itself.

    public static IReadOnlySet<string> SourceTypeCodes { get; } = new HashSet<string>(
        Capabilities.Where(item => item.SupportsSource).Select(item => item.TypeCode),
        StringComparer.OrdinalIgnoreCase);

    public static IReadOnlySet<string> DestinationTypeCodes { get; } = new HashSet<string>(
        Capabilities.Where(item => item.SupportsDestination).Select(item => item.TypeCode),
        StringComparer.OrdinalIgnoreCase);

    // Used to unwrap MOVEit host objects. Wrapper lookup is case-insensitive and type codes are
    // normalized to the canonical capability value before any capability decision is made.
    public static IReadOnlyList<string> WrapperTypeCodes { get; } = Capabilities
        .Select(item => item.TypeCode)
        .ToList();

    public static TraditionalTaskHostCapability? Get(string? typeCode)
    {
        var normalized = NormalizeTypeCode(typeCode);
        return ByType.TryGetValue(normalized, out var capability) ? capability : null;
    }

    public static string NormalizeTypeCode(string? typeCode)
    {
        var value = (typeCode ?? string.Empty).Trim();
        foreach (var known in Capabilities)
            if (value.Equals(known.TypeCode, StringComparison.OrdinalIgnoreCase)) return known.TypeCode;
        return value;
    }

    public static string FriendlyName(string? typeCode)
    {
        var normalized = NormalizeTypeCode(typeCode);
        return Get(normalized)?.Type ?? (normalized.Length == 0 ? "Unknown Host" : normalized);
    }

    public static bool CanWrite(string? kind, string? typeCode)
    {
        var capability = Get(typeCode);
        return capability is not null && (string.Equals(kind, "Source", StringComparison.OrdinalIgnoreCase)
            ? capability.SourceWritable
            : string.Equals(kind, "Destination", StringComparison.OrdinalIgnoreCase) && capability.DestinationWritable);
    }

    public static bool CanBrowse(string? kind, string? typeCode)
    {
        var capability = Get(typeCode);
        return capability is not null && (string.Equals(kind, "Source", StringComparison.OrdinalIgnoreCase)
            ? capability.SourceBrowsable
            : string.Equals(kind, "Destination", StringComparison.OrdinalIgnoreCase) && capability.DestinationBrowsable);
    }

    public static bool SupportsSourceTimestamps(string? typeCode)
        => Get(typeCode)?.SupportsSourceTimestamps == true;

    public static string WriteProfile(string? kind, string? typeCode)
    {
        var capability = Get(typeCode);
        if (capability is null) return string.Empty;
        return string.Equals(kind, "Source", StringComparison.OrdinalIgnoreCase)
            ? capability.SourceWriteProfile
            : string.Equals(kind, "Destination", StringComparison.OrdinalIgnoreCase)
                ? capability.DestinationWriteProfile
                : string.Empty;
    }

    public static string BrowseProfile(string? kind, string? typeCode)
    {
        var capability = Get(typeCode);
        if (capability is null) return string.Empty;
        return string.Equals(kind, "Source", StringComparison.OrdinalIgnoreCase)
            ? capability.SourceBrowseProfile
            : string.Equals(kind, "Destination", StringComparison.OrdinalIgnoreCase)
                ? capability.DestinationBrowseProfile
                : string.Empty;
    }

    public static string ChangeHostProfile(string? kind, string? typeCode)
    {
        var capability = Get(typeCode);
        if (capability is null) return string.Empty;
        return string.Equals(kind, "Source", StringComparison.OrdinalIgnoreCase)
            ? capability.SourceChangeHostProfile
            : string.Equals(kind, "Destination", StringComparison.OrdinalIgnoreCase)
                ? capability.DestinationChangeHostProfile
                : string.Empty;
    }

    public static bool CanChangeHost(string? kind, string? existingTypeCode, string? requestedTypeCode)
    {
        if (!CanWrite(kind, requestedTypeCode)) return false;
        var existing = NormalizeTypeCode(existingTypeCode);
        var requested = NormalizeTypeCode(requestedTypeCode);
        if (existing.Equals(requested, StringComparison.OrdinalIgnoreCase)) return true;
        var existingProfile = ChangeHostProfile(kind, existing);
        var requestedProfile = ChangeHostProfile(kind, requested);
        return existingProfile.Length > 0 && existingProfile.Equals(requestedProfile, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPatchFieldAllowed(string? kind, string? typeCode, string fieldName)
    {
        if (StepIdentityPatchFields.Contains(fieldName)) return true;

        var isSource = string.Equals(kind, "Source", StringComparison.OrdinalIgnoreCase);
        var isDestination = string.Equals(kind, "Destination", StringComparison.OrdinalIgnoreCase);
        if (!isSource && !isDestination) return false;

        var writeProfile = WriteProfile(kind, typeCode);
        if (writeProfile.Length == 0) return false;

        // Location fields are profile-specific. Keep that contract separate from the generic file-step
        // behaviors so MOVEit Transfer's FolderName/FolderID representation cannot leak into Path-based hosts.
        if (LocationFieldsByWriteProfile.TryGetValue(writeProfile, out var locationFields)
            && locationFields.Contains(fieldName)) return true;

        // Cloud-object destinations do not expose Create Directories in Web Admin. ForceDir is a
        // generic file-destination member, but it is intentionally outside these profile contracts.
        if (isDestination
            && fieldName.Equals("ForceDir", StringComparison.OrdinalIgnoreCase)
            && CloudObjectWriteProfiles.Contains(writeProfile)) return false;

        var changeHostProfile = ChangeHostProfile(kind, typeCode);
        if (isSource
            && changeHostProfile.Equals(FileSourceChangeHostProfile, StringComparison.OrdinalIgnoreCase)
            && SourceCommonBehaviorPatchFields.Contains(fieldName)) return true;
        if (isDestination
            && changeHostProfile.Equals(FileDestinationChangeHostProfile, StringComparison.OrdinalIgnoreCase)
            && DestinationCommonBehaviorPatchFields.Contains(fieldName)) return true;

        if (!PatchFieldsByWriteProfile.TryGetValue(writeProfile, out var profileFields)) return false;
        return (isSource ? profileFields.SourceFields : profileFields.DestinationFields).Contains(fieldName);
    }

    // Validate the profile catalog at application startup. This is an internal consistency
    // check only; it does not probe MOVEit. It catches incomplete future profile declarations early instead
    // of allowing a writable/browsable capability to drift away from the server-owned allow-list/dispatcher.
    public static void ValidateCatalog()
    {
        foreach (var capability in Capabilities)
        {
            ValidateDirectionContract(capability, source: true);
            ValidateDirectionContract(capability, source: false);
            if (capability.SupportsSourceTimestamps && !capability.SupportsSource)
                throw new InvalidOperationException($"Host capability {capability.TypeCode} enables Source Timestamps without Source support.");
        }
    }

    private static void ValidateDirectionContract(TraditionalTaskHostCapability capability, bool source)
    {
        var direction = source ? "Source" : "Destination";
        var supports = source ? capability.SupportsSource : capability.SupportsDestination;
        var writable = source ? capability.SourceWritable : capability.DestinationWritable;
        var browsable = source ? capability.SourceBrowsable : capability.DestinationBrowsable;
        var writeProfile = source ? capability.SourceWriteProfile : capability.DestinationWriteProfile;
        var browseProfile = source ? capability.SourceBrowseProfile : capability.DestinationBrowseProfile;
        var changeHostProfile = source ? capability.SourceChangeHostProfile : capability.DestinationChangeHostProfile;

        if (writable && !supports)
            throw new InvalidOperationException($"Host capability {capability.TypeCode} declares a writable {direction} without {direction} support.");
        if (browsable && !supports)
            throw new InvalidOperationException($"Host capability {capability.TypeCode} declares a browsable {direction} without {direction} support.");
        if (writable && (writeProfile.Length == 0 || !PatchFieldsByWriteProfile.ContainsKey(writeProfile)))
            throw new InvalidOperationException($"Host capability {capability.TypeCode} declares a writable {direction} without an implemented write profile.");
        if (!writable && writeProfile.Length > 0)
            throw new InvalidOperationException($"Host capability {capability.TypeCode} declares {direction} write profile {writeProfile} but is not writable.");
        if (browsable && (browseProfile.Length == 0 || !ImplementedBrowseProfiles.Contains(browseProfile)))
            throw new InvalidOperationException($"Host capability {capability.TypeCode} declares a browsable {direction} without an implemented browse profile.");
        if (!browsable && browseProfile.Length > 0)
            throw new InvalidOperationException($"Host capability {capability.TypeCode} declares {direction} browse profile {browseProfile} but is not browsable.");
        if (changeHostProfile.Length > 0 && !supports)
            throw new InvalidOperationException($"Host capability {capability.TypeCode} declares a {direction} Change Host profile without {direction} support.");
    }

    public static IReadOnlySet<string> RetainedFieldsOnHostChange(string? kind)
        => string.Equals(kind, "Source", StringComparison.OrdinalIgnoreCase)
            ? SourceRetainedOnHostChange
            : string.Equals(kind, "Destination", StringComparison.OrdinalIgnoreCase)
                ? DestinationRetainedOnHostChange
                : EmptyPatchFields;

    private static IReadOnlySet<string> FieldSet(params string[] values)
        => new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);

    private static TraditionalTaskHostCapability Capability(
        string typeCode,
        string type,
        bool source,
        bool destination,
        bool sourceTimestamps = false,
        bool sourceWritable = false,
        bool destinationWritable = false,
        bool sourceBrowsable = false,
        bool destinationBrowsable = false,
        string sourceWriteProfile = "",
        string destinationWriteProfile = "",
        string sourceBrowseProfile = "",
        string destinationBrowseProfile = "",
        string sourceChangeHostProfile = "",
        string destinationChangeHostProfile = "")
        => new(
            typeCode,
            type,
            source,
            destination,
            sourceTimestamps,
            sourceWritable,
            destinationWritable,
            sourceBrowsable,
            destinationBrowsable,
            sourceWriteProfile,
            destinationWriteProfile,
            sourceBrowseProfile,
            destinationBrowseProfile,
            sourceChangeHostProfile,
            destinationChangeHostProfile);
}

/// <summary>
/// Native MOVEit host capabilities independently indicate whether a type can be read,
/// edited as a Traditional Task step, or browsed. These do not by themselves authorize
/// the current RC1 Quick Transfer / spreadsheet workflows to use a host.
/// </summary>
public sealed record TraditionalTaskHostCapability(
    string TypeCode,
    string Type,
    bool SupportsSource,
    bool SupportsDestination,
    bool SupportsSourceTimestamps,
    bool SourceWritable,
    bool DestinationWritable,
    bool SourceBrowsable,
    bool DestinationBrowsable,
    string SourceWriteProfile,
    string DestinationWriteProfile,
    string SourceBrowseProfile,
    string DestinationBrowseProfile,
    string SourceChangeHostProfile,
    string DestinationChangeHostProfile);

/// <summary>
/// Shared native Host capability contract for optional Task/Host editors.
/// Readable capability does not automatically grant use by Quick Transfer or CSV Import.
/// </summary>
public interface ITraditionalTaskHostCapabilityCatalog
{
    IReadOnlyList<TraditionalTaskHostCapability> Capabilities { get; }
    TraditionalTaskHostCapability? GetCapability(string? typeCode);
    bool CanEditorWrite(string role, string? typeCode);
    bool CanEditorBrowse(string role, string? typeCode);
    bool IsEditorPatchFieldAllowed(string role, string? typeCode, string field);
    bool CanEditorChangeHost(string role, string? fromTypeCode, string? toTypeCode);
    void ValidateCapabilities();
}


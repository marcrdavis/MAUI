// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using System.Text.Json.Nodes;
using MoveItAutomationUI.Services.MoveIt.TraditionalTasks;

namespace MoveItAutomationUI.Services.MoveIt;

public enum TraditionalTaskLocationRootKind
{
    None,
    Bucket,
    Container,
    DocumentLibrary
}

public sealed record TraditionalTaskHostProfile(
    string TypeCode,
    string DisplayName,
    string ProfileId,
    bool SourceWriteSupported,
    bool DestinationWriteSupported,
    bool BrowseSupported,
    TraditionalTaskLocationRootKind RootKind = TraditionalTaskLocationRootKind.None,
    string RootStepField = "",
    string RootStepIdField = "",
    string UseDefaultRootField = "",
    string HostDefaultRootField = "",
    string HostDefaultRootIdField = "",
    bool DestinationSupportsForceDir = true);

/// <summary>
/// Shared, protocol-aware Traditional Task Host contract catalog.
/// Task editors, import and Quick Transfer share native defaults and location rules.
/// Workflow-specific exclusions (such as Quick Transfer Host 0) stay at their entry points.
/// </summary>
public interface ITraditionalTaskHostProfileCatalog
{
    IReadOnlyCollection<TraditionalTaskHostProfile> Profiles { get; }
    string NormalizeType(string? typeCode);
    TraditionalTaskHostProfile? Find(string? typeCode);
    TraditionalTaskHostProfile Require(string? typeCode, string role);
    void ApplySourceDefaults(JsonObject step, TraditionalTaskHostProfile profile);
    void ApplyDestinationDefaults(JsonObject step, TraditionalTaskHostProfile profile);
    void ApplyLocation(JsonObject step, TraditionalTaskHostProfile profile, string path, string rootName = "", string rootId = "");
}

// RC1's verified file-transfer defaults are kept intact. The richer RC2 native
// field/direction capabilities live in TraditionalTaskHostCapabilities and are
// discoverable through the shared capability registry introduced in Phase 1.
public sealed class TraditionalTaskHostProfileCatalog : ITraditionalTaskHostProfileCatalog, ITraditionalTaskHostCapabilityCatalog
{
    private static readonly IReadOnlyDictionary<string, TraditionalTaskHostProfile> ProfilesByType =
        new Dictionary<string, TraditionalTaskHostProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["FileSystem"] = Profile("FileSystem"),
            ["Share"] = Profile("Share"),
            ["siLock"] = Profile("siLock"),
            ["FTP"] = Profile("FTP"),
            ["SSHFTP"] = Profile("SSHFTP"),
            ["S3"] = Profile(
                "S3",
                TraditionalTaskLocationRootKind.Bucket,
                rootStepField: "Bucket",
                useDefaultRootField: "UseDefBucket",
                hostDefaultRootField: "DefBucket",
                destinationSupportsForceDir: false),
            ["AzureBlob"] = Profile(
                "AzureBlob",
                TraditionalTaskLocationRootKind.Container,
                rootStepField: "Container",
                useDefaultRootField: "UseDefContainer",
                hostDefaultRootField: "DefContainer",
                destinationSupportsForceDir: false),
            ["SharePoint"] = Profile(
                "SharePoint",
                TraditionalTaskLocationRootKind.DocumentLibrary,
                rootStepField: "DocumentLibrary",
                rootStepIdField: "DocumentLibraryGuid",
                useDefaultRootField: "UseDefDocumentLibrary",
                hostDefaultRootField: "DefDocumentLibrary",
                hostDefaultRootIdField: "DefDocumentLibraryGuid"),
            ["GoogleStorage"] = Profile(
                "GoogleStorage",
                TraditionalTaskLocationRootKind.Bucket,
                rootStepField: "Bucket",
                useDefaultRootField: "UseDefBucket",
                hostDefaultRootField: "DefBucket",
                destinationSupportsForceDir: false)
        };

    // Protocol identity, friendly names, editor write/browse flags and profile IDs
    // now have one authoritative owner: the shared RC2-derived capability catalog.
    // Local File System and protocol profiles share these same editor/import capabilities.
    private static TraditionalTaskHostProfile Profile(
        string typeCode,
        TraditionalTaskLocationRootKind rootKind = TraditionalTaskLocationRootKind.None,
        string rootStepField = "",
        string rootStepIdField = "",
        string useDefaultRootField = "",
        string hostDefaultRootField = "",
        string hostDefaultRootIdField = "",
        bool destinationSupportsForceDir = true)
    {
        var capability = TraditionalTaskHostCapabilities.Get(typeCode)
            ?? throw new InvalidOperationException($"No verified Host capability metadata exists for '{typeCode}'.");
        return new TraditionalTaskHostProfile(
            capability.TypeCode, capability.Type,
            capability.SourceWriteProfile,
            capability.SourceWritable,
            capability.DestinationWritable,
            capability.SourceBrowsable || capability.DestinationBrowsable,
            rootKind, rootStepField, rootStepIdField, useDefaultRootField,
            hostDefaultRootField, hostDefaultRootIdField, destinationSupportsForceDir);
    }

    public IReadOnlyCollection<TraditionalTaskHostProfile> Profiles => ProfilesByType.Values.ToArray();

    public IReadOnlyList<TraditionalTaskHostCapability> Capabilities => TraditionalTaskHostCapabilities.ClientCapabilities;

    public TraditionalTaskHostCapability? GetCapability(string? typeCode) => TraditionalTaskHostCapabilities.Get(typeCode);

    public bool CanEditorWrite(string role, string? typeCode) => TraditionalTaskHostCapabilities.CanWrite(role, typeCode);

    public bool CanEditorBrowse(string role, string? typeCode) => TraditionalTaskHostCapabilities.CanBrowse(role, typeCode);

    public bool IsEditorPatchFieldAllowed(string role, string? typeCode, string field)
        => TraditionalTaskHostCapabilities.IsPatchFieldAllowed(role, typeCode, field);

    public bool CanEditorChangeHost(string role, string? fromTypeCode, string? toTypeCode)
        => TraditionalTaskHostCapabilities.CanChangeHost(role, fromTypeCode, toTypeCode);

    public void ValidateCapabilities() => TraditionalTaskHostCapabilities.ValidateCatalog();

    public string NormalizeType(string? typeCode)
    {
        var value = TraditionalTaskHostCapabilities.NormalizeTypeCode(typeCode);
        if (value.Equals("SiLock", StringComparison.OrdinalIgnoreCase)) return "siLock";
        foreach (var known in ProfilesByType.Keys)
            if (value.Equals(known, StringComparison.OrdinalIgnoreCase)) return known;
        return value;
    }

    public TraditionalTaskHostProfile? Find(string? typeCode)
    {
        var normalized = NormalizeType(typeCode);
        return ProfilesByType.TryGetValue(normalized, out var profile) ? profile : null;
    }

    public TraditionalTaskHostProfile Require(string? typeCode, string role)
    {
        var profile = Find(typeCode)
            ?? throw new InvalidOperationException($"{role} Host type '{(typeCode ?? string.Empty).Trim()}' does not have a verified Traditional Task profile.");
        var supported = role.Equals("Source", StringComparison.OrdinalIgnoreCase)
            ? profile.SourceWriteSupported
            : profile.DestinationWriteSupported;
        if (!supported)
            throw new InvalidOperationException($"{role} is not supported for MOVEit Host type '{profile.DisplayName}'.");
        return profile;
    }

    public void ApplySourceDefaults(JsonObject step, TraditionalTaskHostProfile profile)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(profile);

        step["UseDefRetryCount"] = 1;
        step["UseDefRetryTimeoutSecs"] = 1;
        step["UseDefRescanSecs"] = 1;

        switch (profile.TypeCode)
        {
            case "FileSystem":
                return;
            case "Share":
                step["UseDefWinCopyFileAPI"] = 1;
                return;
            case "siLock":
                step["UseDefUser"] = 1;
                step["UseDefClientCert"] = 1;
                step["UseDefConnTimeoutSecs"] = 1;
                step["UseDefDataTimeoutSecs"] = 1;
                step["UseDefSort"] = 1;
                return;
            case "FTP":
                ApplyCommonFileProtocolDefaults(step, sourceStep: true);
                step["UseDefAB"] = 1;
                step["UseDefClientCert"] = 1;
                step["UseDefPassive"] = 1;
                step["UseDefRsm"] = 1;
                step["UseDefXS"] = 1;
                step["UseDefReuseSSL"] = 1;
                step["UseDefBlindDL"] = 1;
                step["UseDefMD5File"] = 1;
                step["UseDefMD5Get"] = 1;
                return;
            case "SSHFTP":
                ApplyCommonFileProtocolDefaults(step, sourceStep: true);
                step["UseDefClientKey"] = 1;
                step["UseDefRsm"] = 1;
                step["UseDefBlindDL"] = 1;
                return;
            case "S3":
                ApplyCloudDefaults(step, sourceStep: true);
                step["UseDefDLInteg"] = 1;
                return;
            case "AzureBlob":
                ApplyCloudDefaults(step, sourceStep: true);
                step["UseDefDLInteg"] = 1;
                return;
            case "SharePoint":
                ApplyCloudDefaults(step, sourceStep: true);
                return;
            case "GoogleStorage":
                ApplyCloudDefaults(step, sourceStep: true);
                return;
            default:
                throw new InvalidOperationException($"Traditional Task Source defaults are not defined for Host type '{profile.TypeCode}'.");
        }
    }

    public void ApplyDestinationDefaults(JsonObject step, TraditionalTaskHostProfile profile)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(profile);

        step["UseDefRetryCount"] = 1;
        step["UseDefRetryTimeoutSecs"] = 1;

        switch (profile.TypeCode)
        {
            case "FileSystem":
                return;
            case "Share":
                step["UseDefWinCopyFileAPI"] = 1;
                step["UseDefPreserveFileTimestamp"] = 1;
                return;
            case "siLock":
                step["UseDefUser"] = 1;
                step["UseDefClientCert"] = 1;
                step["UseDefConnTimeoutSecs"] = 1;
                step["UseDefDataTimeoutSecs"] = 1;
                return;
            case "FTP":
                ApplyCommonFileProtocolDefaults(step, sourceStep: false);
                step["UseDefAB"] = 1;
                step["UseDefClientCert"] = 1;
                step["UseDefPassive"] = 1;
                step["UseDefRsm"] = 1;
                step["UseDefXS"] = 1;
                step["UseDefReuseSSL"] = 1;
                step["UseDefBlindUL"] = 1;
                return;
            case "SSHFTP":
                ApplyCommonFileProtocolDefaults(step, sourceStep: false);
                step["UseDefClientKey"] = 1;
                step["UseDefRsm"] = 1;
                step["UseDefBlindUL"] = 1;
                return;
            case "S3":
            case "AzureBlob":
            case "SharePoint":
            case "GoogleStorage":
                ApplyCloudDefaults(step, sourceStep: false);
                return;
            default:
                throw new InvalidOperationException($"Traditional Task Destination defaults are not defined for Host type '{profile.TypeCode}'.");
        }
    }

    public void ApplyLocation(JsonObject step, TraditionalTaskHostProfile profile, string path, string rootName = "", string rootId = "")
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(profile);
        var normalizedPath = (path ?? string.Empty).Trim();
        var normalizedRoot = (rootName ?? string.Empty).Trim();
        var normalizedRootId = (rootId ?? string.Empty).Trim();

        if (profile.TypeCode.Equals("siLock", StringComparison.OrdinalIgnoreCase))
        {
            step["FolderName"] = normalizedPath;
            step["FolderID"] = string.Empty;
        }
        else
        {
            step["Path"] = normalizedPath;
        }

        if (profile.RootKind == TraditionalTaskLocationRootKind.None) return;

        if (normalizedRoot.Length == 0)
        {
            step[profile.UseDefaultRootField] = 1;
            step[profile.RootStepField] = string.Empty;
            if (profile.RootStepIdField.Length > 0) step[profile.RootStepIdField] = string.Empty;
            return;
        }

        if (profile.RootKind == TraditionalTaskLocationRootKind.DocumentLibrary && normalizedRootId.Length == 0)
            throw new InvalidOperationException("A SharePoint Document Library selected for a Traditional Task must include its MOVEit library GUID. Re-select the library with Browse or use the Host default library.");

        step[profile.UseDefaultRootField] = 0;
        step[profile.RootStepField] = normalizedRoot;
        if (profile.RootStepIdField.Length > 0) step[profile.RootStepIdField] = normalizedRootId;
    }

    private static void ApplyCommonFileProtocolDefaults(JsonObject step, bool sourceStep)
    {
        step["UseDefUser"] = 1;
        step["UseDefConnTimeoutSecs"] = 1;
        step["UseDefDataTimeoutSecs"] = 1;
        if (sourceStep) step["UseDefRescanSecs"] = 1;
    }

    private static void ApplyCloudDefaults(JsonObject step, bool sourceStep)
    {
        step["UseDefUser"] = 1;
        step["UseDefConnTimeoutSecs"] = 1;
        step["UseDefDataTimeoutSecs"] = 1;
        if (sourceStep) step["UseDefRescanSecs"] = 1;
    }
}

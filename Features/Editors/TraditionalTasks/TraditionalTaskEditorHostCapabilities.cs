// RC2 editor-compatible API facade over the authoritative shared Host capability catalog.
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt.TraditionalTasks;
namespace MoveItAutomationUI.Services.MoveIt;
internal static class TraditionalTaskEditorHostCapabilities
{
    public const string FileSystemWriteProfile = TraditionalTaskHostCapabilities.FileSystemWriteProfile;
    public const string FileSystemBrowseProfile = TraditionalTaskHostCapabilities.FileSystemBrowseProfile;
    public const string ShareWriteProfile = TraditionalTaskHostCapabilities.ShareWriteProfile;
    public const string ShareBrowseProfile = TraditionalTaskHostCapabilities.ShareBrowseProfile;
    public const string MoveItTransferWriteProfile = TraditionalTaskHostCapabilities.MoveItTransferWriteProfile;
    public const string MoveItTransferBrowseProfile = TraditionalTaskHostCapabilities.MoveItTransferBrowseProfile;
    public const string FtpWriteProfile = TraditionalTaskHostCapabilities.FtpWriteProfile;
    public const string FtpBrowseProfile = TraditionalTaskHostCapabilities.FtpBrowseProfile;
    public const string SftpWriteProfile = TraditionalTaskHostCapabilities.SftpWriteProfile;
    public const string SftpBrowseProfile = TraditionalTaskHostCapabilities.SftpBrowseProfile;
    public const string S3WriteProfile = TraditionalTaskHostCapabilities.S3WriteProfile;
    public const string S3BrowseProfile = TraditionalTaskHostCapabilities.S3BrowseProfile;
    public const string AzureBlobWriteProfile = TraditionalTaskHostCapabilities.AzureBlobWriteProfile;
    public const string AzureBlobBrowseProfile = TraditionalTaskHostCapabilities.AzureBlobBrowseProfile;
    public const string SharePointWriteProfile = TraditionalTaskHostCapabilities.SharePointWriteProfile;
    public const string SharePointBrowseProfile = TraditionalTaskHostCapabilities.SharePointBrowseProfile;
    public const string GoogleStorageWriteProfile = TraditionalTaskHostCapabilities.GoogleStorageWriteProfile;
    public const string GoogleStorageBrowseProfile = TraditionalTaskHostCapabilities.GoogleStorageBrowseProfile;
    public const string Pop3WriteProfile = TraditionalTaskHostCapabilities.Pop3WriteProfile;
    public const string SmtpWriteProfile = TraditionalTaskHostCapabilities.SmtpWriteProfile;
    public static IReadOnlyList<TraditionalTaskEditorHostCapability> ClientCapabilities => TraditionalTaskHostCapabilities.ClientCapabilities.Select(x => new TraditionalTaskEditorHostCapability(x.TypeCode, x.Type, x.SupportsSource, x.SupportsDestination, x.SupportsSourceTimestamps, x.SourceWritable, x.DestinationWritable, x.SourceBrowsable, x.DestinationBrowsable, x.SourceWriteProfile, x.DestinationWriteProfile, x.SourceBrowseProfile, x.DestinationBrowseProfile, x.SourceChangeHostProfile, x.DestinationChangeHostProfile)).ToArray();
    public static TraditionalTaskEditorHostCapability? Get(string? typeCode) => ClientCapabilities.FirstOrDefault(x => x.TypeCode.Equals(TraditionalTaskHostCapabilities.NormalizeTypeCode(typeCode), StringComparison.OrdinalIgnoreCase));
    public static IReadOnlySet<string> SourceTypeCodes => TraditionalTaskHostCapabilities.SourceTypeCodes;
    public static IReadOnlySet<string> DestinationTypeCodes => TraditionalTaskHostCapabilities.DestinationTypeCodes;
    public static IReadOnlyList<string> WrapperTypeCodes => TraditionalTaskHostCapabilities.WrapperTypeCodes;
    public static string NormalizeTypeCode(string? typeCode) => TraditionalTaskHostCapabilities.NormalizeTypeCode(typeCode);
    public static string FriendlyName(string? typeCode) => TraditionalTaskHostCapabilities.FriendlyName(typeCode);
    public static bool CanWrite(string? kind, string? typeCode) => TraditionalTaskHostCapabilities.CanWrite(kind, typeCode);
    public static bool CanBrowse(string? kind, string? typeCode) => TraditionalTaskHostCapabilities.CanBrowse(kind, typeCode);
    public static bool SupportsSourceTimestamps(string? typeCode) => TraditionalTaskHostCapabilities.SupportsSourceTimestamps(typeCode);
    public static string WriteProfile(string? kind, string? typeCode) => TraditionalTaskHostCapabilities.WriteProfile(kind, typeCode);
    public static string BrowseProfile(string? kind, string? typeCode) => TraditionalTaskHostCapabilities.BrowseProfile(kind, typeCode);
    public static string ChangeHostProfile(string? kind, string? typeCode) => TraditionalTaskHostCapabilities.ChangeHostProfile(kind, typeCode);
    public static bool CanChangeHost(string? kind, string? existingTypeCode, string? requestedTypeCode) => TraditionalTaskHostCapabilities.CanChangeHost(kind, existingTypeCode, requestedTypeCode);
    public static bool IsPatchFieldAllowed(string? kind, string? typeCode, string fieldName) => TraditionalTaskHostCapabilities.IsPatchFieldAllowed(kind, typeCode, fieldName);
    public static IReadOnlySet<string> RetainedFieldsOnHostChange(string? kind) => TraditionalTaskHostCapabilities.RetainedFieldsOnHostChange(kind);
    public static void ValidateCatalog() => TraditionalTaskHostCapabilities.ValidateCatalog();
}

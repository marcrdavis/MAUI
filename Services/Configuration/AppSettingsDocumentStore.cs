// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Centralizes installation-owned appsettings.json access for restricted IIS deployments.
// The application pool needs write access to the EXISTING file and Data, never the app root.

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace MoveItAutomationUI.Services.Configuration;

public interface IAppSettingsDocumentStore
{
    string LivePath { get; }
    string TemplatePath { get; }
    bool LiveExists { get; }

    JsonObject ReadTemplate();
    JsonObject ReadLive();
    Task<JsonObject> ReadLiveAsync(CancellationToken cancellationToken = default);
    Task<JsonObject> ReadSeedAsync(CancellationToken cancellationToken = default);
    Task WriteLiveAsync(JsonObject root, CancellationToken cancellationToken = default, bool publishRuntime = true);
    string CreateLiveBackup(string label);
}

// Only these reviewed messages are safe to return on the anonymous Setup endpoint.
// File paths, configuration values, credentials, and underlying exception text stay in server logs.
public sealed class AppSettingsDeploymentException : Exception
{
    public AppSettingsDeploymentException(string message, Exception? innerException = null)
        : base(message, innerException) { }
}

public sealed class AppSettingsDocumentStore : IAppSettingsDocumentStore
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    private readonly string _recoveryDirectory;
    private readonly IConfigurationRoot _runtimeConfiguration;
    private const string RecoveryFileName = "appsettings.lastgood.json";

    public string LivePath { get; }
    public string TemplatePath { get; }
    public bool LiveExists => File.Exists(LivePath);

    public AppSettingsDocumentStore(IWebHostEnvironment environment, IConfiguration configuration)
    {
        _runtimeConfiguration = configuration as IConfigurationRoot
            ?? throw new InvalidOperationException("MAUI requires a reloadable application configuration root.");
        LivePath = Path.Combine(environment.ContentRootPath, "appsettings.json");
        TemplatePath = Path.Combine(environment.ContentRootPath, "appsettings.template.json");
        _recoveryDirectory = Path.Combine(environment.ContentRootPath, "Data", "ConfigurationRecovery");
    }

    public JsonObject ReadTemplate() => ReadObject(TemplatePath, "appsettings.template.json");

    public JsonObject ReadLive() => ReadObject(LivePath, "appsettings.json");

    public async Task<JsonObject> ReadLiveAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(LivePath))
            throw new AppSettingsDeploymentException(MissingLiveMessage);
        return await ReadObjectAsync(LivePath, "appsettings.json", cancellationToken);
    }

    private const string MissingLiveMessage =
        "appsettings.json is missing. The deployment administrator must create an existing file containing {} before MAUI starts and grant the IIS application-pool identity Modify permission on that file.";
    private const string ReadOnlyLiveMessage =
        "MAUI cannot modify the existing appsettings.json file. Grant the IIS application-pool identity Modify permission on appsettings.json (not on the application directory).";
    private const string RecoveryDirectoryMessage =
        "MAUI cannot write configuration recovery copies under Data\\ConfigurationRecovery. Grant the IIS application-pool identity Modify permission on the Data directory and its contents.";

    // Initial Setup may start from a deployment-provisioned {} file. Clone the release template
    // and overlay administrator-supplied settings recursively; never discard existing values.
    // Validate permissions BEFORE provisioning the metadata DB or creating default Tags.
    public async Task<JsonObject> ReadSeedAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureExistingLiveWritable();
        EnsureRecoveryWritable();
        var live = await ReadLiveAsync(cancellationToken);
        if (!File.Exists(TemplatePath))
            throw new AppSettingsDeploymentException("appsettings.template.json is missing from the MAUI installation. Restore the template from the release package.");
        var template = await ReadObjectAsync(TemplatePath, "appsettings.template.json", cancellationToken);
        OverlayExistingValues(template, live);
        return template;
    }

    // Direct writes are not filesystem-atomic. A complete last-known-good snapshot is committed
    // under the writable, non-web-served Data directory BEFORE opening appsettings.json for write.
    // Once the file is opened for mutation, never cancel halfway through (e.g. browser disconnect).
    // Hold an exclusive handle to prevent normal readers observing an intermediate JSON document.
    // AppSettingsWriteCoordinator serializes application-level mutations across all MAUI callers.
    // Backup Restore explicitly defers runtime publication because it requires a controlled restart.
    public Task WriteLiveAsync(JsonObject root, CancellationToken cancellationToken = default, bool publishRuntime = true)
    {
        ArgumentNullException.ThrowIfNull(root);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureExistingLiveWritable();

        var bytes = new UTF8Encoding(false).GetBytes(root.ToJsonString(WriteOptions) + Environment.NewLine);
        ValidateObject(bytes, "generated appsettings.json");
        var previous = ReadValidExistingBytes();
        SaveLastGood(previous);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            WriteExisting(bytes);
            // Verify the persisted bytes, not merely the in-memory JSON object.
            ValidateObject(File.ReadAllBytes(LivePath), "appsettings.json");
            // Commit to all IConfiguration/IOptions consumers before releasing the caller's write gate.
            // Initial Setup alone may subsequently activate startup-sensitive settings.
            if (publishRuntime) _runtimeConfiguration.Reload();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or AppSettingsDeploymentException)
        {
            try
            {
                WriteExisting(previous);
                if (publishRuntime) _runtimeConfiguration.Reload();
            }
            catch (Exception rollbackException)
            {
                throw new AppSettingsDeploymentException(
                    "MAUI could not update appsettings.json and automatic restoration also failed. The administrator must restore Data\\ConfigurationRecovery\\appsettings.lastgood.json before restarting the application.",
                    new AggregateException(ex, rollbackException));
            }
            throw new AppSettingsDeploymentException(
                "MAUI could not write appsettings.json. The previous configuration was restored; check file permissions and application logs.", ex);
        }
        return Task.CompletedTask;
    }

    // A dedicated, restricted Data subfolder replaces old pre-upgrade/pre-restore copies made
    // directly in the installation root. Restore/Upgrade keep their original safety-copy semantics.
    public string CreateLiveBackup(string label)
    {
        var previous = ReadValidExistingBytes();
        EnsureRecoveryDirectory();

        var safeLabel = string.Concat((label ?? string.Empty)
            .Select(ch => char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_' ? ch : '_'));
        if (safeLabel.Length == 0) safeLabel = "backup";
        if (safeLabel.Length > 64) safeLabel = safeLabel[..64];
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmssfff", System.Globalization.CultureInfo.InvariantCulture);
        var backupPath = Path.Combine(_recoveryDirectory, $"appsettings.pre-upgrade-{safeLabel}-{stamp}-{Guid.NewGuid():N}.json");
        try
        {
            // FileMode.CreateNew avoids overwriting an earlier administrator recovery snapshot.
            using var output = new FileStream(backupPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            output.Write(previous);
            output.Flush(flushToDisk: true);
            return backupPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new AppSettingsDeploymentException(RecoveryDirectoryMessage, ex);
        }
    }

    private void EnsureExistingLiveWritable()
    {
        if (!File.Exists(LivePath)) throw new AppSettingsDeploymentException(MissingLiveMessage);
        try
        {
            // Does not change bytes or attributes. Crucially, FileMode.Open cannot create a
            // new file in the directory, so the check matches the intended IIS ACL model.
            using var stream = new FileStream(LivePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new AppSettingsDeploymentException(ReadOnlyLiveMessage, ex);
        }
    }

    private byte[] ReadValidExistingBytes()
    {
        if (!File.Exists(LivePath)) throw new AppSettingsDeploymentException(MissingLiveMessage);
        byte[] bytes;
        try { bytes = File.ReadAllBytes(LivePath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new AppSettingsDeploymentException("MAUI cannot read appsettings.json. Check permissions on that file.", ex);
        }
        ValidateObject(bytes, "appsettings.json");
        return bytes;
    }

    private static void ValidateObject(byte[] bytes, string displayName)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.ValueKind == JsonValueKind.Object) return;
        }
        catch (JsonException ex)
        {
            throw new AppSettingsDeploymentException($"{displayName} does not contain a valid JSON object. Restore a valid configuration before continuing.", ex);
        }
        throw new AppSettingsDeploymentException($"{displayName} does not contain a valid JSON object. Restore a valid configuration before continuing.");
    }

    private void EnsureRecoveryDirectory()
    {
        try { Directory.CreateDirectory(_recoveryDirectory); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new AppSettingsDeploymentException(RecoveryDirectoryMessage, ex);
        }
    }

    // Preflight the directory ACL without touching the configuration file. This prevents
    // first-run database changes when the eventual recovery-copy write would be denied.
    private void EnsureRecoveryWritable()
    {
        EnsureRecoveryDirectory();
        var probePath = Path.Combine(_recoveryDirectory, $"permission-check-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var probe = new FileStream(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                probe.WriteByte(0);
                probe.Flush(flushToDisk: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new AppSettingsDeploymentException(RecoveryDirectoryMessage, ex);
        }
        finally
        {
            try { if (File.Exists(probePath)) File.Delete(probePath); }
            catch { /* The error returned above remains the primary failure. */ }
        }
    }

    private void SaveLastGood(byte[] bytes)
    {
        EnsureRecoveryDirectory();
        var tempPath = Path.Combine(_recoveryDirectory, $"appsettings.recovery-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(tempPath, Path.Combine(_recoveryDirectory, RecoveryFileName), overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new AppSettingsDeploymentException(RecoveryDirectoryMessage, ex);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); }
            catch { /* Keep the write/rename error rather than a secondary cleanup failure. */ }
        }
    }

    private void WriteExisting(byte[] bytes)
    {
        // Never use FileMode.Create or File.Move here. Both require installation-root write
        // privileges and replacement can reset the individually provisioned file ACL.
        using var stream = new FileStream(LivePath, FileMode.Open, FileAccess.Write, FileShare.None);
        stream.Position = 0;
        stream.Write(bytes);
        stream.SetLength(bytes.Length);
        stream.Flush(flushToDisk: true);
    }

    private static void OverlayExistingValues(JsonObject template, JsonObject existing)
    {
        foreach (var pair in existing)
        {
            if (pair.Value is JsonObject existingObject && template[pair.Key] is JsonObject templateObject)
                OverlayExistingValues(templateObject, existingObject);
            else
                template[pair.Key] = pair.Value?.DeepClone();
        }
    }

    // An installation-owned appsettings.json is briefly locked exclusively during in-place
    // writes (required by restricted IIS file ACLs). Readers can overlap that short interval.
    // Retry sharing violations only; never replace missing/malformed configuration with defaults.
    private static JsonObject ReadObject(string path, string displayName)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"{displayName} could not be found.", path);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var json = File.ReadAllText(path);
                return JsonNode.Parse(json) as JsonObject
                    ?? throw new InvalidOperationException($"{displayName} is not a valid JSON object.");
            }
            catch (IOException ex) when (attempt < 4 && IsSharingViolation(ex))
            {
                Thread.Sleep(25 * (attempt + 1));
            }
        }
    }

    private static async Task<JsonObject> ReadObjectAsync(string path, string displayName, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"{displayName} could not be found.", path);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var json = await File.ReadAllTextAsync(path, cancellationToken);
                return JsonNode.Parse(json) as JsonObject
                    ?? throw new InvalidOperationException($"{displayName} is not a valid JSON object.");
            }
            catch (IOException ex) when (attempt < 4 && IsSharingViolation(ex))
            {
                await Task.Delay(25 * (attempt + 1), cancellationToken);
            }
        }
    }

    private static bool IsSharingViolation(IOException exception)
        => (exception.HResult & 0xFFFF) is 32 or 33; // Win32 ERROR_SHARING_VIOLATION / LOCK_VIOLATION
}

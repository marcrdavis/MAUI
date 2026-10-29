// Author: Marc R. Davis (marcdavis@comcast.net)
// Sync folder policy over shared native Source profiles. Folder B is never a Destination.
using System.Text.Json.Nodes;
using MoveItAutomationUI.Services.MoveIt.TraditionalTasks;
using static MoveItAutomationUI.Services.MoveIt.TraditionalTasks.TraditionalTaskDefinitionEditorOperations;

namespace MoveItAutomationUI.Services.MoveIt.Tasks;

public static class SynchronizationTaskDefinitionComposer
{
    public static readonly IReadOnlySet<string> HostTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "FileSystem", "Share", "FTP", "SSHFTP", "S3", "AzureBlob", "SharePoint", "GoogleStorage" };
    private static readonly IReadOnlySet<string> TraditionalOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "NewFilesOnly", "Unzip", "RetryIfNoFiles", "DeleteOrig", "RenameTo", "DelRename", "UseDefWinCopyFileAPI", "UseWinCopyFileAPI" };
    private static readonly IReadOnlySet<string> LocalFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "ID", "HostID", "Type", "Path", "FileMask", "ExFo", "SearchSubdirs", "ExFile", "Criteria", "UseDefPreserveFileTimestamp", "PreserveFileTimestamp" };

    public static void ApplyFolders(JsonObject task, JsonArray? requested)
    {
        if (requested is null || requested.Count > 2 || requested.Any(item => item is not JsonObject))
            throw new InvalidOperationException("Sync Tasks support Folder A and Folder B only.");
        var existing = GetStepObjects(GetNode(task, "steps"));
        if (existing.Count > 2 || existing.Any(item => GetObject(item, "Source") is null))
            throw new InvalidOperationException("The Sync folder definition is not recognized. Use the native editor to inspect it.");
        var output = new JsonArray();
        var nextId = Math.Max(GetInt(task, "NextEID", 1), existing.Select(wrapper => GetInt(GetObject(wrapper, "Source"), "ID", 0)).DefaultIfEmpty(0).Max() + 1);
        for (var index = 0; index < requested.Count; index++)
        {
            var item = requested[index]!.AsObject();
            if (!GetString(item, "kind").Equals("Source", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Both Sync folders must be native Source entries.");
            var current = index < existing.Count ? GetObject(existing[index], "Source") : null;
            var id = GetString(item, "id").Trim();
            if (current is not null && !GetString(current, "ID").Equals(id, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Sync Folder A/B identities and order cannot be changed.");
            if (current is not null && new[] { "SyF", "SyT", "SyDel", "UseLCaseComp" }.Any(field => GetNode(current, field) is null))
                throw new InvalidOperationException("MOVEit did not return the complete native Sync flags. Use the native editor to inspect this Task.");
            var patch = GetObject(item, "patch");
            if (current is not null && GetNode(patch, "ID") is not null && GetString(patch, "ID") != id)
                throw new InvalidOperationException("A Sync folder patch cannot change its native ID.");
            if (patch is null)
            {
                if (current is null) throw new InvalidOperationException("A new Sync folder needs a supported Host and folder.");
                output.Add(existing[index].DeepClone());
                continue;
            }
            var type = TraditionalTaskHostCapabilities.NormalizeTypeCode(GetString(patch, "Type"));
            if (!HostTypes.Contains(type)) throw new InvalidOperationException("This Host type is not supported for Sync folders.");
            foreach (var field in patch.Select(pair => pair.Key))
            {
                if (TraditionalOnly.Contains(field) || (field is "UseDefPreserveFileTimestamp" or "PreserveFileTimestamp" ? false : type == "FileSystem" ? !LocalFields.Contains(field)
                    : !TraditionalTaskHostCapabilities.IsPatchFieldAllowed("Source", type, field)))
                    throw new InvalidOperationException($"Field {field} is not an editable Sync folder property.");
            }
            var hostChanged = current is not null && (!GetString(current, "HostID").Equals(GetString(patch, "HostID"), StringComparison.OrdinalIgnoreCase)
                || !NormalizeCurrentType(current).Equals(type, StringComparison.OrdinalIgnoreCase));
            foreach (var field in new[] { "UseDefPreserveFileTimestamp", "PreserveFileTimestamp" })
                if (GetNode(patch, field) is not null && GetString(patch, field) is not ("0" or "1"))
                    throw new InvalidOperationException($"Sync field {field} must be 0 or 1.");
            if (GetString(patch, "HostID").Trim().Length == 0 || GetString(patch, "Path").Trim().Length == 0)
                throw new InvalidOperationException($"Select the Host and folder for Folder {(index == 0 ? "A" : "B")}.");
            if (GetString(patch, "FileMask").Trim().Length == 0)
                throw new InvalidOperationException("Sync File(s) is required.");
            if (type == "FileSystem" && GetString(patch, "HostID") != "0")
                throw new InvalidOperationException("Local Sync folders require Host ID 0.");
            if (type != "FileSystem") ValidateHostStepPatch("Source", type, patch, current is null || hostChanged);
            var target = hostChanged ? ChangeHost(current!, type, index)
                : current?.DeepClone().AsObject() ?? NewFolder(type, index);
            MergeStepPatch(target, patch);
            if (current is null) target["ID"] = (nextId++).ToString(System.Globalization.CultureInfo.InvariantCulture);
            // Native role/delete/comparison/timestamp flags remain on the fresh server document.
            var wrapper = index < existing.Count ? existing[index].DeepClone().AsObject() : new JsonObject();
            wrapper["Source"] = target;
            output.Add(wrapper);
        }
        if (output.Count == 2)
        {
            var a = output[0]!["Source"]!.AsObject();
            var b = output[1]!["Source"]!.AsObject();
            if (GetString(a, "HostID") == GetString(b, "HostID") && GetString(a, "Path") == GetString(b, "Path")
                && GetString(a, "Bucket") == GetString(b, "Bucket") && GetString(a, "Container") == GetString(b, "Container")
                && GetString(a, "DocLibGUID") == GetString(b, "DocLibGUID"))
                throw new InvalidOperationException("Folder A and Folder B must be different locations.");
        }
        if (GetBool(task, "Active") && output.Count != 2)
            throw new InvalidOperationException("Define both Sync folders before enabling the Task.");
        task["steps"] = output;
        if (requested.Count > existing.Count) task["NextEID"] = nextId;
    }

    public static IReadOnlyList<JsonObject> RequireFolders(JsonObject task)
    {
        var folders = GetStepObjects(GetNode(task, "steps"));
        if (folders.Count != 2 || folders.Any(wrapper => GetObject(wrapper, "Source") is null))
            throw new InvalidOperationException("Define Folder A and Folder B before using this Sync action.");
        return folders;
    }

    public static void SwapFolders(JsonObject task)
    {
        var before = RequireFolders(task);
        var swapped = new JsonArray(before[1].DeepClone(), before[0].DeepClone());
        for (var index = 0; index < 2; index++)
        {
            var source = GetObject(swapped[index]!.AsObject(), "Source")!;
            var role = GetObject(before[index], "Source")!;
            foreach (var field in new[] { "SyF", "SyT", "SyDel" })
                source[field] = GetNode(role, field)?.DeepClone() ?? throw new InvalidOperationException("The Sync folder roles are incomplete.");
        }
        task["steps"] = swapped;
    }

    public static void VerifySwappedFolders(JsonObject expected, JsonObject saved)
    {
        var before = RequireFolders(expected); var after = RequireFolders(saved);
        for (var index = 0; index < 2; index++)
            foreach (var field in new[] { "HostID", "Type", "Path", "Bucket", "Container", "DocLibGUID" })
                if (GetString(GetObject(before[index], "Source"), field) != GetString(GetObject(after[index], "Source"), field))
                    throw new InvalidOperationException("MOVEit did not preserve the swapped Sync folder location.");
    }

    public static bool ApplySettings(JsonObject task, JsonObject? settings)
    {
        if (settings is null) return false;
        if (settings.Any(pair => pair.Key is not ("direction" or "deletePolicy" or "caseSensitive" or "sameSeconds")))
            throw new InvalidOperationException("The Sync settings contain an unsupported property.");
        var folders = GetStepObjects(GetNode(task, "steps"));
        if (folders.Count != 2) throw new InvalidOperationException("Define Folder A and Folder B before changing Sync options.");
        var direction = GetString(settings, "direction");
        if (direction.Length > 0 && direction is not ("unidirectional" or "bidirectional"))
            throw new InvalidOperationException("Select a valid Sync Type.");
        var bidirectional = direction == "bidirectional" || direction.Length == 0
            && folders.All(wrapper => GetInt(GetObject(wrapper, "Source"), "SyF", 0) == 1
                && GetInt(GetObject(wrapper, "Source"), "SyT", 0) == 1);
        int? deletes = GetNode(settings, "deletePolicy") is null ? null : ReadRequiredWholeNumber(settings, "deletePolicy", "Sync Deletes");
        // A direction change applies the selected Task policy to the newly active folder.
        // Otherwise a one-way Folder B SyDel=0 would silently ignore its deletes in two-way Sync.
        if (direction.Length > 0 && deletes is null) deletes = GetInt(GetObject(folders[0], "Source"), "SyDel", 1);
        if (deletes is < 0 or > 2) throw new InvalidOperationException("Select a valid Sync Deletes policy.");
        if (bidirectional && deletes == 2)
            throw new InvalidOperationException("Deleting extra files on Folder B applies only to unidirectional Sync.");
        bool? caseSensitive = null;
        if (GetNode(settings, "caseSensitive") is JsonNode comparison)
        {
            if (comparison is not JsonValue scalar || !scalar.TryGetValue<bool>(out var comparisonFlag))
                throw new InvalidOperationException("Select a filename comparison policy.");
            caseSensitive = comparisonFlag;
        }
        if (GetNode(settings, "sameSeconds") is not null)
        {
            var seconds = ReadRequiredWholeNumber(settings, "sameSeconds", "Initial Run timestamp tolerance");
            if (seconds < -3) throw new InvalidOperationException("Initial Run timestamp tolerance must be -3, -2, -1, or a nonnegative number of seconds.");
            task["SameSecs"] = seconds;
        }
        var changed = false;
        for (var index = 0; index < 2; index++)
        {
            var source = GetObject(folders[index], "Source") ?? throw new InvalidOperationException("Sync folder is missing.");
            var flags = new Dictionary<string, int>();
            if (direction.Length > 0)
            {
                flags["SyF"] = index == 0 || bidirectional ? 1 : 0;
                flags["SyT"] = index == 1 || bidirectional ? 1 : 0;
            }
            if (deletes.HasValue) flags["SyDel"] = bidirectional || index == 0 ? deletes.Value : 0;
            if (caseSensitive.HasValue) flags["UseLCaseComp"] = caseSensitive.Value ? 0 : 1;
            foreach (var pair in flags)
            {
                changed |= GetInt(source, pair.Key, -1) != pair.Value;
                source[pair.Key] = pair.Value;
            }
        }
        return changed;
    }

    private static JsonObject ChangeHost(JsonObject current, string type, int index)
    {
        // Reuse the Source migration rules so old protocol credentials never cross Hosts.
        var target = NewFolder(type, index);
        foreach (var pair in CreateHostChangeTarget("Source", current)) target[pair.Key] = pair.Value?.DeepClone();
        foreach (var field in new[] { "ID", "SyF", "SyT", "SyDel", "UseLCaseComp", "UseDefPreserveFileTimestamp", "PreserveFileTimestamp" })
            if (GetNode(current, field) is JsonNode value) target[field] = value.DeepClone();
        return target;
    }

    public static void VerifyNativeState(JsonObject expected, JsonObject persisted)
    {
        if (!EquivalentScalar("SameSecs", GetNode(expected, "SameSecs"), GetNode(persisted, "SameSecs")))
            throw new InvalidOperationException("MOVEit did not preserve the native Sync initial-run setting.");
        var before = GetStepObjects(GetNode(expected, "steps"));
        var after = GetStepObjects(GetNode(persisted, "steps"));
        if (before.Count != after.Count) throw new InvalidOperationException("MOVEit did not preserve the Sync folder collection.");
        for (var index = 0; index < before.Count; index++)
        {
            var source = GetObject(before[index], "Source") ?? throw new InvalidOperationException("Sync Source is missing.");
            var saved = GetObject(after[index], "Source") ?? throw new InvalidOperationException("Saved Sync Source is missing.");
            foreach (var field in new[] { "ID", "SyF", "SyT", "SyDel", "UseLCaseComp", "UseDefPreserveFileTimestamp", "PreserveFileTimestamp" })
                if (GetNode(source, field) is not null && !EquivalentScalar(field, GetNode(source, field), GetNode(saved, field)))
                    throw new InvalidOperationException($"MOVEit did not preserve Sync Folder {(index == 0 ? "A" : "B")} field {field}.");
        }
    }

    public static void VerifyFolders(JsonArray? requested, JsonNode? persisted)
        => VerifySteps(requested, persisted);

    private static string NormalizeCurrentType(JsonObject current)
        => GetString(current, "HostID") == "0" && GetString(current, "Type").Length == 0
            ? "FileSystem" : TraditionalTaskHostCapabilities.NormalizeTypeCode(GetString(current, "Type"));

    private static JsonObject NewFolder(string type, int index)
    {
        var folder = new JsonObject
        {
            ["Disabled"] = 0, ["InvalidOverride"] = 0, ["FileMask"] = "*.*", ["ExFile"] = "", ["ExFo"] = "",
            ["SearchSubdirs"] = 0, ["DeleteOrig"] = 0, ["RenameTo"] = "", ["DelRename"] = 1,
            ["NewFilesOnly"] = 0, ["Unzip"] = 0, ["RetryIfNoFiles"] = 0,
            ["SyF"] = index == 0 ? 1 : 0, ["SyT"] = index == 0 ? 0 : 1,
            ["SyDel"] = 1, ["UseLCaseComp"] = 1,
            ["UseDefPreserveFileTimestamp"] = index == 0 ? 1 : 0, ["PreserveFileTimestamp"] = index == 0 ? 0 : 1
        };
        if (type != "FileSystem") ApplyHostChangeProfileDefaults("Source", type, folder);
        if (type == "Share")
        {
            folder["UseDefRetryCount"] = 0; folder["RetryCount"] = 0;
            folder["UseDefRetryTimeoutSecs"] = 0; folder["RetryTimeoutSecs"] = 30;
            folder["UseDefRescanSecs"] = 0; folder["RescanSecs"] = 0;
            folder["UDMxFi"] = 1; folder["MxFi"] = 0; folder["UDMxBy"] = 1; folder["MxBy"] = 0;
            folder["UseDefWinCopyFileAPI"] = 1; folder["UseWinCopyFileAPI"] = 0;
        }
        return folder;
    }
}

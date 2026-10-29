using System.Text.Json.Nodes;

namespace MoveItAutomationUI.Services.MoveIt.SystemSettings;

// Operational lockouts are independent of policy/Globals edits and never use checkout.
public sealed record SystemActiveLockout(string Value, string Stamp, string EditVersion);
public sealed record SystemActiveLockouts(IReadOnlyList<SystemActiveLockout> Addresses, IReadOnlyList<SystemActiveLockout> Users);
public sealed record SystemLockoutSelection(string Value, string EditVersion);
public sealed record SystemUnlockResult(string Kind, IReadOnlyList<SystemActiveLockout> Items, int Unlocked, int AlreadyUnlocked);

internal static class SystemActiveLockoutDefinition
{
    public static string Field(string kind) => kind switch
    {
        "ip" => "IP", "user" => "User", _ => throw new ArgumentException("Select IP addresses or users to unlock.")
    };
    public static string Version(string kind, string value, string stamp)
        => ObjectEditVersion.Create("System Lockout " + kind, value, new JsonObject { ["Value"] = value, ["Stamp"] = stamp });
    public static IReadOnlyList<SystemActiveLockout> Read(string kind, JsonObject response)
    {
        var field = Field(kind);
        if (response["items"] is not JsonArray items) throw new InvalidOperationException("MOVEit returned an incomplete Lockouts list.");
        var result = new List<SystemActiveLockout>(); var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in items)
        {
            if (node is not JsonObject item) throw new InvalidOperationException("MOVEit returned an invalid Lockouts entry.");
            var value = SystemSettingValue.Text(item, field); var stamp = SystemSettingValue.Text(item, "Stamp");
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(stamp) || value.Any(char.IsControl) || !identities.Add(value))
                throw new InvalidOperationException("MOVEit returned an invalid or duplicate Lockouts entry.");
            // Preserve the native timestamp: the capture carries no time-zone offset.
            result.Add(new(value, stamp, Version(kind, value, stamp)));
        }
        return result;
    }
}

public sealed class SystemActiveLockoutConflictException()
    : InvalidOperationException("Selected lockouts changed in MOVEit. Refresh Lockouts and review the selection before unlocking.");
public sealed class SystemUnlockUncertainException(Exception? inner = null)
    : InvalidOperationException("The unlock result could not be confirmed. Refresh Lockouts and review the list before trying again.", inner);

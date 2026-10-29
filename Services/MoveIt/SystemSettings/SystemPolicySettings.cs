using System.Globalization;
using System.Net;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

namespace MoveItAutomationUI.Services.MoveIt.SystemSettings;

// Typed browser projections own only the fields established by the native captures.
// Native numeric flags, unit strings and sparse default fields follow the supplied captures.
internal static class SystemSettingValue
{
    public static int Integer(JsonObject source, string name)
        => int.TryParse(source[name]?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value : throw new InvalidOperationException($"MOVEit returned an invalid or missing {name} setting.");
    public static bool Flag(JsonObject source, string name) => source[name]?.ToString().ToLowerInvariant() switch
    {
        "true" or "1" => true, "false" or "0" => false,
        _ => throw new InvalidOperationException($"MOVEit returned an invalid or missing {name} setting.")
    };
    public static string Text(JsonObject source, string name) => source[name] is JsonValue value && value.TryGetValue<string>(out var text)
        ? text : throw new InvalidOperationException($"MOVEit returned an invalid or missing {name} setting.");
}

public sealed record SystemEventLogSettings
{
    public required string EventLogName { get; init; }
    public required int EventLogLevel { get; init; }
    public required int EventLogAuditLevel { get; init; }
    public void Validate()
    {
        if (EventLogName is not ("Application" or "MOVEit")) throw new ArgumentException("Select Application or MOVEit as the Windows Event Log Name.");
        if (EventLogLevel is not (0 or 10 or 20 or 30 or 40)) throw new ArgumentException("Select a supported Windows Event Log Level.");
        if (EventLogAuditLevel is not (0 or 10 or 30)) throw new ArgumentException("Select a supported Windows Event Audit Log Level.");
    }
    public static SystemEventLogSettings Read(JsonObject source) => new()
    {
        EventLogName = SystemSettingValue.Text(source, nameof(EventLogName)),
        EventLogLevel = SystemSettingValue.Integer(source, nameof(EventLogLevel)),
        EventLogAuditLevel = SystemSettingValue.Integer(source, nameof(EventLogAuditLevel))
    };
    public void Apply(JsonObject target)
    {
        target[nameof(EventLogName)] = EventLogName; target[nameof(EventLogLevel)] = EventLogLevel; target[nameof(EventLogAuditLevel)] = EventLogAuditLevel;
    }
}

public sealed record SystemTaskSettings
{
    public required int MaxSimulTasks { get; init; }
    public required bool AllowMultRuns { get; init; }
    public required bool SuppressQueuedTasksAtStartup { get; init; }
    public required int PollIntervalSecs { get; init; }
    [JsonPropertyName("as2PollIntervalSecs")]
    public required int AS2PollIntervalSecs { get; init; }
    public void Validate()
    {
        if (MaxSimulTasks < 1) throw new ArgumentException("Maximum Running Tasks must be greater than zero.");
        if (PollIntervalSecs < 0 || AS2PollIntervalSecs < 0) throw new ArgumentException("Polling intervals cannot be negative.");
    }
    public static SystemTaskSettings Read(JsonObject source) => new()
    {
        MaxSimulTasks = SystemSettingValue.Integer(source, nameof(MaxSimulTasks)),
        AllowMultRuns = SystemSettingValue.Flag(source, nameof(AllowMultRuns)),
        SuppressQueuedTasksAtStartup = SystemSettingValue.Flag(source, nameof(SuppressQueuedTasksAtStartup)),
        PollIntervalSecs = SystemSettingValue.Integer(source, nameof(PollIntervalSecs)),
        // Native omits this optional override when Notification Polling Interval is used.
        AS2PollIntervalSecs = source.ContainsKey(nameof(AS2PollIntervalSecs)) ? SystemSettingValue.Integer(source, nameof(AS2PollIntervalSecs)) : 0
    };
    public void Apply(JsonObject target)
    {
        target[nameof(MaxSimulTasks)] = MaxSimulTasks; target[nameof(AllowMultRuns)] = AllowMultRuns ? 1 : 0;
        target[nameof(SuppressQueuedTasksAtStartup)] = SuppressQueuedTasksAtStartup ? 1 : 0; target[nameof(PollIntervalSecs)] = PollIntervalSecs; target[nameof(AS2PollIntervalSecs)] = AS2PollIntervalSecs;
    }
}

public sealed record SystemStateFileSettings
{
    public required int DefStateCacheTime { get; init; }
    public string? DefStateCacheUnit { get; init; }
    public void Validate()
    {
        if (DefStateCacheTime < -1) throw new ArgumentException("Select a supported State File cache duration.");
        if (DefStateCacheTime > 0 && DefStateCacheUnit is not ("Minutes" or "Hours")) throw new ArgumentException("Select Minutes or Hours for the State File cache duration.");
        if (DefStateCacheTime <= 0 && DefStateCacheUnit is not null) throw new ArgumentException("A cache unit is only used with a timed State File cache duration.");
    }
    public static SystemStateFileSettings Read(JsonObject source)
    {
        var time = SystemSettingValue.Integer(source, nameof(DefStateCacheTime));
        return new() { DefStateCacheTime = time, DefStateCacheUnit = time > 0 ? SystemSettingValue.Text(source, nameof(DefStateCacheUnit)) : null };
    }
    public void Apply(JsonObject target)
    {
        target[nameof(DefStateCacheTime)] = DefStateCacheTime;
        if (DefStateCacheTime > 0) target[nameof(DefStateCacheUnit)] = DefStateCacheUnit;
        else target.Remove(nameof(DefStateCacheUnit)); // Native sentinel patterns omit the unit.
    }
}

public sealed record SystemTamperSettings
{
    public required bool Enabled { get; init; }
    public static SystemTamperSettings Read(JsonObject source) => new() { Enabled = !SystemSettingValue.Flag(source, "SuppressHashing") };
    public void Apply(JsonObject target) => target["SuppressHashing"] = Enabled ? 0 : 1;
}

public sealed record SystemLockoutPolicy
{
    public required bool IPLockoutEnabled { get; init; }
    public required int IPLockoutTries { get; init; }
    public required int IPLockoutTime { get; init; }
    public required string IPLockoutUnits { get; init; }
    public required bool IPAutoUnlockEnabled { get; init; }
    public required int IPUnlockTime { get; init; }
    public required string IPUnlockUnits { get; init; }
    public required bool UserLockoutEnabled { get; init; }
    public required int UserLockoutTries { get; init; }
    public required int UserLockoutTime { get; init; }
    public required string UserLockoutUnits { get; init; }
    public required bool UserAutoUnlockEnabled { get; init; }
    public required int UserUnlockTime { get; init; }
    public required string UserUnlockUnits { get; init; }

    public void Validate()
    {
        static void Check(string label, bool enabled, int tries, int time, string units, bool autoUnlock, int unlockTime, string unlockUnits)
        {
            if (tries < 0 || time < 0 || unlockTime < 0 || enabled && (tries == 0 || time == 0 || autoUnlock && unlockTime == 0))
                throw new ArgumentException($"Enter positive attempts and durations for enabled {label} settings.");
            if (units is not ("hours" or "minutes" or "days") || unlockUnits is not ("hours" or "minutes" or "days"))
                throw new ArgumentException($"Select Hours, Minutes or Days for {label} settings.");
        }
        Check("IP Lockout", IPLockoutEnabled, IPLockoutTries, IPLockoutTime, IPLockoutUnits, IPAutoUnlockEnabled, IPUnlockTime, IPUnlockUnits);
        Check("User Lockout", UserLockoutEnabled, UserLockoutTries, UserLockoutTime, UserLockoutUnits, UserAutoUnlockEnabled, UserUnlockTime, UserUnlockUnits);
    }
    public static SystemLockoutPolicy Read(JsonObject source) => new()
    {
        IPLockoutEnabled = SystemSettingValue.Flag(source, nameof(IPLockoutEnabled)), IPLockoutTries = SystemSettingValue.Integer(source, nameof(IPLockoutTries)),
        IPLockoutTime = SystemSettingValue.Integer(source, nameof(IPLockoutTime)), IPLockoutUnits = SystemSettingValue.Text(source, nameof(IPLockoutUnits)),
        IPAutoUnlockEnabled = SystemSettingValue.Flag(source, nameof(IPAutoUnlockEnabled)), IPUnlockTime = SystemSettingValue.Integer(source, nameof(IPUnlockTime)), IPUnlockUnits = SystemSettingValue.Text(source, nameof(IPUnlockUnits)),
        UserLockoutEnabled = SystemSettingValue.Flag(source, nameof(UserLockoutEnabled)), UserLockoutTries = SystemSettingValue.Integer(source, nameof(UserLockoutTries)),
        UserLockoutTime = SystemSettingValue.Integer(source, nameof(UserLockoutTime)), UserLockoutUnits = SystemSettingValue.Text(source, nameof(UserLockoutUnits)),
        UserAutoUnlockEnabled = SystemSettingValue.Flag(source, nameof(UserAutoUnlockEnabled)), UserUnlockTime = SystemSettingValue.Integer(source, nameof(UserUnlockTime)), UserUnlockUnits = SystemSettingValue.Text(source, nameof(UserUnlockUnits))
    };
    public JsonObject Apply(JsonObject current)
    {
        var target = current.DeepClone().AsObject();
        target[nameof(IPLockoutEnabled)] = IPLockoutEnabled; target[nameof(IPLockoutTries)] = IPLockoutTries; target[nameof(IPLockoutTime)] = IPLockoutTime; target[nameof(IPLockoutUnits)] = IPLockoutUnits;
        target[nameof(IPAutoUnlockEnabled)] = IPAutoUnlockEnabled; target[nameof(IPUnlockTime)] = IPUnlockTime; target[nameof(IPUnlockUnits)] = IPUnlockUnits;
        target[nameof(UserLockoutEnabled)] = UserLockoutEnabled; target[nameof(UserLockoutTries)] = UserLockoutTries; target[nameof(UserLockoutTime)] = UserLockoutTime; target[nameof(UserLockoutUnits)] = UserLockoutUnits;
        target[nameof(UserAutoUnlockEnabled)] = UserAutoUnlockEnabled; target[nameof(UserUnlockTime)] = UserUnlockTime; target[nameof(UserUnlockUnits)] = UserUnlockUnits;
        return target;
    }
}

public sealed record SystemLockoutState(SystemLockoutPolicy Policy, string EditVersion,
    [property: JsonPropertyName("trustedIps")] IReadOnlyList<string> TrustedIPs, string TrustedEditVersion);

internal static class SystemTrustedIPs
{
    public static string Normalize(string address)
    {
        if (string.IsNullOrWhiteSpace(address) || address.Any(char.IsWhiteSpace) || !IPAddress.TryParse(address, out var ip)
            || ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && address.Count(character => character == '.') != 3)
            throw new ArgumentException("Enter a valid IPv4 or IPv6 address for each Trusted IP Address.");
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && address.Split('.').Any(part =>
            part.Length == 0 || part.Length > 3 || part.Length > 1 && part[0] == '0' || !part.All(char.IsAsciiDigit)))
            throw new ArgumentException("Enter a valid IPv4 or IPv6 address for each Trusted IP Address.");
        return ip.ToString();
    }
    public static IReadOnlyList<string> Validate(IReadOnlyList<string> addresses)
    {
        var normalized = addresses.Select(Normalize).ToArray();
        if (normalized.Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalized.Length)
            throw new ArgumentException("Each Trusted IP Address must be unique.");
        return normalized;
    }
    public static IReadOnlyList<string> Read(JsonObject source)
    {
        if (source["TrustedIP"] is not JsonArray array) throw new InvalidOperationException("MOVEit returned an incomplete Trusted IP Address list.");
        var addresses = array.Select(item => item is JsonObject entry ? SystemSettingValue.Text(entry, "ip")
            : throw new InvalidOperationException("MOVEit returned an invalid Trusted IP Address entry.")).ToArray();
        Validate(addresses);
        return addresses;
    }
    // Address order and equivalent IPv6 spellings do not represent a concurrent change.
    public static JsonObject Definition(JsonObject source) => new()
    {
        ["TrustedIP"] = new JsonArray(Validate(Read(source)).Order(StringComparer.OrdinalIgnoreCase).Select(address => (JsonNode?)JsonValue.Create(address)).ToArray())
    };
    public static bool Same(IReadOnlyList<string> first, IReadOnlyList<string> second)
        => Validate(first).Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(Validate(second).Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
}
public sealed record SystemSettingsUpdate(string EditVersion, SystemLoggingSettings? Logging = null,
    SystemEventLogSettings? EventLog = null, SystemTaskSettings? Tasks = null, SystemStateFileSettings? StateFile = null,
    SystemTamperSettings? Tamper = null, SystemLockoutPolicy? Lockout = null, string? LockoutVersion = null,
    IReadOnlyList<string>? TrustedIPs = null, string? TrustedVersion = null)
{
    public bool HasGlobals => Logging is not null || EventLog is not null || Tasks is not null || StateFile is not null || Tamper is not null;
    public void Validate()
    {
        if (!HasGlobals && Lockout is null && TrustedIPs is null) throw new ArgumentException("There are no MOVEit Settings changes to save.");
        Logging?.Validate(); EventLog?.Validate(); Tasks?.Validate(); StateFile?.Validate(); Lockout?.Validate();
        if (TrustedIPs is not null) SystemTrustedIPs.Validate(TrustedIPs);
    }
    public JsonObject Apply(JsonObject current)
    {
        var target = Logging?.Apply(current) ?? current.DeepClone().AsObject();
        EventLog?.Apply(target); Tasks?.Apply(target); StateFile?.Apply(target); Tamper?.Apply(target); return target;
    }
    public bool Matches(JsonObject saved) => (Logging is null || SystemLoggingSettings.Read(saved) == Logging)
        && (EventLog is null || SystemEventLogSettings.Read(saved) == EventLog) && (Tasks is null || SystemTaskSettings.Read(saved) == Tasks)
        && (StateFile is null || SystemStateFileSettings.Read(saved) == StateFile) && (Tamper is null || SystemTamperSettings.Read(saved) == Tamper);
}

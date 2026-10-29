using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MoveItAutomationUI.Services.MoveIt.SystemSettings;

// Only typed settings projections cross the browser boundary. The full Globals definition
// stays server-side so unknown native properties are neither exposed nor reset.
public sealed record SystemLoggingSettings
{
    public required int DebugLevel { get; init; }
    public required bool LogToDisk { get; init; }
    public required string LogFile { get; init; }
    public required long MaxLogFileSize { get; init; }
    public required bool LogToTaskLog { get; init; }
    public required string TaskLogDir { get; init; }
    // Pin the browser name: default acronym/digit camel-casing produces aS2LogDirectory.
    [JsonPropertyName("as2LogDirectory")]
    public required string AS2LogDirectory { get; init; }
    public required int NumTaskLogsToKeep { get; init; }
    public required bool AuditXferRetries { get; init; }

    public const long MaximumLogBytes = 2147483648L;
    public static readonly int[] DebugLevels = [0, 10, 20, 30, 40, 50, 60, 70];

    public void Validate()
    {
        if (!DebugLevels.Contains(DebugLevel)) throw new ArgumentException("Select a supported System Log Level.");
        if (MaxLogFileSize < 0 || MaxLogFileSize > MaximumLogBytes || LogToDisk && MaxLogFileSize == 0)
            throw new ArgumentException("Maximum System Log File Size must be greater than zero when logging to disk and cannot exceed 2 GB.");
        if (LogFile is null || TaskLogDir is null || AS2LogDirectory is null)
            throw new ArgumentException("Logging text fields are required; optional directories may be blank.");
        if (LogToDisk && string.IsNullOrWhiteSpace(LogFile)) throw new ArgumentException("Enter a System Log File Name.");
        if (NumTaskLogsToKeep < 0) throw new ArgumentException("Default Number of Task Logs Saved cannot be negative.");
        if (new[] { LogFile, TaskLogDir, AS2LogDirectory }.Any(value => value.Any(char.IsControl)))
            throw new ArgumentException("Log file names and directories cannot contain control characters.");
    }

    public static SystemLoggingSettings Read(JsonObject globals)
    {
        long Number(string key)
            => globals.ContainsKey(key) && long.TryParse(globals[key]?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value : throw new InvalidOperationException("MOVEit returned incomplete or invalid Logging settings.");
        int Integer(string key)
        {
            var value = Number(key);
            return value is >= int.MinValue and <= int.MaxValue ? (int)value
                : throw new InvalidOperationException("MOVEit returned an invalid Logging number.");
        }
        bool Flag(string key)
        {
            var value = globals[key]?.ToString();
            return value switch { "0" or "false" => false, "1" or "true" => true,
                _ => throw new InvalidOperationException("MOVEit returned an invalid Logging option.") };
        }
        string Text(string key) => globals.ContainsKey(key) ? globals[key]?.ToString() ?? ""
            : throw new InvalidOperationException("MOVEit returned incomplete Logging settings.");
        return new SystemLoggingSettings
        {
            DebugLevel = Integer(nameof(DebugLevel)), LogToDisk = Flag(nameof(LogToDisk)), LogFile = Text(nameof(LogFile)),
            MaxLogFileSize = Number(nameof(MaxLogFileSize)), LogToTaskLog = Flag(nameof(LogToTaskLog)), TaskLogDir = Text(nameof(TaskLogDir)),
            AS2LogDirectory = Text(nameof(AS2LogDirectory)), NumTaskLogsToKeep = Integer(nameof(NumTaskLogsToKeep)), AuditXferRetries = Flag(nameof(AuditXferRetries))
        };
    }

    public JsonObject Apply(JsonObject current)
    {
        Validate();
        var copy = current.DeepClone().AsObject();
        copy[nameof(DebugLevel)] = DebugLevel; copy[nameof(LogToDisk)] = LogToDisk ? 1 : 0;
        copy[nameof(LogFile)] = LogFile; copy[nameof(MaxLogFileSize)] = MaxLogFileSize;
        copy[nameof(LogToTaskLog)] = LogToTaskLog ? 1 : 0; copy[nameof(TaskLogDir)] = TaskLogDir;
        copy[nameof(AS2LogDirectory)] = AS2LogDirectory; copy[nameof(NumTaskLogsToKeep)] = NumTaskLogsToKeep;
        copy[nameof(AuditXferRetries)] = AuditXferRetries ? 1 : 0;
        return copy;
    }
}

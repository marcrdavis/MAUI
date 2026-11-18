using System.Globalization;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace MoveItAutomationUI.Features.LocalGroups;

// Windows Service wire contract. No service assembly dependency.
public static class LocalGroupsProtocol
{
    public const int Version = 1;
    public const string AgentScheme = "MauiLocalGroupsAgent";
    public const string AgentRoute = "api/local-groups-agent";
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static bool IsMutation(string? operation) => operation is "create" or "describe" or "add-member" or "remove-member" or "delete";
    public static void Validate(LocalGroupRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Operation is not ("list" or "get" or "search" or "resolve") && !IsMutation(request.Operation)) throw new ArgumentException("Unsupported local group operation.");
        if (request.Operation == "create" && (string.IsNullOrWhiteSpace(request.GroupName) || request.GroupName != request.GroupName.Trim()
            || request.GroupName.Length > 256 || request.GroupName.EndsWith('.') || request.GroupName.All(c => c is '.' or ' ')
            || request.GroupName.Any(c => char.IsControl(c) || "\"/\\[]:|<>+=;,?*".Contains(c)))) throw new ArgumentException("Enter a valid Windows local group name (up to 256 characters).");
        if (request.Operation is "create" or "describe" && (request.Description is null || request.Description.Length > 256 || request.Description.Contains('\0')))
            throw new ArgumentException("Group descriptions cannot exceed 256 characters or contain a null character.");
        if (request.Operation == "get" || IsMutation(request.Operation) && request.Operation != "create")
        {
            if (!IsCustomSid(request.GroupSid)) throw new ArgumentException("Select a custom local group. Windows default/reserved groups cannot be managed.");
            if (IsMutation(request.Operation) && (string.IsNullOrWhiteSpace(request.GroupName) || !Hex(request.ExpectedVersion))) throw new ArgumentException("Read the current group before saving changes.");
        }
        if (request.Operation is "resolve" or "add-member" && (string.IsNullOrWhiteSpace(request.Principal) || request.Principal.Length > 256 || request.Principal.Any(char.IsControl)))
            throw new ArgumentException("Select or enter a qualified local or domain user or security group.");
        if (request.Operation == "remove-member" && !IsSid(request.MemberSid)) throw new ArgumentException("Select an existing member to remove.");
        if (request.Operation == "search" && (string.IsNullOrWhiteSpace(request.Query) || request.Query.Trim().Length < 2 || request.Query.Length > 128)) throw new ArgumentException("Enter 2 to 128 characters to search for users or groups.");
        if (request.Operation == "delete" && request.ConfirmDelete != request.GroupName) throw new ArgumentException("Confirm deletion using the exact current group name.");
    }
    public static bool IsComputerName(string? value) => value is not null && Regex.IsMatch(value, @"\A[A-Z0-9][A-Z0-9_-]{0,14}\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    public static bool Hex(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    public static bool IsSid(string? value)
    {
        var parts = value?.Split('-');
        return parts is { Length: >= 4 and <= 18 } && parts[0] == "S" && parts[1] == "1"
            && ulong.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var authority) && authority <= 0xffffffffffff && parts.Skip(3).All(p => uint.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out _));
    }
    public static bool IsCustomSid(string? value)
    {
        var parts = value?.Split('-');
        return IsSid(value) && parts is { Length: 8 } && parts[2] == "5" && parts[3] == "21" && uint.Parse(parts[7]) >= 1000;
    }
    public static void ValidateResult(AgentResult result, string computer)
    {
        if (result.ProtocolVersion != Version || !IsComputerName(result.ComputerName) || !computer.Equals(result.ComputerName, StringComparison.OrdinalIgnoreCase)
            || (!Guid.TryParseExact(result.Id, "D", out var id) || id == Guid.Empty) || result.Message is null || result.Message.Length > 8192)
            throw new ArgumentException("The service result does not match its registered computer or protocol.");
        if (result.Groups?.Count > 10000 || result.Principals?.Count > 25) throw new ArgumentException("The service result contains too many objects.");
        foreach (var group in result.Groups ?? [])
        {
            if (group is null || !IsSid(group.Sid) || string.IsNullOrWhiteSpace(group.Name) || group.Description is null || group.Members is null
                || group.Members.Any(m => m is null || !IsSid(m.Sid))) throw new ArgumentException("MOVEit User Management returned incomplete local group details.");
        }
        if (result.Principals?.Any(p => p is null || !IsSid(p.Sid) || string.IsNullOrWhiteSpace(p.Account)) == true) throw new ArgumentException("The service returned incomplete user or group identities.");
    }
}

public class LocalGroupRequest
{
    public string Operation { get; set; } = "list";
    public string GroupName { get; set; } = "";
    public string GroupSid { get; set; } = "";
    public string ExpectedVersion { get; set; } = "";
    public string Description { get; set; } = "";
    public string Principal { get; set; } = "";
    public string MemberSid { get; set; } = "";
    public string Query { get; set; } = "";
    public string ConfirmDelete { get; set; } = "";
}
public sealed class AgentCommand : LocalGroupRequest
{
    public int ProtocolVersion { get; set; } = LocalGroupsProtocol.Version;
    public string Id { get; set; } = "";
    public string ComputerName { get; set; } = "";
    public string RequestedBy { get; set; } = "";
    public DateTimeOffset ExpiresUtc { get; set; }
}
public sealed record AgentPoll(int ProtocolVersion, string ComputerName);
public sealed class AgentResult
{
    public int ProtocolVersion { get; set; }
    public string ComputerName { get; set; } = "";
    public string Id { get; set; } = "";
    public bool Success { get; set; }
    public bool OutcomeUncertain { get; set; }
    public string Message { get; set; } = "";
    public List<LocalGroupSnapshot>? Groups { get; set; }
    public List<DomainPrincipal>? Principals { get; set; }
}
public sealed class LocalGroupSnapshot
{
    public string Sid { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Protected { get; set; }
    public List<DomainPrincipal> Members { get; set; } = [];
    public string Version { get; set; } = "";
}
public sealed class DomainPrincipal
{
    public string Sid { get; set; } = "";
    public string? Account { get; set; }
    public string? Type { get; set; }
    public string? DisplayName { get; set; }
}

using System.Security.Claims;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.Security;

namespace MoveItAutomationUI.Features.LocalGroups;

public sealed class LocalGroupsOperationException(string message, bool uncertain, bool retryable = false) : InvalidOperationException(message)
{
    public bool OutcomeUncertain { get; } = uncertain;
    public bool Retryable { get; } = retryable;
}

public sealed record ManagedLocalGroup(LocalGroupSnapshot Group, IReadOnlyList<string> ResourceGroups);
public sealed record LocalGroupsInventory(string ComputerName, IReadOnlyList<ManagedLocalGroup> Groups);

public sealed class LocalGroupsService(LocalGroupsConfiguration configuration, LocalGroupsBroker broker,
    IMoveItSessionService sessions, IMoveItRestClient rest, IMauiSecurityService security,
    IServiceScopeFactory scopes, ILogger<LocalGroupsService> logger)
{
    public object State()
    {
        var state = sessions.Get() ?? throw new MoveItAuthenticationException("Your MOVEit session expired. Sign in again.");
        var options = configuration.Read();
        if (!options.Enabled.Value) return new { enabled = false, registered = false, computerName = "", server = state.ServerDisplayName, message = "Enable MOVEit User Management under Experimental and Save." };
        try
        {
            var agent = configuration.RequireSessionAgent(state);
            var connection = broker.Connection(agent);
            return new { enabled = true, registered = true, computerName = connection?.ComputerName ?? "", server = state.ServerDisplayName, lastSeenUtc = connection?.LastSeenUtc, message = "" };
        }
        catch (InvalidOperationException ex) { return new { enabled = true, registered = false, computerName = "", server = state.ServerDisplayName, message = ex.Message }; }
    }

    public async Task<object> ExecuteAsync(LocalGroupRequest request, ClaimsPrincipal principal, CancellationToken ct)
    {
        LocalGroupsProtocol.Validate(request);
        var state = sessions.Get() ?? throw new MoveItAuthenticationException("Your MOVEit session expired. Sign in again.");
        var agent = configuration.RequireSessionAgent(state);
        await AuthorizeAsync(principal, state, ct);
        var computerName = broker.Connection(agent)?.ComputerName ?? throw new LocalGroupsOperationException(
            "The Windows Service has not connected. Set its broker URL and client certificate, then start the service.",
            uncertain: false, retryable: request.Operation == "list");
        var deleting = request.Operation == "delete";
        if (deleting) await ResourceGroupManagementService.UpdateGate.WaitAsync(ct);
        try
        {
            var result = await broker.ExecuteAsync(agent, computerName, state.Username, request, async token =>
            {
                // Polls carry no human cookie or MOVEit credential. Revalidate the live browser principal,
                // then use the existing isolated background-session facility for the dispatch-time checks.
                using var scope = scopes.CreateScope();
                var currentSessions = scope.ServiceProvider.GetRequiredService<IMoveItSessionService>();
                if (!currentSessions.HasLiveBrowserSession(principal)) throw new MoveItAuthenticationException("Your MOVEit session expired. Sign in again.");
                var currentAgent = configuration.RequireSessionAgent(state);
                if (currentAgent.CertificateSha256 != agent.CertificateSha256 || currentAgent.ConnectionBinding != agent.ConnectionBinding)
                    throw new InvalidOperationException("The service registration changed. Refresh before retrying.");
                using var background = currentSessions.PushBackgroundSession(state);
                var currentRest = scope.ServiceProvider.GetRequiredService<IMoveItRestClient>();
                var currentSecurity = scope.ServiceProvider.GetRequiredService<IMauiSecurityService>();
                if (!currentSecurity.IsAdmin(state.Username) || !(await currentRest.GetInfoAsync(state, token)).IsAdmin)
                    throw new UnauthorizedAccessException("MOVEit and MAUI administrator access is required.");
                if (deleting)
                {
                    var local = await currentRest.GetUserGroupDefinitionsAsync("local", token);
                    if (local.Count(group => string.Equals(group["Name"]?.ToString(), request.GroupName, StringComparison.OrdinalIgnoreCase)) > 1)
                        throw new InvalidOperationException("MOVEit could not uniquely identify this local group. Refresh before deleting it.");
                    RequireUnassigned(await currentRest.GetResourceGroupAclDefinitionsAsync(token), request.GroupName, computerName);
                }
            }, ct);
            if (LocalGroupsProtocol.IsMutation(request.Operation))
                logger.Log(result.Success ? LogLevel.Information : LogLevel.Warning,
                    "MOVEit User Management {OperationId}: {Operation} on {ServerKey}, group {GroupSid}/{GroupName}, requested by {Username}; success={Success}, uncertain={Uncertain}. {Details}",
                    result.Id, request.Operation, state.ServerKey, request.GroupSid, request.GroupName, state.Username, result.Success, result.OutcomeUncertain, result.Message);
            if (!result.Success) throw OperationFailure(request.Operation, result);
            // Cached mutation receipts can omit snapshots. The UI reads the group again after saving.
            if (request.Operation == "get" && (result.Groups is not { Count: 1 } || result.Groups[0].Sid != request.GroupSid || !LocalGroupsProtocol.Hex(result.Groups[0].Version)))
                throw new InvalidOperationException("The service did not return the selected group's complete current snapshot.");
            if (request.Operation is "search" or "resolve" && result.Principals is null) throw new InvalidOperationException("The service did not return user/group results.");
            if (request.Operation == "list")
            {
                var groups = result.Groups ?? throw new InvalidOperationException("The service did not return the local groups inventory.");
                var acls = await rest.GetResourceGroupAclDefinitionsAsync(ct);
                return new LocalGroupsInventory(computerName, groups.Where(group => !group.Protected).Select(group =>
                    new ManagedLocalGroup(group, AssignedGroups(acls, group.Name, computerName))).ToArray());
            }
            return result;
        }
        finally { if (deleting) ResourceGroupManagementService.UpdateGate.Release(); }
    }

    internal static LocalGroupsOperationException OperationFailure(string operation, AgentResult result)
    {
        // Protocol 1 agents through 1.0.3 reported this exact resolver failure.
        var message = result.Message;
        if ((operation is "resolve" or "add-member") && message == "Only domain users and domain security groups can be added. Local, built-in and well-known identities are not accepted.")
            message = $"Update the Windows Service on {result.ComputerName} to MAUI Local Groups Service 1.0.4 or later and restart it to manage local accounts.";
        if (message.Length == 0)
            message = "The MOVEit User Management operation failed. See the Automation server service log.";
        return new LocalGroupsOperationException(message, result.OutcomeUncertain);
    }

    private async Task AuthorizeAsync(ClaimsPrincipal principal, MoveItSessionState state, CancellationToken ct)
    {
        if (!sessions.HasLiveBrowserSession(principal) || !security.IsAdmin(state.Username) || !(await rest.GetInfoAsync(state, ct)).IsAdmin)
            throw new UnauthorizedAccessException("MOVEit and MAUI administrator access is required.");
    }

    internal static void RequireUnassigned(IReadOnlyList<System.Text.Json.Nodes.JsonObject> acls, string name, string computer)
    {
        var assignments = AssignedGroups(acls, name, computer);
        if (assignments.Count > 0) throw new InvalidOperationException("This local group is assigned to MOVEit Resource Groups: " + string.Join(", ", assignments) + ". Remove those assignments before deleting it.");
        // MOVEit ACLs carry group names, not Windows SIDs. Unknown authorities with the same
        // leaf name could be a machine alias; they cannot be treated as proof of no reference.
        var ambiguous = acls.Select(acl => acl["UserGroupName"]!.ToString())
            .FirstOrDefault(value => value.Contains('\\') && value[(value.LastIndexOf('\\') + 1)..].Equals(name, StringComparison.OrdinalIgnoreCase));
        if (ambiguous is not null) throw new InvalidOperationException("MOVEit references '" + ambiguous + "'. MAUI cannot confirm this qualified name identifies a different Windows group. Resolve that assignment before deleting the local group.");
    }

    internal static IReadOnlyList<string> AssignedGroups(IReadOnlyList<System.Text.Json.Nodes.JsonObject> acls, string name, string computer)
    {
        var assignments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var acl in acls)
        {
            var userGroup = acl["UserGroupName"]?.ToString();
            var resourceGroup = acl["ResourceGroupName"]?.ToString();
            if (string.IsNullOrWhiteSpace(userGroup) || string.IsNullOrWhiteSpace(resourceGroup))
                throw new InvalidOperationException("MOVEit returned incomplete Resource Group assignments. Refresh before making changes.");
            if (userGroup.Equals(name, StringComparison.OrdinalIgnoreCase)
                || userGroup.Equals(computer + "\\" + name, StringComparison.OrdinalIgnoreCase)
                || userGroup.Equals(".\\" + name, StringComparison.OrdinalIgnoreCase)) assignments.Add(resourceGroup);
        }
        return assignments.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}

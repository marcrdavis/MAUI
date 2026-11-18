using System.Collections.Concurrent;
using System.Text.Json;

namespace MoveItAutomationUI.Features.LocalGroups;

public sealed record LocalGroupsConnection(string ComputerName, DateTimeOffset LastSeenUtc);

public sealed class LocalGroupsBroker
{
    private readonly TimeProvider _clock;
    private readonly LocalGroupsLedger _ledger;
    private readonly ILogger<LocalGroupsBroker> _logger;
    private readonly object _gate = new();
    private readonly Dictionary<string, Work> _work = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _pollers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, LocalGroupsConnection> _seen = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan InventoryTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(40);
    private sealed class Work(LocalGroupsReceipt receipt, Func<CancellationToken, Task>? authorize)
    {
        public LocalGroupsReceipt Receipt { get; } = receipt;
        public Func<CancellationToken, Task>? Authorize { get; set; } = authorize;
        public TaskCompletionSource<AgentResult>? Completion { get; set; } = receipt.Result is null ? new(TaskCreationOptions.RunContinuationsAsynchronously) : null;
        public bool Delivered { get; set; } = authorize is null;
    }
    public LocalGroupsBroker(TimeProvider clock, LocalGroupsLedger ledger, ILogger<LocalGroupsBroker> logger)
    {
        _clock = clock; _ledger = ledger; _logger = logger;
        foreach (var receipt in ledger.Read(clock.GetUtcNow()))
        {
            // An unfinished inventory read has no browser waiting for it after a restart.
            if (receipt.Result is null && receipt.Command.Operation == "list") ledger.Remove(receipt.Command.Id);
            else _work.Add(receipt.Command.Id, new Work(receipt, null));
        }
    }
    private static bool Matches(LocalGroupsReceipt receipt, LocalGroupsAgent agent) => receipt.ServerKey.Equals(agent.ServerKey, StringComparison.OrdinalIgnoreCase)
        && receipt.Certificate.Equals(agent.CertificateSha256, StringComparison.OrdinalIgnoreCase) && receipt.Binding == agent.ConnectionBinding;
    private static string ConnectionKey(LocalGroupsAgent agent) => string.Join("\0", agent.ServerKey, agent.CertificateSha256, agent.ConnectionBinding);
    public LocalGroupsConnection? Connection(LocalGroupsAgent agent) => _seen.TryGetValue(ConnectionKey(agent), out var seen) ? seen : null;
    public async Task<AgentResult> ExecuteAsync(LocalGroupsAgent agent, string computerName, string actor, LocalGroupRequest request, Func<CancellationToken, Task> authorize, CancellationToken ct)
    {
        LocalGroupsProtocol.Validate(request);
        var inventory = request.Operation == "list";
        var mutation = LocalGroupsProtocol.IsMutation(request.Operation);
        if (!LocalGroupsProtocol.IsComputerName(computerName)) throw new ArgumentException("The service must report its Windows computer name before a command can be issued.");
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 256 || actor.Any(char.IsControl)) throw new ArgumentException("A valid authenticated administrator is required.");
        var command = JsonSerializer.Deserialize<AgentCommand>(JsonSerializer.Serialize(request, LocalGroupsProtocol.Json), LocalGroupsProtocol.Json)!;
        command.Id = Guid.NewGuid().ToString("D"); command.ProtocolVersion = LocalGroupsProtocol.Version; command.ComputerName = computerName;
        command.RequestedBy = actor; command.ExpiresUtc = _clock.GetUtcNow().AddSeconds(35);
        var work = new Work(new LocalGroupsReceipt { ServerKey = agent.ServerKey, Certificate = agent.CertificateSha256, Binding = agent.ConnectionBinding, Command = command }, authorize);
        var awaiting = work.Completion!.Task;
        lock (_gate)
        {
            Prune();
            if (Connection(agent) is { } connected && !connected.ComputerName.Equals(computerName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The connected Windows computer changed. Refresh the local groups before continuing.");
            if (_work.Values.Count(w => w.Receipt.Result is null) >= 128 || _work.Values.Any(w => Matches(w.Receipt, agent) && w.Receipt.Result is null))
            {
                const string message = "The service is completing another request. Wait briefly, then refresh.";
                if (inventory) throw new LocalGroupsOperationException(message, uncertain: false, retryable: true);
                throw new InvalidOperationException(message);
            }
            _work.Add(command.Id, work);
        }
        try { return await awaiting.WaitAsync(inventory ? InventoryTimeout : OperationTimeout, mutation ? CancellationToken.None : ct); }
        catch (TimeoutException)
        {
            lock (_gate) throw new LocalGroupsOperationException(work.Delivered
                ? "The operation result could not be confirmed. Refresh the local group before another change. Check the Automation server service log."
                : $"The Windows Service did not respond. Verify it is installed and running on {computerName} with outbound HTTPS access to MAUI.",
                uncertain: work.Delivered && mutation, retryable: inventory);
        }
        finally
        {
            lock (_gate)
            {
                if (!work.Delivered) _work.Remove(command.Id);
                else if (inventory && work.Receipt.Result is null)
                {
                    // Retire abandoned reads so an unanswered request cannot block a fresh list.
                    _ledger.Remove(command.Id);
                    _work.Remove(command.Id);
                }
                // Retain only the issued command/compact outcome after the browser request ends.
                work.Authorize = null;
                work.Completion = null;
            }
        }
    }
    public async Task<AgentCommand?> PollAsync(LocalGroupsAgent agent, string computerName, CancellationToken ct)
    {
        if (!LocalGroupsProtocol.IsComputerName(computerName)) throw new ArgumentException("The service reported an invalid Windows computer name.");
        var poller = _pollers.GetOrAdd(agent.CertificateSha256, _ => new SemaphoreSlim(1, 1));
        if (!await poller.WaitAsync(0, ct)) throw new InvalidOperationException("Another poll is active for this service certificate.");
        try
        {
            lock (_gate)
            {
                Prune();
                var pending = _work.Values.FirstOrDefault(w => Matches(w.Receipt, agent) && w.Receipt.Result is null);
                if (pending is not null && !pending.Receipt.Command.ComputerName.Equals(computerName, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Another Windows computer has an outstanding command for this certificate. Use a distinct certificate for each Automation server.");
                _seen[ConnectionKey(agent)] = new(computerName, _clock.GetUtcNow());
            }
            for (var attempt = 0; attempt < 50; attempt++)
            {
                Work? work;
                lock (_gate) { Prune(); work = _work.Values.FirstOrDefault(w => Matches(w.Receipt, agent) && w.Receipt.Result is null); }
                if (work is not null)
                {
                    var expired = work.Receipt.Command.ExpiresUtc <= _clock.GetUtcNow();
                    var authorize = work.Authorize;
                    // After recycle there is no live browser authorization. Wait until expiry;
                    // an expired command can only reconcile a service receipt, never make a new write.
                    if (authorize is null && !expired) { await Task.Delay(300, ct); continue; }
                    try
                    {
                        if (!expired) await authorize!(ct);
                        lock (_gate)
                        {
                            if (!_work.ContainsKey(work.Receipt.Command.Id) || work.Receipt.Result is not null) continue;
                            if (!work.Receipt.Command.ComputerName.Equals(computerName, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The service computer changed while a command was pending.");
                            if (!work.Delivered && work.Receipt.Command.ExpiresUtc <= _clock.GetUtcNow()) continue;
                            _ledger.Save(work.Receipt); // Persist before any command leaves MAUI.
                            work.Delivered = true;
                            return work.Receipt.Command;
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        lock (_gate)
                        {
                            if (!work.Delivered) _work.Remove(work.Receipt.Command.Id);
                            else if (work.Receipt.Command.ExpiresUtc <= _clock.GetUtcNow()) throw;
                        }
                        work.Completion?.TrySetException(work.Delivered && LocalGroupsProtocol.IsMutation(work.Receipt.Command.Operation)
                            ? new LocalGroupsOperationException(ex.Message + " The command was already issued; refresh Windows state before retrying.", true) : ex);
                    }
                }
                await Task.Delay(300, ct);
            }
            return null;
        }
        finally { poller.Release(); }
    }
    public void Complete(LocalGroupsAgent agent, AgentResult result)
    {
        LocalGroupsProtocol.ValidateResult(result, result.ComputerName);
        lock (_gate)
        {
            Prune();
            if (!_work.TryGetValue(result.Id, out var work))
            {
                // An old result after data retention/recovery must not strand the service's
                // result-delivery loop. Record it without satisfying an unrelated UI request.
                _logger.LogWarning("Uncorrelated local group result {Id} from {Server}; success={Success}, uncertain={Uncertain}. Refresh Windows state before further changes.", result.Id, agent.ServerKey, result.Success, result.OutcomeUncertain);
                return;
            }
            if (!work.Delivered || !Matches(work.Receipt, agent) || !work.Receipt.Command.ComputerName.Equals(result.ComputerName, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("The result does not identify an issued command for this service.");
            if (work.Receipt.Result is { } prior)
            {
                if (prior.Success != result.Success || prior.OutcomeUncertain != result.OutcomeUncertain || prior.Message != result.Message) throw new ArgumentException("The result changed for an already completed command.");
                return;
            }
            _seen[ConnectionKey(agent)] = new(result.ComputerName, _clock.GetUtcNow());
            var compact = new AgentResult { ProtocolVersion = result.ProtocolVersion, ComputerName = result.ComputerName, Id = result.Id, Success = result.Success, OutcomeUncertain = result.OutcomeUncertain, Message = result.Message };
            var receipt = new LocalGroupsReceipt { ServerKey = work.Receipt.ServerKey, Certificate = work.Receipt.Certificate, Binding = work.Receipt.Binding, Command = work.Receipt.Command, Result = compact };
            _ledger.Save(receipt); // A lost HTTP acknowledgement can safely retry this same result.
            work.Receipt.Result = compact;
            var completion = work.Completion;
            work.Authorize = null;
            work.Completion = null;
            completion?.TrySetResult(result);
        }
    }
    private void Prune()
    {
        foreach (var id in _work.Where(p => p.Value.Receipt.Command.ExpiresUtc < _clock.GetUtcNow().AddDays(-2)).Select(p => p.Key).ToArray())
        {
            _ledger.Remove(id);
            _work.Remove(id);
        }
    }
}

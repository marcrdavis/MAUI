using System.Text.Json;

namespace MoveItAutomationUI.Features.LocalGroups;

// Only issued-command metadata and compact results are durable; browser credentials are never stored.
public sealed class LocalGroupsLedger
{
    private readonly string _directory;
    private readonly object _gate = new();
    public LocalGroupsLedger(IWebHostEnvironment environment) : this(Path.Combine(environment.ContentRootPath, "Data", "LocalGroupsBroker")) { }
    internal LocalGroupsLedger(string directory) => _directory = directory;
    public IReadOnlyList<LocalGroupsReceipt> Read(DateTimeOffset now)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(_directory);
            var result = new List<LocalGroupsReceipt>();
            foreach (var path in Directory.EnumerateFiles(_directory, "*.json"))
            {
                if (File.GetLastWriteTimeUtc(path) < now.UtcDateTime.AddDays(-2)) { File.Delete(path); continue; }
                if (new FileInfo(path).Length > 128 * 1024) throw new InvalidOperationException("A local group operation receipt is invalid. Review Data/LocalGroupsBroker before continuing.");
                var receipt = JsonSerializer.Deserialize<LocalGroupsReceipt>(File.ReadAllText(path), LocalGroupsProtocol.Json)
                    ?? throw new InvalidOperationException("A local group operation receipt could not be read.");
                if (!Guid.TryParseExact(receipt.Command.Id, "D", out _) || !LocalGroupsProtocol.Hex(receipt.Certificate)) throw new InvalidOperationException("A local group operation receipt is incomplete.");
                result.Add(receipt);
            }
            return result;
        }
    }
    internal void Remove(string id)
    {
        lock (_gate) File.Delete(Path.Combine(_directory, Guid.ParseExact(id, "D").ToString("N") + ".json"));
    }
    public void Save(LocalGroupsReceipt receipt)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(_directory);
            var path = Path.Combine(_directory, Guid.ParseExact(receipt.Command.Id, "D").ToString("N") + ".json");
            var temporary = path + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, receipt, LocalGroupsProtocol.Json); stream.Flush(true); }
            File.Move(temporary, path, overwrite: true);
        }
    }
}
public sealed class LocalGroupsReceipt
{
    public string ServerKey { get; set; } = "";
    public string Certificate { get; set; } = "";
    public string Binding { get; set; } = "";
    public AgentCommand Command { get; set; } = new();
    public AgentResult? Result { get; set; }
}

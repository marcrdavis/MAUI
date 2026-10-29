using System.Text.Json.Nodes;
using System.Xml.Linq;
using MoveItAutomationUI.Services.MoveIt.Tasks.AdvancedTasks;
using static MoveItAutomationUI.Services.MoveIt.TraditionalTasks.TraditionalTaskDefinitionEditorOperations;

namespace MoveItAutomationUI.Services.MoveIt.Tasks;

public sealed partial class TaskCrudCoordinator
{
    public async Task<TaskCreationResult> CreateAdvancedFromTraditionalAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var id = RequireId(taskId);
        if (_access is not null) await _access.RequireAsync("Task", id, "Edit", cancellationToken);
        TaskCreationResult? created = null;
        var creationAttempted = false;
        try
        {
            // Conversion reads the source and creates a different Task. Borrow an open
            // editor's checkout without treating the source as a PUT/releasing its lease.
            return await ObjectCheckout.ReadAsync("Task", id,
                ct => _rest.CheckoutTaskV0Async(id, ct),
                async ct =>
                {
                    var source = await GetDefinitionAsync(id, ct);
                    if (GetString(source, "ID") != id) throw new InvalidOperationException("MOVEit returned a different source Task.");
                    if (!ObjectCheckout.CanEditCheckedOut("Task", id, source["xAlter"] is null || GetBool(source, "xAlter")))
                        throw new InvalidOperationException("You do not have permission to convert this Task.");
                    var sourceName = GetString(source, "Name").Trim();
                    if (sourceName.Length == 0) throw new InvalidOperationException("The source Task has no name.");
                    var name = await UniqueAdvancedNameAsync(sourceName, id, ct);
                    var conversion = TraditionalTaskAdvancedConversion.Build(source, name);
                    var native = await GetNativeConfigurationAsync(id, ct);
                    var xml = conversion.ToNativeXml(native.Xml);
                    if (_access is not null) await _access.RequireXmlCreationAsync("Task", XElement.Parse(xml), ct);
                    ct.ThrowIfCancellationRequested();
                    creationAttempted = true;
                    var saved = await CreateFromNativeConfigurationAsync(name, xml, leaveDisabled: true, cancellationToken: ct);
                    created = new TaskCreationResult(saved.Id, saved.Name, "Advanced");
                    // The native creation path verifies identity. Verify the converted definition
                    // as well, without weakening the existing shared Advanced readback contract.
                    var persisted = await GetDefinitionAsync(saved.Id, ct);
                    if (GetString(persisted, "ID") != saved.Id || GetString(persisted, "Name") != name
                        || GetString(persisted, "TT") != "COS" || GetBool(persisted, "Active"))
                        throw new InvalidOperationException("The new Advanced Task identity/type/disabled state could not be verified.");
                    AdvancedTaskDefinition.Verify(conversion.Definition, persisted, new JsonObject { ["steps"] = new JsonArray() });
                    VerifyCopiedConfiguration(xml, saved.Xml);
                    return created;
                },
                ct => _rest.ReleaseTaskCheckoutV0Async(id, ct), cancellationToken);
        }
        catch (Exception ex) when (creationAttempted)
        {
            // A create may have committed even if its response/readback/checkout cleanup failed.
            // Preserve a known ID and never replay or delete a possibly valid created Task.
            throw new TaskAdvancedConversionOutcomeException(created?.Id, created?.Name, ex);
        }
    }

    private async Task<string> UniqueAdvancedNameAsync(string sourceName, string sourceId, CancellationToken ct)
    {
        for (var number = 1; ; number++)
        {
            ct.ThrowIfCancellationRequested();
            var suffix = number == 1 ? " Advanced" : $" Advanced ({number})";
            var prefix = sourceName[..Math.Min(sourceName.Length, 254 - suffix.Length)].TrimEnd();
            if (prefix.Length > 0 && char.IsHighSurrogate(prefix[^1])) prefix = prefix[..^1];
            var candidate = prefix + suffix;
            if (!candidate.Equals(sourceName, StringComparison.OrdinalIgnoreCase)
                && !await _rest.TaskNameExistsForOtherTaskAsync(candidate, sourceId, ct)) return candidate;
        }
    }

    private static void VerifyCopiedConfiguration(string proposedXml, string savedXml)
    {
        var expected = XElement.Parse(proposedXml); var actual = XElement.Parse(savedXml);
        foreach (var name in new[] { "Info", "Parameters", "Schedules", "Group" })
        {
            // Native XML is used here because REST suppresses secure values and can project
            // paired schedules. Compare native subtrees without attribute-order/whitespace noise.
            var left = expected.Elements().Where(element => element.Name.LocalName == name).ToList();
            var right = actual.Elements().Where(element => element.Name.LocalName == name).ToList();
            if (left.Count != right.Count || left.Where((element, index) => !Equivalent(element, right[index])).Any())
                throw new InvalidOperationException($"The converted Task {name} could not be verified.");
        }
        foreach (var step in expected.Descendants().Where(element => element.Name.LocalName is "Source" or "Process" or "Destination" or "Email" or "RunTask"))
        {
            var match = actual.Descendants().FirstOrDefault(element => element.Name.LocalName == step.Name.LocalName
                && (string?)element.Attribute("ID") == (string?)step.Attribute("ID"));
            if (match is null || !Equivalent(step, match)) throw new InvalidOperationException("A converted step's native configuration could not be verified.");
        }
        static bool Equivalent(XElement left, XElement right, bool secured = false)
        {
            if (left.Name.LocalName != right.Name.LocalName) return false;
            secured |= (string?)left.Attribute("Secure") is "1" or "true";
            foreach (var attribute in left.Attributes())
            {
                var key = attribute.Name.LocalName;
                if (key is "ID" or "DisplayValue") continue;
                var actualAttribute = right.Attribute(attribute.Name);
                if (actualAttribute is null && (key is "Disabled" or "Secure" or "IsDest") && attribute.Value == "0") continue;
                if (actualAttribute is null && key.StartsWith("UseDef", StringComparison.Ordinal) && attribute.Value == "1") continue;
                var value = (string?)actualAttribute ?? "";
                var secret = key.Contains("Password", StringComparison.OrdinalIgnoreCase) || key.Contains("Secret", StringComparison.OrdinalIgnoreCase)
                    || key == "Account" || key == "value" && secured;
                if (secret && !secured && (string?)left.Attribute("UseDefUser") != "0") continue;
                if (secret ? attribute.Value.Length > 0 && value.Length == 0 : attribute.Value != value) return false;
            }
            var children = left.Elements().ToList(); var actualChildren = right.Elements().ToList();
            if (children.Count != actualChildren.Count) return false;
            if (children.Count == 0) return secured ? left.Value.Length == 0 || right.Value.Length > 0 : left.Value.Replace("\r\n", "\n") == right.Value.Replace("\r\n", "\n");
            return !children.Where((child, index) => !Equivalent(child, actualChildren[index], secured)).Any();
        }
    }
}

public sealed class TaskAdvancedConversionOutcomeException : InvalidOperationException
{
    public string? TaskId { get; }
    public string? TaskName { get; }
    public TaskAdvancedConversionOutcomeException(string? id, string? name, Exception inner)
        : base(string.IsNullOrWhiteSpace(id)
            ? "MAUI could not confirm whether the Advanced Task was created. Inspect Tasks before creating another copy."
            : $"The Advanced Task '{name}' (ID {id}) was created, but its saved state or source checkout cleanup could not be confirmed. Inspect that Task before creating another copy.", inner)
    { TaskId = id; TaskName = name; }
}

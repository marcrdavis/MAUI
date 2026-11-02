using System.Xml.Linq;

namespace MoveItAutomationUI.Services.MoveIt.Hosts;

// Compose only Name on the authoritative native checkout for retained COM Host types.
public static class HostRenameDefinition
{
    public static void Apply(XElement root, string name, string originalName)
    {
        if ((string?)root.Attribute("ID") == "0" || string.Equals((string?)root.Attribute("Type"), "FileSystem", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The built-in Local File System Host cannot be renamed.");
        if (!string.Equals(((string?)root.Attribute("Name") ?? "").Trim(), (originalName ?? "").Trim(), StringComparison.Ordinal))
            throw new ObjectEditConflictException("Host");
        root.SetAttributeValue("Name", name);
    }
}

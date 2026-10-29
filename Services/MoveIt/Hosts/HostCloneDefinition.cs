using System.Xml.Linq;

namespace MoveItAutomationUI.Services.MoveIt.Hosts;

// Clone within the same MOVEit environment: retain encrypted credentials, references and unknown settings.
internal static class HostCloneDefinition
{
    public static XElement Read(string xml, string? expectedId = null)
    {
        var root = XElement.Parse(xml, LoadOptions.PreserveWhitespace);
        var id = ((string?)root.Attribute("ID") ?? "").Trim();
        if (id.Length == 0 || id == "0" || root.Name.LocalName.Equals("FileSystem", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The Local File System Host cannot be cloned.");
        if (expectedId is not null && id != expectedId)
            throw new InvalidOperationException("The Host snapshot contains a different Host ID.");
        return root;
    }

    public static string Compose(XElement source, string id, string name)
    {
        if (string.IsNullOrWhiteSpace(id) || id == "0" || id == (string?)source.Attribute("ID"))
            throw new InvalidOperationException("MOVEit did not provide a new Host ID.");
        var clone = new XElement(source);
        clone.SetAttributeValue("ID", id);
        clone.SetAttributeValue("Name", name);
        return clone.ToString(SaveOptions.DisableFormatting);
    }
}

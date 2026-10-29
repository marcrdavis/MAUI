// Project: MAUI - MOVEit Automation UI
// Read-only presentation of native audit snapshots. Never persist, decrypt, or log their contents.
using System.Text;
using System.Xml;
using System.Xml.Linq;
using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Features.AuditChanges;

public static class AuditChangeComparer
{
    private const int MaxCharacters = 1_048_576;
    private const int MaxLines = 20_000;
    private const long MaxComparisonCells = 4_000_000;

    public static AuditChangeComparison Compare(AuditChangeRecord record, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var before = Format(record.PreChange, out var beforePlain);
        var after = Format(record.PostChange, out var afterPlain);
        if (beforePlain || afterPlain) { before = Split(record.PreChange); after = Split(record.PostChange); }
        var lines = new List<AuditChangeLine>();
        var added = 0; var removed = 0; var grouped = false;
        void Add(string kind, int? oldIndex, int? newIndex)
        {
            lines.Add(new(kind, oldIndex + 1, newIndex + 1, oldIndex.HasValue ? before[oldIndex.Value] : after[newIndex!.Value]));
            if (kind == "added") added++;
            if (kind == "removed") removed++;
        }

        // Strip common ends first: ordinary edits in a large Task need only a tiny middle comparison.
        var start = 0;
        while (start < before.Length && start < after.Length && before[start] == after[start])
        { cancellationToken.ThrowIfCancellationRequested(); Add("same", start, start); start++; }
        var oldEnd = before.Length; var newEnd = after.Length;
        while (oldEnd > start && newEnd > start && before[oldEnd - 1] == after[newEnd - 1])
        { cancellationToken.ThrowIfCancellationRequested(); oldEnd--; newEnd--; }
        var oldCount = oldEnd - start; var newCount = newEnd - start;
        if ((long)(oldCount + 1) * (newCount + 1) <= MaxComparisonCells)
        {
            // Bounded longest-common-subsequence table. Compare ordinal text after XML formatting,
            // retaining child order/text while ignoring attribute ordering and indentation changes.
            var common = new int[oldCount + 1, newCount + 1];
            for (var i = oldCount - 1; i >= 0; i--)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (var j = newCount - 1; j >= 0; j--)
                    common[i, j] = before[start + i] == after[start + j]
                        ? common[i + 1, j + 1] + 1 : Math.Max(common[i + 1, j], common[i, j + 1]);
            }
            var a = 0; var b = 0;
            while (a < oldCount || b < newCount)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (a < oldCount && b < newCount && before[start + a] == after[start + b]) Add("same", start + a++, start + b++);
                else if (a < oldCount && (b == newCount || common[a + 1, b] >= common[a, b + 1])) Add("removed", start + a++, null);
                else Add("added", null, start + b++);
            }
        }
        else
        {
            // Exact before/after content is still shown if a very large edit exceeds the work budget.
            // Group the changed middle instead of allocating an unbounded quadratic table.
            grouped = true;
            for (var i = start; i < oldEnd; i++) { cancellationToken.ThrowIfCancellationRequested(); Add("removed", i, null); }
            for (var i = start; i < newEnd; i++) { cancellationToken.ThrowIfCancellationRequested(); Add("added", null, i); }
        }
        while (oldEnd < before.Length) { cancellationToken.ThrowIfCancellationRequested(); Add("same", oldEnd++, newEnd++); }
        return new(lines, added, removed, grouped, beforePlain || afterPlain);
    }

    private static string[] Format(string text, out bool plainText)
    {
        plainText = false;
        if (text.Length > MaxCharacters) throw new InvalidOperationException("This audit comparison is too large to display in MAUI.");
        if (text.Length == 0) return [];
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = MaxCharacters };
        string formatted;
        try
        {
            // Limit nesting/node count before LINQ-to-XML/XmlWriter recursion. No external entities/DTDs.
            using (var reader = XmlReader.Create(new StringReader(text), settings))
            {
                var nodes = 0;
                while (reader.Read())
                    if (reader.Depth > 64 || ++nodes > MaxLines)
                        throw new InvalidOperationException("This audit comparison is too large to display in MAUI.");
            }
            using var input = XmlReader.Create(new StringReader(text), settings);
            var document = XDocument.Load(input, LoadOptions.PreserveWhitespace);
            document.Nodes().OfType<XText>().Where(textNode => string.IsNullOrWhiteSpace(textNode.Value)).Remove();
            foreach (var element in document.Descendants())
            {
                // Ignore indentation between child elements, preserving leaf values and mixed content.
                if (element.HasElements && !element.Nodes().OfType<XText>().Any(node => node is XCData || !string.IsNullOrWhiteSpace(node.Value)))
                    element.Nodes().OfType<XText>().Where(node => node is not XCData).Remove();
                element.ReplaceAttributes(element.Attributes().OrderBy(attribute => attribute.Name.ToString(), StringComparer.Ordinal).ToArray());
            }
            var output = new StringBuilder();
            using (var writer = XmlWriter.Create(output, new XmlWriterSettings { OmitXmlDeclaration = true, Indent = true,
                IndentChars = "  ", NewLineChars = "\n", NewLineOnAttributes = true, NewLineHandling = NewLineHandling.Entitize }))
                document.Save(writer);
            formatted = output.ToString();
        }
        catch (XmlException)
        {
            // Native legacy/malformed text remains visible and is always rendered as text by the client.
            // Compare uses raw text on both sides when either cannot be formatted.
            plainText = true; formatted = text;
        }
        return Split(formatted);
    }

    private static string[] Split(string text)
    {
        if (text.Length == 0) return [];
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        if (lines.Length > MaxLines) throw new InvalidOperationException("This audit comparison is too large to display in MAUI.");
        return lines;
    }
}

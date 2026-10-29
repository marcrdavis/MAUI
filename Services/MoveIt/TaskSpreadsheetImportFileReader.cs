// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// CSV/XLSX first-sheet reader used only by Task spreadsheet import.

using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace MoveItAutomationUI.Services.MoveIt;

internal sealed record TaskSpreadsheetRawRow(int RowNumber, IReadOnlyDictionary<string, string> Values);
internal sealed record TaskSpreadsheetFileData(IReadOnlyList<string> Headers, IReadOnlyList<TaskSpreadsheetRawRow> Rows, int SkippedBlankRows);

internal static class TaskSpreadsheetImportFileReader
{
    // Import Row Limit controls preview/execution batching; these protect initial parsing and retained analysis.
    internal const int MaxDataRows = 10_000;
    internal const int MaxColumns = 100;
    internal const int MaxPopulatedCells = 250_000;
    internal const int MaxCellCharacters = 32_767;
    internal const long MaxAggregateCellCharacters = 5_000_000;
    internal const long MaxWorksheetXmlBytes = 32L * 1024 * 1024;
    internal const long MaxSharedStringsXmlBytes = 16L * 1024 * 1024;
    internal const long MaxTotalXmlBytes = 64L * 1024 * 1024;
    private const long MaxOtherXmlBytes = 8L * 1024 * 1024;
    private const int MaxPhysicalRows = 20_000;
    private const int MaxArchiveEntries = 2_000;

    private static readonly XNamespace SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace OfficeRelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static TaskSpreadsheetFileData Read(byte[] bytes, string fileName, bool preservePasswordCells = false)
    {
        if (bytes is null || bytes.Length == 0) throw new InvalidOperationException("The selected import file is empty.");
        if (bytes.Length > 50L * 1024 * 1024)
            throw new InvalidOperationException("The import file exceeds the maximum upload size of 50 MB.");
        var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        return extension switch
        {
            ".csv" => ReadCsv(bytes, preservePasswordCells),
            ".xlsx" => ReadXlsx(bytes, preservePasswordCells),
            _ => throw new InvalidOperationException("Unsupported import file type. Select a .csv or .xlsx file.")
        };
    }

    private static TaskSpreadsheetFileData ReadCsv(byte[] bytes, bool preservePasswordCells)
    {
        var text = DecodeText(bytes);
        var records = ParseCsvRecords(text);
        if (records.Count == 0) throw new InvalidOperationException("The selected CSV file is empty.");
        var headerIndex = records.FindIndex(row => row.Any(value => !string.IsNullOrWhiteSpace(value)));
        if (headerIndex < 0) throw new InvalidOperationException("The selected CSV file does not contain a header row.");
        return BuildFileData(records[headerIndex], records.Skip(headerIndex + 1).ToList(), headerIndex + 1, null, preservePasswordCells: preservePasswordCells);
    }

    private static TaskSpreadsheetFileData ReadXlsx(byte[] bytes, bool preservePasswordCells)
    {
        try
        {
            using var memory = new MemoryStream(bytes, writable: false);
            using var archive = new ZipArchive(memory, ZipArchiveMode.Read, leaveOpen: false);
            ValidateArchive(archive);
            var worksheet = GetFirstWorksheetEntry(archive)
                ?? throw new InvalidOperationException("The selected XLSX file does not contain a readable first worksheet.");
            var sharedStrings = LoadSharedStrings(archive);
            var timeStyles = LoadTimeFormattedStyleIndexes(archive);
            var rows = ReadWorksheetRows(worksheet, sharedStrings, timeStyles);
            var headerIndex = rows.FindIndex(row => row.Cells.Any(value => !string.IsNullOrWhiteSpace(value.Value)));
            if (headerIndex < 0) throw new InvalidOperationException("The first worksheet does not contain a header row.");

            var headers = rows[headerIndex].Cells.Select(cell => cell.Value).ToList();
            var dataRows = rows.Skip(headerIndex + 1).ToList();
            var values = dataRows.Select(row => row.Cells.Select(cell => cell.Value).ToList()).ToList();
            var styles = dataRows.Select(row => row.Cells).ToList();
            var rowNumbers = dataRows.Select(row => row.RowNumber).ToList();
            return BuildFileData(headers, values, rows[headerIndex].RowNumber, styles, rowNumbers, preservePasswordCells);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is InvalidDataException or XmlException)
        {
            throw new InvalidOperationException("The selected XLSX file is not a valid or readable Excel workbook.", ex);
        }
    }

    private static TaskSpreadsheetFileData BuildFileData(
        IReadOnlyList<string> rawHeaders,
        IReadOnlyList<List<string>> rawRows,
        int headerRowNumber,
        IReadOnlyList<List<XlsxCellValue>>? xlsxRows,
        IReadOnlyList<int>? sourceRowNumbers = null,
        bool preservePasswordCells = false)
    {
        if (rawHeaders.Count > MaxColumns)
            throw new InvalidOperationException($"The import file exceeds the maximum of {MaxColumns} columns.");
        if (rawRows.Count > MaxPhysicalRows)
            throw new InvalidOperationException($"The import file exceeds the supported number of physical rows ({MaxPhysicalRows}).");
        var headers = rawHeaders.Select(NormalizeHeader).ToList();
        if (headers.Count == 0 || headers.All(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("The import file does not contain a usable header row.");

        var duplicates = headers.Where(header => header.Length > 0)
            .GroupBy(header => header, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicates.Length > 0)
            throw new InvalidOperationException($"The import file contains duplicate column names after normalization: {string.Join(", ", duplicates)}.");

        var rows = new List<TaskSpreadsheetRawRow>();
        var skipped = 0;
        long totalCharacters = 0;
        var populatedCells = 0;
        var dataRowCount = 0;
        for (var rowIndex = 0; rowIndex < rawRows.Count; rowIndex++)
        {
            var raw = rawRows[rowIndex];
            if (raw.Count > MaxColumns)
                throw new InvalidOperationException($"The import file exceeds the maximum of {MaxColumns} columns.");
            if (raw.All(string.IsNullOrWhiteSpace)) { skipped++; continue; }
            if (++dataRowCount > MaxDataRows)
                throw new InvalidOperationException($"The import file exceeds the maximum of {MaxDataRows:N0} data rows. Split it into smaller files.");
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var columnIndex = 0; columnIndex < headers.Count; columnIndex++)
            {
                var header = headers[columnIndex];
                if (header.Length == 0) continue;
                var value = columnIndex < raw.Count ? raw[columnIndex] ?? string.Empty : string.Empty;
                if (xlsxRows is not null && rowIndex < xlsxRows.Count && columnIndex < xlsxRows[rowIndex].Count)
                    value = NormalizeImportedXlsxValue(header, xlsxRows[rowIndex][columnIndex]);
                else
                    value = NormalizeImportedValue(header, value);
                // Passwords can contain significant leading/trailing whitespace and literal \x
                // sequences. The Host importer opts into exact preservation; Task import
                // continues using its historical normalization path.
                var cleaned = preservePasswordCells && header.Equals("PASSWORD", StringComparison.OrdinalIgnoreCase)
                    ? (xlsxRows is not null && rowIndex < xlsxRows.Count && columnIndex < xlsxRows[rowIndex].Count
                        ? xlsxRows[rowIndex][columnIndex].Value
                        : value)
                    : Clean(value);
                CountCell(cleaned, ref populatedCells, ref totalCharacters);
                values[header] = cleaned;
            }
            var sourceRowNumber = sourceRowNumbers is not null && rowIndex < sourceRowNumbers.Count
                ? sourceRowNumbers[rowIndex]
                : headerRowNumber + rowIndex + 1;
            rows.Add(new TaskSpreadsheetRawRow(sourceRowNumber, values));
        }
        return new TaskSpreadsheetFileData(headers.Where(header => header.Length > 0).ToArray(), rows, skipped);
    }

    private static string NormalizeHeader(string? value)
    {
        var cleaned = Clean(value ?? string.Empty);
        if (cleaned.Length == 0) return string.Empty;
        var upper = cleaned.ToUpperInvariant();
        return Regex.Replace(upper, @"[^A-Z0-9]+", "_").Trim('_');
    }

    internal static string Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var cleaned = value.Replace('\uFFFD', ' ').Replace('\u00A0', ' ').Trim();
        cleaned = Regex.Replace(cleaned, @"\\x[0-9A-Fa-f]{2}|\\u[0-9A-Fa-f]{4}", string.Empty);
        return cleaned.Replace('\u00A0', ' ').Trim();
    }

    private static string DecodeText(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes, writable: false), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    // RFC4180-style parser. Unlike line-based parsing this also preserves quoted line breaks.
    private static List<List<string>> ParseCsvRecords(string text)
    {
        var records = new List<List<string>>();
        var completedCells = 0;
        long completedCharacters = 0;
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < text.Length; index++)
        {
            var ch = text[index];
            if (quoted)
            {
                if (ch == '"')
                {
                    if (index + 1 < text.Length && text[index + 1] == '"') { cell.Append('"'); CheckCellBuffer(cell); index++; }
                    else quoted = false;
                }
                else { cell.Append(ch); CheckCellBuffer(cell); }
                continue;
            }
            if (ch == '"') { quoted = true; continue; }
            if (ch == ',') { FinishCell(); continue; }
            if (ch == '\r' || ch == '\n')
            {
                if (ch == '\r' && index + 1 < text.Length && text[index + 1] == '\n') index++;
                FinishCell(); FinishRow(); continue;
            }
            cell.Append(ch);
            CheckCellBuffer(cell);
        }
        if (quoted) throw new InvalidOperationException("The CSV file contains an unterminated quoted field.");
        if (cell.Length > 0 || row.Count > 0) { FinishCell(); FinishRow(); }
        return records;

        void FinishCell()
        {
            if (row.Count >= MaxColumns)
                throw new InvalidOperationException($"The CSV file exceeds the maximum of {MaxColumns} columns.");
            var value = cell.ToString();
            CountCell(value, ref completedCells, ref completedCharacters);
            row.Add(value);
            cell.Clear();
        }
        void FinishRow()
        {
            if (records.Count >= MaxPhysicalRows)
                throw new InvalidOperationException($"The CSV file exceeds the maximum of {MaxPhysicalRows:N0} physical rows.");
            records.Add(row);
            row = new List<string>();
        }
    }

    private static string NormalizeImportedValue(string columnName, string value)
    {
        var normalized = NormalizeHeader(columnName);
        return normalized switch
        {
            "SCHED_START" or "SCHED_END" => NormalizeImportedScheduleTime(value),
            "SCHED_EVERY_MIN" => ConvertHoursAndMinutesToMinutes(value),
            _ => value ?? string.Empty
        };
    }

    private static string NormalizeImportedXlsxValue(string columnName, XlsxCellValue value)
    {
        var normalized = NormalizeHeader(columnName);
        if (value.IsTimeFormattedNumber)
        {
            if (normalized == "SCHED_EVERY_MIN") return ConvertExcelTimeSerialToMinutes(value.Value);
            if (normalized is "SCHED_START" or "SCHED_END") return ConvertExcelTimeSerialToClockTime(value.Value);
        }
        return NormalizeImportedValue(columnName, value.Value);
    }

    private static string NormalizeImportedScheduleTime(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var trimmed = value.Trim();
        var zone = Regex.Match(trimmed, @"^(?<time>.*?)(?:\s*)(?<zone>[A-Za-z]{2,5})\s*$");
        if (zone.Success && !zone.Groups["zone"].Value.Equals("AM", StringComparison.OrdinalIgnoreCase) && !zone.Groups["zone"].Value.Equals("PM", StringComparison.OrdinalIgnoreCase))
            trimmed = zone.Groups["time"].Value.TrimEnd();
        // Proven importer tolerated malformed 24-hour times suffixed with PM by removing the invalid designator.
        if (Regex.IsMatch(trimmed, @"^(?:1[3-9]|2[0-3]):[0-5]\d\s*PM$", RegexOptions.IgnoreCase))
            trimmed = Regex.Replace(trimmed, @"\s*PM$", string.Empty, RegexOptions.IgnoreCase);
        return trimmed;
    }

    private static string ConvertHoursAndMinutesToMinutes(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var trimmed = value.Trim();
        var match = Regex.Match(trimmed, @"^(?<hours>\d{1,3}):(?<minutes>[0-5]\d)$");
        if (!match.Success) return trimmed;
        var hours = int.Parse(match.Groups["hours"].Value, CultureInfo.InvariantCulture);
        var minutes = int.Parse(match.Groups["minutes"].Value, CultureInfo.InvariantCulture);
        return checked(hours * 60 + minutes).ToString(CultureInfo.InvariantCulture);
    }

    private static string ConvertExcelTimeSerialToMinutes(string value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial)) return value;
        var totalMinutes = (int)Math.Round(serial * 24d * 60d, MidpointRounding.AwayFromZero);
        return Math.Max(0, totalMinutes).ToString(CultureInfo.InvariantCulture);
    }

    private static string ConvertExcelTimeSerialToClockTime(string value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial)) return value;
        var fraction = serial - Math.Floor(serial);
        if (fraction < 0) fraction += 1;
        var totalMinutes = ((int)Math.Round(fraction * 24d * 60d, MidpointRounding.AwayFromZero)) % (24 * 60);
        return $"{totalMinutes / 60:00}:{totalMinutes % 60:00}";
    }

    private static ZipArchiveEntry? GetFirstWorksheetEntry(ZipArchive archive)
    {
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (workbookEntry is not null && relsEntry is not null)
        {
            var workbook = LoadBoundedXml(workbookEntry, MaxOtherXmlBytes);
            var rels = LoadBoundedXml(relsEntry, MaxOtherXmlBytes);
            var firstSheet = workbook.Root?.Element(SpreadsheetNs + "sheets")?.Elements(SpreadsheetNs + "sheet").FirstOrDefault();
            var relId = (string?)firstSheet?.Attribute(OfficeRelNs + "id");
            if (!string.IsNullOrWhiteSpace(relId))
            {
                var target = (string?)rels.Root?.Elements(PackageRelNs + "Relationship")
                    .FirstOrDefault(item => string.Equals((string?)item.Attribute("Id"), relId, StringComparison.OrdinalIgnoreCase))?.Attribute("Target");
                var resolved = ResolveWorkbookRelationshipTarget(archive, target);
                if (resolved is not null) return resolved;
            }
        }
        return archive.GetEntry("xl/worksheets/sheet1.xml");
    }

    private static ZipArchiveEntry? ResolveWorkbookRelationshipTarget(ZipArchive archive, string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return null;
        var normalized = target.Replace('\\', '/').Trim();
        if (normalized.StartsWith('/')) normalized = normalized.TrimStart('/');
        else if (!normalized.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)) normalized = "xl/" + normalized.TrimStart('.', '/');
        var parts = new List<string>();
        foreach (var part in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".") continue;
            if (part == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); continue; }
            parts.Add(part);
        }
        return archive.GetEntry(string.Join('/', parts));
    }

    private static List<string> LoadSharedStrings(ZipArchive archive)
    {
        var result = new List<string>();
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return result;
        var doc = LoadBoundedXml(entry, MaxSharedStringsXmlBytes);
        long stringCharacters = 0;
        foreach (var item in doc.Descendants(SpreadsheetNs + "si"))
        {
            var value = string.Concat(item.Descendants(SpreadsheetNs + "t").Select(node => (string?)node ?? string.Empty));
            if (value.Length > MaxCellCharacters)
                throw new InvalidOperationException($"The XLSX file contains a shared string longer than {MaxCellCharacters:N0} characters.");
            stringCharacters += value.Length;
            if (stringCharacters > MaxAggregateCellCharacters)
                throw new InvalidOperationException($"The XLSX file exceeds the maximum of {MaxAggregateCellCharacters:N0} shared-string characters.");
            result.Add(value);
        }
        return result;
    }

    private static List<XlsxRow> ReadWorksheetRows(ZipArchiveEntry worksheetEntry, IList<string> sharedStrings, ISet<int> timeStyles)
    {
        // Stream worksheet rows instead of materializing a potentially huge XDocument.
        // Keep only the current row XElement (for the existing cell/value rules) and the bounded result set.
        using var buffer = ReadBoundedXmlStream(worksheetEntry, MaxWorksheetXmlBytes);
        using var xml = CreateSafeXmlReader(buffer, MaxWorksheetXmlBytes);
        var rows = new List<XlsxRow>();
        var fallbackRowNumber = 0;
        var inSheetData = false;
        while (!xml.EOF)
        {
            if (xml.NodeType == XmlNodeType.Element && xml.NamespaceURI == SpreadsheetNs.NamespaceName)
            {
                if (xml.LocalName == "sheetData") inSheetData = true;
                else if (inSheetData && xml.LocalName == "row")
                {
                    if (rows.Count >= MaxPhysicalRows)
                        throw new InvalidOperationException($"The XLSX worksheet exceeds the maximum of {MaxPhysicalRows:N0} physical rows.");
                    // ReadFrom advances the XML reader past this single row, so do not call Read again here.
                    var rowElement = (XElement)XNode.ReadFrom(xml);
                    fallbackRowNumber++;
                    var rowNumber = int.TryParse((string?)rowElement.Attribute("r"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedRow) && parsedRow > 0
                        ? parsedRow : fallbackRowNumber;
                    if (rowNumber > 1_048_576)
                        throw new InvalidOperationException("The XLSX worksheet contains a row reference outside Excel's supported range.");
                    fallbackRowNumber = rowNumber;
                    var values = new List<XlsxCellValue>();
                    foreach (var cell in rowElement.Elements(SpreadsheetNs + "c"))
                    {
                        var reference = (string?)cell.Attribute("r");
                        // SpreadsheetML may omit cell references; those cells are sequential.
                        // Explicit references must be ordered and unique, otherwise appending a
                        // backwards/duplicate reference would silently shift Task field values.
                        var columnIndex = string.IsNullOrWhiteSpace(reference)
                            ? values.Count : GetColumnIndex(reference);
                        if (columnIndex >= MaxColumns || values.Count >= MaxColumns)
                            throw new InvalidOperationException($"The XLSX worksheet exceeds the maximum of {MaxColumns} columns.");
                        if (columnIndex < values.Count)
                            throw new InvalidOperationException("The XLSX worksheet contains duplicate or out-of-order cell references; Task columns cannot be mapped safely.");
                        while (values.Count < columnIndex) values.Add(XlsxCellValue.Empty);
                        var value = GetCellValue(cell, sharedStrings, timeStyles);
                        if (value.Value.Length > MaxCellCharacters)
                            throw new InvalidOperationException($"The XLSX file contains a cell longer than {MaxCellCharacters:N0} characters.");
                        values.Add(value);
                    }
                    while (values.Count > 0 && string.IsNullOrEmpty(values[^1].Value)) values.RemoveAt(values.Count - 1);
                    rows.Add(new XlsxRow(rowNumber, values));
                    continue;
                }
            }
            else if (xml.NodeType == XmlNodeType.EndElement
                && xml.LocalName == "sheetData" && xml.NamespaceURI == SpreadsheetNs.NamespaceName)
            {
                inSheetData = false;
            }
            xml.Read();
        }
        return rows;
    }

    private static XlsxCellValue GetCellValue(XElement cell, IList<string> sharedStrings, ISet<int> timeStyles)
    {
        var type = (string?)cell.Attribute("t") ?? string.Empty;
        if (type.Equals("inlineStr", StringComparison.OrdinalIgnoreCase))
            return new XlsxCellValue(string.Concat(cell.Element(SpreadsheetNs + "is")?.Descendants(SpreadsheetNs + "t").Select(node => (string?)node ?? string.Empty) ?? Array.Empty<string>()), false);
        var raw = (string?)cell.Element(SpreadsheetNs + "v");
        if (raw is null) return XlsxCellValue.Empty;
        if (type.Equals("s", StringComparison.OrdinalIgnoreCase) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) && index >= 0 && index < sharedStrings.Count)
            return new XlsxCellValue(sharedStrings[index], false);
        if (type.Equals("b", StringComparison.OrdinalIgnoreCase)) return new XlsxCellValue(raw == "1" ? "TRUE" : "FALSE", false);
        var hasTimeFormat = int.TryParse((string?)cell.Attribute("s"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var styleIndex) && timeStyles.Contains(styleIndex);
        return new XlsxCellValue(raw, hasTimeFormat);
    }

    private static HashSet<int> LoadTimeFormattedStyleIndexes(ZipArchive archive)
    {
        var result = new HashSet<int>();
        var entry = archive.GetEntry("xl/styles.xml");
        if (entry is null) return result;
        var doc = LoadBoundedXml(entry, MaxOtherXmlBytes);
        var customFormats = new Dictionary<int, string>();
        foreach (var numFmt in doc.Root?.Element(SpreadsheetNs + "numFmts")?.Elements(SpreadsheetNs + "numFmt") ?? Enumerable.Empty<XElement>())
            if (int.TryParse((string?)numFmt.Attribute("numFmtId"), out var customNumFmtId)) customFormats[customNumFmtId] = (string?)numFmt.Attribute("formatCode") ?? string.Empty;
        var cellXfs = doc.Root?.Element(SpreadsheetNs + "cellXfs");
        if (cellXfs is null) return result;
        var styleIndex = 0;
        foreach (var xf in cellXfs.Elements(SpreadsheetNs + "xf"))
        {
            _ = int.TryParse((string?)xf.Attribute("numFmtId"), out var styleNumFmtId);
            customFormats.TryGetValue(styleNumFmtId, out var format);
            if (IsTimeNumberFormat(styleNumFmtId, format)) result.Add(styleIndex);
            styleIndex++;
        }
        return result;
    }

    private static bool IsTimeNumberFormat(int numFmtId, string? formatCode)
    {
        if (numFmtId is 18 or 19 or 20 or 21 or 45 or 46 or 47) return true;
        if (string.IsNullOrWhiteSpace(formatCode)) return false;
        var cleaned = Regex.Replace(formatCode, "\"[^\"]*\"", string.Empty);
        cleaned = Regex.Replace(cleaned, @"\\.", string.Empty);
        return cleaned.Contains(':') && Regex.IsMatch(cleaned, @"(?i)[hms]");
    }

    private static int GetColumnIndex(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return 0;
        var index = 0;
        foreach (var ch in reference)
        {
            if (!char.IsLetter(ch)) break;
            if (index >= MaxColumns)
                throw new InvalidOperationException($"The XLSX file references a column outside the maximum of {MaxColumns} columns.");
            index = checked(index * 26 + (char.ToUpperInvariant(ch) - 'A' + 1));
        }
        return Math.Max(0, index - 1);
    }

    private static void CheckCellBuffer(StringBuilder cell)
    {
        if (cell.Length > MaxCellCharacters)
            throw new InvalidOperationException($"The import file contains a cell longer than {MaxCellCharacters:N0} characters.");
    }

    private static void CountCell(string value, ref int count, ref long characters)
    {
        if (value.Length > MaxCellCharacters)
            throw new InvalidOperationException($"The import file contains a cell longer than {MaxCellCharacters:N0} characters.");
        if (value.Length == 0) return;
        count++;
        characters += value.Length;
        if (count > MaxPopulatedCells)
            throw new InvalidOperationException($"The import file exceeds the maximum of {MaxPopulatedCells:N0} populated cells.");
        if (characters > MaxAggregateCellCharacters)
            throw new InvalidOperationException($"The import file exceeds the maximum of {MaxAggregateCellCharacters:N0} total cell characters.");
    }

    private static void ValidateArchive(ZipArchive archive)
    {
        if (archive.Entries.Count > MaxArchiveEntries)
            throw new InvalidOperationException("The XLSX workbook contains too many ZIP entries.");
        long totalXml = 0;
        foreach (var entry in archive.Entries)
        {
            var path = entry.FullName;
            if (!(path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))) continue;
            var limit = path.Equals("xl/sharedStrings.xml", StringComparison.OrdinalIgnoreCase)
                ? MaxSharedStringsXmlBytes
                : path.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase)
                    ? MaxWorksheetXmlBytes : MaxOtherXmlBytes;
            if (entry.Length > limit)
                throw new InvalidOperationException($"The XLSX XML entry '{path}' exceeds the allowed decompressed size.");
            totalXml = checked(totalXml + entry.Length);
            if (totalXml > MaxTotalXmlBytes)
                throw new InvalidOperationException("The XLSX workbook exceeds the 64 MB total decompressed XML limit.");
        }
    }

    // Validate *actual* expanded bytes, not just potentially incorrect ZIP metadata.
    private static MemoryStream ReadBoundedXmlStream(ZipArchiveEntry entry, long maxBytes)
    {
        using var stream = entry.Open();
        var buffer = new MemoryStream();
        try
        {
            var block = new byte[16 * 1024];
            long readTotal = 0;
            int count;
            while ((count = stream.Read(block, 0, block.Length)) > 0)
            {
                readTotal += count;
                if (readTotal > maxBytes)
                    throw new InvalidOperationException($"The XLSX XML entry '{entry.FullName}' exceeds its decompressed size limit.");
                buffer.Write(block, 0, count);
            }
            buffer.Position = 0;
            return buffer;
        }
        catch { buffer.Dispose(); throw; }
    }

    private static XmlReader CreateSafeXmlReader(Stream stream, long maxBytes)
        => XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = maxBytes,
            CloseInput = false
        });

    private static XDocument LoadBoundedXml(ZipArchiveEntry entry, long maxBytes)
    {
        using var buffer = ReadBoundedXmlStream(entry, maxBytes);
        using var xml = CreateSafeXmlReader(buffer, maxBytes);
        return XDocument.Load(xml);
    }

    private sealed record XlsxRow(int RowNumber, List<XlsxCellValue> Cells);

    private sealed record XlsxCellValue(string Value, bool IsTimeFormattedNumber)
    {
        public static XlsxCellValue Empty { get; } = new(string.Empty, false);
    }
}

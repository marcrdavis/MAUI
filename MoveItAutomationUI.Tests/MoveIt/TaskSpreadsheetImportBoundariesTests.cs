using System.IO.Compression;
using System.Text;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class TaskSpreadsheetImportBoundariesTests
{
    [Fact]
    public void Csv_AcceptsExactlyTenThousandDataRows()
    {
        var data = "TASK_NAME\n" + string.Concat(Enumerable.Range(1, 10_000).Select(i => $"TASK_{i}\n"));
        var result = TaskSpreadsheetImportFileReader.Read(Encoding.UTF8.GetBytes(data), "tasks.csv");
        Assert.Equal("TASK_10000", result.Rows[^1].Values["TASK_NAME"]);
    }

    [Fact]
    public void Csv_RejectsMoreThanTenThousandDataRows()
    {
        var data = "TASK_NAME\n" + string.Concat(Enumerable.Range(1, 10_001).Select(i => $"TASK_{i}\n"));
        var error = Assert.Throws<InvalidOperationException>(() => TaskSpreadsheetImportFileReader.Read(Encoding.UTF8.GetBytes(data), "tasks.csv"));
        Assert.Contains("10,000", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Csv_RejectsMoreThanOneHundredColumns()
    {
        var data = string.Join(",", Enumerable.Range(1, 101).Select(i => $"C{i}")) + "\nvalue\n";
        var error = Assert.Throws<InvalidOperationException>(() => TaskSpreadsheetImportFileReader.Read(Encoding.UTF8.GetBytes(data), "tasks.csv"));
        Assert.Contains("100 columns", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Csv_RejectsOversizedQuotedCellBeforeMaterializingResults()
    {
        var data = "TASK_NAME\n\"" + new string('x', 32_768) + "\"\n";
        var error = Assert.Throws<InvalidOperationException>(() => TaskSpreadsheetImportFileReader.Read(Encoding.UTF8.GetBytes(data), "tasks.csv"));
        Assert.Contains("32,767", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Csv_RejectsMoreThanTwoHundredFiftyThousandPopulatedCells()
    {
        var headers = string.Join(",", Enumerable.Range(1, 26).Select(i => $"C{i}")) + "\n";
        var row = string.Join(",", Enumerable.Repeat("x", 26)) + "\n";
        var csv = headers + string.Concat(Enumerable.Repeat(row, 10_000));
        var error = Assert.Throws<InvalidOperationException>(() => TaskSpreadsheetImportFileReader.Read(Encoding.UTF8.GetBytes(csv), "tasks.csv"));
        Assert.Contains("250,000", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Csv_RejectsMoreThanFiveMillionTotalCharacters()
    {
        var row = new string('x', 501) + "\n";
        var csv = "TASK_NAME\n" + string.Concat(Enumerable.Repeat(row, 10_000));
        var error = Assert.Throws<InvalidOperationException>(() => TaskSpreadsheetImportFileReader.Read(Encoding.UTF8.GetBytes(csv), "tasks.csv"));
        Assert.Contains("5,000,000", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Xlsx_AcceptsSimpleInlineStringWorksheet()
    {
        var bytes = Workbook("""
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="1"><c r="A1" t="inlineStr"><is><t>TASK_NAME</t></is></c></row>
                <row r="2"><c r="A2" t="inlineStr"><is><t>EXAMPLE</t></is></c></row>
              </sheetData>
            </worksheet>
            """);
        var result = TaskSpreadsheetImportFileReader.Read(bytes, "tasks.xlsx");
        Assert.Single(result.Rows);
        Assert.Equal("EXAMPLE", result.Rows[0].Values["TASK_NAME"]);
    }

    [Fact]
    public void Xlsx_PreservesSparseCellColumnMapping()
    {
        var bytes = Workbook("""
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="1"><c r="A1" t="inlineStr"><is><t>TASK_NAME</t></is></c><c r="C1" t="inlineStr"><is><t>RESOURCE_GROUP</t></is></c></row>
                <row r="2"><c r="A2" t="inlineStr"><is><t>TEST</t></is></c><c r="C2" t="inlineStr"><is><t>Finance</t></is></c></row>
              </sheetData>
            </worksheet>
            """);
        var row = Assert.Single(TaskSpreadsheetImportFileReader.Read(bytes, "tasks.xlsx").Rows);
        Assert.Equal("TEST", row.Values["TASK_NAME"]);
        Assert.Equal("Finance", row.Values["RESOURCE_GROUP"]);
    }

    [Fact]
    public void Xlsx_RejectsOutOfOrderCellReferencesRatherThanShiftingTaskValues()
    {
        var bytes = Workbook("""
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="1"><c r="B1" t="inlineStr"><is><t>RESOURCE_GROUP</t></is></c><c r="A1" t="inlineStr"><is><t>TASK_NAME</t></is></c></row>
                <row r="2"><c r="A2" t="inlineStr"><is><t>TEST</t></is></c><c r="B2" t="inlineStr"><is><t>Finance</t></is></c></row>
              </sheetData>
            </worksheet>
            """);
        var error = Assert.Throws<InvalidOperationException>(() => TaskSpreadsheetImportFileReader.Read(bytes, "tasks.xlsx"));
        Assert.Contains("out-of-order", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Xlsx_SequentialCellsWithoutReferencesAreSupported()
    {
        var bytes = Workbook("""
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row><c t="inlineStr"><is><t>TASK_NAME</t></is></c><c t="inlineStr"><is><t>RESOURCE_GROUP</t></is></c></row>
                <row><c t="inlineStr"><is><t>TEST</t></is></c><c t="inlineStr"><is><t>Finance</t></is></c></row>
              </sheetData>
            </worksheet>
            """);
        var row = Assert.Single(TaskSpreadsheetImportFileReader.Read(bytes, "tasks.xlsx").Rows);
        Assert.Equal("TEST", row.Values["TASK_NAME"]);
        Assert.Equal("Finance", row.Values["RESOURCE_GROUP"]);
    }

    [Fact]
    public void Xlsx_RejectsFarOutsideColumnReferenceBeforeExpandingRow()
    {
        var bytes = Workbook("""
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData><row r="1"><c r="ZZZ1"><v>1</v></c></row></sheetData>
            </worksheet>
            """);
        var error = Assert.Throws<InvalidOperationException>(() => TaskSpreadsheetImportFileReader.Read(bytes, "tasks.xlsx"));
        Assert.Contains("100 columns", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Xlsx_RejectsOversizedDecompressedWorksheet()
    {
        var bytes = Workbook("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>"
            + new string(' ', 32 * 1024 * 1024) + "</sheetData></worksheet>");
        var error = Assert.Throws<InvalidOperationException>(() => TaskSpreadsheetImportFileReader.Read(bytes, "tasks.xlsx"));
        Assert.Contains("decompressed", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Xlsx_RejectsXmlDtd()
    {
        var bytes = Workbook("""
            <!DOCTYPE worksheet [<!ENTITY test "value">]>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData><row><c t="inlineStr"><is><t>&test;</t></is></c></row></sheetData>
            </worksheet>
            """);
        var error = Assert.Throws<InvalidOperationException>(() => TaskSpreadsheetImportFileReader.Read(bytes, "tasks.xlsx"));
        Assert.Contains("valid or readable", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] Workbook(string worksheetXml)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Optimal);
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(worksheetXml);
        }
        return output.ToArray();
    }
}

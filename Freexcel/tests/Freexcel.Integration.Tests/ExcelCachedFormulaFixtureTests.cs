using System.Globalization;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Freexcel.Core.Formula;
using Freexcel.Core.IO;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Integration.Tests;

public sealed class ExcelCachedFormulaFixtureTests
{
    public static IEnumerable<object[]> CachedFormulaFixtureFiles()
    {
        var directory = FindWorkspacePath("test-corpus", "regressions", "formula-cached");
        if (!Directory.Exists(directory))
            yield break;

        foreach (var path in Directory.EnumerateFiles(directory, "*.xlsx").Order(StringComparer.OrdinalIgnoreCase))
            yield return [path];
    }

    [Theory]
    [MemberData(nameof(CachedFormulaFixtureFiles))]
    public void ExcelCachedFormulaFixture_MatchesFreexcelEvaluation(string path)
    {
        using var stream = File.OpenRead(path);
        var workbook = new XlsxFileAdapter().Load(stream);

        var mismatches = CompareFormulaCellsToCachedResults(workbook).ToArray();

        mismatches.Should().BeEmpty(Path.GetFileName(path));
    }

    [Fact]
    public void CachedFormulaFixtureHarness_ComparesFreexcelEvaluationToWorkbookCachedResults()
    {
        using var fixture = CreateCachedFormulaWorkbook();
        var workbook = new XlsxFileAdapter().Load(fixture);

        var mismatches = CompareFormulaCellsToCachedResults(workbook).ToArray();

        mismatches.Should().BeEmpty();
    }

    private static IEnumerable<string> CompareFormulaCellsToCachedResults(Workbook workbook)
    {
        var evaluator = new FormulaEvaluator();

        foreach (var sheet in workbook.Sheets)
        {
            foreach (var (address, cell) in sheet.EnumerateCells())
            {
                if (!cell.HasFormula || string.IsNullOrWhiteSpace(cell.FormulaText))
                    continue;

                var actual = evaluator.Evaluate("=" + cell.FormulaText, sheet, workbook);
                if (!ScalarValuesMatch(cell.Value, actual))
                {
                    yield return $"{sheet.Name}!{address}: expected cached {Describe(cell.Value)} from {cell.FormulaText}, got {Describe(actual)}";
                }
            }
        }
    }

    private static bool ScalarValuesMatch(ScalarValue expected, ScalarValue actual)
    {
        return (expected, actual) switch
        {
            (NumberValue e, NumberValue a) => Math.Abs(e.Value - a.Value) <= 1e-10,
            (DateTimeValue e, DateTimeValue a) => Math.Abs(e.Value - a.Value) <= 1e-10,
            _ => Equals(expected, actual)
        };
    }

    private static string Describe(ScalarValue value) => value switch
    {
        NumberValue number => number.Value.ToString("R", CultureInfo.InvariantCulture),
        DateTimeValue date => date.Value.ToString("R", CultureInfo.InvariantCulture),
        TextValue text => '"' + text.Value + '"',
        BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
        ErrorValue error => error.Code,
        BlankValue => "<blank>",
        _ => value.ToString() ?? value.GetType().Name
    };

    private static MemoryStream CreateCachedFormulaWorkbook()
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddXml(archive, "[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """);
            AddXml(archive, "_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """);
            AddXml(archive, "xl/_rels/workbook.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                </Relationships>
                """);
            AddXml(archive, "xl/workbook.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="FormulaCases" sheetId="1" r:id="rId1"/>
                  </sheets>
                  <calcPr calcMode="auto"/>
                </workbook>
                """);
            AddXml(archive, "xl/worksheets/sheet1.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <sheetData>
                    <row r="1">
                      <c r="A1"><v>2</v></c>
                      <c r="B1"><v>3</v></c>
                      <c r="C1"><f>SUM(A1:B1)</f><v>5</v></c>
                    </row>
                    <row r="2">
                      <c r="A2"><v>4</v></c>
                      <c r="B2"><v>5</v></c>
                      <c r="C2"><f>PRODUCT(A2:B2)</f><v>20</v></c>
                    </row>
                    <row r="3">
                      <c r="A3"><v>0</v></c>
                      <c r="B3"><v>0</v></c>
                      <c r="C3" t="b"><f>EXACT("Excel","Excel")</f><v>1</v></c>
                    </row>
                    <row r="4">
                      <c r="C4" t="str"><f>CONCAT("Free","excel")</f><v>Freeexcel</v></c>
                    </row>
                    <row r="5">
                      <c r="C5" t="e"><f>NA()</f><v>#N/A</v></c>
                    </row>
                    <row r="6">
                      <c r="A6"><v>1</v></c>
                      <c r="B6"><v>2</v></c>
                      <c r="C6"><v>3</v></c>
                      <c r="D6"><f>XLOOKUP(2,A6:C6,A6:C6)</f><v>2</v></c>
                    </row>
                    <row r="7">
                      <c r="A7"><v>1</v></c>
                      <c r="B7"><v>2</v></c>
                      <c r="C7"><v>3</v></c>
                      <c r="D7"><f>XMATCH(3,A7:C7)</f><v>3</v></c>
                    </row>
                    <row r="8">
                      <c r="C8"><f>SUM(SEQUENCE(3))</f><v>6</v></c>
                    </row>
                    <row r="9">
                      <c r="C9"><f>LET(x,5,x*2)</f><v>10</v></c>
                    </row>
                    <row r="10">
                      <c r="C10" t="str"><f>TEXTJOIN("|",TRUE,"A","","B")</f><v>A|B</v></c>
                    </row>
                    <row r="11">
                      <c r="A11" t="e"><v>#DIV/0!</v></c>
                      <c r="A12"><v>4</v></c>
                      <c r="A13"><v>6</v></c>
                      <c r="B11" t="str"><v>error row</v></c>
                      <c r="B12" t="str"><v>smaller</v></c>
                      <c r="B13" t="str"><v>larger</v></c>
                      <c r="D11" t="e"><f>XLOOKUP(5,A11:A13,B11:B13,"",-1)</f><v>#DIV/0!</v></c>
                    </row>
                    <row r="14">
                      <c r="A14" t="e"><v>#DIV/0!</v></c>
                      <c r="A15"><v>4</v></c>
                      <c r="A16"><v>6</v></c>
                      <c r="D14" t="e"><f>XMATCH(5,A14:A16,-1)</f><v>#DIV/0!</v></c>
                    </row>
                    <row r="17">
                      <c r="A17"><v>1</v></c>
                      <c r="D17" t="e"><f>TAKE(A17:A17,0)</f><v>#CALC!</v></c>
                    </row>
                    <row r="18">
                      <c r="A18"><v>1</v></c>
                      <c r="D18" t="e"><f>DROP(A18:A18,1)</f><v>#CALC!</v></c>
                    </row>
                  </sheetData>
                </worksheet>
                """);
        }

        stream.Position = 0;
        return stream;
    }

    private static void AddXml(ZipArchive archive, string path, string xml)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(XDocument.Parse(xml).ToString(SaveOptions.DisableFormatting));
    }

    private static string FindWorkspacePath(params string[] relativeParts)
    {
        return FindWorkspacePathFromSource(relativeParts);
    }

    private static string FindWorkspacePathFromSource(
        string[] relativeParts,
        [CallerFilePath] string sourceFilePath = "")
    {
        var current = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath) ?? AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(new[] { current.FullName }.Concat(relativeParts).ToArray());
            if (Directory.Exists(candidate))
                return candidate;

            current = current.Parent;
        }

        return Path.Combine(new[] { Directory.GetCurrentDirectory() }.Concat(relativeParts).ToArray());
    }
}

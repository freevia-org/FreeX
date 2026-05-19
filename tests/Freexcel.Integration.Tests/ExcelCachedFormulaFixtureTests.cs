using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using Freexcel.Core.Formula;
using Freexcel.Core.IO;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Integration.Tests;

public sealed class ExcelCachedFormulaFixtureTests
{
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
}

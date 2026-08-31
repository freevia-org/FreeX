using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round 64 regression tests:
/// <list type="bullet">
///   <item>
///     R64-io-data-validation-6-1 -- Date/Time data-validation bounds must be canonicalized to
///     Excel's OLE Automation date serial before being written to formula1/formula2 (Excel itself
///     would parse the raw human text "1/1/2024" as the arithmetic expression (1/1)/2024, a
///     near-zero number, silently corrupting the rule). Decimal/WholeNumber bounds must be written
///     using an invariant dot-decimal separator. A formula/cell-reference bound must be left
///     completely untouched.
///   </item>
///   <item>
///     R64-io-data-validation-6-2 -- on load, a second data-validation rule whose primary range
///     exactly equals an already-loaded rule's range must not be silently discarded as a duplicate
///     unless its content (type/operator/formula/messages) also matches. Two independent rules that
///     merely happen to share a range must both survive; a genuine ClosedXML split-artifact (same
///     range AND same content) must still collapse to one.
///   </item>
/// </list>
/// </summary>
public sealed class R64_DataValidationSerialAndContentDedupTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // ── R64-io-data-validation-6-1 ──────────────────────────────────────────

    [Fact]
    public void Save_DateValidation_HumanDateBounds_CanonicalizesToOleAutomationSerial()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");

        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 5, 1)),
            Type = DvType.Date,
            Operator = DvOperator.Between,
            Formula1 = "1/1/2024",
            Formula2 = "12/31/2024",
        });

        using var saved = XlsxPackageTestHelper.SaveWorkbook(workbook);
        var (formula1, formula2) = ReadFormulas(saved);

        // Excel's OLE Automation date serial for 2024-01-01 / 2024-12-31.
        formula1.Should().Be("45292",
            "Excel stores Date bounds as the OLE Automation date serial, never as human date text " +
            "-- '1/1/2024' would otherwise be parsed by Excel as the arithmetic expression (1/1)/2024");
        formula2.Should().Be("45657");

        // Reloading and reinterpreting the serial must reproduce the original calendar dates.
        DateTime.FromOADate(double.Parse(formula1!)).Date.Should().Be(new DateTime(2024, 1, 1));
        DateTime.FromOADate(double.Parse(formula2!)).Date.Should().Be(new DateTime(2024, 12, 31));
    }

    [Fact]
    public void Save_DecimalValidation_InvariantBound_StaysInvariantDotDecimal()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");

        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 5, 1)),
            Type = DvType.Decimal,
            Operator = DvOperator.GreaterThanOrEqual,
            Formula1 = "1.5",
        });

        using var saved = XlsxPackageTestHelper.SaveWorkbook(workbook);
        var (formula1, _) = ReadFormulas(saved);

        formula1.Should().Be("1.5",
            "a Decimal bound already in invariant dot-decimal form must round-trip unchanged");
    }

    [Fact]
    public void Save_DateValidation_FormulaReferenceBound_IsLeftUntouched()
    {
        // Sibling/no-regression case: a formula/cell-reference bound must never be reinterpreted as
        // a date/number -- it fails to parse as either, so it must pass through unchanged.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");

        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 5, 1)),
            Type = DvType.Date,
            Operator = DvOperator.GreaterThan,
            Formula1 = "=A1",
        });

        using var saved = XlsxPackageTestHelper.SaveWorkbook(workbook);
        var (formula1, _) = ReadFormulas(saved);

        formula1.Should().Be("=A1",
            "a formula/cell-reference bound must never be reinterpreted as a date or number");
    }

    private static (string? Formula1, string? Formula2) ReadFormulas(MemoryStream package)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/worksheets/sheet1.xml")!;
        using var stream = entry.Open();
        var dv = XDocument.Load(stream).Root!
            .Element(WorksheetNs + "dataValidations")?
            .Element(WorksheetNs + "dataValidation");
        var result = (
            dv?.Element(WorksheetNs + "formula1")?.Value,
            dv?.Element(WorksheetNs + "formula2")?.Value);
        package.Position = 0;
        return result;
    }

    // ── R64-io-data-validation-6-2 ──────────────────────────────────────────

    [Fact]
    public void Load_TwoDistinctRulesSharingSameRange_LoadsBothRulesNotJustOne()
    {
        var worksheetBody = """
            <dataValidations count="2">
              <dataValidation type="list" allowBlank="1" showInputMessage="1" showErrorMessage="1" sqref="A1:A10">
                <formula1>"Yes,No"</formula1>
              </dataValidation>
              <dataValidation type="custom" allowBlank="1" showInputMessage="1" showErrorMessage="1" sqref="A1:A10">
                <formula1>ISNUMBER(A1)</formula1>
              </dataValidation>
            </dataValidations>
            """;

        using var stream = BuildMinimalXlsx(worksheetBody);
        var workbook = new XlsxFileAdapter().Load(stream);
        var validations = workbook.GetSheetAt(0).DataValidations;

        validations.Should().HaveCount(2,
            "two rules with different type/formula that merely happen to share the exact same " +
            "range are independent rules, not a ClosedXML split-artifact duplicate, and must both load");
        validations.Should().Contain(dv => dv.Type == DvType.List && dv.Formula1 == "Yes,No");
        validations.Should().Contain(dv => dv.Type == DvType.Custom && dv.Formula1 == "ISNUMBER(A1)");
    }

    [Fact]
    public void Load_MultiAreaRuleWhoseSecondAreaClosedXmlSurfacesSeparately_StillCollapsesToOne()
    {
        // Sibling/no-regression case: a genuine ClosedXML split-artifact -- one Excel rule spanning
        // two areas that ClosedXML's own DataValidations enumeration surfaces as two entries with
        // identical content -- must still collapse back down to a single rule with AdditionalRanges.
        var worksheetBody = """
            <dataValidations count="1">
              <dataValidation type="whole" operator="between" allowBlank="1" showInputMessage="1" showErrorMessage="1" sqref="A2:A5 C2:C5">
                <formula1>1</formula1>
                <formula2>100</formula2>
              </dataValidation>
            </dataValidations>
            """;

        using var stream = BuildMinimalXlsx(worksheetBody);
        var workbook = new XlsxFileAdapter().Load(stream);
        var validations = workbook.GetSheetAt(0).DataValidations;

        validations.Should().ContainSingle(
            "a single multi-area Excel rule must remain a single rule after load, regardless of how " +
            "the underlying reader surfaces its areas internally");
        var dv = validations[0];
        dv.Type.Should().Be(DvType.WholeNumber);
        dv.Formula1.Should().Be("1");
        dv.Formula2.Should().Be("100");
    }

    [Fact]
    public void Load_DenseDistinctValidations_LoadsEveryRuleWithoutCrossRangeDeduplication()
    {
        const int ruleCount = 192;
        var rules = string.Join(
            Environment.NewLine,
            Enumerable.Range(1, ruleCount).Select(row =>
                $"""
                  <dataValidation type="whole" operator="equal" allowBlank="1" showInputMessage="1" showErrorMessage="1" sqref="A{row}">
                    <formula1>{row}</formula1>
                  </dataValidation>
                  """));
        var worksheetBody = $"""
            <dataValidations count="{ruleCount}">
            {rules}
            </dataValidations>
            """;

        using var stream = BuildMinimalXlsx(worksheetBody);
        var validations = new XlsxFileAdapter().Load(stream).GetSheetAt(0).DataValidations;

        validations.Should().HaveCount(ruleCount,
            "distinct dense validation ranges are independent rules and must not be collapsed merely because their shape is similar");
        validations.Select(validation => validation.AppliesTo.Start.Row)
            .Should().Equal(Enumerable.Range(1, ruleCount).Select(row => (uint)row));
    }

    /// <summary>
    /// Builds the smallest possible valid XLSX stream that contains one worksheet with a few
    /// numeric cells and the given extra worksheet-root XML (dataValidations) appended after
    /// &lt;sheetData&gt;.
    /// </summary>
    private static MemoryStream BuildMinimalXlsx(string worksheetBodyXml)
    {
        var worksheetXml = $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="{WorksheetNs}">
              <sheetData>
                <row r="1"><c r="A1"><v>1</v></c></row>
                <row r="2"><c r="A2"><v>2</v></c></row>
                <row r="3"><c r="A3"><v>3</v></c></row>
                <row r="4"><c r="A4"><v>4</v></c></row>
                <row r="5"><c r="A5"><v>5</v></c></row>
              </sheetData>
              {worksheetBodyXml}
            </worksheet>
            """;
        var workbookXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets>
                <sheet name="Sheet1" sheetId="1" r:id="rId1"/>
              </sheets>
            </workbook>
            """;
        var workbookRels = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1"
                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"
                Target="worksheets/sheet1.xml"/>
            </Relationships>
            """;
        var packageRels = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1"
                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"
                Target="xl/workbook.xml"/>
            </Relationships>
            """;
        var contentTypes = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml"  ContentType="application/xml"/>
              <Override PartName="/xl/workbook.xml"
                ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
              <Override PartName="/xl/worksheets/sheet1.xml"
                ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
            </Types>
            """;

        var ms = XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", contentTypes),
            ("_rels/.rels", packageRels),
            ("xl/workbook.xml", workbookXml),
            ("xl/_rels/workbook.xml.rels", workbookRels),
            ("xl/worksheets/sheet1.xml", worksheetXml));
        return ms;
    }
}

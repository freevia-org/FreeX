using System.IO;
using FluentAssertions;
using FreeX.Core.Model;
using FreeX.Core.Calc;
using FreeX.Core.Formula;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for the dynamic-array spill continuation cell bug.
///
/// Excel 365 dynamic array formulas (t="array" aca="1" ca="1") store their
/// non-anchor spill cells as empty-formula cells with a &lt;f ca="1"/&gt; marker
/// rather than as plain &lt;v&gt; value cells.  FreeX must recognise these as
/// provisional spill cells so they do NOT block the anchor's spill on recalculation.
///
/// Root cause: XlsxFileAdapter's branch for plain value cells
/// (!xlCell.HasFormula) misses cells that carry &lt;f ca="1"/&gt;, which ClosedXML
/// sees as HasFormula=true / FormulaA1="".  Those cells were treated as independent
/// (empty) formula cells, blocking the anchor and causing #SPILL!.
/// </summary>
public sealed class XlsxDynamicArraySpillContinuationTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds an in-memory XLSX containing:
    ///   A1 = {=SEQUENCE(3)}  with declared ref A1:A3  (dynamic array, aca="1" ca="1")
    ///   A2 = &lt;f ca="1"/&gt; with cached value 2    (spill continuation, NOT a formula)
    ///   A3 = &lt;f ca="1"/&gt; with cached value 3    (spill continuation, NOT a formula)
    ///   B1 = 10  (plain value, used to verify B column unaffected)
    /// </summary>
    private static MemoryStream CreateDynamicArrayWorkbookWithSpillContinuationCells()
    {
        return XlsxPackageTestFixtures.CreatePackage(
            (
                "[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """),
            (
                "_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """),
            (
                "xl/workbook.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Data" sheetId="1" r:id="rId1"/>
                  </sheets>
                  <calcPr calcId="191028"/>
                </workbook>
                """),
            (
                "xl/_rels/workbook.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                </Relationships>
                """),
            (
                "xl/styles.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <fonts><font><sz val="11"/><name val="Calibri"/></font></fonts>
                  <fills><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>
                  <borders><border><left/><right/><top/><bottom/><diagonal/></border></borders>
                  <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
                  <cellXfs><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs>
                </styleSheet>
                """),
            (
                "xl/worksheets/sheet1.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <sheetData>
                    <row r="1">
                      <c r="A1"><f t="array" aca="1" ref="A1:A3" ca="1">SEQUENCE(3)</f><v>1</v></c>
                      <c r="B1"><v>10</v></c>
                    </row>
                    <row r="2">
                      <c r="A2"><f ca="1"/><v>2</v></c>
                    </row>
                    <row r="3">
                      <c r="A3"><f ca="1"/><v>3</v></c>
                    </row>
                  </sheetData>
                </worksheet>
                """));
    }

    // ── test 1: display without recalc shows cached values ─────────────────────

    [Fact]
    public void DynamicArraySpillContinuationCells_LoadedWithoutRecalc_SpillCellsHaveCachedValues()
    {
        using var package = CreateDynamicArrayWorkbookWithSpillContinuationCells();

        var workbook = new XlsxFileAdapter().Load(package);
        var sheet = workbook.GetSheetAt(0);

        // On load (no recalc), the cached values must be visible.
        sheet.GetValue(1, 1).Should().Be(new NumberValue(1),
            "A1 is the anchor; its cached value 1 must be preserved on load");
        sheet.GetValue(2, 1).Should().Be(new NumberValue(2),
            "A2 is a spill continuation; its cached value 2 must be visible on load");
        sheet.GetValue(3, 1).Should().Be(new NumberValue(3),
            "A3 is a spill continuation; its cached value 3 must be visible on load");
    }

    // ── test 2: recalc re-spills correctly, no #SPILL! ─────────────────────────

    [Fact]
    public void DynamicArraySpillContinuationCells_AfterRecalc_AnchorSpillsWithoutBlocker()
    {
        using var package = CreateDynamicArrayWorkbookWithSpillContinuationCells();

        var workbook = new XlsxFileAdapter().Load(package);

        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        engine.RecalculateAllFormulas(workbook);

        var sheet = workbook.GetSheetAt(0);

        // After recalc, SEQUENCE(3) should spill 1, 2, 3 — NOT produce #SPILL!
        sheet.GetValue(1, 1).Should().Be(new NumberValue(1),
            "A1 anchor after recalc: SEQUENCE(3)[0]=1");
        sheet.GetValue(2, 1).Should().Be(new NumberValue(2),
            "A2 spill after recalc: SEQUENCE(3)[1]=2, must NOT be #SPILL!");
        sheet.GetValue(3, 1).Should().Be(new NumberValue(3),
            "A3 spill after recalc: SEQUENCE(3)[2]=3, must NOT be #SPILL!");

        // Anchor itself must not have #SPILL! error
        var anchorCell = sheet.GetCell(1, 1);
        anchorCell.Should().NotBeNull();
        anchorCell!.Value.Should().NotBe(ErrorValue.Spill,
            "the anchor must not produce #SPILL! — the spill continuation cells should not block it");
    }
}

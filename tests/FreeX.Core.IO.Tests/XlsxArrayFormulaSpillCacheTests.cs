using System.IO;
using FluentAssertions;
using FreeX.Core.Model;
using FreeX.Core.Calc;
using FreeX.Core.Formula;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for the array-formula cached-spill-value display bug.
///
/// When an XLSX has <c f t="array" ref="A1:C1"/> without fullCalcOnLoad, FreeX
/// must NOT recalculate on open.  The spill cells (B1, C1) carry Excel's cached
/// <v> values; these must be visible on open WITHOUT recalc.  At the same time,
/// when the user DOES recalculate, the anchor re-evaluates and the spill cells
/// update to the freshly computed values — without producing #SPILL!.
/// </summary>
public sealed class XlsxArrayFormulaSpillCacheTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds an in-memory XLSX containing:
    ///   A1 = 10, B1 = 20, C1 = 30      (data row)
    ///   A3 = {=A1:C1*2}  declared ref A3:C3   (array formula, CSE-style)
    ///   B3 = 40 (cached), C3 = 60 (cached)    (Excel's cached spill results)
    ///   calcPr has NO fullCalcOnLoad            (so FreeX won't recalc on open)
    /// </summary>
    private static MemoryStream CreateArrayFormulaWorkbookWithCachedSpill()
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
                      <c r="A1"><v>10</v></c>
                      <c r="B1"><v>20</v></c>
                      <c r="C1"><v>30</v></c>
                    </row>
                    <row r="3">
                      <c r="A3"><f t="array" ref="A3:C3">A1:C1*2</f><v>20</v></c>
                      <c r="B3"><v>40</v></c>
                      <c r="C3"><v>60</v></c>
                    </row>
                  </sheetData>
                </worksheet>
                """));
    }

    // ── test 1: display without recalc ────────────────────────────────────────

    [Fact]
    public void ArrayFormulaWithCachedSpill_LoadedWithoutRecalc_SpillCellsHaveCachedValues()
    {
        using var package = CreateArrayFormulaWorkbookWithCachedSpill();

        var workbook = new XlsxFileAdapter().Load(package);
        var sheet = workbook.GetSheetAt(0);

        // Anchor: formula cell at A3 — should have its cached value (20)
        sheet.GetValue(3, 1).Should().Be(new NumberValue(20),
            "A3 is the anchor; its cached <v>20</v> must be preserved on load");

        // Spill targets: B3 and C3 — must have Excel's cached values (40, 60)
        // WITHOUT any recalculation having run.
        sheet.GetValue(3, 2).Should().Be(new NumberValue(40),
            "B3 is a spill target; its cached <v>40</v> must be visible on load without recalc");
        sheet.GetValue(3, 3).Should().Be(new NumberValue(60),
            "C3 is a spill target; its cached <v>60</v> must be visible on load without recalc");
    }

    // ── test 2: recalc still works, no #SPILL! ────────────────────────────────

    [Fact]
    public void ArrayFormulaWithCachedSpill_AfterRecalc_SpillCellsHaveRecomputedValues()
    {
        using var package = CreateArrayFormulaWorkbookWithCachedSpill();

        var workbook = new XlsxFileAdapter().Load(package);

        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        engine.RecalculateAllFormulas(workbook);

        var sheet = workbook.GetSheetAt(0);

        // After recalc, A1:C1*2 with A1=10, B1=20, C1=30 should give 20, 40, 60
        sheet.GetValue(3, 1).Should().Be(new NumberValue(20),
            "A3 anchor recalc: 10*2=20");
        sheet.GetValue(3, 2).Should().Be(new NumberValue(40),
            "B3 spill recalc: 20*2=40, and must NOT be #SPILL!");
        sheet.GetValue(3, 3).Should().Be(new NumberValue(60),
            "C3 spill recalc: 30*2=60, and must NOT be #SPILL!");

        // Specifically assert no #SPILL! error on the anchor
        var anchorCell = sheet.GetCell(3, 1);
        anchorCell.Should().NotBeNull();
        anchorCell!.Value.Should().NotBe(ErrorValue.Spill,
            "the anchor must not produce #SPILL! after recalc");
    }
}

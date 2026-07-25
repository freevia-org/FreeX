using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for R86-io-shared-array-formula-5-2: on a full (ClosedXML-regenerate)
/// save, a legacy CSE array formula's non-anchor member cells were silently blanked. Range
/// .FormulaArrayA1 (used to write the anchor's array-formula range) only wires up ClosedXML's
/// Formula slice for every cell in the extent -- it has no way to evaluate the formula, so it
/// never carries any cached value for the non-anchor cells. XlsxFileAdapter.Save.cs's outer loop
/// used to skip re-writing those member cells entirely (assuming their value was "already
/// represented by the array range write"), so they round-tripped as fully empty &lt;c/&gt;
/// elements and reloaded as BlankValue.
/// </summary>
public sealed class R86_CseArrayMemberValueSaveTests
{
    private static byte[] CreateLegacyArraySourcePackage()
    {
        using var package = XlsxPackageTestFixtures.CreatePackage(
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
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Data" sheetId="1" r:id="rId1"/>
                  </sheets>
                  <calcPr calcId="191029"/>
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
                  <fonts count="1"><font><sz val="11"/><color theme="1"/><name val="Calibri"/><family val="2"/><scheme val="minor"/></font></fonts>
                  <fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>
                  <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
                  <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
                  <cellXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs>
                  <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
                  <dxfs count="0"/>
                  <tableStyles count="0" defaultTableStyle="TableStyleMedium2" defaultPivotStyle="PivotStyleLight16"/>
                </styleSheet>
                """),
            (
                "xl/worksheets/sheet1.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <dimension ref="A1:C3"/>
                  <sheetData>
                    <row r="1"><c r="A1"><v>2</v></c><c r="C1"><f t="array" ref="C1:C3">A1:A3*B1:B3</f><v>6</v></c></row>
                    <row r="2"><c r="C2"><v>40</v></c></row>
                    <row r="3"><c r="C3"><v>90</v></c></row>
                  </sheetData>
                </worksheet>
                """));

        return package.ToArray();
    }

    [Fact]
    public void Save_AfterUnrelatedEditForcesFullSave_PreservesArrayMemberCachedValues()
    {
        var sourceBytes = CreateLegacyArraySourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);

        // Sanity: C2/C3 loaded as provisional/value cells carrying their own cached results.
        sheet.GetValue(2, 3).Should().Be(new NumberValue(40));
        sheet.GetValue(3, 3).Should().Be(new NumberValue(90));
        sheet.GetCell(2, 3)!.HasFormula.Should().BeFalse();
        sheet.GetCell(3, 3)!.HasFormula.Should().BeFalse();

        // Force the full (ClosedXML-regenerate) save path, as any unrelated edit elsewhere would
        // once the patch path can no longer replay the source package verbatim.
        XlsxFileAdapter.DetachSourcePackage(workbook);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(99)); // unrelated edit (A1)

        using var savedStream = new MemoryStream();
        adapter.Save(workbook, savedStream);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);
        savedStream.Position = 0;

        var reloaded = adapter.Load(savedStream);
        var reloadedSheet = reloaded.GetSheetAt(0);

        // Pre-fix these came back BlankValue -- the array-formula range write dropped the member
        // cells' cached results entirely.
        reloadedSheet.GetValue(2, 3).Should().Be(new NumberValue(40),
            "a legacy CSE array formula's non-anchor member cell must keep its cached result across a full save");
        reloadedSheet.GetValue(3, 3).Should().Be(new NumberValue(90),
            "a legacy CSE array formula's non-anchor member cell must keep its cached result across a full save");
    }

    /// <summary>
    /// No-regression sibling: the array anchor's own formula/array identity, and an entirely
    /// unrelated plain value cell, must still round-trip correctly -- guarding against the fix
    /// (restoring member values via a reflected ClosedXML setter) having any side effect beyond
    /// the member cells it targets.
    /// </summary>
    [Fact]
    public void Save_AfterUnrelatedEditForcesFullSave_AnchorAndUnrelatedCellStillRoundTripCorrectly()
    {
        var sourceBytes = CreateLegacyArraySourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        XlsxFileAdapter.DetachSourcePackage(workbook);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(99)); // unrelated edit (A1)

        using var savedStream = new MemoryStream();
        adapter.Save(workbook, savedStream);
        savedStream.Position = 0;

        var reloaded = adapter.Load(savedStream);
        var reloadedSheet = reloaded.GetSheetAt(0);

        var anchor = reloadedSheet.GetCell(1, 3)!; // C1
        anchor.HasFormula.Should().BeTrue();
        anchor.FormulaText.Should().Be("A1:A3*B1:B3");
        anchor.ArrayMode.Should().Be(FormulaArrayMode.Dynamic);
        anchor.LegacyArrayRows.Should().Be(3u);

        reloadedSheet.GetValue(1, 1).Should().Be(new NumberValue(99), "the unrelated edit itself must still round-trip");
    }
}

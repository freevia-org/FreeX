using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-13 bucket S10 fix verification (patch-save deletion of a shared-formula master cell).
/// See scratchpad r13-S10.md for the full finding text.
/// </summary>
public sealed class FreeXR13S10Tests
{
    // R13-shared-formulas-io-1: an Excel-authored package stores a dragged formula column as one
    // shared group: B1 is the master (<f t="shared" ref="B1:B3" si="0">$A$1*2</f>) and B2/B3 are
    // slaves (<f t="shared" si="0"/>, no formula text of their own -- they rely on the master).
    // Clearing B1 and saving used to patch-apply a bare `cell.Remove()` on the master's <c>
    // element, deleting the ONLY place si="0"'s formula text/ref lived while leaving B2/B3's
    // dangling `<f t="shared" si="0"/>` untouched -- an orphaned reference that corrupts the
    // package (Excel repair prompt; B2/B3 lose their formulas). The fix must recognize that the
    // deleted cell's <f> carries attributes (shared/array/data-table) and bail to the full-save
    // fallback instead of patching, so B2/B3 keep working, correctly-valued formulas.
    [Fact]
    public void Save_DeletingSharedFormulaMasterCell_DoesNotOrphanSlaveFormulas()
    {
        var sourceBytes = CreateSharedFormulaGroupSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        var b3 = new CellAddress(sheet.Id, 3, 2);

        // Sanity: the loaded model expanded the shared group into three independent formula
        // cells, all referencing the same absolute cell (so the fitted formula text is identical
        // regardless of the shared-formula offset direction ClosedXML applies).
        sheet.GetCell(b1)!.FormulaText.Should().Be("$A$1*2");
        sheet.GetCell(b2)!.FormulaText.Should().Be("$A$1*2");
        sheet.GetCell(b3)!.FormulaText.Should().Be("$A$1*2");

        sheet.ClearCell(b1);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        // A bare cell.Remove() on a shared-formula master would have "succeeded" as a patch save;
        // deleting it must instead be recognized as unsafe and fall back to a full regenerate.
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);

        using var reload = new MemoryStream(savedBytes, writable: false);
        var reloadedSheet = adapter.Load(reload).GetSheetAt(0);

        reloadedSheet.GetCell(b1).Should().BeNull("B1 was cleared by the user");
        reloadedSheet.GetCell(b2)!.FormulaText.Should().Be("$A$1*2");
        reloadedSheet.GetCell(b2)!.Value.Should().Be(new NumberValue(4));
        reloadedSheet.GetCell(b3)!.FormulaText.Should().Be("$A$1*2");
        reloadedSheet.GetCell(b3)!.Value.Should().Be(new NumberValue(4));
    }

    private static byte[] CreateSharedFormulaGroupSourcePackage()
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
                  <dimension ref="A1:B3"/>
                  <sheetData>
                    <row r="1"><c r="A1"><v>2</v></c><c r="B1"><f t="shared" ref="B1:B3" si="0">$A$1*2</f><v>4</v></c></row>
                    <row r="2"><c r="B2"><f t="shared" si="0"/><v>4</v></c></row>
                    <row r="3"><c r="B3"><f t="shared" si="0"/><v>4</v></c></row>
                  </sheetData>
                </worksheet>
                """));

        return package.ToArray();
    }
}

using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for FreeX cleanup batch MED9 (MED finding P108).
///
/// A dynamic/CSE array formula declared over C1:C3 whose last member (C3) carries no cached
/// &lt;v&gt; (e.g. a producer that omits cached spill values, or simply a blank result) must still
/// round-trip its full declared extent. Previously the XLSX loader skipped registering a
/// provisional spill-member entry for any member cell whose mapped value was BlankValue, so
/// Sheet.TryGetArrayExtent could only see the bounding box of members that DID have a cached
/// value — silently shrinking the saved array range (C1:C2 instead of C1:C3) and detaching C3.
/// </summary>
public sealed class FreeXCleanupMED9Tests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void CseArray_WithBlankLastMember_PreservesFullDeclaredExtent_OnRoundTripSave()
    {
        using var package = CreateCseArrayWorkbookWithBlankLastMember();

        var workbook = new XlsxFileAdapter().Load(package);
        var sheet = workbook.GetSheetAt(0);

        // Root cause check: the loader must recognise C3 as a provisional member of the C1:C3
        // array even though it carries no cached value, so the array's full extent is recoverable.
        var anchorAddr = new CellAddress(sheet.Id, 1, 3);
        sheet.TryGetArrayExtent(anchorAddr, out var anchor, out var rows, out var cols)
            .Should().BeTrue("the anchor's declared C1:C3 array range must still be recognised");
        anchor.Row.Should().Be(1);
        anchor.Col.Should().Be(3);
        rows.Should().Be(3, "the declared ref range is C1:C3 (3 rows) even though C3 has no cached value");
        cols.Should().Be(1);

        // End-to-end: saving must write the array formula over the FULL C1:C3 range, not shrink it
        // to C1:C2 (which would silently detach C3 from the array on round-trip). Defining a new
        // named range is an unsupported model delta for the cell-patch path, forcing a FULL save
        // (rather than the unchanged-model source-copy path, which would trivially echo the
        // original bytes back out without exercising Sheet.TryGetArrayExtent at all).
        var adapter = new XlsxFileAdapter();
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should().BeTrue(blockReason);
        workbook.DefineNamedRange("MED9UnrelatedName", new GridRange(
            new CellAddress(sheet.Id, 5, 5),
            new CellAddress(sheet.Id, 5, 5)));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var sheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var arrayFormula = sheetXml.Descendants(WorkbookNs + "f")
            .FirstOrDefault(f => f.Attribute("t")?.Value == "array");

        arrayFormula.Should().NotBeNull("the anchor must still be saved as an array formula");
        arrayFormula!.Attribute("ref")!.Value.Should().Be("C1:C3",
            "the array's declared extent must round-trip as the full C1:C3 range, not shrink to C1:C2");
    }

    /// <summary>
    /// Builds an in-memory XLSX containing a dynamic-array formula declared over C1:C3, matching
    /// the shape XlsxDynamicArraySpillContinuationTests already proves the loader recognises
    /// (Excel 365's &lt;f ca="1"/&gt; empty-formula spill-continuation marker), except C3's cached
    /// value is omitted entirely (blank result — e.g. a producer that skips caching a blank spill
    /// member):
    ///   C1 = {=SEQUENCE(3)} anchor, ref="C1:C3", aca/ca="1", cached value 1
    ///   C2 = &lt;f ca="1"/&gt; spill-continuation cell, cached value 2
    ///   C3 = &lt;f ca="1"/&gt; spill-continuation cell, NO cached &lt;v&gt; at all (blank result)
    /// </summary>
    private static MemoryStream CreateCseArrayWorkbookWithBlankLastMember()
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
                      <c r="C1"><f t="array" aca="1" ref="C1:C3" ca="1">SEQUENCE(3)</f><v>1</v></c>
                    </row>
                    <row r="2">
                      <c r="C2"><f ca="1"/><v>2</v></c>
                    </row>
                    <row r="3">
                      <c r="C3"><f ca="1"/></c>
                    </row>
                  </sheetData>
                </worksheet>
                """));
    }
}

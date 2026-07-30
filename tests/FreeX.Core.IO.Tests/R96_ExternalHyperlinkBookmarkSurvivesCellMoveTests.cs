using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round 96 regression tests for src/FreeX.Core.IO/XlsxFileAdapter.Hyperlinks.cs and
/// src/FreeX.Core.IO/XlsxFileAdapter.SavePostProcessing.cs:
///
/// R96-io-hyperlink-external-bookmark: an EXTERNAL hyperlink that also carries a "location"
/// sub-address (Excel's "Insert Hyperlink &gt; Existing File or Web Page &gt; Bookmark..." feature,
/// which produces both an r:id relationship AND a "location" attribute per ECMA-376) previously lost
/// that sub-address the moment its anchor cell moved to a different address (e.g. a row insert above
/// it) AND the workbook was FULL-saved (ClosedXML). CreateXlsxHyperlink never set
/// XLHyperlink.InternalAddress for anything but a PlaceInThisDocument link, so the only thing that
/// ever backfilled the missing "location" was XlsxWorksheetMetadataPreserver's post-save merge pass
/// -- which matches a hyperlink between the pre-edit source XML and the freshly regenerated
/// worksheet purely by the "ref" (cell address) STRING, so it misses entirely once the anchor cell's
/// address changes. Fixed via a new post-processing step (FixExternalHyperlinkBookmarkLocations)
/// that looks each affected hyperlink up by its CURRENT address straight from the live model instead
/// of a stale source-XML ref, so it survives the shift unconditionally.
/// </summary>
public sealed class R96_ExternalHyperlinkBookmarkSurvivesCellMoveTests
{
    [Fact]
    public void Save_ExternalHyperlinkWithBookmark_SurvivesFullSaveAfterAnchorCellMoves()
    {
        var sourceBytes = CreateExternalHyperlinkWithLocationSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        var originalAddress = new CellAddress(sheet.Id, 1, 1); // A1
        sheet.Hyperlinks[originalAddress].Should().Be("https://example.com/data.xlsx");
        sheet.HyperlinkMetadata[originalAddress].Bookmark.Should().Be("Sheet2!A5");

        // Insert a row above row 1 -- this is the SAME dictionary-key move RowColumnShiftHelpers
        // performs for a real row-insert-above, shifting the hyperlink's anchor cell from A1 to A2.
        var ctx = new TestCommandContext(workbook);
        new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 1).Apply(ctx).Success.Should().BeTrue();

        var movedAddress = new CellAddress(sheet.Id, 2, 1); // A2
        sheet.Hyperlinks.Should().NotContainKey(originalAddress);
        sheet.Hyperlinks[movedAddress].Should().Be("https://example.com/data.xlsx");
        sheet.HyperlinkMetadata[movedAddress].Bookmark.Should().Be(
            "Sheet2!A5",
            "the insert-row shift itself must not disturb the in-memory model's bookmark");

        // Force the FULL (ClosedXML) save path: adding a sheet is a structural change the fast
        // cell-patch path cannot represent.
        workbook.AddSheet("ExtraSheet");

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);

        ReadHyperlinkAttribute(savedBytes, "xl/worksheets/sheet1.xml", "A2", "location")
            .Should()
            .Be(
                "Sheet2!A5",
                "an external hyperlink's bookmark sub-address must survive a full save even after its anchor cell moves to a new address");

        // The URL and tooltip must still be intact too (they never regressed, but assert them
        // alongside the fix so a future change can't silently break one while "fixing" the other).
        ReadHyperlinkRelationshipTarget(savedBytes, "xl/worksheets/sheet1.xml", "xl/worksheets/_rels/sheet1.xml.rels", "A2")
            .Should()
            .Be("https://example.com/data.xlsx");
        ReadHyperlinkAttribute(savedBytes, "xl/worksheets/sheet1.xml", "A2", "tooltip")
            .Should()
            .Be("Jump to name");

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloadedSheet = adapter.Load(reloadStream).GetSheetAt(0);
        var reloadedAddress = new CellAddress(reloadedSheet.Id, 2, 1);
        reloadedSheet.Hyperlinks[reloadedAddress].Should().Be("https://example.com/data.xlsx");
        reloadedSheet.HyperlinkMetadata[reloadedAddress].Bookmark.Should().Be("Sheet2!A5");
    }

    [Fact]
    public void Save_ExternalHyperlinkWithoutBookmark_StillSurvivesFullSaveAfterAnchorCellMoves()
    {
        // Sibling no-regression case: a plain external hyperlink with NO bookmark must keep saving
        // and reloading correctly after the same cell-move + full-save sequence, with no spurious
        // "location" attribute ever added.
        var sourceBytes = CreateExternalHyperlinkWithLocationSourcePackage(location: null);
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        var ctx = new TestCommandContext(workbook);
        new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 1).Apply(ctx).Success.Should().BeTrue();

        workbook.AddSheet("ExtraSheet");

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);

        ReadHyperlinkAttribute(savedBytes, "xl/worksheets/sheet1.xml", "A2", "location")
            .Should()
            .BeNull("a hyperlink with no bookmark must never gain a spurious location attribute");
        ReadHyperlinkRelationshipTarget(savedBytes, "xl/worksheets/sheet1.xml", "xl/worksheets/_rels/sheet1.xml.rels", "A2")
            .Should()
            .Be("https://example.com/data.xlsx");

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloadedSheet = adapter.Load(reloadStream).GetSheetAt(0);
        var reloadedAddress = new CellAddress(reloadedSheet.Id, 2, 1);
        reloadedSheet.Hyperlinks[reloadedAddress].Should().Be("https://example.com/data.xlsx");
        reloadedSheet.HyperlinkMetadata[reloadedAddress].Bookmark.Should().BeEmpty();
    }

    private static byte[] CreateExternalHyperlinkWithLocationSourcePackage(string? location = "Sheet2!A5")
    {
        var locationAttr = location is null ? "" : $" location=\"{location}\"";
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
                    <sheet name="Sheet1" sheetId="1" r:id="rId1"/>
                  </sheets>
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
                $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <dimension ref="A1:A1"/>
                  <sheetData>
                    <row r="1"><c r="A1" t="inlineStr"><is><t>Jump</t></is></c></row>
                  </sheetData>
                  <hyperlinks>
                    <hyperlink ref="A1" r:id="rIdExt"{locationAttr} tooltip="Jump to name"/>
                  </hyperlinks>
                </worksheet>
                """),
            (
                "xl/worksheets/_rels/sheet1.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdExt" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink" Target="https://example.com/data.xlsx" TargetMode="External"/>
                </Relationships>
                """));

        return package.ToArray();
    }

    private static string? ReadHyperlinkAttribute(
        byte[] packageBytes,
        string worksheetPath,
        string reference,
        string attributeName)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var document = XlsxPackageTestFixtures.LoadPackageXml(archive, worksheetPath);
        var ns = document.Root!.Name.Namespace;
        var hyperlinks = document.Root.Element(ns + "hyperlinks");
        return hyperlinks
            ?.Elements(ns + "hyperlink")
            .SingleOrDefault(element => string.Equals(element.Attribute("ref")?.Value, reference, StringComparison.OrdinalIgnoreCase))
            ?.Attribute(attributeName)
            ?.Value;
    }

    private static string? ReadHyperlinkRelationshipTarget(
        byte[] packageBytes,
        string worksheetPath,
        string relsPath,
        string reference)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var document = XlsxPackageTestFixtures.LoadPackageXml(archive, worksheetPath);
        var ns = document.Root!.Name.Namespace;
        var relNs = (XNamespace)"http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var hyperlinks = document.Root.Element(ns + "hyperlinks");
        var rId = hyperlinks
            ?.Elements(ns + "hyperlink")
            .SingleOrDefault(element => string.Equals(element.Attribute("ref")?.Value, reference, StringComparison.OrdinalIgnoreCase))
            ?.Attribute(relNs + "id")
            ?.Value;
        if (string.IsNullOrEmpty(rId))
            return null;

        var relsDocument = XlsxPackageTestFixtures.LoadPackageXml(archive, relsPath);
        var relsRootNs = relsDocument.Root!.Name.Namespace;
        return relsDocument.Root
            .Elements(relsRootNs + "Relationship")
            .SingleOrDefault(element => string.Equals(element.Attribute("Id")?.Value, rId, StringComparison.OrdinalIgnoreCase))
            ?.Attribute("Target")
            ?.Value;
    }
}

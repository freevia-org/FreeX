using System.IO.Compression;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round 79 regression test for src/FreeX.Core.IO/XlsxWorksheetMetadataPreserver.CellMetadata.cs
/// (MergeWorksheetHyperlinkMetadata):
///  - R79-io-hyperlink-name-5-1: editing an external ("Existing File &gt; Bookmark...") hyperlink's
///    target/location/tooltip and forcing a FULL (ClosedXML) save must not resurrect the stale
///    location/tooltip from the pristine pre-edit source snapshot -- ClosedXML has no API to
///    natively serialize an external hyperlink's "location" sub-address (and omits "tooltip"
///    entirely once the model's ScreenTip is blank), so the preserver used to blindly backfill
///    both attributes from the ORIGINAL source XML whenever the freshly regenerated hyperlink
///    element was missing them, with no check that the edit actually changed them.
/// </summary>
public sealed class R79_ExternalHyperlinkEditResurrectionTests
{
    [Fact]
    public void Save_EditedExternalHyperlinkClearingBookmarkAndTooltip_DoesNotResurrectStaleValues()
    {
        var sourceBytes = CreateExternalHyperlinkSourcePackage(
            target: "https://example.com/data.xlsx",
            location: "Sheet2!A5",
            tooltip: "Old ScreenTip text");
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.Hyperlinks[address].Should().Be("https://example.com/data.xlsx");
        sheet.HyperlinkMetadata[address].Bookmark.Should().Be("Sheet2!A5");
        sheet.HyperlinkMetadata[address].ScreenTip.Should().Be("Old ScreenTip text");

        // Edit the hyperlink: point it at an unrelated file and explicitly clear the sub-address
        // and tooltip.
        sheet.Hyperlinks[address] = "https://example.com/OTHER-FILE.xlsx";
        sheet.HyperlinkMetadata[address] = new HyperlinkMetadata(
            HyperlinkTargetKind.ExistingFileOrWebPage,
            ScreenTip: "",
            Bookmark: "");

        // Force the FULL (ClosedXML) save path: adding a sheet is a structural change the fast
        // cell-patch path cannot represent, and XlsxCellPatchBaseline.ApplyHyperlinkChanges also
        // unconditionally bails for any hyperlink carrying an r:id, so this is the only save path
        // reachable for an edited external hyperlink regardless.
        workbook.AddSheet("ExtraSheet");

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloadedSheet = adapter.Load(reloadStream).GetSheetAt(0);
        var reloadedAddress = new CellAddress(reloadedSheet.Id, 1, 1);

        reloadedSheet.Hyperlinks[reloadedAddress].Should().Be("https://example.com/OTHER-FILE.xlsx");
        reloadedSheet.HyperlinkMetadata[reloadedAddress].Bookmark.Should().BeEmpty(
            "the user cleared the sub-address; the stale 'Sheet2!A5' from the original hyperlink must not be resurrected");
        reloadedSheet.HyperlinkMetadata[reloadedAddress].ScreenTip.Should().BeEmpty(
            "the user cleared the tooltip; the stale 'Old ScreenTip text' from the original hyperlink must not be resurrected");
    }

    [Fact]
    public void Save_UneditedExternalHyperlinkWithBookmarkAndTooltip_StillPreservesThemOnFullSave()
    {
        // Sibling no-regression case: when the external hyperlink's location/tooltip are left
        // untouched (still needed because ClosedXML cannot natively emit them), a full save must
        // still restore them from the current model -- the fix must not turn the backfill off
        // entirely, only stop it from reading the stale pre-edit XML instead of the live model.
        var sourceBytes = CreateExternalHyperlinkSourcePackage(
            target: "https://example.com/data.xlsx",
            location: "Sheet2!A5",
            tooltip: "Old ScreenTip text");
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        var address = new CellAddress(sheet.Id, 1, 1);

        // No edit to the hyperlink itself; force a full save via an unrelated structural change.
        workbook.AddSheet("ExtraSheet");

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloadedSheet = adapter.Load(reloadStream).GetSheetAt(0);
        var reloadedAddress = new CellAddress(reloadedSheet.Id, 1, 1);

        reloadedSheet.Hyperlinks[reloadedAddress].Should().Be("https://example.com/data.xlsx");
        reloadedSheet.HyperlinkMetadata[reloadedAddress].Bookmark.Should().Be("Sheet2!A5");
        reloadedSheet.HyperlinkMetadata[reloadedAddress].ScreenTip.Should().Be("Old ScreenTip text");
    }

    private static byte[] CreateExternalHyperlinkSourcePackage(string target, string? location, string? tooltip)
    {
        var locationAttr = location is null ? "" : $" location=\"{location}\"";
        var tooltipAttr = tooltip is null ? "" : $" tooltip=\"{tooltip}\"";
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
                    <hyperlink ref="A1" r:id="rIdExt"{locationAttr}{tooltipAttr}/>
                  </hyperlinks>
                </worksheet>
                """),
            (
                "xl/worksheets/_rels/sheet1.xml.rels",
                $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdExt" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink" Target="{target}" TargetMode="External"/>
                </Relationships>
                """));

        return package.ToArray();
    }
}

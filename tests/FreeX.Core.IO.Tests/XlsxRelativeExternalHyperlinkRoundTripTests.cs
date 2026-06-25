using System.IO;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for M4: relative-path external hyperlinks must round-trip as external links,
/// not be silently demoted to in-document (PlaceInThisDocument) references.
/// </summary>
public sealed class XlsxRelativeExternalHyperlinkRoundTripTests
{
    // M4 regression: a relative file path (e.g. docs/report.pdf, ../other.xlsx) is a valid
    // external hyperlink target in Excel. Before the fix, CreateXlsxHyperlink's else branch
    // (non-absolute URI) set IsExternal=false / InternalAddress=linkTarget, corrupting the link.
    [Fact]
    public void XlsxAdapter_RoundTrip_RelativeExternalHyperlink_PreservesExternalLinkAndTarget()
    {
        var workbook = new Workbook("RelHyperlinkTest");
        var sheet = workbook.AddSheet("S1");

        // Relative subpath file link (the M4 bug case)
        var relSubAddr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(relSubAddr, new TextValue("Sub report"));
        sheet.Hyperlinks[relSubAddr] = "docs/report.pdf";
        sheet.HyperlinkMetadata[relSubAddr] = new HyperlinkMetadata(
            HyperlinkTargetKind.ExistingFileOrWebPage, "Open report", "");

        // Relative parent-dir file link (also common: ../sibling.xlsx)
        var relParentAddr = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(relParentAddr, new TextValue("Sibling wb"));
        sheet.Hyperlinks[relParentAddr] = "../other.xlsx";
        sheet.HyperlinkMetadata[relParentAddr] = new HyperlinkMetadata(
            HyperlinkTargetKind.ExistingFileOrWebPage, "Open sibling", "");

        // Absolute http link – must still work after the change
        var absHttpAddr = new CellAddress(sheet.Id, 3, 1);
        sheet.SetCell(absHttpAddr, new TextValue("Docs site"));
        sheet.Hyperlinks[absHttpAddr] = "https://example.com/docs";
        sheet.HyperlinkMetadata[absHttpAddr] = new HyperlinkMetadata(
            HyperlinkTargetKind.ExistingFileOrWebPage, "Online docs", "");

        // In-document / place-in-this-document link – must remain internal
        var inDocAddr = new CellAddress(sheet.Id, 4, 1);
        sheet.SetCell(inDocAddr, new TextValue("Jump"));
        sheet.Hyperlinks[inDocAddr] = "Sheet1!A1";
        sheet.HyperlinkMetadata[inDocAddr] = new HyperlinkMetadata(
            HyperlinkTargetKind.PlaceInThisDocument, "Jump to A1", "Sheet1!A1");

        var adapter = new XlsxFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);
        var loadedSheet = loaded.GetSheetAt(0);

        // Addresses must be re-created with the loaded sheet's SheetId because SheetId is not
        // stable across save/load (it is a new GUID assigned by the loader).
        var lRelSubAddr    = new CellAddress(loadedSheet.Id, 1, 1);
        var lRelParentAddr = new CellAddress(loadedSheet.Id, 2, 1);
        var lAbsHttpAddr   = new CellAddress(loadedSheet.Id, 3, 1);
        var lInDocAddr     = new CellAddress(loadedSheet.Id, 4, 1);

        // Relative subpath: must be external, target preserved
        loadedSheet.Hyperlinks.Should().ContainKey(lRelSubAddr);
        loadedSheet.Hyperlinks[lRelSubAddr].Should().Be("docs/report.pdf");
        loadedSheet.HyperlinkMetadata.Should().ContainKey(lRelSubAddr);
        loadedSheet.HyperlinkMetadata[lRelSubAddr].LinkType
            .Should().Be(HyperlinkTargetKind.ExistingFileOrWebPage,
                "a relative file-path hyperlink must round-trip as ExistingFileOrWebPage, not PlaceInThisDocument");

        // Relative parent dir: must be external, target preserved
        loadedSheet.Hyperlinks.Should().ContainKey(lRelParentAddr);
        loadedSheet.Hyperlinks[lRelParentAddr].Should().Be("../other.xlsx");
        loadedSheet.HyperlinkMetadata[lRelParentAddr].LinkType
            .Should().Be(HyperlinkTargetKind.ExistingFileOrWebPage);

        // Absolute http: still external
        loadedSheet.Hyperlinks.Should().ContainKey(lAbsHttpAddr);
        loadedSheet.Hyperlinks[lAbsHttpAddr].Should().StartWith("https://example.com");
        loadedSheet.HyperlinkMetadata[lAbsHttpAddr].LinkType
            .Should().Be(HyperlinkTargetKind.ExistingFileOrWebPage);

        // In-document: still internal (PlaceInThisDocument)
        loadedSheet.Hyperlinks.Should().ContainKey(lInDocAddr);
        loadedSheet.Hyperlinks[lInDocAddr].Should().Be("Sheet1!A1");
        loadedSheet.HyperlinkMetadata[lInDocAddr].LinkType
            .Should().Be(HyperlinkTargetKind.PlaceInThisDocument,
                "an in-document hyperlink must not be treated as external after the fix");
    }
}

using System.IO;
using System.Reflection;
using System.Xml.Linq;
using FluentAssertions;
using Free.Shared.Opc;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round 84 regression tests:
///  - R84-io-sheet-props-5-1 (src/FreeX.Core.IO/XlsxWorksheetMetadataPreserver.SheetViews.cs):
///    sheetFormatPr outlineLevelRow/outlineLevelCol must NOT be force-copied back from the stale
///    pre-edit source snapshot once ClosedXML's full-save rebuild has already recomputed (or
///    correctly omitted) them.
///  - R84-io-hyperlink-defined-name-5-1 (src/FreeX.Core.IO/XlsxFileAdapter.Hyperlinks.cs): an
///    external hyperlink target containing a backslash (a Windows drive-letter path or a UNC
///    path) must round-trip as a working link, not be silently dropped or corrupted into a bogus
///    percent-encoded relative Uri.
/// </summary>
public sealed class R84_SheetFormatOutlineLevelAndBackslashHyperlinkTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // --- R84-io-sheet-props-5-1 --------------------------------------------------------------

    // Primary case, matching the real ClosedXML 0.105.0 full-save behavior observed empirically:
    // the freshly rebuilt target worksheet's sheetFormatPr never carries an outlineLevelRow
    // attribute at all (ClosedXML computes and writes only a single recomputed outline value, not
    // a per-axis one) -- but the merge must not resurrect the STALE pre-edit source value into it.
    [Fact]
    public void MergeWorksheetSheetFormatProperties_DoesNotResurrectStaleOutlineLevelRowIntoFreshTarget()
    {
        // Pre-edit source snapshot: rows were grouped 2 levels deep before the edit.
        var source = new XElement(WorkbookNs + "sheetFormatPr",
            new XAttribute("defaultRowHeight", "15"),
            new XAttribute("outlineLevelRow", "2"));

        // Freshly rebuilt target from ClosedXML's full save: no outlineLevelRow attribute at all
        // (matches ClosedXML's actual behavior; only its own separately-computed value, if any,
        // would appear here -- never the stale pre-edit "2").
        var targetRoot = new XElement(WorkbookNs + "worksheet",
            new XElement(WorkbookNs + "sheetFormatPr", new XAttribute("defaultRowHeight", "15")));

        InvokeMergeWorksheetSheetFormatProperties(source, targetRoot);

        var targetSheetFormatPr = targetRoot.Element(WorkbookNs + "sheetFormatPr")!;
        targetSheetFormatPr.Attribute("outlineLevelRow").Should().BeNull(
            "ClosedXML's live-recomputed (or correctly-omitted) outline level must not be clobbered back to the stale pre-edit source value");
    }

    // No-regression sibling: baseColWidth is a genuinely unmodeled attribute (ClosedXML never
    // recomputes it) and must still be resurrected verbatim from the stale pre-edit source when
    // the freshly rebuilt target lacks it -- the fix must only stop this for outlineLevelRow/Col,
    // not for the rest of nativeOnlyAttributes.
    [Fact]
    public void MergeWorksheetSheetFormatProperties_StillPreservesGenuinelyNativeOnlyAttributes()
    {
        var source = new XElement(WorkbookNs + "sheetFormatPr",
            new XAttribute("defaultRowHeight", "15"),
            new XAttribute("baseColWidth", "8"));

        var targetRoot = new XElement(WorkbookNs + "worksheet",
            new XElement(WorkbookNs + "sheetFormatPr", new XAttribute("defaultRowHeight", "15")));

        InvokeMergeWorksheetSheetFormatProperties(source, targetRoot);

        var targetSheetFormatPr = targetRoot.Element(WorkbookNs + "sheetFormatPr")!;
        targetSheetFormatPr.Attribute("baseColWidth")?.Value.Should().Be("8",
            "baseColWidth is never recomputed by ClosedXML and must still be preserved verbatim from the source");
    }

    private static void InvokeMergeWorksheetSheetFormatProperties(XElement source, XElement targetRoot)
    {
        XlsxWorksheetMetadataPreserver.MergeWorksheetSheetFormatProperties(source, targetRoot, WorkbookNs);
    }

    // --- R84-io-hyperlink-defined-name-5-1 ----------------------------------------------------

    // Primary case: a Windows drive-letter path target. Before the fix, EscapeExternalHyperlinkTarget
    // percent-encodes the backslashes, and the resulting "C:%5CReports%5CQ1.xlsx" throws a
    // UriFormatException from `new Uri(...)`, which XlsxFileAdapter.Save.cs's surrounding try/catch
    // swallows -- the hyperlink is silently dropped from the saved file (and a warning recorded).
    [Fact]
    public void XlsxAdapter_RoundTrip_DriveLetterPathHyperlink_IsNotSilentlyDropped()
    {
        var workbook = new Workbook("BackslashHyperlinkTest");
        var sheet = workbook.AddSheet("S1");

        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, new TextValue("Local report"));
        sheet.Hyperlinks[addr] = @"C:\Reports\Q1.xlsx";
        sheet.HyperlinkMetadata[addr] = new HyperlinkMetadata(
            HyperlinkTargetKind.ExistingFileOrWebPage, "Open local report", "");

        var adapter = new XlsxFileAdapter();
        using var ms = new MemoryStream();
        var saveResult = adapter.SaveWithWarnings(workbook, ms);

        saveResult.Warnings.Should().BeEmpty(
            "a drive-letter local file path is a valid Excel hyperlink target and must not be silently dropped during save");

        ms.Position = 0;
        var loaded = adapter.Load(ms);
        var loadedSheet = loaded.GetSheetAt(0);
        var loadedAddr = new CellAddress(loadedSheet.Id, 1, 1);

        loadedSheet.Hyperlinks.Should().ContainKey(loadedAddr,
            "the drive-letter hyperlink must survive the save/load round trip, not be dropped");
        loadedSheet.Hyperlinks[loadedAddr].Should().Be("file:///C:/Reports/Q1.xlsx");
        loadedSheet.HyperlinkMetadata[loadedAddr].LinkType.Should().Be(HyperlinkTargetKind.ExistingFileOrWebPage);
    }

    // Sibling covering the other half of the same finding: a UNC path target. Before the fix this
    // does not throw (unlike the drive-letter case), but the percent-encoded backslashes produce a
    // bogus *relative* Uri holding the literal "%5C%5C..." text -- a broken link that never
    // resolves back to \\server\share\Q1.xlsx.
    [Fact]
    public void XlsxAdapter_RoundTrip_UncPathHyperlink_ResolvesToWorkingFileUriNotPercentEncodedGarbage()
    {
        var workbook = new Workbook("UncHyperlinkTest");
        var sheet = workbook.AddSheet("S1");

        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, new TextValue("Shared report"));
        sheet.Hyperlinks[addr] = @"\\server\share\Q1.xlsx";
        sheet.HyperlinkMetadata[addr] = new HyperlinkMetadata(
            HyperlinkTargetKind.ExistingFileOrWebPage, "Open shared report", "");

        var adapter = new XlsxFileAdapter();
        using var ms = new MemoryStream();
        var saveResult = adapter.SaveWithWarnings(workbook, ms);

        saveResult.Warnings.Should().BeEmpty();

        ms.Position = 0;
        var loaded = adapter.Load(ms);
        var loadedSheet = loaded.GetSheetAt(0);
        var loadedAddr = new CellAddress(loadedSheet.Id, 1, 1);

        loadedSheet.Hyperlinks.Should().ContainKey(loadedAddr);
        var loadedTarget = loadedSheet.Hyperlinks[loadedAddr];
        loadedTarget.Should().NotContain("%5C",
            "the UNC target must not be left as literal percent-encoded garbage that never resolves back to the intended path");
        loadedTarget.Should().Be("file://server/share/Q1.xlsx",
            "a UNC path must round-trip as the standard, working file://host/share URI form");
        loadedSheet.HyperlinkMetadata[loadedAddr].LinkType.Should().Be(HyperlinkTargetKind.ExistingFileOrWebPage);
    }

    // No-regression sibling: an ordinary absolute http URL (no backslashes at all) must keep
    // round-tripping unaffected by the backslash-handling changes above.
    [Fact]
    public void XlsxAdapter_RoundTrip_PlainHttpHyperlink_StillWorksUnaffectedByBackslashHandling()
    {
        var workbook = new Workbook("PlainHttpHyperlinkTest");
        var sheet = workbook.AddSheet("S1");

        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, new TextValue("Docs site"));
        sheet.Hyperlinks[addr] = "https://example.com/docs";
        sheet.HyperlinkMetadata[addr] = new HyperlinkMetadata(
            HyperlinkTargetKind.ExistingFileOrWebPage, "Online docs", "");

        var adapter = new XlsxFileAdapter();
        using var ms = new MemoryStream();
        var saveResult = adapter.SaveWithWarnings(workbook, ms);

        saveResult.Warnings.Should().BeEmpty();

        ms.Position = 0;
        var loaded = adapter.Load(ms);
        var loadedSheet = loaded.GetSheetAt(0);
        var loadedAddr = new CellAddress(loadedSheet.Id, 1, 1);

        loadedSheet.Hyperlinks.Should().ContainKey(loadedAddr);
        loadedSheet.Hyperlinks[loadedAddr].Should().StartWith("https://example.com");
        loadedSheet.HyperlinkMetadata[loadedAddr].LinkType.Should().Be(HyperlinkTargetKind.ExistingFileOrWebPage);
    }
}

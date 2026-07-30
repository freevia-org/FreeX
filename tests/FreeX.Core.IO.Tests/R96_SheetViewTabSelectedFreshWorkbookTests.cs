using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for R96-io-worksheet-tabselected-fresh-workbook-5-1: sheetView/@tabSelected was
/// only ever synced from Workbook.ActiveSheetIndex by XlsxWorksheetPrimaryViewMetadataWriter.Save, which
/// itself only ran when XlsxWorksheetSourceIndependentMetadataBatchWriter's gate saw some OTHER native
/// worksheet metadata (autoFilter/data validation/page breaks/print options/dimension/sheetPr/primary
/// view/page margins/header-footer/protection) already populated on some sheet. Those bags are populated
/// exclusively by loading an existing .xlsx (XlsxFileAdapter.LoadSheetXmlLayoutApplication.cs) or a native
/// JSON file (NativeJsonAdapter.cs), so a workbook created fresh in-app (File > New, Insert Sheet) and
/// never loaded from either never had any of that metadata -- meaning switching its active sheet and
/// saving left bookViews/@activeTab correctly updated (XlsxWorkbookMetadataWriter, gated only on
/// ActiveSheetIndex being non-null) while every sheetView/@tabSelected stayed exactly as ClosedXML's
/// full-rebuild happened to write it, permanently disagreeing with activeTab. The fix gates the
/// tabSelected sync on the same condition the workbook writer uses (ActiveSheetIndex is not null),
/// independent of any other native metadata being present.
/// </summary>
public sealed class R96_SheetViewTabSelectedFreshWorkbookTests
{
    private const string WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static XElement GetPrimarySheetView(XDocument worksheetXml)
    {
        XNamespace ns = WorksheetNs;
        return worksheetXml.Root!
            .Element(ns + "sheetViews")!
            .Elements(ns + "sheetView")
            .Single(view => string.Equals(view.Attribute("workbookViewId")?.Value ?? "0", "0", StringComparison.Ordinal));
    }

    private static string? ReadTabSelected(byte[] packageBytes, string worksheetPath)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry(worksheetPath)!;
        using var entryStream = entry.Open();
        return GetPrimarySheetView(XDocument.Load(entryStream)).Attribute("tabSelected")?.Value;
    }

    private static string? ReadActiveTab(byte[] packageBytes)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry("xl/workbook.xml")!;
        using var entryStream = entry.Open();
        var root = XDocument.Load(entryStream).Root!;
        XNamespace ns = WorkbookNs;
        var primaryView = root.Element(ns + "bookViews")!
            .Elements(ns + "workbookView")
            .First();
        return primaryView.Attribute("activeTab")?.Value;
    }

    [Fact]
    public void Save_BrandNewWorkbookWithNoNativeMetadata_SwitchingActiveSheet_StampsTabSelectedOnNewActiveSheet()
    {
        // A workbook that was never loaded from an .xlsx or native JSON file at all (equivalent to
        // File > New + Insert Sheet) has every per-sheet native metadata bag null (no AutoFilter, no
        // data validation, no page breaks, no PrimaryViewMetadata/PrintOptionsMetadata/etc.), so the
        // old gate (XlsxWorksheetNativeMetadataBatchWriter.HasMetadata over all sheets) was false and
        // the whole XlsxWorksheetSourceIndependentMetadataBatchWriter.Save call short-circuited.
        var workbook = new Workbook("FreshWorkbookTabSelectedTest");
        workbook.AddSheet("Sheet1");
        workbook.AddSheet("Sheet2");

        // Switching the active sheet in the app sets Workbook.ActiveSheetIndex directly
        // (WorkbookSheetSelectionService.SelectSheet), outside of any per-sheet native metadata.
        workbook.ActiveSheetIndex = 1;

        var adapter = new XlsxFileAdapter();
        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        ReadActiveTab(savedBytes).Should().Be(
            "1",
            "bookViews/@activeTab is written unconditionally whenever ActiveSheetIndex is set");
        ReadTabSelected(savedBytes, "xl/worksheets/sheet2.xml").Should().Be(
            "1",
            "Sheet2 (index 1) is the live active sheet, so its sheetView must carry tabSelected=\"1\" " +
            "even though the workbook has no other native worksheet metadata at all");
        ReadTabSelected(savedBytes, "xl/worksheets/sheet1.xml").Should().BeNull(
            "Sheet1 is no longer the active sheet, so its sheetView must not carry tabSelected");
    }

    [Fact]
    public void Save_BrandNewWorkbookWithNoNativeMetadata_DefaultFirstSheetActive_TabSelectedMatchesActiveTab()
    {
        // Sibling no-regression case: a fresh workbook whose active sheet was explicitly resolved to
        // the first sheet (index 0, e.g. via WorkbookSheetSelectionService.EnsureActiveSheet on
        // File > New) must still get a consistent tabSelected="1" on Sheet1 and nothing on Sheet2,
        // exactly as before this fix (this path already happened to work because ClosedXML's own
        // fresh-workbook default also marks the first sheet selected, but it must keep doing so under
        // the new gate).
        var workbook = new Workbook("FreshWorkbookTabSelectedDefaultTest");
        workbook.AddSheet("Sheet1");
        workbook.AddSheet("Sheet2");
        workbook.ActiveSheetIndex = 0;

        var adapter = new XlsxFileAdapter();
        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        ReadActiveTab(savedBytes).Should().Be("0");
        ReadTabSelected(savedBytes, "xl/worksheets/sheet1.xml").Should().Be("1");
        ReadTabSelected(savedBytes, "xl/worksheets/sheet2.xml").Should().BeNull();
    }
}

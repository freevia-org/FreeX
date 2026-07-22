using System.IO.Compression;
using System.Xml.Linq;
using ClosedXML.Excel;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression test for R74-io-workbook-sheet-view-4-1: when Workbook.ActiveSheetIndex points at a
/// HIDDEN sheet, XlsxWorkbookMetadataWriter redirects bookViews/workbookView/@activeTab to the first
/// visible sheet (ClampToVisibleSheetIndex), but XlsxWorksheetPrimaryViewMetadataWriter.ResolveActiveSheet
/// only clamped the index range and never applied the same hidden-sheet redirect -- so
/// sheetView/@tabSelected="1" was stamped onto the hidden sheet's own sheetView while the (different)
/// visible sheet named by activeTab got no tabSelected at all. Real Excel never writes a state where the
/// selected tab (activeTab) and the tabSelected="1" sheetView disagree, nor where a hidden sheet carries
/// tabSelected="1". ResolveActiveSheet must apply the identical redirect so both stay in lockstep.
/// </summary>
public sealed class R74_SheetViewTabSelectedHiddenActiveSheetTests
{
    private const string WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static void PrepareLoadedWorkbookForEdit(Workbook workbook)
    {
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);
    }

    private static byte[] CreateTwoSheetSourcePackage()
    {
        using var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var sheet1 = workbook.AddWorksheet("Sheet1");
            sheet1.Cell("A1").Value = "original value";
            var sheet2 = workbook.AddWorksheet("Sheet2");
            sheet2.Cell("A1").Value = "other sheet";
            workbook.SaveAs(stream);
        }

        return stream.ToArray();
    }

    private static XElement GetPrimarySheetView(XDocument worksheetXml)
    {
        XNamespace ns = WorksheetNs;
        return worksheetXml.Root!
            .Element(ns + "sheetViews")!
            .Elements(ns + "sheetView")
            .Single(view => string.Equals(view.Attribute("workbookViewId")?.Value ?? "0", "0", StringComparison.Ordinal));
    }

    private static byte[] SeedSheetViewTabSelected(byte[] sourceBytes, string worksheetPath)
    {
        using var stream = new MemoryStream();
        stream.Write(sourceBytes, 0, sourceBytes.Length);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry(worksheetPath)!;
            XDocument worksheetXml;
            using (var entryStream = entry.Open())
                worksheetXml = XDocument.Load(entryStream);

            GetPrimarySheetView(worksheetXml).SetAttributeValue("tabSelected", "1");

            entry.Delete();
            var replacement = archive.CreateEntry(worksheetPath);
            using var replacementStream = replacement.Open();
            worksheetXml.Save(replacementStream, System.Xml.Linq.SaveOptions.DisableFormatting);
        }

        return stream.ToArray();
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
    public void Save_WithActiveSheetIndexPointingAtHiddenSheet_StampsTabSelectedOnFirstVisibleSheetOnly()
    {
        // Seed tabSelected="1" on Sheet1 so the load-time native metadata bag captures a sheetView
        // entry for it (matching the sibling XlsxWorksheetPrimaryViewMetadataWriterTabSelectedZoomTests
        // pattern), which is what makes XlsxWorksheetNativeMetadataBatchWriter.HasMetadata true and
        // enables PrimaryViewMetadata reconciliation to run on save.
        var sourceBytes = SeedSheetViewTabSelected(CreateTwoSheetSourcePackage(), "xl/worksheets/sheet1.xml");

        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet2 = workbook.GetSheetAt(1);
        sheet2.IsHidden = true;

        // Model's ActiveSheetIndex points at the now-hidden Sheet2 (e.g. hide -> unhide -> undo
        // leaving ActiveSheetIndex stale, same failure scenario as R40_SheetVisibilityActiveTabTests).
        workbook.ActiveSheetIndex = 1;
        var sheet1 = workbook.GetSheetAt(0);
        sheet1.SetCell(new CellAddress(sheet1.Id, 2, 1), new TextValue("patched value"));
        workbook.DefineNamedRange("TabSelectedHiddenRedirectUnrelatedName", new GridRange(
            new CellAddress(sheet1.Id, 5, 5),
            new CellAddress(sheet1.Id, 5, 5)));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);

        ReadActiveTab(savedBytes).Should().Be(
            "0",
            "Sheet2 (index 1) is hidden, so bookViews/@activeTab must be redirected to Sheet1 (index 0)");
        ReadTabSelected(savedBytes, "xl/worksheets/sheet1.xml").Should().Be(
            "1",
            "activeTab was redirected to Sheet1 so its sheetView must be the one carrying " +
            "tabSelected=\"1\", matching bookViews/@activeTab");
        ReadTabSelected(savedBytes, "xl/worksheets/sheet2.xml").Should().BeNull(
            "Sheet2 is hidden and is no longer the effective active sheet after the redirect - Excel " +
            "never marks a hidden sheet's sheetView as tabSelected");
    }

    [Fact]
    public void Save_WithActiveSheetIndexPointingAtVisibleSheet_StampsTabSelectedOnThatSheetUnchanged()
    {
        // Sibling no-regression case: a normal visible active sheet must still get tabSelected="1"
        // on its own sheetView (no redirect applies), exactly as before this fix.
        var sourceBytes = SeedSheetViewTabSelected(CreateTwoSheetSourcePackage(), "xl/worksheets/sheet1.xml");

        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        workbook.ActiveSheetIndex = 1;
        var sheet2 = workbook.GetSheetAt(1);
        sheet2.SetCell(new CellAddress(sheet2.Id, 2, 1), new TextValue("patched value"));
        workbook.DefineNamedRange("TabSelectedVisibleUnrelatedName", new GridRange(
            new CellAddress(sheet2.Id, 5, 5),
            new CellAddress(sheet2.Id, 5, 5)));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);

        ReadActiveTab(savedBytes).Should().Be("1", "Sheet2 is visible so no redirect applies");
        ReadTabSelected(savedBytes, "xl/worksheets/sheet1.xml").Should().BeNull(
            "Sheet1 is not the active sheet");
        ReadTabSelected(savedBytes, "xl/worksheets/sheet2.xml").Should().Be(
            "1",
            "Sheet2 is visible and genuinely active, so it must keep tabSelected=\"1\" unchanged");
    }
}

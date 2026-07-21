using System.IO.Compression;
using System.Xml.Linq;
using ClosedXML.Excel;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for two round-58 findings in XlsxWorksheetPrimaryViewMetadataWriter:
///
/// R58-services-zoom-view-state-6-1: sheetView@tabSelected was never repointed to the new active
/// sheet on save (bookViews/workbookView/@activeTab was updated, but no per-sheet tabSelected sync
/// existed), and the load-time bag's stale tabSelected value was force-reapplied every save because
/// the ApplyNativeAttributes whitelist omitted it.
///
/// R58-services-zoom-view-state-6-2: RefreshPerViewModeZoom unconditionally overwrote the CURRENT
/// view mode's own remembered zoomScale&lt;Mode&gt; attribute with the shared live Sheet.ZoomPercent,
/// even when the user only switched view mode and never touched zoom -- silently discarding the
/// other mode's genuinely-remembered zoom value.
/// </summary>
public sealed class XlsxWorksheetPrimaryViewMetadataWriterTabSelectedZoomTests
{
    private const string WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

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
            var sheet1 = workbook.AddWorksheet("Data");
            sheet1.Cell("A1").Value = "original value";
            var sheet2 = workbook.AddWorksheet("Sheet2");
            sheet2.Cell("A1").Value = "other sheet";
            workbook.SaveAs(stream);
        }

        return stream.ToArray();
    }

    private static XDocument LoadPackageXml(ZipArchive archive, string path) =>
        XlsxPackageTestFixtures.LoadPackageXml(archive, path);

    private static void ReplacePackageXml(ZipArchive archive, string path, XDocument document)
    {
        archive.GetEntry(path)?.Delete();
        var replacement = archive.CreateEntry(path);
        using var replacementStream = replacement.Open();
        document.Save(replacementStream, System.Xml.Linq.SaveOptions.DisableFormatting);
    }

    private static XElement GetPrimarySheetView(XDocument worksheetXml)
    {
        XNamespace ns = WorksheetNs;
        return worksheetXml.Root!
            .Element(ns + "sheetViews")!
            .Elements(ns + "sheetView")
            .Single(view => string.Equals(view.Attribute("workbookViewId")?.Value ?? "0", "0", StringComparison.Ordinal));
    }

    private static byte[] SetSheetViewAttributes(byte[] sourceBytes, string worksheetPath, IReadOnlyDictionary<string, string?> attributes)
    {
        using var stream = new MemoryStream();
        stream.Write(sourceBytes, 0, sourceBytes.Length);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var worksheetXml = LoadPackageXml(archive, worksheetPath);
            var sheetView = GetPrimarySheetView(worksheetXml);
            foreach (var (name, value) in attributes)
                sheetView.SetAttributeValue(name, value);

            ReplacePackageXml(archive, worksheetPath, worksheetXml);
        }

        return stream.ToArray();
    }

    private static string? ReadPrimarySheetViewAttribute(byte[] packageBytes, string worksheetPath, string attributeName)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var document = LoadPackageXml(archive, worksheetPath);
        return GetPrimarySheetView(document).Attribute(attributeName)?.Value;
    }

    [Fact]
    public void Save_SwitchingActiveSheet_RepointsTabSelectedToNewActiveSheetAndClearsOldOne()
    {
        // Regression: source loaded with Sheet1 (sheet1.xml) as the tabSelected sheet. The user
        // switches the active sheet to Sheet2 (workbook.ActiveSheetIndex now points at Sheet2) and
        // saves. Real Excel repoints tabSelected to whichever sheet is now active; FreeX must do the
        // same instead of leaving Sheet1 permanently marked selected forever.
        var sourceBytes = SetSheetViewAttributes(
            CreateTwoSheetSourcePackage(),
            "xl/worksheets/sheet1.xml",
            new Dictionary<string, string?> { ["tabSelected"] = "1" });

        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        workbook.ActiveSheetIndex.Should().Be(0, "the source file's workbookView/@activeTab pointed at Sheet1");

        var sheet2 = workbook.GetSheetAt(1);
        workbook.ActiveSheetIndex = 1;
        sheet2.SetCell(new CellAddress(sheet2.Id, 2, 1), new TextValue("patched value"));
        workbook.DefineNamedRange("TabSelectedSwitchUnrelatedName", new GridRange(
            new CellAddress(sheet2.Id, 5, 5),
            new CellAddress(sheet2.Id, 5, 5)));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);

        ReadPrimarySheetViewAttribute(savedBytes, "xl/worksheets/sheet1.xml", "tabSelected")
            .Should()
            .BeNull("Sheet1 is no longer the active sheet, so its stale tabSelected must be cleared");
        ReadPrimarySheetViewAttribute(savedBytes, "xl/worksheets/sheet2.xml", "tabSelected")
            .Should()
            .Be("1", "Sheet2 is now the active sheet and must carry tabSelected=\"1\"");
    }

    [Fact]
    public void Save_ActiveSheetUnchanged_TabSelectedStaysOnTheSameSheet()
    {
        // Sibling already-working case: the active sheet never changes across the save, so the
        // sync logic must not disturb the correctly-placed tabSelected="1" on Sheet1.
        var sourceBytes = SetSheetViewAttributes(
            CreateTwoSheetSourcePackage(),
            "xl/worksheets/sheet1.xml",
            new Dictionary<string, string?> { ["tabSelected"] = "1" });

        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        workbook.ActiveSheetIndex.Should().Be(0);

        var sheet1 = workbook.GetSheetAt(0);
        sheet1.SetCell(new CellAddress(sheet1.Id, 2, 1), new TextValue("patched value"));
        workbook.DefineNamedRange("TabSelectedUnchangedUnrelatedName", new GridRange(
            new CellAddress(sheet1.Id, 5, 5),
            new CellAddress(sheet1.Id, 5, 5)));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);

        ReadPrimarySheetViewAttribute(savedBytes, "xl/worksheets/sheet1.xml", "tabSelected")
            .Should()
            .Be("1", "Sheet1 is still the active sheet and must keep tabSelected=\"1\"");
        ReadPrimarySheetViewAttribute(savedBytes, "xl/worksheets/sheet2.xml", "tabSelected")
            .Should()
            .BeNull("Sheet2 was never the active sheet");
    }

    [Fact]
    public void Save_SwitchingViewModeWithNoZoomChange_PreservesOtherModesRememberedZoom()
    {
        // Regression: source remembers zoomScaleNormal="100" (matching the top-level zoomScale the
        // sheet loaded with) and zoomScalePageLayoutView="150" (an independently-remembered Page
        // Layout zoom from a previous Excel session). The user merely switches view mode to Page
        // Layout without ever touching zoom. FreeX's single shared ZoomPercent (100, inherited from
        // Normal) must NOT clobber the file's remembered 150% for Page Layout.
        var sourceBytes = SetSheetViewAttributes(
            CreateTwoSheetSourcePackage(),
            "xl/worksheets/sheet1.xml",
            new Dictionary<string, string?>
            {
                ["zoomScale"] = "100",
                ["zoomScaleNormal"] = "100",
                ["zoomScalePageLayoutView"] = "150",
            });

        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet1 = workbook.GetSheetAt(0);
        sheet1.ViewMode.Should().Be(WorksheetViewMode.Normal);
        sheet1.ZoomPercent.Should().Be(100);

        sheet1.ViewMode = WorksheetViewMode.PageLayout;
        sheet1.SetCell(new CellAddress(sheet1.Id, 2, 1), new TextValue("patched value"));
        workbook.DefineNamedRange("ZoomModeSwitchUnrelatedName", new GridRange(
            new CellAddress(sheet1.Id, 5, 5),
            new CellAddress(sheet1.Id, 5, 5)));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);

        ReadPrimarySheetViewAttribute(savedBytes, "xl/worksheets/sheet1.xml", "zoomScalePageLayoutView")
            .Should()
            .Be("150", "the file's own remembered Page Layout zoom must survive a pure mode switch");
        ReadPrimarySheetViewAttribute(savedBytes, "xl/worksheets/sheet1.xml", "zoomScaleNormal")
            .Should()
            .Be("100", "Normal mode's remembered zoom is untouched by switching away from it");
    }

    [Fact]
    public void Save_ZoomActuallyChangedInCurrentMode_StillPersistsToThatModesAttribute()
    {
        // Sibling already-working case: the user stays in Normal view (no mode switch) and
        // genuinely changes the zoom. The fix must not prevent a real same-mode zoom change from
        // being written into that mode's own remembered attribute.
        var sourceBytes = SetSheetViewAttributes(
            CreateTwoSheetSourcePackage(),
            "xl/worksheets/sheet1.xml",
            new Dictionary<string, string?>
            {
                ["zoomScale"] = "100",
                ["zoomScaleNormal"] = "100",
                ["zoomScalePageLayoutView"] = "150",
            });

        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet1 = workbook.GetSheetAt(0);
        sheet1.ViewMode.Should().Be(WorksheetViewMode.Normal);

        sheet1.ZoomPercent = 90;
        sheet1.SetCell(new CellAddress(sheet1.Id, 2, 1), new TextValue("patched value"));
        workbook.DefineNamedRange("ZoomRealChangeUnrelatedName", new GridRange(
            new CellAddress(sheet1.Id, 5, 5),
            new CellAddress(sheet1.Id, 5, 5)));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);

        ReadPrimarySheetViewAttribute(savedBytes, "xl/worksheets/sheet1.xml", "zoomScaleNormal")
            .Should()
            .Be("90", "a genuine zoom change made while still in Normal view must update Normal's own remembered zoom");
        ReadPrimarySheetViewAttribute(savedBytes, "xl/worksheets/sheet1.xml", "zoomScalePageLayoutView")
            .Should()
            .Be("150", "Page Layout's remembered zoom is unrelated to a Normal-mode zoom change and must stay untouched");
    }
}

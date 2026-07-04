using System.IO.Compression;
using System.Xml.Linq;
using ClosedXML.Excel;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for:
///  - G30: a genuine (non-frozen) Excel window split (state="split") stores xSplit/ySplit as
///    twentieths-of-a-point pixel positions per OOXML, not row/column counts, and must not be
///    misread as a literal row/column index like the frozen-pane path.
///  - G31: patching worksheet-view attributes (e.g. zoom, gridlines) must preserve the sheet's
///    existing topLeftCell (scroll position) instead of silently stripping it.
/// </summary>
public sealed class XlsxWorksheetViewMSplitTopLeftTests
{
    private static void PrepareLoadedWorkbookForEdit(Workbook workbook)
    {
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);
    }

    private static byte[] CreateSourcePackage()
    {
        using var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Data");
            sheet.Cell("A1").Value = "original value";
            sheet.Cell("B2").Value = 123.45;
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

    private static byte[] SetGenuineSplitPane(byte[] sourceBytes, string xSplit, string ySplit)
    {
        using var stream = new MemoryStream();
        stream.Write(sourceBytes, 0, sourceBytes.Length);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
            var sheetView = worksheetXml.Root!
                .Element(worksheetNs + "sheetViews")!
                .Elements(worksheetNs + "sheetView")
                .Single(view => string.Equals(view.Attribute("workbookViewId")?.Value ?? "0", "0", StringComparison.Ordinal));

            sheetView.Elements(worksheetNs + "pane").Remove();
            sheetView.AddFirst(new XElement(
                worksheetNs + "pane",
                new XAttribute("xSplit", xSplit),
                new XAttribute("ySplit", ySplit),
                new XAttribute("state", "split")));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        return stream.ToArray();
    }

    private static byte[] SetTopLeftCell(byte[] sourceBytes, string topLeftCell)
    {
        using var stream = new MemoryStream();
        stream.Write(sourceBytes, 0, sourceBytes.Length);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
            var sheetView = worksheetXml.Root!
                .Element(worksheetNs + "sheetViews")!
                .Elements(worksheetNs + "sheetView")
                .Single(view => string.Equals(view.Attribute("workbookViewId")?.Value ?? "0", "0", StringComparison.Ordinal));

            sheetView.SetAttributeValue("topLeftCell", topLeftCell);
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        return stream.ToArray();
    }

    private static string? ReadPrimarySheetViewAttribute(byte[] packageBytes, string worksheetPath, string attributeName)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var document = LoadPackageXml(archive, worksheetPath);
        var ns = document.Root!.Name.Namespace;
        return document.Root!
            .Element(ns + "sheetViews")
            ?.Elements(ns + "sheetView")
            .FirstOrDefault(view => string.Equals(view.Attribute("workbookViewId")?.Value ?? "0", "0", StringComparison.Ordinal))
            ?.Attribute(attributeName)
            ?.Value;
    }

    [Fact]
    public void XlsxAdapter_Load_GenuineExcelSplitPane_DoesNotMisreadTwipsAsRowColumnIndex()
    {
        // xSplit="2400" ySplit="1800" are twentieths-of-a-point pixel positions (a genuine
        // Excel View > Split), not row/column counts. ClosedXML only populates
        // SheetView.SplitRow/SplitColumn for its own freeze-pane API, so they are 0 here.
        var sourceBytes = SetGenuineSplitPane(CreateSourcePackage(), xSplit: "2400", ySplit: "1800");

        var adapter = new XlsxFileAdapter();
        using var source = new MemoryStream(sourceBytes, writable: false);
        var loaded = adapter.Load(source);
        var sheet = loaded.GetSheetAt(0);

        sheet.SplitRow.Should().BeNull("a twips-based split position must not be reinterpreted as a row index");
        sheet.SplitColumn.Should().BeNull("a twips-based split position must not be reinterpreted as a column index");
        sheet.FrozenRows.Should().Be(0);
        sheet.FrozenCols.Should().Be(0);
    }

    [Fact]
    public void XlsxAdapter_Load_GenuineExcelSplitPane_WithSmallTwipsValue_StillDoesNotBecomeSplitIndex()
    {
        // Even a small xSplit/ySplit value (which could be mistaken for a plausible row/col
        // count) must still be treated as a twips position for a real state="split" pane,
        // since ClosedXML's own SplitRow/SplitColumn (the only trustworthy source here) are 0.
        var sourceBytes = SetGenuineSplitPane(CreateSourcePackage(), xSplit: "3", ySplit: "2");

        var adapter = new XlsxFileAdapter();
        using var source = new MemoryStream(sourceBytes, writable: false);
        var loaded = adapter.Load(source);
        var sheet = loaded.GetSheetAt(0);

        sheet.SplitRow.Should().BeNull();
        sheet.SplitColumn.Should().BeNull();
    }

    [Fact]
    public void Save_LoadedWorkbookWithZoomEdit_PreservesExistingTopLeftCell()
    {
        // Regression for G31: patching an unrelated worksheet-view attribute (zoom) must not
        // clobber the sheet's existing scroll position (topLeftCell).
        var sourceBytes = SetTopLeftCell(CreateSourcePackage(), "B5");
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.ZoomPercent.Should().Be(100);
        sheet.ViewTopRow.Should().Be(5);
        sheet.ViewLeftCol.Should().Be(2);

        sheet.ZoomPercent = 125;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("patched value"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        ReadPrimarySheetViewAttribute(savedBytes, "xl/worksheets/sheet1.xml", "zoomScale")
            .Should()
            .Be("125");
        ReadPrimarySheetViewAttribute(savedBytes, "xl/worksheets/sheet1.xml", "topLeftCell")
            .Should()
            .Be("B5", "an unrelated zoom-attribute patch must not discard the existing scroll position");

        using var reload = new MemoryStream(savedBytes, writable: false);
        var reloaded = adapter.Load(reload).GetSheetAt(0);
        reloaded.ZoomPercent.Should().Be(125);
        reloaded.ViewTopRow.Should().Be(5);
        reloaded.ViewLeftCol.Should().Be(2);
    }

    [Fact]
    public void Save_LoadedWorkbookWithGridlinesEdit_PreservesExistingTopLeftCell()
    {
        var sourceBytes = SetTopLeftCell(CreateSourcePackage(), "C10");
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.ViewTopRow.Should().Be(10);
        sheet.ViewLeftCol.Should().Be(3);

        sheet.ShowGridlines = false;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("patched value"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        ReadPrimarySheetViewAttribute(savedBytes, "xl/worksheets/sheet1.xml", "showGridLines")
            .Should()
            .Be("0");
        ReadPrimarySheetViewAttribute(savedBytes, "xl/worksheets/sheet1.xml", "topLeftCell")
            .Should()
            .Be("C10");
    }
}

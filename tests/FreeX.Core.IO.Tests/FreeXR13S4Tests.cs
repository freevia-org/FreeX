using System.IO.Compression;
using System.Xml.Linq;
using ClosedXML.Excel;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for round-13 bucket S4:
///  - R13-view-state-1: a patch-save of an unrelated worksheet-view attribute (e.g. zoom) must not
///    collapse a preserved multi-area <c>&lt;selection&gt;</c> sqref that already names the model's
///    active cell, and must never leave a dangling <c>activeCellId</c> when it does have to collapse
///    a stale selection.
///  - R13-view-state-2: a user-created View &gt; Split must be written under
///    <c>state="split"</c> as xSplit/ySplit twentieths-of-a-point pane-bar positions (per OOXML),
///    not as the raw row/column split index.
/// </summary>
public sealed class FreeXR13S4Tests
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
            sheet.Cell("D4").Value = "target";
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

    private static byte[] SetMultiAreaSelection(byte[] sourceBytes, string activeCell, string sqref, string activeCellId)
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

            sheetView.Elements(worksheetNs + "selection").Remove();
            sheetView.Add(new XElement(
                worksheetNs + "selection",
                new XAttribute("activeCell", activeCell),
                new XAttribute("activeCellId", activeCellId),
                new XAttribute("sqref", sqref)));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        return stream.ToArray();
    }

    private static XElement? ReadPrimarySelection(byte[] packageBytes, string worksheetPath)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var document = LoadPackageXml(archive, worksheetPath);
        var ns = document.Root!.Name.Namespace;
        var sheetView = document.Root!
            .Element(ns + "sheetViews")
            ?.Elements(ns + "sheetView")
            .FirstOrDefault(view => string.Equals(view.Attribute("workbookViewId")?.Value ?? "0", "0", StringComparison.Ordinal));

        return sheetView?.Element(ns + "selection");
    }

    private static string? ReadPrimaryPaneAttribute(byte[] packageBytes, string worksheetPath, string attributeName)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var document = LoadPackageXml(archive, worksheetPath);
        var ns = document.Root!.Name.Namespace;
        var sheetView = document.Root!
            .Element(ns + "sheetViews")
            ?.Elements(ns + "sheetView")
            .FirstOrDefault(view => string.Equals(view.Attribute("workbookViewId")?.Value ?? "0", "0", StringComparison.Ordinal));

        return sheetView?.Element(ns + "pane")?.Attribute(attributeName)?.Value;
    }

    [Fact]
    public void R13_view_state_1_ZoomOnlyPatchSave_PreservesMultiAreaSelectionAndActiveCellId()
    {
        // Source worksheet already has a genuine multi-area selection whose active cell (D4)
        // matches what FreeX will load as the sheet's active cell.
        var sourceBytes = SetMultiAreaSelection(CreateSourcePackage(), activeCell: "D4", sqref: "A1:B2 D4:E5", activeCellId: "1");

        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.ActiveRow.Should().Be(4, "the loaded active cell should be D4");
        sheet.ActiveCol.Should().Be(4);

        // User changes ONLY the zoom -- an unrelated worksheet-view attribute -- and saves.
        sheet.ZoomPercent = 150;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("patched value"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);

        var selection = ReadPrimarySelection(savedBytes, "xl/worksheets/sheet1.xml");
        selection.Should().NotBeNull();
        selection!.Attribute("activeCell")?.Value.Should().Be("D4");
        selection.Attribute("sqref")?.Value.Should().Be(
            "A1:B2 D4:E5",
            "an unrelated zoom-only patch save must not collapse a preserved multi-area selection");
        selection.Attribute("activeCellId")?.Value.Should().Be(
            "1",
            "activeCellId must survive alongside its untouched multi-area sqref");
    }

    [Fact]
    public void R13_view_state_2_SplitPaneSave_WritesTwipsNotRawRowColumnIndex()
    {
        var sourceBytes = CreateSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.FrozenRows.Should().Be(0);
        sheet.FrozenCols.Should().Be(0);

        // Pin the row/column metrics that feed the twips conversion so the expected values below
        // are deterministic, matching FreeX.Core.Model.Sheet's own documented defaults.
        sheet.DefaultRowHeight = 20.0;
        sheet.DefaultColumnWidth = 8.43;
        sheet.RowHeights.Clear();
        sheet.ColumnWidths.Clear();

        // A user-created View > Split at row index 5 / column index 4 (rows 1-4 and columns A-C
        // sit above/left of the divider).
        var splitRow = 5u;
        var splitColumn = 4u;
        sheet.SplitRow = splitRow;
        sheet.SplitColumn = splitColumn;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("patched value"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        // Expected twips = cumulative pixel extent above the split boundary * 15 (20 twips/point *
        // 72/96 points/pixel). 4 default-height rows @ 20px = 80px -> 1200 twips. 3 default-width
        // columns @ 8.43 chars (-> 64px per FreeX's character-width-to-pixel formula) = 192px ->
        // 2880 twips.
        var expectedYSplitTwips = (4 * 20.0 * 15.0).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var expectedXSplitTwips = (3 * 64.0 * 15.0).ToString(System.Globalization.CultureInfo.InvariantCulture);

        var ySplit = ReadPrimaryPaneAttribute(savedBytes, "xl/worksheets/sheet1.xml", "ySplit");
        var xSplit = ReadPrimaryPaneAttribute(savedBytes, "xl/worksheets/sheet1.xml", "xSplit");
        var state = ReadPrimaryPaneAttribute(savedBytes, "xl/worksheets/sheet1.xml", "state");

        state.Should().Be("split");
        ySplit.Should().NotBe(
            splitRow.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "ySplit under state=\"split\" must be a twips pixel position, not the raw row index");
        xSplit.Should().NotBe(
            splitColumn.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "xSplit under state=\"split\" must be a twips pixel position, not the raw column index");
        ySplit.Should().Be(expectedYSplitTwips);
        xSplit.Should().Be(expectedXSplitTwips);
    }
}

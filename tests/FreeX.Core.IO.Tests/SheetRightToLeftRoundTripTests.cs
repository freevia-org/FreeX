using System.IO.Compression;
using ClosedXML.Excel;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for K13: FreeX had no sheet-level RTL flag in the domain model, so an
/// Excel-authored right-to-left sheet (OOXML <c>sheetView/@rightToLeft="1"</c>) always rendered
/// and re-saved as left-to-right, silently diverging from what Excel would produce. These tests
/// cover <see cref="Sheet.IsRightToLeft"/> round-tripping through: (1) a brand-new XLSX
/// fresh-save/load, (2) the source-preserving surgical patch path, and (3) the native .fxl format.
/// </summary>
public sealed class SheetRightToLeftRoundTripTests
{
    [Fact]
    public void XlsxAdapter_FreshSave_RoundTrips_RightToLeftFlag_WhenTrue()
    {
        var workbook = new Workbook("RtlBook");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.IsRightToLeft = true;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("value"));

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(stream);

        reloaded.Sheets[0].IsRightToLeft.Should().BeTrue(
            "an RTL sheet must round-trip through a fresh XLSX save/load");
    }

    [Fact]
    public void XlsxAdapter_FreshSave_RoundTrips_RightToLeftFlag_WhenFalse()
    {
        var workbook = new Workbook("LtrBook");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("value"));

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(stream);

        reloaded.Sheets[0].IsRightToLeft.Should().BeFalse("a normal sheet must stay left-to-right");
    }

    [Fact]
    public void XlsxAdapter_Load_ReadsRightToLeftAttribute_FromExcelAuthoredWorkbook()
    {
        var sourceBytes = CreateSourcePackageWithRightToLeftSheetView();

        var adapter = new XlsxFileAdapter();
        using var source = new MemoryStream(sourceBytes, writable: false);
        var loaded = adapter.Load(source);

        loaded.GetSheetAt(0).IsRightToLeft.Should().BeTrue(
            "an Excel-authored sheetView/@rightToLeft=\"1\" must be surfaced on the Sheet model");
    }

    [Fact]
    public void Save_LoadedRightToLeftWorkbook_WithUnrelatedEdit_PreservesRightToLeftOnSourcePatch()
    {
        // Regression for the source-preserving surgical patch path: editing an unrelated cell must
        // not silently drop the sheet's RTL flag when XlsxWorksheetViewWriter re-patches the view.
        var sourceBytes = CreateSourcePackageWithRightToLeftSheetView();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.IsRightToLeft.Should().BeTrue();

        sheet.ZoomPercent = 125;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("patched value"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        using var reload = new MemoryStream(savedBytes, writable: false);
        var reloaded = adapter.Load(reload).GetSheetAt(0);
        reloaded.IsRightToLeft.Should().BeTrue(
            "patching an unrelated view attribute (zoom) must not clobber the sheet's RTL flag");
        reloaded.ZoomPercent.Should().Be(125);
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrips_RightToLeftFlag()
    {
        var workbook = new Workbook("RtlBook");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.IsRightToLeft = true;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("value"));

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        var reloaded = adapter.Load(stream);

        reloaded.Sheets[0].IsRightToLeft.Should().BeTrue(".fxl must round-trip the sheet's RTL flag");
    }

    private static byte[] CreateSourcePackageWithRightToLeftSheetView()
    {
        using var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Sheet1");
            sheet.Cell("A1").Value = "מבחן";
            workbook.SaveAs(stream);
        }

        var bytes = stream.ToArray();
        using var editStream = new MemoryStream();
        editStream.Write(bytes, 0, bytes.Length);
        using (var archive = new ZipArchive(editStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
            var ns = worksheetXml.Root!.Name.Namespace;
            var sheetView = worksheetXml.Root!
                .Element(ns + "sheetViews")!
                .Elements(ns + "sheetView")
                .Single(view => string.Equals(view.Attribute("workbookViewId")?.Value ?? "0", "0", StringComparison.Ordinal));

            sheetView.SetAttributeValue("rightToLeft", "1");

            archive.GetEntry("xl/worksheets/sheet1.xml")?.Delete();
            var replacement = archive.CreateEntry("xl/worksheets/sheet1.xml");
            using var replacementStream = replacement.Open();
            worksheetXml.Save(replacementStream, System.Xml.Linq.SaveOptions.DisableFormatting);
        }

        return editStream.ToArray();
    }
}

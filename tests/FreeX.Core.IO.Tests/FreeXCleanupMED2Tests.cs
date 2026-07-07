using System.IO.Compression;
using ClosedXML.Excel;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression test for review P31: the source-preserving save path re-added cleared modeled
/// sheetView attributes (rightToLeft, showGridLines, showFormulas, view) from the untouched
/// source package because MergeWorksheetSheetViews called
/// XlsxNativeXmlMerger.MergeElementNativeAttributesAndChildren with an EMPTY modeled-attribute
/// exclusion list. XlsxWorksheetViewWriter encodes a "cleared" boolean as attribute ABSENCE, so
/// once the writer removed e.g. rightToLeft="1", the merge (running afterwards, against the
/// original source XML) could not tell "cleared by the model" apart from "never written by the
/// source" and copied the stale value straight back.
/// </summary>
public sealed class FreeXCleanupMED2Tests
{
    private static byte[] CreateSourcePackageWithNonDefaultSheetView()
    {
        using var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Sheet1");
            sheet.Cell("A1").Value = "value";
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

            // Non-default modeled attributes that XlsxWorksheetViewWriter clears via attribute
            // removal when the corresponding Sheet flag is turned off/on.
            sheetView.SetAttributeValue("rightToLeft", "1");
            sheetView.SetAttributeValue("showGridLines", "0");
            sheetView.SetAttributeValue("showFormulas", "1");
            sheetView.SetAttributeValue("view", "pageBreakPreview");

            archive.GetEntry("xl/worksheets/sheet1.xml")?.Delete();
            var replacement = archive.CreateEntry("xl/worksheets/sheet1.xml");
            using var replacementStream = replacement.Open();
            worksheetXml.Save(replacementStream, System.Xml.Linq.SaveOptions.DisableFormatting);
        }

        return editStream.ToArray();
    }

    [Fact]
    public void Save_LoadedWorkbook_WithClearedSheetViewFlags_DoesNotResurrectSourceAttributes()
    {
        var sourceBytes = CreateSourcePackageWithNonDefaultSheetView();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        // Sanity: the source's non-default flags surfaced correctly on load.
        sheet.IsRightToLeft.Should().BeTrue();
        sheet.ShowGridlines.Should().BeFalse();
        sheet.ShowFormulas.Should().BeTrue();
        sheet.ViewMode.Should().Be(WorksheetViewMode.PageBreakPreview);

        // Clear every modeled view flag back to Excel's defaults, plus an unrelated edit that
        // forces the source-preserving save (rebuild + merge-back) path to run.
        sheet.IsRightToLeft = false;
        sheet.ShowGridlines = true;
        sheet.ShowFormulas = false;
        sheet.ViewMode = WorksheetViewMode.Normal;
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("edited"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);

        saved.Position = 0;
        var reloaded = adapter.Load(saved).GetSheetAt(0);

        reloaded.IsRightToLeft.Should().BeFalse(
            "clearing RTL in the model must not be undone by re-merging the untouched source sheetView");
        reloaded.ShowGridlines.Should().BeTrue(
            "re-enabling gridlines in the model must not be undone by the source merge");
        reloaded.ShowFormulas.Should().BeFalse(
            "turning off Show Formulas in the model must not be undone by the source merge");
        reloaded.ViewMode.Should().Be(WorksheetViewMode.Normal,
            "returning to Normal view must not be undone by the source merge re-adding view=\"pageBreakPreview\"");
    }
}

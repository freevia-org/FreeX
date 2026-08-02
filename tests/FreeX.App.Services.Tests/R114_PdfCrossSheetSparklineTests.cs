using FluentAssertions;
using FreeX.Core.Model;
using Free.Shared.Pdf;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R114 finding: <see cref="WorkbookPdfContentBuilder"/> (the Avalonia/portable PDF export path,
/// reused by both <c>SkiaPdfDocumentExporter</c> and <c>AvaloniaPdfDocumentExporter</c>) reads every
/// sparkline's data range off the HOST sheet only, ignoring <see cref="SparklineModel.DataRange"/>'s
/// own <c>Start.Sheet</c> -- so a sparkline whose data lives on a different sheet than the one it is
/// displayed on (Excel's "Edit Data" cross-sheet data range, preserved by <c>XlsxSparklineMapper</c>)
/// silently read whatever sat at the same row/col coordinates on the host sheet instead. These tests
/// drive the real product entry point -- <c>WorkbookExportPrintPlanner</c> →
/// <c>PortablePdfExportPlanner</c> → <see cref="WorkbookPdfContentBuilder.BuildWithPageSetup"/> -- the
/// same call chain the portable PDF exporters use, mirroring R96_PdfDataBarIconSetSparklineTests'
/// harness, and assert ink is actually emitted, not merely that state is readable.
/// </summary>
public sealed class R114_PdfCrossSheetSparklineTests
{
    [Fact]
    public void BuildWithPageSetup_CrossSheetLineSparkline_DrawsInkFromSourceSheetNotHostSheet()
    {
        var workbook = new Workbook { Name = "Book1.xlsx" };
        var hostSheet = workbook.AddSheet("Sheet1");
        var dataSheet = workbook.AddSheet("Sheet2");

        // Host sheet's own A1:C1 (the same row/col coordinates the sparkline's data range uses) hold
        // only TEXT -- non-numeric, so if the reader ever fell back to reading the host sheet by
        // row/col, the series would be entirely blank and no line segments would be drawn at all.
        hostSheet.SetCell(new CellAddress(hostSheet.Id, 1, 1), new TextValue("x"));
        hostSheet.SetCell(new CellAddress(hostSheet.Id, 1, 2), new TextValue("y"));
        hostSheet.SetCell(new CellAddress(hostSheet.Id, 1, 3), new TextValue("z"));

        // The sparkline's actual data source: real numbers on Sheet2.
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 1, 1), new NumberValue(1));
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 1, 2), new NumberValue(9));
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 1, 3), new NumberValue(3));

        hostSheet.Sparklines.Add(new SparklineModel
        {
            DataRange = new GridRange(
                new CellAddress(dataSheet.Id, 1, 1),
                new CellAddress(dataSheet.Id, 1, 3)),
            Location = new CellAddress(hostSheet.Id, 2, 1),
            Kind = SparklineKind.Line,
        });

        var doc = BuildDocument(workbook, "A1:C2");

        doc.Pages[0].Ops.OfType<PdfLine>().Should().NotBeEmpty(
            "the sparkline's data range lives on Sheet2, so its real numeric values -- not Sheet1's blank text cells at the same coordinates -- must be drawn in the exported PDF");
    }

    [Fact]
    public void BuildWithPageSetup_SameSheetSparkline_StillDrawsInkAsBefore()
    {
        // No-regression sibling: an ordinary same-sheet sparkline (data range's sheet == host sheet,
        // matching R96_PdfDataBarIconSetSparklineTests' existing coverage) must be unaffected by the
        // cross-sheet resolution fix.
        var workbook = new Workbook { Name = "Book1.xlsx" };
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(9));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(3));

        sheet.Sparklines.Add(new SparklineModel
        {
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 3)),
            Location = new CellAddress(sheet.Id, 2, 1),
            Kind = SparklineKind.Line,
        });

        var doc = BuildDocument(workbook, "A1:C2");

        doc.Pages[0].Ops.OfType<PdfLine>().Should().NotBeEmpty(
            "an ordinary same-sheet sparkline must keep drawing its line ink after the cross-sheet fix");
    }

    private static PdfContentDocument BuildDocument(Workbook workbook, string selectedRange)
    {
        var intent = new WorkbookExportPrintIntent(
            WorkbookExportPrintScope.SelectedRange,
            WorkbookExportPrintOutputKind.Pdf,
            SelectedRange: GridRange.Parse(selectedRange, workbook.GetSheetAt(0)!.Id));

        var exportPlan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(workbook, intent);
        exportPlan.IsReady.Should().BeTrue(exportPlan.StatusText);

        var pdfPlan = PortablePdfExportPlanner.CreatePlan(exportPlan);
        pdfPlan.IsReady.Should().BeTrue(pdfPlan.StatusText);

        var doc = WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, pdfPlan);
        doc.Pages.Should().NotBeEmpty();
        return doc;
    }
}

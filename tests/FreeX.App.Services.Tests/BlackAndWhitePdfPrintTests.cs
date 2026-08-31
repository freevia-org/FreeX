using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Model;
using Free.Shared.Pdf;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Tests that the B&W flag (Sheet.PrintBlackAndWhite) suppresses cell fill colors and forces
/// font colors to black in the Avalonia/Skia PDF content model built by WorkbookPdfContentBuilder.
/// </summary>
public sealed class BlackAndWhitePdfPrintTests
{
    [Fact]
    public void BuildWithPageSetup_BlackAndWhite_SuppressesCellFillRects()
    {
        var workbook = new Workbook("B&W PDF");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PrintBlackAndWhite = true;

        // Cell with a bright red fill.
        var redStyle = workbook.RegisterStyle(new CellStyle { FillColor = new CellColor(255, 0, 0) });
        var redCell = Cell.FromValue(new TextValue("Red"));
        redCell.StyleId = redStyle;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), redCell);

        var intent = new WorkbookExportPrintIntent(
            WorkbookExportPrintScope.ActiveSheet,
            WorkbookExportPrintOutputKind.Pdf,
            ActiveSheetIndex: 0);

        var exportPlan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(workbook, intent);
        exportPlan.IsReady.Should().BeTrue();

        var pdfPlan = PortablePdfExportPlanner.CreatePlan(exportPlan);
        pdfPlan.IsReady.Should().BeTrue();

        var doc = WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, pdfPlan);
        doc.Pages.Should().NotBeEmpty();

        var ops = doc.Pages[0].Ops;
        // No PdfFillRect should carry a red colour (255, 0, 0) — B&W suppresses all fills.
        ops.OfType<PdfFillRect>()
            .Should().NotContain(r => r.Color.R == 255 && r.Color.G == 0 && r.Color.B == 0,
                "B&W mode should suppress colored cell fill rects");
    }

    [Fact]
    public void BuildWithPageSetup_BlackAndWhite_ForcesTextColorToBlack()
    {
        var workbook = new Workbook("B&W Font");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PrintBlackAndWhite = true;

        // Cell with bright blue font.
        var blueStyle = workbook.RegisterStyle(new CellStyle { FontColor = new CellColor(0, 0, 255) });
        var blueCell = Cell.FromValue(new TextValue("Blue text"));
        blueCell.StyleId = blueStyle;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), blueCell);

        var intent = new WorkbookExportPrintIntent(
            WorkbookExportPrintScope.ActiveSheet,
            WorkbookExportPrintOutputKind.Pdf,
            ActiveSheetIndex: 0);

        var exportPlan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(workbook, intent);
        var pdfPlan = PortablePdfExportPlanner.CreatePlan(exportPlan);
        var doc = WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, pdfPlan);

        var ops = doc.Pages[0].Ops;
        // Any PdfText for "Blue text" must have black colour.
        ops.OfType<PdfText>()
            .Where(t => t.Text.Contains("Blue text"))
            .Should().OnlyContain(t => t.Color.R == 0 && t.Color.G == 0 && t.Color.B == 0,
                "B&W mode must force all font colors to black");
    }

    [Fact]
    public void BuildWithPageSetup_NormalMode_PreservesCellFills()
    {
        var workbook = new Workbook("Color PDF");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PrintBlackAndWhite = false; // normal color mode

        var redStyle = workbook.RegisterStyle(new CellStyle { FillColor = new CellColor(255, 0, 0) });
        var redCell = Cell.FromValue(new TextValue("Red"));
        redCell.StyleId = redStyle;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), redCell);

        var intent = new WorkbookExportPrintIntent(
            WorkbookExportPrintScope.ActiveSheet,
            WorkbookExportPrintOutputKind.Pdf,
            ActiveSheetIndex: 0);

        var exportPlan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(workbook, intent);
        var pdfPlan = PortablePdfExportPlanner.CreatePlan(exportPlan);
        var doc = WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, pdfPlan);

        var ops = doc.Pages[0].Ops;
        // Normal mode should include a red fill.
        ops.OfType<PdfFillRect>()
            .Should().Contain(r => r.Color.R == 255 && r.Color.G == 0 && r.Color.B == 0,
                "color mode should preserve cell fill colors");
    }

    // freex-print-page-setup-F1: Page Setup > Sheet > "Black and white" is implemented to force every
    // fill/font/border to solid black for print (Excel's grayscale-print behavior), and the WPF native
    // print path (PrintRenderer.GridCells.cs's BlackAndWhiteGridlinePen) already does this for
    // gridlines too. This shared PDF content builder (used by both the Avalonia Save-As-PDF exporter
    // and, through PortablePdfExportPlanner, the print-preview geometry) previously drew gridlines with
    // a fixed light-gray color regardless of the flag, so PDF/preview output disagreed with what WPF
    // actually prints.
    [Fact]
    public void BuildWithPageSetup_BlackAndWhite_ForcesGridlinesToBlack()
    {
        var workbook = new Workbook("B&W Gridlines");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PrintBlackAndWhite = true;
        sheet.PrintGridlines = true;

        var cell = Cell.FromValue(new TextValue("X"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        var intent = new WorkbookExportPrintIntent(
            WorkbookExportPrintScope.ActiveSheet,
            WorkbookExportPrintOutputKind.Pdf,
            ActiveSheetIndex: 0);

        var exportPlan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(workbook, intent);
        exportPlan.IsReady.Should().BeTrue(exportPlan.StatusText);

        var pdfPlan = PortablePdfExportPlanner.CreatePlan(exportPlan);
        pdfPlan.IsReady.Should().BeTrue(pdfPlan.StatusText);

        var doc = WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, pdfPlan);
        doc.Pages.Should().NotBeEmpty();

        var gridLines = doc.Pages[0].Ops.OfType<PdfLine>().ToList();
        gridLines.Should().NotBeEmpty("PrintGridlines is on, so gridline PdfLine ops must be emitted");
        gridLines.Should().OnlyContain(
            l => l.Color.R == 0 && l.Color.G == 0 && l.Color.B == 0,
            "Black-and-white print must force gridlines to solid black, matching the WPF native print " +
            "path's BlackAndWhiteGridlinePen (PrintRenderer.GridCells.cs)");
    }

    [Fact]
    public void BuildWithPageSetup_NormalMode_KeepsGridlinesLightGray()
    {
        var workbook = new Workbook("Color Gridlines");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PrintBlackAndWhite = false; // normal color mode
        sheet.PrintGridlines = true;

        var cell = Cell.FromValue(new TextValue("X"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        var intent = new WorkbookExportPrintIntent(
            WorkbookExportPrintScope.ActiveSheet,
            WorkbookExportPrintOutputKind.Pdf,
            ActiveSheetIndex: 0);

        var exportPlan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(workbook, intent);
        var pdfPlan = PortablePdfExportPlanner.CreatePlan(exportPlan);
        var doc = WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, pdfPlan);

        var gridLines = doc.Pages[0].Ops.OfType<PdfLine>().ToList();
        gridLines.Should().NotBeEmpty("PrintGridlines is on, so gridline PdfLine ops must be emitted");
        gridLines.Should().OnlyContain(
            l => l.Color.R == 180 && l.Color.G == 185 && l.Color.B == 190,
            "normal (color) print mode must keep the sibling case unchanged -- gridlines stay the " +
            "existing light gray-blue, not forced to black");
    }
}

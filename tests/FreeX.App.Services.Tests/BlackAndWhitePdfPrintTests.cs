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
}

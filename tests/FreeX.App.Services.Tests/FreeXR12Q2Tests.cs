using FluentAssertions;
using FreeX.Core.Model;
using Free.Shared.Pdf;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Round-12 bucket Q2 fixes for <see cref="WorkbookPdfContentBuilder"/>'s page-setup-aware PDF path
/// (the Avalonia/Skia portable exporter).
/// </summary>
public sealed class FreeXR12Q2Tests
{
    /// <summary>
    /// R12-wpf-print-export-deep-2: Page Setup Scaling "Adjust to 200% normal size" must GROW every
    /// printed element in the portable/Skia PDF export, matching both Excel and FreeX's own WPF print
    /// path (PrintRenderer.HeaderFooter.cs pushes a ScaleTransform whenever scaleRatio != 1.0, not only
    /// when it shrinks). Pre-fix, ComputeActualGridSizes only multiplied col/row sizes when
    /// scaleRatio &lt; 1.0 and clamped text scale to Math.Min(1.0, scaleRatio), so a 200% scale rendered
    /// identically to 100%.
    /// </summary>
    [Fact]
    public void BuildWithPageSetup_ScaleUpTo200Percent_GrowsGridAndTextVersusUnscaled()
    {
        // Cells need an explicit fill color so BuildWithPageSetup actually emits a PdfFillRect op for
        // them -- the page-setup-aware path only fills cells that have a resolved fill color or sit in
        // a print title row/column (see WorkbookPdfContentBuilder.BuildPageWithPageSetup), unlike the
        // legacy Build path which always strokes/fills every cell rect.
        var fillColor = new CellColor(200, 210, 220);

        var normalWorkbook = new Workbook("Scale 100");
        var normalSheet = normalWorkbook.AddSheet("Sheet1");
        normalSheet.ScaleToFit = new WorksheetScaleToFit(100, null, null);
        var normalStyle = normalWorkbook.RegisterStyle(new CellStyle { FontSize = 10, FillColor = fillColor });
        var normalCell = Cell.FromValue(new TextValue("Hi"));
        normalCell.StyleId = normalStyle;
        normalSheet.SetCell(new CellAddress(normalSheet.Id, 1, 1), normalCell);

        var scaledWorkbook = new Workbook("Scale 200");
        var scaledSheet = scaledWorkbook.AddSheet("Sheet1");
        scaledSheet.ScaleToFit = new WorksheetScaleToFit(200, null, null);
        var scaledStyle = scaledWorkbook.RegisterStyle(new CellStyle { FontSize = 10, FillColor = fillColor });
        var scaledCell = Cell.FromValue(new TextValue("Hi"));
        scaledCell.StyleId = scaledStyle;
        scaledSheet.SetCell(new CellAddress(scaledSheet.Id, 1, 1), scaledCell);

        var intent = new WorkbookExportPrintIntent(
            WorkbookExportPrintScope.ActiveSheet,
            WorkbookExportPrintOutputKind.Pdf,
            ActiveSheetIndex: 0);

        var normalExportPlan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(normalWorkbook, intent);
        var normalPdfPlan = PortablePdfExportPlanner.CreatePlan(normalExportPlan);
        var normalDoc = WorkbookPdfContentBuilder.BuildWithPageSetup(normalWorkbook, normalPdfPlan);

        var scaledExportPlan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(scaledWorkbook, intent);
        var scaledPdfPlan = PortablePdfExportPlanner.CreatePlan(scaledExportPlan);
        var scaledDoc = WorkbookPdfContentBuilder.BuildWithPageSetup(scaledWorkbook, scaledPdfPlan);

        var normalFill = normalDoc.Pages[0].Ops.OfType<PdfFillRect>().First(r => r.Width > 0);
        var scaledFill = scaledDoc.Pages[0].Ops.OfType<PdfFillRect>().First(r => r.Width > 0);

        // Pre-fix: scaledFill.Width == normalFill.Width (200% silently ignored). Post-fix: ~2x wider.
        scaledFill.Width.Should().BeGreaterThan(normalFill.Width * 1.9,
            "Page Setup Scaling > 100% must grow the printed grid geometry, matching Excel and the WPF print path");
        scaledFill.Height.Should().BeGreaterThan(normalFill.Height * 1.9);

        var normalText = normalDoc.Pages[0].Ops.OfType<PdfText>().First(t => t.Text.Contains("Hi"));
        var scaledText = scaledDoc.Pages[0].Ops.OfType<PdfText>().First(t => t.Text.Contains("Hi"));

        // Pre-fix: textScale was capped at Math.Min(1.0, scaleRatio) == 1.0, so font size never grew.
        scaledText.FontSize.Should().BeGreaterThan(normalText.FontSize * 1.9,
            "text must scale up along with the grid for Scale% > 100, matching Excel and the WPF print path");
    }

    /// <summary>
    /// R12-wpf-print-export-deep-3: the portable/Skia PDF export's &amp;N (total pages) must include
    /// any "at end of sheet" comment-summary pages appended after the grid pages, matching the WPF
    /// PrintRenderer path (totalPages = printPlan.GridPageCount + commentSummaryPages.Count) and Excel.
    /// Pre-fix, ResolveEffectiveSheetTotalPages summed only sheetPlan.PageCount (grid pages), so a
    /// sheet with 1 grid page + 1 appended comment-summary page rendered "Page X of 1" on every page
    /// instead of "Page X of 2".
    /// </summary>
    [Fact]
    public void BuildWithPageSetup_PrintCommentsAtEnd_TotalPagesIncludesCommentSummaryPage()
    {
        var workbook = new Workbook("Budget");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PrintComments = WorksheetPrintComments.AtEnd;
        var addr = new CellAddress(sheet.Id, 2, 2);
        var cell = Cell.FromValue(new TextValue("Total"));
        sheet.SetCell(addr, cell);
        sheet.ThreadedComments[addr] = new ThreadedComment("Please verify", "Alice");
        sheet.PageFooter = new WorksheetHeaderFooter("", "Page &P of &N", "");

        var intent = new WorkbookExportPrintIntent(
            WorkbookExportPrintScope.ActiveSheet,
            WorkbookExportPrintOutputKind.Pdf,
            ActiveSheetIndex: 0);

        var exportPlan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(workbook, intent);
        var pdfPlan = PortablePdfExportPlanner.CreatePlan(exportPlan, workbook);
        pdfPlan.IsReady.Should().BeTrue();

        // Sanity: exactly one grid page + one appended comment-summary page.
        pdfPlan.PageRequests.Should().HaveCount(2);
        pdfPlan.PageRequests[0].IsCommentSummaryPage.Should().BeFalse();
        pdfPlan.PageRequests[1].IsCommentSummaryPage.Should().BeTrue();

        var doc = WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, pdfPlan);
        doc.Pages.Should().HaveCount(2);

        // Pre-fix: both pages' footer read "Page X of 1" (grid-page-count only).
        // Post-fix: both pages must agree the sheet has 2 total pages, same as the WPF path.
        var gridPageFooter = doc.Pages[0].Ops.OfType<PdfText>().First(t => t.Text.StartsWith("Page "));
        gridPageFooter.Text.Should().Be("Page 1 of 2");

        var commentPageFooter = doc.Pages[1].Ops.OfType<PdfText>().First(t => t.Text.StartsWith("Page "));
        commentPageFooter.Text.Should().Be("Page 2 of 2");
    }
}

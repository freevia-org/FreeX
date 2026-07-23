using FluentAssertions;
using FreeX.Core.Model;
using Free.Shared.Pdf;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R79-services-pagesetup-print-5-1 / R79-services-pagesetup-print-5-2: the portable/Skia PDF export
/// path (<see cref="WorkbookPdfContentBuilder.BuildWithPageSetup"/>) must honor
/// <see cref="Sheet.CenterHorizontallyOnPage"/>/<see cref="Sheet.CenterVerticallyOnPage"/> (offsetting
/// the printed grid within the page margins) and <see cref="Sheet.PrintHeadings"/> (drawing the
/// A/B/C.../1/2/3... row/column heading gutter), the same way the WPF print path already does via
/// <c>PageContentRenderModelBuilder</c> / <c>PrintRenderer.Headings.cs</c>. Pre-fix, the portable
/// builder ignored both settings entirely: the grid was always pinned flush to the top-left content
/// margin and no heading gutter was ever drawn.
/// </summary>
public sealed class PdfPageSetupCenterAndHeadingsTests
{
    [Fact]
    public void BuildWithPageSetup_CenterOnBothAxes_OffsetsGridAwayFromTopLeftMargin()
    {
        // A tiny single-cell used range on a full Letter/A4 page: with centering OFF the grid sits
        // flush against the top-left content margin; with centering ON it must move well to the
        // right and well down toward the middle of the page.
        var (offsetX, offsetY) = BuildFirstCellTextPosition(centerHorizontally: true, centerVertically: true);
        var (flushX, flushY) = BuildFirstCellTextPosition(centerHorizontally: false, centerVertically: false);

        offsetX.Should().BeGreaterThan(flushX + 50,
            "Center Horizontally on page must shift the tiny used-range grid well to the right of its " +
            "flush-left position, matching PageContentRenderModelBuilder's xOffset for the WPF path");
        offsetY.Should().BeLessThan(flushY - 50,
            "Center Vertically on page must shift the grid down from the top margin toward the page's " +
            "vertical center (lower PDF y-up value), matching PageContentRenderModelBuilder's yOffset");
    }

    [Fact]
    public void BuildWithPageSetup_CenterHorizontallyOnlyOnPage_ShiftsXButNotY()
    {
        // No-regression / axis-isolation sibling: enabling only Center Horizontally must not also
        // move the grid vertically -- the two flags are independent, exactly as
        // PageContentRenderModelBuilder computes xOffset/yOffset from two separate flags.
        var (centeredX, centeredY) = BuildFirstCellTextPosition(centerHorizontally: true, centerVertically: false);
        var (flushX, flushY) = BuildFirstCellTextPosition(centerHorizontally: false, centerVertically: false);

        centeredX.Should().BeGreaterThan(flushX + 50,
            "Center Horizontally alone must still shift the grid rightward");
        centeredY.Should().BeApproximately(flushY, 0.01,
            "Center Horizontally alone must leave the vertical position unchanged");
    }

    [Fact]
    public void BuildWithPageSetup_PrintHeadingsEnabled_DrawsColumnLetterAndRowNumberHeadings()
    {
        var page = BuildFirstPage(printHeadings: true);

        page.Ops.OfType<PdfText>().Should().Contain(t => t.Text == "A",
            "Print row and column headings must draw the 'A' column-letter heading for the first printed column");
        page.Ops.OfType<PdfText>().Should().Contain(t => t.Text == "1",
            "Print row and column headings must draw the '1' row-number heading for the first printed row");
    }

    [Fact]
    public void BuildWithPageSetup_PrintHeadingsDisabled_DrawsNoHeadingLabels()
    {
        // No-regression sibling: leaving the setting off (the default) must not draw the heading
        // gutter at all -- proving the fix is gated on Sheet.PrintHeadings rather than unconditional.
        var page = BuildFirstPage(printHeadings: false);

        page.Ops.OfType<PdfText>().Should().NotContain(t => t.Text == "A",
            "Column-letter headings must not be drawn when Print row and column headings is off");
        page.Ops.OfType<PdfText>().Should().NotContain(t => t.Text == "1",
            "Row-number headings must not be drawn when Print row and column headings is off");
    }

    private static (double X, double Y) BuildFirstCellTextPosition(bool centerHorizontally, bool centerVertically)
    {
        var workbook = new Workbook("Center");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.CenterHorizontallyOnPage = centerHorizontally;
        sheet.CenterVerticallyOnPage = centerVertically;

        var cell = Cell.FromValue(new TextValue("Hi"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        var page = BuildPage(workbook);
        var op = page.Ops.OfType<PdfText>().First(t => t.Text == "Hi");
        return (op.X, op.Y);
    }

    private static PdfContentPage BuildFirstPage(bool printHeadings)
    {
        var workbook = new Workbook("Headings");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PrintHeadings = printHeadings;

        // Numeric, non-colliding content so the assertions below can unambiguously look for the
        // heading labels "A"/"1" without matching the cell's own displayed text.
        var cell = Cell.FromValue(new NumberValue(42));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        return BuildPage(workbook);
    }

    private static PdfContentPage BuildPage(Workbook workbook)
    {
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
        return doc.Pages[0];
    }
}

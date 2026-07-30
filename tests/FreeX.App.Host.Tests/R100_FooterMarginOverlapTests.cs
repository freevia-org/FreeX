using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R100-app-host-footer-margin-overlap-1: mirror-image of R99-app-host-header-footer-margin-overlap-1.
/// Excel treats Footer margin as the distance from the page edge to the footer TEXT band, which sits
/// WITHIN the Bottom margin band as long as it doesn't exceed it -- but once Footer margin IS larger
/// than the Bottom margin, Excel keeps the footer text below the printed grid's own bottom edge
/// (<c>pageH - Math.Max(bottomMargin, footerMargin)</c>, the same <c>bodyBottomInches</c>
/// PagePaginationPlanner already used to size this page's row capacity) rather than letting it climb
/// up into the grid. <see cref="PrintRenderer.DrawHeaderFooter"/> (PrintRenderer.HeaderFooterDrawing.cs)
/// computed the footer text band purely from <c>pageH - footerMargin - footerHeight</c> with no
/// reference to the grid's own bottom edge, so whenever FooterMargin exceeded BottomMargin the footer
/// text band landed entirely inside the grid's own vertical span -- printing on top of the last
/// printed row(s), even though R99 already fixed the mirror-image header case in this same file. These
/// tests render through the real PrintRenderer.RenderWorksheet entry point (not a hand-built visual)
/// and assert on the actual drawn text-overlay Y coordinates.
/// </summary>
public sealed class R100_FooterMarginOverlapTests
{
    [Fact]
    public void RenderWorksheet_FooterMarginExceedsBottomMargin_FooterStaysAtOrBelowGridBottomEdge()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("FooterMarginOverlap.xlsx");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("DATA"));
            sheet.PageFooter = new WorksheetHeaderFooter("", "FTR", "");

            // Footer margin (1.5in) is deliberately larger than the bottom margin (0.2in) -- the
            // mirror-image scenario of the R99 header-side bug case, but on the footer/bottom side.
            sheet.PageMargins = new WorksheetPageMargins(Left: 0.7, Right: 0.7, Top: 0.75, Bottom: 0.2);
            sheet.HeaderMargin = 0.3;
            sheet.FooterMargin = 1.5;

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = document.Pages[0].GetPageRoot(forceReload: false)!;
            var overlays = PdfTextOverlayExtractor.Extract(page);

            var footerOverlay = overlays.Should().ContainSingle(o => o.Text == "FTR").Subject;

            var metrics = WorksheetPrintRenderPlanner.BuildMetrics(sheet);
            var gridBottomEdge = metrics.PageHeight - Math.Max(metrics.MarginBottom, metrics.FooterMargin);

            // The footer text band must never start above (a smaller Y than) the grid's own bottom
            // edge -- otherwise the footer text draws on top of the last printed row(s). A small
            // tolerance absorbs sub-pixel rounding while remaining far smaller than the footer-height
            // overlap (tens of px) the bug produced.
            footerOverlay.Y.Should().BeGreaterThanOrEqualTo(gridBottomEdge - 1.0,
                "the footer band must sit at or below the grid's own bottom edge " +
                "(max(bottomMargin, footerMargin) from the page top), never above/inside it");
        });
    }

    [Fact]
    public void RenderWorksheet_BottomMarginExceedsFooterMargin_FooterStillAnchorsAtPlainFooterMargin()
    {
        // Sibling/no-regression case: when the bottom margin is already the larger of the two (the
        // common/default case), the footer band must still resolve to its plain, unclamped position
        // exactly as before -- the fix must not shift the footer up when there's no overage to
        // compensate for.
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("FooterMarginNormal.xlsx");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("DATA"));
            sheet.PageFooter = new WorksheetHeaderFooter("", "FTR", "");

            sheet.PageMargins = new WorksheetPageMargins(Left: 0.7, Right: 0.7, Top: 0.75, Bottom: 0.75);
            sheet.HeaderMargin = 0.3;
            sheet.FooterMargin = 0.3;

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = document.Pages[0].GetPageRoot(forceReload: false)!;
            var overlays = PdfTextOverlayExtractor.Extract(page);

            var footerOverlay = overlays.Should().ContainSingle(o => o.Text == "FTR").Subject;

            var metrics = WorksheetPrintRenderPlanner.BuildMetrics(sheet);
            var footerHeight = footerOverlay.FontSize + 4; // approximate; exact height is measured internally
            var expectedFooterY = metrics.PageHeight - metrics.FooterMargin - footerHeight;

            footerOverlay.Y.Should().BeInRange(expectedFooterY - 20.0, expectedFooterY + 20.0,
                "with the default/normal margins (bottom margin already bigger than footer margin), the " +
                "footer band should still sit near its plain, unclamped position");
        });
    }
}

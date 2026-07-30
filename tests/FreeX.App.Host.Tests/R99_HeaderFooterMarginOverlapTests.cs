using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R99-app-host-header-footer-margin-overlap-1: Excel treats Header/Footer margin as the distance
/// from the page edge to the header/footer text band, which sits WITHIN the Top/Bottom margin band
/// as long as it doesn't exceed it -- but once a Header/Footer margin IS larger than its
/// corresponding Top/Bottom margin, Excel pushes the printed grid down/up so it never overlaps the
/// header/footer text. PagePaginationPlanner already reserves fewer rows per page for this case
/// (Math.Max(margins.Top, headerMarginInches)), but PrintRenderer.HeaderFooter.cs's RenderPageVisual
/// used to compute the grid's actual drawn top position from the plain Top margin only, so the
/// printed page still started the grid where the un-adjusted margin said to -- visually colliding
/// with the header text -- even though the pagination math had already agreed the row budget should
/// shrink to make room. These tests render through the real PrintRenderer.RenderWorksheet entry
/// point (not a hand-built visual) and assert on the actual drawn text-overlay Y coordinates.
/// </summary>
public sealed class R99_HeaderFooterMarginOverlapTests
{
    private const double Dpi = 96.0;

    [Fact]
    public void RenderWorksheet_HeaderMarginExceedsTopMargin_GridStartsAtHeaderMarginNotTopMargin()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("HeaderMarginOverlap.xlsx");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("DATA"));
            sheet.PageHeader = new WorksheetHeaderFooter("", "HDR", "");

            // Header margin (0.6in) is deliberately larger than the top margin (0.2in) -- the
            // scenario the R88 pagination fix already handles for row capacity, but which this
            // render path used to ignore when positioning the actual drawn grid.
            sheet.PageMargins = new WorksheetPageMargins(Left: 0.7, Right: 0.7, Top: 0.2, Bottom: 0.75);
            sheet.HeaderMargin = 0.6;
            sheet.FooterMargin = 0.3;

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = document.Pages[0].GetPageRoot(forceReload: false)!;
            var overlays = PdfTextOverlayExtractor.Extract(page);

            var headerOverlay = overlays.Should().ContainSingle(o => o.Text == "HDR").Subject;
            var cellOverlay = overlays.Should().ContainSingle(o => o.Text == "DATA").Subject;

            // The grid's top edge must land at (or below) the header-margin line -- not at the
            // smaller plain top margin -- so the printed first row never starts above where the
            // header text itself is anchored. A generous 10px tolerance absorbs cell-internal
            // vertical-alignment padding while remaining far smaller than the ~38px gap the bug
            // produced (0.2in vs 0.6in @ 96dpi).
            cellOverlay.Y.Should().BeGreaterThanOrEqualTo((0.6 * Dpi) - 10.0);

            // Directly assert the overlap is gone: the first printed row's text must sit at or
            // below the header's own text, never above/inside it.
            cellOverlay.Y.Should().BeGreaterThanOrEqualTo(headerOverlay.Y);
        });
    }

    [Fact]
    public void RenderWorksheet_TopMarginExceedsHeaderMargin_GridStillStartsAtPlainTopMargin()
    {
        // Sibling/no-regression case: when the top margin is already the larger of the two (the
        // common/default case), Math.Max(marginTop, headerMargin) must resolve to the plain top
        // margin exactly as before -- the fix must not shift the grid down when there's no header
        // margin overage to compensate for.
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("HeaderMarginNormal.xlsx");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("DATA"));
            sheet.PageHeader = new WorksheetHeaderFooter("", "HDR", "");

            sheet.PageMargins = new WorksheetPageMargins(Left: 0.7, Right: 0.7, Top: 0.75, Bottom: 0.75);
            sheet.HeaderMargin = 0.3;
            sheet.FooterMargin = 0.3;

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = document.Pages[0].GetPageRoot(forceReload: false)!;
            var overlays = PdfTextOverlayExtractor.Extract(page);

            var cellOverlay = overlays.Should().ContainSingle(o => o.Text == "DATA").Subject;

            // With the default/normal margins (top margin already bigger than header margin), the
            // grid's top edge should still sit at essentially the plain top margin (0.75in @
            // 96dpi = 72px), within the same generous tolerance used above.
            cellOverlay.Y.Should().BeInRange((0.75 * Dpi) - 5.0, (0.75 * Dpi) + 25.0);
        });
    }
}

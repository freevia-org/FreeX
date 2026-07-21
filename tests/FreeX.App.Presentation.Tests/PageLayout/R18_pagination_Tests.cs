using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

/// <summary>
/// R18-print-pagination-exact-1 and R18-print-pagination-exact-3: PagePaginationPlanner must (1)
/// derive a single uniform scale from whichever axis carries an explicit fit-to-pages request and
/// apply that same scale to the free (unconstrained) axis instead of leaving it at 100% capacity, and
/// (2) slice pages by the real ACCUMULATED row height / column width, not by a fixed count derived
/// from the AVERAGE row height / column width across the whole print range.
/// </summary>
public sealed class R18_pagination_Tests
{
    private static readonly Dictionary<uint, double> EmptyDict = new();

    private static GridRange Range(uint startRow, uint startCol, uint endRow, uint endCol)
    {
        var sheetId = SheetId.New();
        return new GridRange(
            new CellAddress(sheetId, startRow, startCol),
            new CellAddress(sheetId, endRow, endCol));
    }

    /// <summary>
    /// R18-print-pagination-exact-1: a 100-column x 60-row sheet with "Fit to 1 page wide by [auto]
    /// tall" must collapse to a single 1x1 page. Pre-fix, only the constrained column axis was
    /// shrunk (to fit 100 columns on 1 page); the free row axis stayed at its unscaled ~51-rows/page
    /// capacity, so 60 rows needed 2 row pages, giving 1x2 = 2 pages total instead of Excel's uniformly
    /// shrunk 1x1.
    /// </summary>
    [Fact]
    public void BuildPlan_FitToOneWideAutoTall_UniformlyShrinksFreeRowAxisToASinglePage()
    {
        var plan = PagePaginationPlanner.BuildPlan(
            Range(1, 1, 60, 100),
            new WorksheetScaleToFit(ScalePercent: null, FitToPagesWide: 1, FitToPagesTall: null),
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.A4,
            WorksheetPageOrientation.Portrait,
            WorksheetPageMargins.Narrow,
            rowHeights: EmptyDict,
            defaultRowHeight: PagePaginationPlanner.NominalRowHeight,
            columnWidths: EmptyDict,
            defaultColumnWidth: ColumnWidthPixelMapper.PixelsToColumnWidth(PagePaginationPlanner.MinimumPrintColumnWidth),
            headerMarginInches: 0.0,
            footerMarginInches: 0.0);

        plan.ColumnPageCount.Should().Be(1, "Fit-to-1-wide collapses all 100 columns onto one page");
        plan.RowPageCount.Should().Be(1,
            "the row axis must shrink by the same uniform ratio implied by the column axis's fit-to-1 " +
            "request, instead of staying at 100% capacity and needing a second row page for 60 rows");
        plan.PageCount.Should().Be(1, "Excel's single uniform shrink produces a 1x1 grid, not 1x2");
    }

    /// <summary>
    /// R18-print-pagination-exact-3: a range whose first 10 rows are 100px tall and remaining rows are
    /// 20px tall must break its first page once the real cumulative height would exceed the printable
    /// body (~978px for A4 narrow — Excel's real narrow margins are 0.25" left/right, 0.75" top/bottom),
    /// not after a fixed count derived from the AVERAGE row height (28px average -&gt; ~34 rows/page,
    /// which would put ~1360px of real content on a ~978px page).
    /// </summary>
    [Fact]
    public void BuildPlan_NonUniformRowHeights_BreaksFirstPageByAccumulatedHeightNotAverageCount()
    {
        var rowHeights = new Dictionary<uint, double>();
        for (var row = 1u; row <= 10u; row++)
            rowHeights[row] = 100.0;
        for (var row = 11u; row <= 100u; row++)
            rowHeights[row] = 20.0;

        var plan = PagePaginationPlanner.BuildPlan(
            Range(1, 1, 100, 5),
            WorksheetScaleToFit.Default,
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.A4,
            WorksheetPageOrientation.Portrait,
            WorksheetPageMargins.Narrow,
            rowHeights: rowHeights,
            defaultRowHeight: 20.0,
            columnWidths: EmptyDict,
            defaultColumnWidth: ColumnWidthPixelMapper.PixelsToColumnWidth(PagePaginationPlanner.MinimumPrintColumnWidth),
            headerMarginInches: 0.0,
            footerMarginInches: 0.0);

        // Real cumulative height: 9 rows @100px = 900px (<= printable ~978.24px); row 10 @100px would
        // push it to 1000px, over budget -> the first page stops at row 9.
        plan.RowPlans[0].BodyRows[^1].Should().Be(9u,
            "9 rows@100px = 900px fits the ~978px printable body, but a 10th 100px row would " +
            "overflow it -- the average-based (28px average -> ~34 rows/page) approach would instead put " +
            "34 rows (~1360px of real content) on this page");
        plan.RowPageCount.Should().BeGreaterThan(1, "100 rows of mixed height need more than one page");
    }

    /// <summary>
    /// R18-print-pagination-exact-3 (columns): mirrors the row test above for non-uniform column
    /// widths. Two very wide (40-char / 285px) columns followed by narrower (default 8.43-char /
    /// 64px) columns must break once the real cumulative width would exceed the printable body
    /// (~745.92px for A4 narrow — Excel's real narrow margins are 0.25" left/right, 0.75" top/bottom),
    /// not after a fixed count derived from the AVERAGE column width.
    /// </summary>
    [Fact]
    public void BuildPlan_NonUniformColumnWidths_BreaksFirstPageByAccumulatedWidthNotAverageCount()
    {
        var columnWidths = new Dictionary<uint, double>
        {
            [1] = 40.0,
            [2] = 40.0,
        };

        var plan = PagePaginationPlanner.BuildPlan(
            Range(1, 1, 5, 50),
            WorksheetScaleToFit.Default,
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.A4,
            WorksheetPageOrientation.Portrait,
            WorksheetPageMargins.Narrow,
            rowHeights: EmptyDict,
            defaultRowHeight: PagePaginationPlanner.NominalRowHeight,
            columnWidths: columnWidths,
            defaultColumnWidth: 8.43,
            headerMarginInches: 0.0,
            footerMarginInches: 0.0);

        // Real cumulative width: col1+col2 @285px each = 570px, + col3 @64px (default) = 634px, +
        // col4 @64px (default) = 698px (<= printable ~745.92px); col5 would push it to 762px, over
        // budget -> the first page stops at column 4. The average-based approach (average ~72.8px/col
        // across all 50 columns -> ~10 columns/page) would instead put ~10 columns on this page.
        plan.ColumnPlans[0].BodyColumns[^1].Should().Be(4u,
            "285+285+64+64 = 698px fits the ~745.92px printable body, but a 5th (64px) column would " +
            "overflow it");
        plan.ColumnPageCount.Should().BeGreaterThan(1, "50 columns of mixed width need more than one page");
    }
}

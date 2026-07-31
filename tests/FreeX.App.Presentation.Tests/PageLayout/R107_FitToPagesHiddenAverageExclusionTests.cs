using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

/// <summary>
/// R107-presentation-pagination-fit-to-pages-hidden-average-exclusion: the "base" (unscaled)
/// rows/columns-per-page that <c>CalculatePageCapacityDetail</c> derives the implied Fit-to-N-pages
/// scale from comes from <c>AverageRowHeightPixels</c>/<c>AverageColumnWidthPixels</c>, averaged over
/// ALL rows/columns in the print range -- including hidden ones. The "target" rows/columns-per-page
/// (via <c>PageGeometryRules.CountBodyItems</c>) correctly excludes hidden rows/columns
/// (R102-presentation-pagination-fit-to-pages-hidden-exclusion). When a hidden block's real recorded
/// height/width differs materially from the visible rows/columns' average -- e.g. "Format &gt; Hide
/// Rows" on a block of tall autofit rows, which leaves the original height in
/// <c>sheet.RowHeights</c> for unhide -- the "base" (hidden-polluted) and "target" (visible-only)
/// populations diverge, producing a wildly wrong implied scale and per-page body budget that collapses
/// all visible content onto far fewer real pages than Excel produces. R102's own tests never exposed
/// this because they use an empty row-heights/column-widths dictionary, so every row/column (hidden or
/// visible) resolves to the identical default size -- masking the average-side gap entirely.
/// </summary>
public sealed class R107_FitToPagesHiddenAverageExclusionTests
{
    private static readonly Dictionary<uint, double> EmptyDict = new();

    // Letter portrait (11.0in tall) with 2.375in top/bottom margins gives an exact 600px printable
    // body height at 96 dpi ((11.0 - 4.75) * 96 = 600), and 20px default row height gives an exact
    // 30 baseline rows/page (600 / 20 = 30) -- the same fixture as R102's row tests.
    private static readonly WorksheetPageMargins TallBodyMargins = new(Left: 0.75, Right: 0.75, Top: 2.375, Bottom: 2.375);

    // Letter portrait (8.5in wide) with 2.25in left/right margins gives an exact 384px printable body
    // width at 96 dpi ((8.5 - 4.5) * 96 = 384), and a 40px minimum column width gives an exact 9
    // baseline columns/page (384 / 40 = 9.6 -> floor 9) -- the same fixture as R102's column tests.
    private static readonly WorksheetPageMargins WideBodyMargins = new(Left: 2.25, Right: 2.25, Top: 0.75, Bottom: 0.75);

    private static GridRange Range(uint startRow, uint startCol, uint endRow, uint endCol)
    {
        var sheetId = SheetId.New();
        return new GridRange(
            new CellAddress(sheetId, startRow, startCol),
            new CellAddress(sheetId, endRow, endCol));
    }

    /// <summary>
    /// Reproduces the defect evidence exactly: print range rows 1-300, rows 1-90 visible at the 20px
    /// default, rows 91-300 hidden via <c>isRowHidden</c> but carrying an explicit 200px
    /// <c>sheet.RowHeights</c> entry each (exactly what Format &gt; Hide Rows leaves behind for a
    /// previously-tall hidden block), "Fit to 3 pages tall". 90 visible rows over a 30-rows/page
    /// baseline need EXACTLY 3 pages at 100% scale (Excel's ground truth -- no shrink needed at all).
    /// Before the fix, the 210 hidden 200px rows pollute the base average
    /// ((90*20 + 210*200)/300 = 146px -&gt; baseRowsPerPage = floor(600/146) = 4), which combined with
    /// the correctly hidden-aware target of 30 rows/page derives a tiny rowScale (4/30) and a hugely
    /// inflated per-page budget (4500px) that swallows all 90 visible rows (only 1800px) onto ONE page.
    /// </summary>
    [Fact]
    public void BuildPlan_FitToPagesTall_HiddenRowsWithTallerRecordedHeight_ExcludesHiddenFromBaseAverage()
    {
        var rowHeights = new Dictionary<uint, double>();
        for (uint row = 91; row <= 300; row++)
            rowHeights[row] = 200.0;

        var plan = PagePaginationPlanner.BuildPlan(
            Range(1, 1, 300, 1),
            new WorksheetScaleToFit(ScalePercent: null, FitToPagesWide: null, FitToPagesTall: 3),
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.Letter,
            WorksheetPageOrientation.Portrait,
            TallBodyMargins,
            rowHeights: rowHeights,
            defaultRowHeight: PagePaginationPlanner.NominalRowHeight,
            columnWidths: EmptyDict,
            defaultColumnWidth: ColumnWidthPixelMapper.PixelsToColumnWidth(PagePaginationPlanner.MinimumPrintColumnWidth),
            headerMarginInches: 0.0,
            footerMarginInches: 0.0,
            isRowHidden: row => row > 90);

        plan.RowPageCount.Should().Be(3,
            "90 visible rows over a 30-rows/page baseline need exactly 3 pages at 100% scale -- folding " +
            "the hidden 91-300 rows' real 200px recorded height into the base average must not inflate " +
            "the per-page budget and collapse all 90 visible rows onto one page");
        plan.RowPlans[0].BodyRows.Should().HaveCount(30, "each of the 3 pages should hold 30 of the 90 visible rows");
        plan.EffectiveScalePercent.Should().BeApproximately(100.0, 0.01,
            "no shrink is needed once hidden rows are excluded from BOTH the base average and the target count");
    }

    /// <summary>
    /// Column-axis counterpart: print range columns 1-300, columns 1-27 visible at the 40px default,
    /// columns 28-300 hidden via <c>isColumnHidden</c> but carrying an explicit maximum (255-char,
    /// 1790px) <c>sheet.ColumnWidths</c> entry each. 27 visible columns over a 9-columns/page baseline
    /// need exactly 3 pages at 100% scale for "Fit to 3 pages wide".
    /// </summary>
    [Fact]
    public void BuildPlan_FitToPagesWide_HiddenColumnsWithWiderRecordedWidth_ExcludesHiddenFromBaseAverage()
    {
        var columnWidths = new Dictionary<uint, double>();
        for (uint col = 28; col <= 300; col++)
            columnWidths[col] = ColumnWidthPixelMapper.MaximumColumnWidth;

        var plan = PagePaginationPlanner.BuildPlan(
            Range(1, 1, 1, 300),
            new WorksheetScaleToFit(ScalePercent: null, FitToPagesWide: 3, FitToPagesTall: null),
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.Letter,
            WorksheetPageOrientation.Portrait,
            WideBodyMargins,
            rowHeights: EmptyDict,
            defaultRowHeight: PagePaginationPlanner.NominalRowHeight,
            columnWidths: columnWidths,
            defaultColumnWidth: ColumnWidthPixelMapper.PixelsToColumnWidth(PagePaginationPlanner.MinimumPrintColumnWidth),
            headerMarginInches: 0.0,
            footerMarginInches: 0.0,
            isColumnHidden: col => col > 27);

        plan.ColumnPageCount.Should().Be(3,
            "27 visible columns over a 9-columns/page baseline need exactly 3 pages at 100% scale -- " +
            "folding the hidden 28-300 columns' real 1790px recorded width into the base average must " +
            "not inflate the per-page budget and collapse all 27 visible columns onto one page");
        plan.ColumnPlans[0].BodyColumns.Should().HaveCount(9, "each of the 3 pages should hold 9 of the 27 visible columns");
    }

    /// <summary>
    /// No-regression sibling covering the exact R102 scenario (hidden rows resolving to the SAME
    /// default height as visible ones, via an empty row-heights dictionary): the base average and the
    /// hidden-aware target must still agree and still resolve to exactly 3 pages of 30 rows. Proves the
    /// hidden-exclusion added to the average does not perturb the case where hidden and visible rows
    /// happen to share the same height (the case R102's own tests already covered).
    /// </summary>
    [Fact]
    public void BuildPlan_FitToPagesTall_HiddenRowsSameDefaultHeight_StillProducesThreePagesOfThirty()
    {
        var plan = PagePaginationPlanner.BuildPlan(
            Range(1, 1, 300, 1),
            new WorksheetScaleToFit(ScalePercent: null, FitToPagesWide: null, FitToPagesTall: 3),
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.Letter,
            WorksheetPageOrientation.Portrait,
            TallBodyMargins,
            rowHeights: EmptyDict,
            defaultRowHeight: PagePaginationPlanner.NominalRowHeight,
            columnWidths: EmptyDict,
            defaultColumnWidth: ColumnWidthPixelMapper.PixelsToColumnWidth(PagePaginationPlanner.MinimumPrintColumnWidth),
            headerMarginInches: 0.0,
            footerMarginInches: 0.0,
            isRowHidden: row => row > 90);

        plan.RowPageCount.Should().Be(3);
        plan.RowPlans[0].BodyRows.Should().HaveCount(30);
    }

    /// <summary>
    /// Unit-level coverage of the fixed helper directly: with a hidden predicate, rows in the hidden
    /// range must be excluded from both the sum and the divisor of the average, not merely skipped in
    /// the sum (which would silently understate the average instead of computing the correct one).
    /// </summary>
    [Fact]
    public void AverageRowHeightPixels_WithHiddenPredicate_ExcludesHiddenRowsFromAverage()
    {
        var rowHeights = new Dictionary<uint, double>();
        for (uint row = 91; row <= 300; row++)
            rowHeights[row] = 200.0;

        // Rows 1-90 default to 20px (absent from the dictionary); rows 91-300 are 200px but hidden.
        var avg = PagePaginationPlanner.AverageRowHeightPixels(
            1, 300, rowHeights, defaultRowHeight: 20.0, isHidden: row => row > 90);

        avg.Should().Be(20.0, "the hidden 91-300 rows' 200px heights must not be folded into the average " +
            "at all -- neither in the sum nor in the divisor -- leaving only the 90 visible 20px rows");
    }

    /// <summary>
    /// Unit-level coverage of the column-axis fixed helper directly, mirroring
    /// <see cref="AverageRowHeightPixels_WithHiddenPredicate_ExcludesHiddenRowsFromAverage"/>.
    /// </summary>
    [Fact]
    public void AverageColumnWidthPixels_WithHiddenPredicate_ExcludesHiddenColumnsFromAverage()
    {
        var columnWidths = new Dictionary<uint, double>();
        for (uint col = 28; col <= 300; col++)
            columnWidths[col] = ColumnWidthPixelMapper.MaximumColumnWidth;

        var defaultChars = ColumnWidthPixelMapper.PixelsToColumnWidth(PagePaginationPlanner.MinimumPrintColumnWidth);
        var expectedPx = ColumnWidthPixelMapper.ColumnWidthToPixels(defaultChars);

        var avg = PagePaginationPlanner.AverageColumnWidthPixels(
            1, 300, columnWidths, defaultColumnWidth: defaultChars, isHidden: col => col > 27);

        avg.Should().BeApproximately(expectedPx, 0.01,
            "the hidden 28-300 columns' 1790px widths must not be folded into the average at all, " +
            "leaving only the 27 visible default-width columns");
    }
}

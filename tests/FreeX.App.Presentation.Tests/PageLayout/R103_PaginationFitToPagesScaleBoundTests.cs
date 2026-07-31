using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

/// <summary>
/// R103-print-pagination-scale-bound-1: Excel's Page Setup scale (typed directly or implicitly
/// derived from a "Fit to N pages wide/tall" request) is hard-bounded to 10%-400%
/// (<c>PagePaginationPlanner</c>'s <c>MinScalePercent</c>/<c>MaxScalePercent</c>). When only ONE axis
/// carries an explicit fit-to-pages request (the "wideConstrained-only"/"tallConstrained-only"
/// branches of <c>CalculatePageCapacityDetail</c>), the constrained axis's OWN resolved capacity must
/// be bound to that same range too -- not just the free axis that <c>ApplyUniformScaleToFreeAxis</c>
/// already clamps. Before the fix, the constrained axis kept <c>ApplyScaleToFitCapacity</c>'s raw,
/// unbounded result while the free axis got the clamped percent, baking two different real scales
/// into what R18's own comment calls ONE uniform scale -- so a Fit-to-N request that would need a
/// shrink below 10% silently crammed all requested content onto exactly N pages at an unbounded,
/// unreadably tiny scale instead of correctly spilling onto extra pages the way real Excel does.
/// </summary>
public sealed class R103_PaginationFitToPagesScaleBoundTests
{
    private static readonly Dictionary<uint, double> EmptyDict = new();

    // Same exact-round-number margins as R102_FitToPagesHiddenRowsColumnsCapacityTests: Letter
    // portrait (8.5in wide) with 2.25in left/right margins gives an exact 384px printable body width
    // at 96 dpi ((8.5 - 4.5) * 96 = 384), and a 40px minimum column width gives an exact 9 baseline
    // columns/page (384 / 40 = 9.6 -> floor 9).
    private static readonly WorksheetPageMargins WideBodyMargins = new(Left: 2.25, Right: 2.25, Top: 0.75, Bottom: 0.75);

    // Letter portrait (11.0in tall) with 2.375in top/bottom margins gives an exact 600px printable
    // body height at 96 dpi ((11.0 - 4.75) * 96 = 600), and 20px default row height gives an exact
    // 30 baseline rows/page (600 / 20 = 30).
    private static readonly WorksheetPageMargins TallBodyMargins = new(Left: 0.75, Right: 0.75, Top: 2.375, Bottom: 2.375);

    private static GridRange Range(uint startRow, uint startCol, uint endRow, uint endCol)
    {
        var sheetId = SheetId.New();
        return new GridRange(
            new CellAddress(sheetId, startRow, startCol),
            new CellAddress(sheetId, endRow, endCol));
    }

    /// <summary>
    /// Reproduces the defect evidence: a 1x300 print range (baseline 9 columns/page) with "Fit to 1
    /// page wide" (column axis constrained, row axis free). Literally honoring "1 page wide" here
    /// would require shrinking to 9/300 = 3% -- far below Excel's 10% floor. Real Excel instead floors
    /// the scale at 10% and lets the sheet spill onto MORE than 1 page wide (4 pages of ~96 columns
    /// each at 40px/column against the 384px/10%=3840px-equivalent budget). Before the fix, the
    /// constrained column axis kept the raw, unbounded 3% implied scale (300 columns/page, all 300
    /// columns fit on a single page); after the fix it is bounded to 10% (90 columns/page) exactly
    /// like the free row axis already was, correctly spilling onto 4 pages.
    /// </summary>
    [Fact]
    public void BuildPlan_FitToOnePageWide_BeyondTenPercentFloor_SpillsOntoExtraPagesInsteadOfUnboundedShrink()
    {
        var plan = PagePaginationPlanner.BuildPlan(
            Range(1, 1, 1, 300),
            new WorksheetScaleToFit(ScalePercent: null, FitToPagesWide: 1, FitToPagesTall: null),
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.Letter,
            WorksheetPageOrientation.Portrait,
            WideBodyMargins,
            rowHeights: EmptyDict,
            defaultRowHeight: PagePaginationPlanner.NominalRowHeight,
            columnWidths: EmptyDict,
            defaultColumnWidth: ColumnWidthPixelMapper.PixelsToColumnWidth(PagePaginationPlanner.MinimumPrintColumnWidth),
            headerMarginInches: 0.0,
            footerMarginInches: 0.0);

        plan.ColumnPageCount.Should().Be(4,
            "300 columns at 40px each against a 10%-floored budget (384px / 10% = 3840px, i.e. 96 " +
            "columns/page) spill onto 4 pages (96+96+96+12) -- honoring the literal 'fit to 1 page wide' " +
            "request would require an unbounded 3% shrink Excel never applies, which is exactly what the " +
            "pre-fix code did by crushing all 300 columns onto a single page");
        plan.Capacity.ColumnsPerPage.Should().Be(90,
            "the constrained column axis's OWN resolved capacity must also be bound to the 10% floor " +
            "(9 baseline columns/page * 100/10 = 90), matching the same clamp ApplyUniformScaleToFreeAxis " +
            "already applies to the free row axis, instead of keeping ApplyScaleToFitCapacity's raw " +
            "unbounded 300-columns/page result");
    }

    /// <summary>
    /// Row-axis mirror of the above: a 300x1 print range (baseline 30 rows/page) with "Fit to 1 page
    /// tall" (row axis constrained, column axis free). The literal request needs a 30/300 = 10%...
    /// use 450 rows so the implied shrink (30/450 = 6.67%) is clearly beyond the 10% floor and the
    /// bug's rounding tolerance.
    /// </summary>
    [Fact]
    public void BuildPlan_FitToOnePageTall_BeyondTenPercentFloor_SpillsOntoExtraPagesInsteadOfUnboundedShrink()
    {
        var plan = PagePaginationPlanner.BuildPlan(
            Range(1, 1, 450, 1),
            new WorksheetScaleToFit(ScalePercent: null, FitToPagesWide: null, FitToPagesTall: 1),
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
            footerMarginInches: 0.0);

        // Baseline 30 rows/page, 20px/row: 10%-floored budget = 600px / 10% = 6000px = 300 rows/page.
        // 450 rows at 20px each spills onto 2 pages (300 + 150) instead of the pre-fix single page.
        plan.RowPageCount.Should().Be(2,
            "450 rows at 20px each against a 10%-floored budget (600px / 10% = 6000px, i.e. 300 " +
            "rows/page) spill onto 2 pages (300+150) -- honoring the literal 'fit to 1 page tall' " +
            "request would require an unbounded ~6.7% shrink Excel never applies");
        plan.Capacity.RowsPerPage.Should().Be(300,
            "the constrained row axis's OWN resolved capacity must also be bound to the 10% floor " +
            "(30 baseline rows/page * 100/10 = 300), matching the clamp already applied to the free " +
            "column axis, instead of the raw unbounded 450-rows/page result");
    }

    /// <summary>
    /// No-regression sibling: the identical wideConstrained-only shape, but with a column count whose
    /// implied shrink (9/45 = 20%) stays comfortably WITHIN the [10, 400] range. The fix must not
    /// change this case at all -- both the raw unbounded capacity and the newly-bounded capacity agree
    /// exactly (45 columns/page), and the sheet still collapses onto exactly the requested 1 page.
    /// </summary>
    [Fact]
    public void BuildPlan_FitToOnePageWide_WithinScaleRange_StillCollapsesOntoRequestedSinglePage()
    {
        var plan = PagePaginationPlanner.BuildPlan(
            Range(1, 1, 1, 45),
            new WorksheetScaleToFit(ScalePercent: null, FitToPagesWide: 1, FitToPagesTall: null),
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.Letter,
            WorksheetPageOrientation.Portrait,
            WideBodyMargins,
            rowHeights: EmptyDict,
            defaultRowHeight: PagePaginationPlanner.NominalRowHeight,
            columnWidths: EmptyDict,
            defaultColumnWidth: ColumnWidthPixelMapper.PixelsToColumnWidth(PagePaginationPlanner.MinimumPrintColumnWidth),
            headerMarginInches: 0.0,
            footerMarginInches: 0.0);

        plan.ColumnPageCount.Should().Be(1,
            "45 columns need a 9/45 = 20% shrink to fit on 1 page -- well within Excel's [10, 400] " +
            "scale range, so the requested single page is honored exactly, both before and after the " +
            "10%-floor fix (the fix only changes behavior once the implied shrink falls outside range)");
        plan.Capacity.ColumnsPerPage.Should().Be(45,
            "within [10, 400] the bounded and unbounded capacity resolutions agree exactly");
        plan.EffectiveScalePercent.Should().BeApproximately(100.0, 0.01,
            "the actual resulting page count already equals the requested 1, so CalculateEffectiveScalePercent " +
            "correctly reports 100% here -- there is no unbounded shrink hiding underneath this particular case");
    }
}

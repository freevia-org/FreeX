using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

/// <summary>
/// R102-presentation-pagination-fit-to-pages-hidden-exclusion: when a print range has hidden
/// rows/columns AND Page Setup uses "Fit to N pages wide/tall" (not a plain scale percent),
/// <c>PagePaginationPlanner.ApplyScaleToFitCapacity</c> must resolve the target items-per-page over
/// VISIBLE rows/columns only -- matching what <c>ComputeAccumulationBreakPoints</c> and
/// <c>PrintLayoutPlanner</c> actually pack onto each printed page. Counting hidden rows/columns in the
/// raw [start,end] span (the pre-fix behavior) inflates the body count, which derives a tiny uniform
/// scale and a hugely inflated per-page body budget, letting real (visible) content collapse onto far
/// fewer pages than the requested fit-to-N-pages count and than real Excel produces.
/// </summary>
public sealed class R102_FitToPagesHiddenRowsColumnsCapacityTests
{
    private static readonly Dictionary<uint, double> EmptyDict = new();

    // Letter portrait (11.0in tall) with 2.375in top/bottom margins gives an exact 600px printable
    // body height at 96 dpi ((11.0 - 4.75) * 96 = 600), and 20px default row height gives an exact
    // 30 baseline rows/page (600 / 20 = 30) -- the same round numbers as the reported defect evidence.
    private static readonly WorksheetPageMargins TallBodyMargins = new(Left: 0.75, Right: 0.75, Top: 2.375, Bottom: 2.375);

    // Letter portrait (8.5in wide) with 2.25in left/right margins gives an exact 384px printable body
    // width at 96 dpi ((8.5 - 4.5) * 96 = 384), and a 40px minimum column width gives an exact 9
    // baseline columns/page (384 / 40 = 9.6 -> floor 9).
    private static readonly WorksheetPageMargins WideBodyMargins = new(Left: 2.25, Right: 2.25, Top: 0.75, Bottom: 0.75);

    private static GridRange Range(uint startRow, uint startCol, uint endRow, uint endCol)
    {
        var sheetId = SheetId.New();
        return new GridRange(
            new CellAddress(sheetId, startRow, startCol),
            new CellAddress(sheetId, endRow, endCol));
    }

    /// <summary>
    /// Reproduces the defect evidence exactly: print range rows 1-300 with only rows 1-90 visible
    /// (91-300 hidden), "Fit to 3 pages tall". 90 visible rows over a 30-rows/page baseline needs
    /// EXACTLY 3 pages at 100% scale (Excel's ground truth) -- no shrink required at all. The bug
    /// resolved the fit-to-pages target against the raw 300-row span instead of the 90 visible rows,
    /// deriving a hugely inflated per-page budget that swallowed all 90 visible rows onto one page.
    /// </summary>
    [Fact]
    public void BuildPlan_FitToPagesTall_WithHiddenRows_ExcludesHiddenRowsFromCapacityTarget()
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

        plan.RowPageCount.Should().Be(3,
            "90 visible rows over a 30-rows/page baseline need exactly 3 pages at 100% scale, matching " +
            "Excel's fit-to-3-pages-tall result -- counting the 210 hidden rows (91-300) in the capacity " +
            "target inflates the per-page budget so all 90 visible rows wrongly collapse onto one page");
        plan.RowPlans[0].BodyRows.Should().HaveCount(30, "each of the 3 pages should hold 30 of the 90 visible rows");
        plan.EffectiveScalePercent.Should().BeApproximately(100.0, 0.01,
            "no shrink is needed once hidden rows are correctly excluded from the fit-to-3-pages target");
    }

    /// <summary>
    /// Column-axis counterpart: print range columns 1-300 with only columns 1-27 visible (28-300
    /// hidden), "Fit to 3 pages wide". 27 visible columns over a 9-columns/page baseline need exactly
    /// 3 pages at 100% scale.
    /// </summary>
    [Fact]
    public void BuildPlan_FitToPagesWide_WithHiddenColumns_ExcludesHiddenColumnsFromCapacityTarget()
    {
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
            columnWidths: EmptyDict,
            defaultColumnWidth: ColumnWidthPixelMapper.PixelsToColumnWidth(PagePaginationPlanner.MinimumPrintColumnWidth),
            headerMarginInches: 0.0,
            footerMarginInches: 0.0,
            isColumnHidden: col => col > 27);

        plan.ColumnPageCount.Should().Be(3,
            "27 visible columns over a 9-columns/page baseline need exactly 3 pages at 100% scale -- " +
            "counting the 273 hidden columns (28-300) in the capacity target inflates the per-page budget " +
            "so all 27 visible columns wrongly collapse onto one page");
        plan.ColumnPlans[0].BodyColumns.Should().HaveCount(9, "each of the 3 pages should hold 9 of the 27 visible columns");
    }

    /// <summary>
    /// No-regression sibling: the identical "Fit to 3 pages tall" request over the SAME 300-row range
    /// with NO hidden rows must still resolve against the full 300-row body (100 rows/page, 30% scale,
    /// 3 pages of 100 rows) -- proving the hidden-row exclusion only kicks in when a hidden predicate
    /// actually reports hidden rows, and does not change behavior for a fully-visible range.
    /// </summary>
    [Fact]
    public void BuildPlan_FitToPagesTall_NoHiddenRows_StillResolvesAgainstFullRange()
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
            isRowHidden: null);

        plan.RowPageCount.Should().Be(3,
            "with no hidden rows, all 300 rows are body rows: fit-to-3-pages-tall resolves to 100 " +
            "rows/page (300/3) and 3 pages of 100 rows each -- the actual page count exactly matches the " +
            "requested 3, which is the same real-world case R18's fit-to-pages tests already cover, and " +
            "must not regress once hidden-row exclusion is introduced");
        plan.RowPlans[0].BodyRows.Should().HaveCount(100);
    }
}

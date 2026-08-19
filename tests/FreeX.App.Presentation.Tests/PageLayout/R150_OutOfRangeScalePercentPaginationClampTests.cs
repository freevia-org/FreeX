using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

/// <summary>
/// freex-page-setup F1: an explicit Page Setup scale percent outside Excel's 10%-400% UI bound (e.g.
/// loaded unchecked from a non-Excel-authored .xlsx's &lt;pageSetup scale="500"/&gt;) must be CLAMPED
/// for pagination capacity, exactly like <see cref="PagePaginationPlanner.CalculateEffectiveScalePercent"/>
/// already clamps it for the visual scale that Print Preview / Print / PDF export draw the page at.
/// Before the fix, <c>ApplyScaleToFitCapacity</c>'s guard required the percent to already be in range,
/// so an out-of-range percent fell through to the (no-op, since FitToPagesWide/Tall are null here)
/// fit-to-pages branch and returned the UNSCALED, natural (100%) capacity -- while the sibling render
/// path still drew every cell at the 400%-clamped scale, packing ~4x too many rows/columns onto (and
/// off the edge of) every page.
/// </summary>
public sealed class R150_OutOfRangeScalePercentPaginationClampTests
{
    private static readonly Dictionary<uint, double> EmptyDict = new();

    // Letter portrait, 0.75in margins all around (WorksheetPageMargins default-ish "Normal" margins
    // used by the finding's repro): gives a fixed, deterministic printable body in pixels at 96 dpi.
    private static readonly WorksheetPageMargins NormalMargins = new(Left: 0.75, Right: 0.75, Top: 0.75, Bottom: 0.75);

    private static GridRange Range(uint startRow, uint startCol, uint endRow, uint endCol)
    {
        var sheetId = SheetId.New();
        return new GridRange(
            new CellAddress(sheetId, startRow, startCol),
            new CellAddress(sheetId, endRow, endCol));
    }

    private static PagePaginationPlan Build(int? scalePercent, GridRange range) =>
        PagePaginationPlanner.BuildPlan(
            range,
            new WorksheetScaleToFit(ScalePercent: scalePercent, FitToPagesWide: null, FitToPagesTall: null),
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.Letter,
            WorksheetPageOrientation.Portrait,
            NormalMargins,
            rowHeights: EmptyDict,
            defaultRowHeight: PagePaginationPlanner.NominalRowHeight,
            columnWidths: EmptyDict,
            defaultColumnWidth: ColumnWidthPixelMapper.PixelsToColumnWidth(PagePaginationPlanner.MinimumPrintColumnWidth),
            headerMarginInches: 0.0,
            footerMarginInches: 0.0);

    /// <summary>
    /// The core defect: ScalePercent=500 (out of Excel's 10-400 range) and ScalePercent=400 (in range,
    /// the value 500 clamps to) must produce the SAME row/column capacity, because
    /// CalculateEffectiveScalePercent -- which the render/PDF-export paths use to pick the actual drawn
    /// scale -- reports EffectiveScalePercent=400 for BOTH. Before the fix, ScalePercent=500 silently
    /// fell through to the unscaled, natural 100%-equivalent capacity (far larger than the 400%-scaled
    /// capacity), even though both plans report the identical EffectiveScalePercent.
    /// </summary>
    [Fact]
    public void BuildPlan_OutOfRangeScalePercent_ProducesSameCapacityAsItsClampedValue()
    {
        var range = Range(1, 1, 100, 21);

        var outOfRangePlan = Build(scalePercent: 500, range);
        var clampedInRangePlan = Build(scalePercent: 400, range);

        outOfRangePlan.EffectiveScalePercent.Should().Be(400,
            "CalculateEffectiveScalePercent already clamps 500 -> 400 for the visual/render scale");
        clampedInRangePlan.EffectiveScalePercent.Should().Be(400);

        outOfRangePlan.Capacity.RowsPerPage.Should().Be(clampedInRangePlan.Capacity.RowsPerPage,
            "a ScalePercent of 500 must paginate identically to an explicit 400, since both draw at " +
            "the same clamped 400% scale -- before the fix, 500 fell through to the unscaled natural " +
            "capacity (far more rows/page than actually fit at 400%), clipping most of every page");
        outOfRangePlan.Capacity.ColumnsPerPage.Should().Be(clampedInRangePlan.Capacity.ColumnsPerPage);
    }

    /// <summary>
    /// A below-floor out-of-range percent (5, clamps to the 10% floor) must likewise match its clamped
    /// in-range equivalent, not the unscaled natural capacity.
    /// </summary>
    [Fact]
    public void BuildPlan_BelowFloorScalePercent_ProducesSameCapacityAsTenPercentFloor()
    {
        var range = Range(1, 1, 100, 21);

        var outOfRangePlan = Build(scalePercent: 5, range);
        var flooredPlan = Build(scalePercent: 10, range);

        outOfRangePlan.EffectiveScalePercent.Should().Be(10);
        outOfRangePlan.Capacity.RowsPerPage.Should().Be(flooredPlan.Capacity.RowsPerPage);
        outOfRangePlan.Capacity.ColumnsPerPage.Should().Be(flooredPlan.Capacity.ColumnsPerPage);
    }

    /// <summary>
    /// Sibling no-regression: an in-range explicit scale percent (100, i.e. no scaling at all) must be
    /// completely unaffected by the clamp change -- the fix only changes behaviour for percents already
    /// outside [10, 400].
    /// </summary>
    [Fact]
    public void BuildPlan_InRangeScalePercent_IsUnaffected()
    {
        var range = Range(1, 1, 100, 21);

        var plan = Build(scalePercent: 100, range);
        var baselinePlan = Build(scalePercent: null, range);

        plan.EffectiveScalePercent.Should().Be(100);
        plan.Capacity.RowsPerPage.Should().Be(baselinePlan.Capacity.RowsPerPage,
            "an explicit 100% scale is a no-op and must match the natural (no scale-to-fit) capacity");
        plan.Capacity.ColumnsPerPage.Should().Be(baselinePlan.Capacity.ColumnsPerPage);
    }

    /// <summary>
    /// Sibling no-regression: the fit-to-pages fallback path (no explicit ScalePercent at all) is
    /// untouched by this fix -- it still resolves via FitToPagesWide/Tall exactly as before.
    /// </summary>
    [Fact]
    public void BuildPlan_FitToPagesWithNoScalePercent_StillResolvesViaFitToPages()
    {
        var range = Range(1, 1, 1, 300);

        var plan = PagePaginationPlanner.BuildPlan(
            range,
            new WorksheetScaleToFit(ScalePercent: null, FitToPagesWide: 1, FitToPagesTall: null),
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.Letter,
            WorksheetPageOrientation.Portrait,
            NormalMargins,
            rowHeights: EmptyDict,
            defaultRowHeight: PagePaginationPlanner.NominalRowHeight,
            columnWidths: EmptyDict,
            defaultColumnWidth: ColumnWidthPixelMapper.PixelsToColumnWidth(PagePaginationPlanner.MinimumPrintColumnWidth),
            headerMarginInches: 0.0,
            footerMarginInches: 0.0);

        // Fit-to-1-page-wide over 300 columns needs far more shrink than the 10% floor allows, so the
        // column axis's own capacity is floored at 10% (R103) and the sheet spills onto 2 pages rather
        // than an unbounded shrink onto 1 -- unrelated to (and unaffected by) this fix, which only
        // touches the explicit-ScalePercent branch, not this null-ScalePercent fit-to-pages branch.
        plan.ColumnPageCount.Should().BeGreaterThan(1);
        plan.EffectiveScalePercent.Should().Be(50,
            "unaffected by this fix: with the column capacity floored at 10% (160 cols/page), 300 " +
            "columns split across ceil(300/160)=2 pages, and CalculateEffectiveScalePercent reports " +
            "the ratio actually needed to fit that page count (1 requested / 2 actual = 50%)");
    }
}

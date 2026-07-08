using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Round-14 bucket T13 regression tests (App.Services half). One focused test per finding.
/// </summary>
public sealed class FreeXR14T13Tests
{
    // R14-freeze-scroll-render-2: a 4-way Window > Split pane (SetSplitPanesCommand) has its own
    // per-pane scroll offsets (bottom-left pane's own vertical offset, top-right pane's own
    // horizontal offset) that are completely independent of the main scrollbars. Reveal-on-select
    // must scroll whichever pane the target cell actually falls in, not just the main (bottom-right)
    // scrollbars -- otherwise a cell that's out of view in an independently-scrolled bottom-left
    // pane never gets revealed at all.
    [Fact]
    public void PlanCellReveal_RevealsTargetInIndependentlyScrolledBottomLeftSplitPane()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1") { SplitRow = 6, SplitColumn = 5 };

        // Main (bottom-right) pane is unscrolled, showing rows/cols from the very top -- it already
        // contains nothing relevant to this reveal since the target column (1) is in the pinned
        // left-column zone (< SplitColumn 5).
        var mainRowMetrics = new List<RowMetric> { new(1, 20, 0), new(2, 20, 20) };
        var mainColMetrics = new List<ColMetric> { new(1, 64, 0), new(2, 64, 64) };

        var topRows = new List<RowMetric>
        {
            new(1, 20, 0), new(2, 20, 20), new(3, 20, 40), new(4, 20, 60), new(5, 20, 80),
        };
        var leftColumns = new List<ColMetric>
        {
            new(1, 64, 0), new(2, 64, 64), new(3, 64, 128), new(4, 64, 192),
        };

        // The bottom-left pane has been independently scrolled (via its own vertical scrollbar) so
        // it currently shows rows 100-120, unrelated to the main scrollbars above.
        var bottomLeftRows = Enumerable.Range(100, 21)
            .Select(row => new RowMetric((uint)row, 20, (row - 100) * 20))
            .ToList();

        var viewport = new ViewportModel(
            [],
            mainRowMetrics,
            mainColMetrics,
            SplitPanes: new SplitPaneState(
                Row: 6,
                Column: 5,
                TopRows: topRows,
                LeftColumns: leftColumns,
                Cells: [],
                TopRightColumns: mainColMetrics,
                BottomLeftRows: bottomLeftRows));

        // Active cell A50, arrow-down to A51: row 51 (> SplitRow 6) and col 1 (< SplitColumn 5) is
        // the bottom-left region, scrolled out of view in that pane (which shows rows 100-120).
        var plan = WorkbookViewportScrollPlanner.PlanCellReveal(
            viewport,
            sheet,
            new CellAddress(sheet.Id, 51, 1),
            currentVerticalMaximum: 12,
            currentHorizontalMaximum: 5);

        plan.Vertical.ShouldScroll.Should().BeFalse(
            "the main (bottom-right) scrollbars have nothing to do with a bottom-left-pane cell");
        plan.BottomLeftTopRow.Should().Be(51u,
            "the bottom-left pane's own independent offset must scroll up to reveal row 51");
    }
}

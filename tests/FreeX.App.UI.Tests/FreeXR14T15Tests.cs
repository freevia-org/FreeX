using System.Reflection;
using System.Windows;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

/// <summary>
/// Round-14 bucket T15 fix verification (R14-freeze-scroll-render-1): a cell selected inside a
/// Window > Split fixed pane must keep its selection outline once the scrollable main pane scrolls
/// past it, matching Excel. CalculateVisibleSingleCellSelectionLayout previously looked the selected
/// cell's row/column up only in viewport.RowMetrics/ColMetrics (the scrolled main pane's own
/// metrics), which no longer contain the fixed pane's pinned rows/columns once the main pane has
/// scrolled past them -- losing the outline entirely even though the cell is still drawn by
/// RenderSplitPaneCells.
/// </summary>
public class FreeXR14T15Tests
{
    [Fact]
    public void SingleCellSelectionLayout_StaysVisibleInFixedSplitPaneAfterMainPaneScrollsPastIt()
    {
        WpfTestThread.Run(() =>
        {
            var grid = new GridView();

            // Window > Split at column 4: columns 1-3 are pinned in the left pane
            // (SplitPanes.LeftColumns). Select B2 (row 2, col 2), then scroll the main/right pane
            // horizontally so column 2 no longer appears in viewport.ColMetrics.
            var columnSplitSheet = SheetId.New();
            var columnSplitViewport = new ViewportModel(
                [],
                [new RowMetric(1, 20, 0), new RowMetric(2, 20, 20)],
                [new ColMetric(15, 60, 0), new ColMetric(16, 60, 60)],
                SplitPanes: new SplitPaneState(
                    Row: null,
                    Column: 4,
                    LeftColumns:
                    [
                        new ColMetric(1, 40, 0),
                        new ColMetric(2, 40, 40),
                        new ColMetric(3, 40, 80)
                    ]));
            var b2 = new GridRange(
                new CellAddress(columnSplitSheet, 2, 2),
                new CellAddress(columnSplitSheet, 2, 2));

            var b2Layout = (SelectionMarqueeLayoutPlanner.SelectionMarqueeLayout?)grid.CalculateVisibleSingleCellSelectionLayout(columnSplitViewport, b2, GridView.RowHeaderWidth, GridView.ColHeaderHeight);

            b2Layout.Should().NotBeNull(
                "Excel keeps a selected cell's outline visible in a Window > Split fixed pane even after the scrollable pane scrolls past it");
            b2Layout!.Value.Rect.Should().Be(new Rect(
                GridView.RowHeaderWidth + 40, GridView.ColHeaderHeight + 20, 40, 20));

            // Mirrored scenario: Window > Split at row 4 (rows 1-3 pinned in the top pane). Select
            // A2 (a top-pane cell), then scroll the main/bottom pane down so row 2 no longer
            // appears in viewport.RowMetrics.
            var rowSplitSheet = SheetId.New();
            var rowSplitViewport = new ViewportModel(
                [],
                [new RowMetric(15, 20, 0), new RowMetric(16, 20, 20)],
                [new ColMetric(1, 60, 0), new ColMetric(2, 60, 60)],
                SplitPanes: new SplitPaneState(
                    Row: 4,
                    Column: null,
                    TopRows:
                    [
                        new RowMetric(1, 40, 0),
                        new RowMetric(2, 40, 40),
                        new RowMetric(3, 40, 80)
                    ]));
            var a2 = new GridRange(
                new CellAddress(rowSplitSheet, 2, 1),
                new CellAddress(rowSplitSheet, 2, 1));

            var a2Layout = (SelectionMarqueeLayoutPlanner.SelectionMarqueeLayout?)grid.CalculateVisibleSingleCellSelectionLayout(rowSplitViewport, a2, GridView.RowHeaderWidth, GridView.ColHeaderHeight);

            a2Layout.Should().NotBeNull(
                "Excel keeps a selected cell's outline visible in a Window > Split top pane even after the scrollable pane scrolls past it");
            a2Layout!.Value.Rect.Should().Be(new Rect(
                GridView.RowHeaderWidth, GridView.ColHeaderHeight + 40, 60, 40));
        });
    }
}

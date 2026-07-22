using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;
using System.Windows;

namespace FreeX.App.UI.Tests;

/// <summary>
/// R65-render-cell-overflow-6-3: a plain hidden column has NO entry in a split pane's column
/// lookup at all (mirrors ViewportService.Metrics, which skips hidden columns entirely rather than
/// giving them a zero-width entry), so SplitPaneCellLayoutPlanner's overflow scan must treat a
/// missing lookup entry as "this column is hidden, keep going" rather than "stop here" -- exactly
/// like the un-split GridView.Rendering.cs overflow scan (which uses a maxViewportCol bound to tell
/// "past the end" apart from "hidden"). Pre-fix, the split-pane scan stopped dead at the first
/// missing entry, so overflow text in a split pane clipped short at a hidden neighbor even though
/// the same layout in an un-split view would tunnel straight through it.
/// </summary>
public sealed partial class GridViewSplitPaneLayoutTests
{
    [Fact]
    public void CalculateSplitPaneCellLayouts_OverflowTunnelsThroughHiddenColumnStoppingAtOccupiedCell()
    {
        var viewport = new ViewportModel(
            [],
            [new RowMetric(20, 18, 0)],
            [new ColMetric(10, 64, 0)],
            SplitPanes: new SplitPaneState(
                4,
                4,
                [new RowMetric(1, 18, 0)],
                [
                    // Column 2 is hidden -- deliberately absent from LeftColumns, exactly like a
                    // hidden column is absent from ViewportService.Metrics.BuildColMetrics.
                    new ColMetric(1, 64, 0),
                    new ColMetric(3, 64, 64),
                    new ColMetric(4, 80, 128)
                ],
                [
                    Cell(1, 1, "overflow"),
                    Cell(1, 4, "stop")
                ]));

        var layouts = GridView.CalculateSplitPaneCellLayouts(viewport);

        // Pre-fix: the scan hits the missing entry for hidden column 2 first and stops dead, so
        // the clip rect never grows past A1's own 64px width. Post-fix: column 2 is transparent to
        // overflow, so the scan continues into empty column 3 (width 64) and only stops at the
        // occupied column 4 -- matching the un-split GridView.Rendering.cs behavior.
        layouts.Single(layout => layout.Cell.Col == 1).TextClipRect
            .Should().Be(new Rect(GridView.RowHeaderWidth, GridView.ColHeaderHeight, 128, 18));
    }

    [Fact]
    public void CalculateSplitPaneCellLayouts_OverflowStopsAtPanesLastColumn_NoRegression()
    {
        var viewport = new ViewportModel(
            [],
            [new RowMetric(20, 18, 0)],
            [new ColMetric(10, 64, 0)],
            SplitPanes: new SplitPaneState(
                4,
                4,
                [new RowMetric(1, 18, 0)],
                [
                    new ColMetric(1, 64, 0),
                    new ColMetric(2, 80, 64)
                ],
                [
                    Cell(1, 1, "overflow")
                ]));

        var layouts = GridView.CalculateSplitPaneCellLayouts(viewport);

        // No column 3 exists in this pane at all (not hidden -- simply past the pane's last
        // column), so the scan must still stop there rather than reading out of range. This holds
        // both before and after the fix; it pins that the new maxCol bound doesn't let overflow run
        // past the pane's own last column metric.
        layouts.Single(layout => layout.Cell.Col == 1).TextClipRect
            .Should().Be(new Rect(GridView.RowHeaderWidth, GridView.ColHeaderHeight, 144, 18));
    }

    [Fact]
    public void CalculateSplitPaneCellLayouts_RightAlignedOverflowTunnelsThroughHiddenColumnLeftward()
    {
        var viewport = new ViewportModel(
            [],
            [new RowMetric(20, 18, 0)],
            [new ColMetric(10, 64, 0)],
            SplitPanes: new SplitPaneState(
                4,
                4,
                [new RowMetric(1, 18, 0)],
                [
                    new ColMetric(1, 50, 0),
                    // Column 2 is hidden -- absent from LeftColumns.
                    new ColMetric(3, 64, 50),
                    new ColMetric(4, 90, 114)
                ],
                [
                    Cell(1, 4, "overflow", new CellStyle { HorizontalAlignment = FreeX.Core.Model.HorizontalAlignment.Right })
                ]));

        var layouts = GridView.CalculateSplitPaneCellLayouts(viewport);

        // Pre-fix: the leftward scan adds empty column 3's width, then hits the missing entry for
        // hidden column 2 and stops immediately, never reaching empty column 1. Post-fix: column 2
        // is transparent, so the scan continues into column 1 too, mirroring the rightward fix.
        layouts.Single(layout => layout.Cell.Col == 4).TextClipRect
            .Should().Be(new Rect(GridView.RowHeaderWidth, GridView.ColHeaderHeight, 204, 18));
    }
}

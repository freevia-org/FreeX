using System.Reflection;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

// R83-render-selection-fillhandle-5-1: when the sole selection is a single cell that is itself
// part of a merged region (e.g. one click on a merged B2:D2 title cell), WorkbookSession's
// SetSingleSelectedRange never merge-expands the range (it stays an anchor-only 1x1 GridRange).
// Excel always draws the selection outline/fill-handle around the WHOLE merge in that case, and
// GetActiveCellRect already merge-expands via FindMerge for the separate active-cell locator box
// - CalculateSelectionRangeLayout must do the same for the selection outline/handle layout.
public sealed class GridViewSelectionMergedSingleCellLayoutTests
{
    private static SelectionMarqueeLayoutPlanner.SelectionMarqueeLayout? InvokeCalculateSelectionRangeLayout(
        GridView grid, ViewportModel viewport, GridRange range, double rowHeaderWidth, double columnHeaderHeight)
    {
        return (SelectionMarqueeLayoutPlanner.SelectionMarqueeLayout?)grid.CalculateSelectionRangeLayout(viewport, range, rowHeaderWidth, columnHeaderHeight);
    }

    private static void SetMergeLookup(GridView grid, Dictionary<(uint Row, uint Col), GridRange> mergeLookup)
    {
        var field = typeof(GridView).GetField("_mergeLookup", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        field!.SetValue(grid, mergeLookup);
    }

    private static ViewportModel ThreeColumnViewport(SheetId sheetId) =>
        new(
            [],
            [new RowMetric(2, 20, 0)],
            [
                new ColMetric(2, 60, 0),   // B
                new ColMetric(3, 60, 60),  // C
                new ColMetric(4, 60, 120)  // D
            ]);

    [Fact]
    public void CalculateSelectionRangeLayout_ExpandsAnchorOnlySelectionToFullMergeFootprint()
    {
        WpfTestThread.Run(() =>
        {
            var sheetId = SheetId.New();
            // B2:D2 merged; the raw selected range is anchor-only (B2:B2), as
            // WorkbookSession.SetSingleSelectedRange produces for a plain click on a merged cell.
            var merge = new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 2, 4));
            var anchorOnlyRange = new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 2, 2));

            var grid = new GridView();
            SetMergeLookup(grid, new Dictionary<(uint Row, uint Col), GridRange>
            {
                [(2u, 2u)] = merge,
                [(2u, 3u)] = merge,
                [(2u, 4u)] = merge,
            });

            var viewport = ThreeColumnViewport(sheetId);

            var layout = InvokeCalculateSelectionRangeLayout(grid, viewport, anchorOnlyRange, rowHeaderWidth: 30, columnHeaderHeight: 18);

            layout.Should().NotBeNull();
            // Full B2:D2 span (3 columns x 60px = 180px wide), not just B2's own 60px.
            layout!.Value.Rect.Width.Should().Be(180);
            layout.Value.Rect.Right.Should().Be(30 + 180);
            layout.Value.HasRightEdge.Should().BeTrue();
            layout.Value.HasBottomEdge.Should().BeTrue();
        });
    }

    [Fact]
    public void CalculateSelectionRangeLayout_NoRegression_PlainUnmergedSingleCellKeepsOwnMetricSize()
    {
        // Sibling/no-regression: a genuinely unmerged single-cell selection must still be sized
        // from its own column/row metrics only, not from some unrelated merge.
        WpfTestThread.Run(() =>
        {
            var sheetId = SheetId.New();
            var range = new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 2, 2));

            var grid = new GridView();
            SetMergeLookup(grid, []);

            var viewport = ThreeColumnViewport(sheetId);

            var layout = InvokeCalculateSelectionRangeLayout(grid, viewport, range, rowHeaderWidth: 30, columnHeaderHeight: 18);

            layout.Should().NotBeNull();
            layout!.Value.Rect.Width.Should().Be(60);
            layout.Value.Rect.Right.Should().Be(30 + 60);
            layout.Value.HasRightEdge.Should().BeTrue();
            layout.Value.HasBottomEdge.Should().BeTrue();
        });
    }
}

using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

// Regression coverage for R46-render-merged-cell-frozen-2-1: a merged cell that crosses a
// View>Split vertical divider must render (clipped) in BOTH the left and right pane, not just
// the anchor's own pane, matching real Excel.
public sealed partial class GridViewSplitPaneLayoutTests
{
    [Fact]
    public void CalculateSplitPaneCellLayouts_MergeCrossingVerticalSplit_RendersInBothPanes()
    {
        var sheetId = SheetId.New();
        var viewport = new ViewportModel(
            [],
            [new RowMetric(1, 18, 0)],
            [new ColMetric(1, 64, 0), new ColMetric(2, 80, 64), new ColMetric(3, 64, 144), new ColMetric(4, 80, 208)],
            SplitPanes: new SplitPaneState(
                2,
                3,
                [new RowMetric(1, 18, 0)],
                [new ColMetric(1, 64, 0), new ColMetric(2, 80, 64)],
                [
                    Cell(1, 1, "merged", new CellStyle { FillColor = new CellColor(255, 0, 0) })
                ],
                [new ColMetric(3, 64, 0), new ColMetric(4, 80, 64)]));
        var mergedRegions = new[]
        {
            new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 1, 4))
        };

        var layouts = GridView.CalculateSplitPaneCellLayouts(viewport, mergedRegions);

        // Left pane: the anchor's own box, full content, NOT truncated to a single column's width
        // (the pre-fix bug: SumMergedColumnWidths only summed leftColumnLookup, so a merge column
        // past the split divider contributed zero width).
        var leftBox = layouts.Should().ContainSingle(l => l.Region == SplitPaneRegion.TopLeft).Subject;
        leftBox.Cell.DisplayText.Should().Be("merged");
        leftBox.Rect.Width.Should().Be(144); // col1(64) + col2(80)
        leftBox.Cell.Style!.FillColor.Should().Be(new CellColor(255, 0, 0));

        // Right pane: the pre-fix bug drew nothing at all here (anchor-only filter skips every
        // non-anchor merge cell in every pane). The fix must render the merge's continuation box
        // here too - fill/border via Style, but with content suppressed so it isn't drawn twice.
        var rightBox = layouts.Should().ContainSingle(l => l.Region == SplitPaneRegion.TopRight).Subject;
        rightBox.Cell.DisplayText.Should().BeEmpty();
        rightBox.Rect.Width.Should().Be(144); // col3(64) + col4(80)
        rightBox.Cell.Style!.FillColor.Should().Be(new CellColor(255, 0, 0));
    }

    [Fact]
    public void CalculateSplitPaneCellLayouts_MergeNotCrossingSplit_NoRegression_RendersOnlyInOwnPane()
    {
        // Sibling no-regression case: a merge fully contained within a single pane's columns must
        // keep behaving exactly as before - one layout entry, sized to its own footprint, and no
        // spurious entry in the pane it never touches.
        var sheetId = SheetId.New();
        var viewport = new ViewportModel(
            [],
            [new RowMetric(1, 18, 0)],
            [new ColMetric(1, 64, 0), new ColMetric(2, 80, 64), new ColMetric(3, 64, 144), new ColMetric(4, 80, 208)],
            SplitPanes: new SplitPaneState(
                2,
                3,
                [new RowMetric(1, 18, 0)],
                [new ColMetric(1, 64, 0), new ColMetric(2, 80, 64)],
                [
                    Cell(1, 1, "merged")
                ],
                [new ColMetric(3, 64, 0), new ColMetric(4, 80, 64)]));
        var mergedRegions = new[]
        {
            new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 1, 2))
        };

        var layouts = GridView.CalculateSplitPaneCellLayouts(viewport, mergedRegions);

        layouts.Should().ContainSingle();
        var box = layouts[0];
        box.Region.Should().Be(SplitPaneRegion.TopLeft);
        box.Cell.DisplayText.Should().Be("merged");
        box.Rect.Width.Should().Be(144); // col1(64) + col2(80)
    }

    // r47 mirror of CalculateSplitPaneCellLayouts_MergeCrossingVerticalSplit_RendersInBothPanes: a
    // merged cell crossing the HORIZONTAL (row) split divider must render in both the top and bottom
    // row-band panes, not just the anchor's own row band.
    [Fact]
    public void CalculateSplitPaneCellLayouts_MergeCrossingHorizontalSplit_RendersInBothPanes()
    {
        var sheetId = SheetId.New();
        var viewport = new ViewportModel(
            [],
            [new RowMetric(1, 18, 0), new RowMetric(2, 22, 18), new RowMetric(3, 18, 40), new RowMetric(4, 20, 58)],
            [new ColMetric(1, 64, 0)],
            SplitPanes: new SplitPaneState(
                3,
                null,
                [new RowMetric(1, 18, 0), new RowMetric(2, 22, 18)],
                [new ColMetric(1, 64, 0)],
                [
                    Cell(1, 1, "merged", new CellStyle { FillColor = new CellColor(255, 0, 0) })
                ],
                BottomLeftRows: [new RowMetric(3, 18, 0), new RowMetric(4, 20, 18)]));
        var mergedRegions = new[]
        {
            new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 4, 1))
        };

        var layouts = GridView.CalculateSplitPaneCellLayouts(viewport, mergedRegions);

        // Top pane: the anchor's own box, full content, NOT truncated to a single row's height (the
        // pre-fix bug: FindMergeRowSpan only ever looked at the anchor's own row band, so a merge row
        // past the split divider contributed zero height, and the other row band never got a layout).
        var topBox = layouts.Should().ContainSingle(l => l.Region == SplitPaneRegion.TopLeft).Subject;
        topBox.Cell.DisplayText.Should().Be("merged");
        topBox.Rect.Height.Should().Be(40); // row1(18) + row2(22)
        topBox.Cell.Style!.FillColor.Should().Be(new CellColor(255, 0, 0));

        // Bottom pane: the pre-fix bug drew nothing at all here. The fix must render the merge's
        // continuation box here too - fill/border via Style, but with content suppressed so it isn't
        // drawn twice.
        var bottomBox = layouts.Should().ContainSingle(l => l.Region == SplitPaneRegion.BottomLeft).Subject;
        bottomBox.Cell.DisplayText.Should().BeEmpty();
        bottomBox.Rect.Height.Should().Be(38); // row3(18) + row4(20)
        bottomBox.Cell.Style!.FillColor.Should().Be(new CellColor(255, 0, 0));
    }

    // Sibling no-regression case for the row axis: a merge fully contained within a single row band
    // must keep behaving exactly as before - one layout entry, and no spurious entry in the row band
    // it never touches.
    [Fact]
    public void CalculateSplitPaneCellLayouts_MergeNotCrossingHorizontalSplit_NoRegression_RendersOnlyInOwnPane()
    {
        var sheetId = SheetId.New();
        var viewport = new ViewportModel(
            [],
            [new RowMetric(1, 18, 0), new RowMetric(2, 22, 18), new RowMetric(3, 18, 40), new RowMetric(4, 20, 58)],
            [new ColMetric(1, 64, 0)],
            SplitPanes: new SplitPaneState(
                3,
                null,
                [new RowMetric(1, 18, 0), new RowMetric(2, 22, 18)],
                [new ColMetric(1, 64, 0)],
                [
                    Cell(1, 1, "merged")
                ],
                BottomLeftRows: [new RowMetric(3, 18, 0), new RowMetric(4, 20, 18)]));
        var mergedRegions = new[]
        {
            new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 2, 1))
        };

        var layouts = GridView.CalculateSplitPaneCellLayouts(viewport, mergedRegions);

        layouts.Should().ContainSingle();
        var box = layouts[0];
        box.Region.Should().Be(SplitPaneRegion.TopLeft);
        box.Cell.DisplayText.Should().Be("merged");
        box.Rect.Height.Should().Be(40); // row1(18) + row2(22)
    }
}

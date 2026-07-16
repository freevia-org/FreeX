using System.Windows;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

/// <summary>
/// Round-43 frozen/split header render fixes (bucket: frozen-header).
///
/// R43-render-frozen-header-2-2: a hidden merge-anchor row/column is kept in
/// RowMetrics/ColMetrics with Height/Width = 0 purely so the merge's value/style
/// stay reachable for cell rendering. The header renderer must not draw the row
/// number / column letter for that zero-size slot - Excel shows nothing for a
/// hidden row/column header, but the old code only guarded row headers against
/// vertical clipping (rect.Bottom &lt;= visibleBottom) and had no guard at all for
/// column headers, so the header text got drawn centered on a zero-size rect and
/// bled into the neighboring header cell.
///
/// R43-render-frozen-header-2-3: RenderFreezeDivider looked up the frozen
/// boundary row/column via an exact Row/Col match against RowMetrics/ColMetrics.
/// When the last frozen row/column is hidden (and not a merge anchor, so it is
/// dropped from RowMetrics/ColMetrics entirely), the lookup returned null and the
/// whole freeze-pane divider silently failed to draw, even though the frozen rows
/// above it were still correctly pinned. Real Excel always draws the divider at
/// the frozen block's actual visible extent regardless of whether the boundary
/// row/column happens to be hidden.
/// </summary>
public sealed class FreeXR43FrozenHeaderTests
{
    // R43-render-frozen-header-2-2 (rows): a hidden merge-anchor row is kept as a
    // zero-height RowMetric. Its header text must not be drawn.
    [Fact]
    public void ShouldDrawRowHeaderText_ReturnsFalse_ForZeroHeightHiddenMergeAnchorRow()
    {
        // Row 5 hidden as a merge anchor: ViewportService keeps it with Height=0.
        var zeroHeightRect = new Rect(0, 96, 30, 0);

        GridView.ShouldDrawRowHeaderText(zeroHeightRect, visibleBottom: 1000).Should().BeFalse();
    }

    // Sibling no-regression case: a normal, fully visible row header must still draw.
    [Fact]
    public void ShouldDrawRowHeaderText_ReturnsTrue_ForNormalVisibleRow()
    {
        var normalRect = new Rect(0, 18, 30, 20);

        GridView.ShouldDrawRowHeaderText(normalRect, visibleBottom: 38).Should().BeTrue();
    }

    // Existing clip behavior (partially-clipped trailing row) must be preserved
    // alongside the new zero-height guard.
    [Fact]
    public void ShouldDrawRowHeaderText_StillClipsPartiallyVisibleTrailingRow()
    {
        var partiallyClippedRect = new Rect(0, 18, 30, 20);

        GridView.ShouldDrawRowHeaderText(partiallyClippedRect, visibleBottom: 37.5).Should().BeFalse();
    }

    // R43-render-frozen-header-2-2 (columns): a hidden merge-anchor column is kept
    // as a zero-width ColMetric. Its header text must not be drawn (DrawColumnHeader
    // previously had no guard at all).
    [Fact]
    public void ShouldDrawColumnHeaderText_ReturnsFalse_ForZeroWidthHiddenMergeAnchorColumn()
    {
        var zeroWidthRect = new Rect(160, 0, 0, 20);

        GridView.ShouldDrawColumnHeaderText(zeroWidthRect).Should().BeFalse();
    }

    // Sibling no-regression case: a normal, fully visible column header must still draw.
    [Fact]
    public void ShouldDrawColumnHeaderText_ReturnsTrue_ForNormalVisibleColumn()
    {
        var normalRect = new Rect(160, 0, 80, 20);

        GridView.ShouldDrawColumnHeaderText(normalRect).Should().BeTrue();
    }

    // R43-render-frozen-header-2-3 (rows): the exact frozen-boundary row is hidden
    // (not a merge anchor) and therefore absent from RowMetrics entirely. The
    // fallback lookup must land on the nearest preceding row still present, so the
    // divider still draws at the frozen block's real visible extent instead of
    // vanishing.
    [Fact]
    public void FindLastRowMetricAtOrBefore_FallsBackToNearestPrecedingRow_WhenBoundaryRowIsHidden()
    {
        // Freeze after row 4; row 4 is hidden (not a merge anchor) so BuildRowMetrics
        // drops it entirely - RowMetrics only has rows 1-3 and 6+.
        var metrics = new List<RowMetric>
        {
            new(1, 20, 0),
            new(2, 20, 20),
            new(3, 20, 40),
            new(6, 20, 60),
            new(7, 20, 80),
        };

        var result = GridView.FindLastRowMetricAtOrBefore(metrics, row: 4);

        result.Should().NotBeNull();
        result!.Row.Should().Be(3);
        result.TopOffset.Should().Be(40);
        result.Height.Should().Be(20);
    }

    // Sibling no-regression case: when the exact frozen-boundary row is present
    // (the common, non-hidden case), the fallback must still return it exactly -
    // matching the old FindRowMetric behavior.
    [Fact]
    public void FindLastRowMetricAtOrBefore_ReturnsExactMatch_WhenBoundaryRowIsPresent()
    {
        var metrics = new List<RowMetric>
        {
            new(1, 20, 0),
            new(2, 20, 20),
            new(3, 20, 40),
            new(4, 20, 60),
        };

        var result = GridView.FindLastRowMetricAtOrBefore(metrics, row: 4);

        result.Should().NotBeNull();
        result!.Row.Should().Be(4);
        result.TopOffset.Should().Be(60);
    }

    // Edge case: every frozen row up to and including the boundary is hidden, so
    // there is no preceding row at all - the frozen block has zero visible height.
    [Fact]
    public void FindLastRowMetricAtOrBefore_ReturnsNull_WhenNoRowAtOrBeforeBoundaryIsVisible()
    {
        var metrics = new List<RowMetric>
        {
            new(6, 20, 0),
            new(7, 20, 20),
        };

        var result = GridView.FindLastRowMetricAtOrBefore(metrics, row: 4);

        result.Should().BeNull();
    }

    // R43-render-frozen-header-2-3 (columns): identical defect/fix for the column axis.
    [Fact]
    public void FindLastColMetricAtOrBefore_FallsBackToNearestPrecedingColumn_WhenBoundaryColumnIsHidden()
    {
        // Freeze after column 4 (D); column 4 is hidden (not a merge anchor) so
        // BuildColMetrics drops it entirely - ColMetrics only has columns 1-3 and 6+.
        var metrics = new List<ColMetric>
        {
            new(1, 80, 0),
            new(2, 80, 80),
            new(3, 80, 160),
            new(6, 80, 240),
        };

        var result = GridView.FindLastColMetricAtOrBefore(metrics, column: 4);

        result.Should().NotBeNull();
        result!.Col.Should().Be(3);
        result.LeftOffset.Should().Be(160);
        result.Width.Should().Be(80);
    }

    // Sibling no-regression case: exact boundary column present -> returned as-is.
    [Fact]
    public void FindLastColMetricAtOrBefore_ReturnsExactMatch_WhenBoundaryColumnIsPresent()
    {
        var metrics = new List<ColMetric>
        {
            new(1, 80, 0),
            new(2, 80, 80),
            new(3, 80, 160),
            new(4, 80, 240),
        };

        var result = GridView.FindLastColMetricAtOrBefore(metrics, column: 4);

        result.Should().NotBeNull();
        result!.Col.Should().Be(4);
        result.LeftOffset.Should().Be(240);
    }
}

using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;
using System.Windows;

namespace FreeX.App.UI.Tests;

public sealed partial class GridViewSplitPaneLayoutTests
{
    [Fact]
    public void CalculateSplitPaneClipRects_ConstrainsEachPaneToItsDividerBand()
    {
        var viewport = SplitViewport();

        var clips = GridView.CalculateSplitPaneClipRects(viewport, actualWidth: 500, actualHeight: 300);

        clips.TopLeft.Should().Be(new Rect(GridView.RowHeaderWidth, GridView.ColHeaderHeight, 208, 58));
        clips.TopRight.Should().Be(new Rect(GridView.RowHeaderWidth + 208, GridView.ColHeaderHeight, 262, 58));
        clips.BottomLeft.Should().Be(new Rect(GridView.RowHeaderWidth, GridView.ColHeaderHeight + 58, 208, 224));
        clips.BottomRight.Should().Be(new Rect(GridView.RowHeaderWidth + 208, GridView.ColHeaderHeight + 58, 262, 224));
    }

    [Fact]
    public void SplitPaneClipLayoutPlanner_ConstrainsEachPaneToItsDividerBandOutsideGridView()
    {
        var viewport = SplitViewport();

        var clips = SplitPaneClipLayoutPlanner.CalculateClipRects(viewport, actualWidth: 500, actualHeight: 300);

        clips.TopLeft.Should().Be(new Rect(GridView.RowHeaderWidth, GridView.ColHeaderHeight, 208, 58));
        clips.TopRight.Should().Be(new Rect(GridView.RowHeaderWidth + 208, GridView.ColHeaderHeight, 262, 58));
        clips.BottomLeft.Should().Be(new Rect(GridView.RowHeaderWidth, GridView.ColHeaderHeight + 58, 208, 224));
        clips.BottomRight.Should().Be(new Rect(GridView.RowHeaderWidth + 208, GridView.ColHeaderHeight + 58, 262, 224));
    }

    [Fact]
    public void CalculateSplitPaneClipRects_ClampsPaneSizesToControlBounds()
    {
        var viewport = SplitViewport();
        var actualWidth = GridView.RowHeaderWidth + 100;
        var actualHeight = GridView.ColHeaderHeight + 30;

        var clips = SplitPaneClipLayoutPlanner.CalculateClipRects(viewport, actualWidth, actualHeight);

        clips.TopLeft.Should().Be(new Rect(GridView.RowHeaderWidth, GridView.ColHeaderHeight, 100, 30));
        clips.TopRight.Should().Be(new Rect(GridView.RowHeaderWidth + 208, GridView.ColHeaderHeight, 0, 30));
        clips.BottomLeft.Should().Be(new Rect(GridView.RowHeaderWidth, GridView.ColHeaderHeight + 58, 100, 0));
        clips.BottomRight.Should().Be(new Rect(GridView.RowHeaderWidth + 208, GridView.ColHeaderHeight + 58, 0, 0));
    }

    [Theory]
    [InlineData(SplitPaneRegion.TopLeft, false, false)]
    [InlineData(SplitPaneRegion.TopRight, false, false)]
    [InlineData(SplitPaneRegion.BottomLeft, false, true)]
    [InlineData(SplitPaneRegion.BottomRight, false, true)]
    [InlineData(SplitPaneRegion.TopLeft, true, false)]
    [InlineData(SplitPaneRegion.BottomLeft, true, false)]
    [InlineData(SplitPaneRegion.TopRight, true, true)]
    [InlineData(SplitPaneRegion.BottomRight, true, true)]
    public void CanScrollSplitPaneRegion_ReflectsPinnedPaneScrollAxes(
        SplitPaneRegion region,
        bool horizontal,
        bool expected)
    {
        GridView.CanScrollSplitPaneRegion(region, horizontal).Should().Be(expected);
    }
}

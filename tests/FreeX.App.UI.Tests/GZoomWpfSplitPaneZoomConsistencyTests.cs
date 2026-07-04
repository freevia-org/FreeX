using FluentAssertions;

namespace FreeX.App.UI.Tests;

/// <summary>
/// Regression coverage for group G-zoom-wpf: split-pane clip rects, the frozen-pane
/// divider line, the live resize-preview guide line, and the split-pane divider/scrollbar
/// chrome must all be sized against the zoom-adjusted logical viewport
/// (GetLogicalViewportWidth/Height = ActualWidth/Height / ZoomFactor), matching the
/// convention already established by GridView.RenderDispatch.cs/GridView.Rendering.cs.
/// Drawing against raw ActualWidth/ActualHeight silently clips/shortens this chrome
/// once GridView's zoom RenderTransform is applied and the control is zoomed below 100%.
/// </summary>
public sealed class GZoomWpfSplitPaneZoomConsistencyTests
{
    // J14: RenderSplitPaneCells must clip split-pane quadrants against the logical
    // (zoom-adjusted) viewport extent, not the raw physical ActualWidth/ActualHeight,
    // or real content in the right/bottom panes is silently clipped away when zoomed out.
    [Fact]
    public void RenderSplitPaneCells_ClipsAgainstLogicalViewportNotActualSize()
    {
        var rendering = AppUiSourceTestSupport.ReadAppUiSources("GridView.Rendering.cs");

        rendering.Should().Contain(
            "CalculateSplitPaneClipRects(Viewport, GetLogicalViewportWidth(), GetLogicalViewportHeight())");
        rendering.Should().NotContain("CalculateSplitPaneClipRects(Viewport, ActualWidth, ActualHeight)");
    }

    // J39: the frozen-pane divider line must span the logical viewport extent, not stop
    // short at the raw (pre-zoom-scale) ActualWidth/ActualHeight.
    [Fact]
    public void RenderFreezeDivider_SpansLogicalViewportNotActualSize()
    {
        var headers = AppUiSourceTestSupport.ReadAppUiSources("GridView.Rendering.Headers.cs");

        headers.Should().Contain("new Point(0, y), new Point(GetLogicalViewportWidth(), y)");
        headers.Should().Contain("new Point(x, 0), new Point(x, GetLogicalViewportHeight())");
        headers.Should().NotContain("new Point(0, y), new Point(ActualWidth, y)");
        headers.Should().NotContain("new Point(x, 0), new Point(x, ActualHeight)");
    }

    // J55: the live column/row resize preview guide line must extend to the logical
    // viewport extent so it doesn't visibly stop partway across the pane while zoomed out.
    [Fact]
    public void RenderResizeLine_SpansLogicalViewportNotActualSize()
    {
        var overlays = AppUiSourceTestSupport.ReadAppUiSources("GridView.Overlays.cs");

        overlays.Should().Contain("new Point(_resizeLinePos, GetLogicalViewportHeight())");
        overlays.Should().Contain("new Point(GetLogicalViewportWidth(), _resizeLinePos)");
        overlays.Should().NotContain("new Point(_resizeLinePos, ActualHeight)");
        overlays.Should().NotContain("new Point(ActualWidth, _resizeLinePos)");
    }

    // P2: the split-pane divider line and the split-pane scrollbar chrome must both be
    // sized/drawn against the logical viewport extent, matching the same zoom convention
    // as the master OnRender clip and every other full-span overlay in this render pipeline.
    [Fact]
    public void RenderSplitDivider_SpansLogicalViewportNotActualSize()
    {
        var splitPanes = AppUiSourceTestSupport.ReadAppUiSources("GridView.SplitPanes.cs");

        splitPanes.Should().Contain(
            "new Point(ActualRowHeaderWidth, horizontalY), new Point(GetLogicalViewportWidth(), horizontalY)");
        splitPanes.Should().Contain(
            "new Point(verticalX, EffectiveColHeaderHeight), new Point(verticalX, GetLogicalViewportHeight())");
        splitPanes.Should().NotContain(
            "new Point(ActualRowHeaderWidth, horizontalY), new Point(ActualWidth, horizontalY)");
        splitPanes.Should().NotContain(
            "new Point(verticalX, EffectiveColHeaderHeight), new Point(verticalX, ActualHeight)");
    }

    [Fact]
    public void RenderSplitPaneScrollbarChrome_MeasuresAgainstLogicalViewportNotActualSize()
    {
        var splitPanes = AppUiSourceTestSupport.ReadAppUiSources("GridView.SplitPanes.cs");

        splitPanes.Should().Contain(
            "CalculateSplitPaneScrollbarChrome(Viewport, GetLogicalViewportWidth(), GetLogicalViewportHeight())");
        splitPanes.Should().NotContain("CalculateSplitPaneScrollbarChrome(Viewport, ActualWidth, ActualHeight)");
    }
}

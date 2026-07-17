using System.Reflection;
using System.Windows;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

/// <summary>
/// Round-48 zoom/hit-test desync fixes:
/// R48-render-zoom-scaling-3-1 (split-pane scrollbar/divider hit-testing on mouse-down used raw
/// ActualWidth/ActualHeight while the render path used the zoom-adjusted logical viewport size), and
/// R48-render-zoom-scaling-3-2 (the autofill-handle and selection-move drag edge-autoscroll boundary
/// had the same raw-vs-zoomed mismatch).
/// Both are fixed by routing the affected call sites in GridView.Input.cs through
/// GetLogicalViewportWidth()/GetLogicalViewportHeight() (ActualWidth/ActualHeight divided by
/// ZoomFactor), matching the convention already used by the render path (GridView.RenderDispatch.cs,
/// GridView.SplitPanes.cs) and covered for the render side by GZoomWpfSplitPaneZoomConsistencyTests.
/// </summary>
public sealed class R48ZoomHitTestFixTests
{
    // R48-render-zoom-scaling-3-1: the split-pane scrollbar chrome and split-divider hit-test used on
    // mouse-down must be computed against the same zoom-adjusted logical viewport extent the render
    // path uses, not the raw (pre-zoom-scale) ActualWidth/ActualHeight -- otherwise the visually
    // rendered thumb/track and the hit-tested thumb/track rects disagree at any zoom != 100%.
    [Fact]
    public void SplitPaneMouseDownHitTest_UsesLogicalViewportNotActualSize()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Input.cs");

        source.Should().Contain(
            "var chrome = CalculateSplitPaneScrollbarChrome(Viewport, GetLogicalViewportWidth(), GetLogicalViewportHeight());",
            "the mouse-down scrollbar-chrome hit-test must use the same zoom-adjusted logical viewport size as the render path");
        source.Should().Contain(
            "if (Viewport is not null && HitTestSplitDividerHandle(Viewport, pos, GetLogicalViewportWidth(), GetLogicalViewportHeight()) is { } splitHandle &&",
            "the mouse-down split-divider hit-test must use the same zoom-adjusted logical viewport size as the render path");

        source.Should().NotContain(
            "var chrome = CalculateSplitPaneScrollbarChrome(Viewport, ActualWidth, ActualHeight);",
            "the raw (un-zoomed) chrome computation must no longer be used for the mouse-down hit-test");
        source.Should().NotContain(
            "HitTestSplitDividerHandle(Viewport, pos, ActualWidth, ActualHeight) is { } splitHandle &&",
            "the raw (un-zoomed) divider hit-test must no longer be used for mouse-down capture");
    }

    // Sibling no-regression case: at the default 100% zoom the logical viewport extent must still
    // equal the raw ActualWidth/ActualHeight exactly, so switching the mouse-down hit-test call sites
    // to the zoom-adjusted helper changes nothing for the overwhelmingly common unzoomed case.
    [Fact]
    public void GetLogicalViewportSize_MatchesActualSizeAtDefaultZoom_NoRegression()
    {
        WpfTestThread.Run(() =>
        {
            var grid = new GridView { Width = 400, Height = 300, ZoomFactor = 1.0 };
            grid.Measure(new Size(400, 300));
            grid.Arrange(new Rect(0, 0, 400, 300));
            grid.UpdateLayout();

            var widthMethod = typeof(GridView).GetMethod("GetLogicalViewportWidth", BindingFlags.NonPublic | BindingFlags.Instance);
            var heightMethod = typeof(GridView).GetMethod("GetLogicalViewportHeight", BindingFlags.NonPublic | BindingFlags.Instance);
            widthMethod.Should().NotBeNull();
            heightMethod.Should().NotBeNull();

            var logicalWidth = (double)widthMethod!.Invoke(grid, null)!;
            var logicalHeight = (double)heightMethod!.Invoke(grid, null)!;

            logicalWidth.Should().BeApproximately(grid.ActualWidth, 0.01);
            logicalHeight.Should().BeApproximately(grid.ActualHeight, 0.01);

            var chromeAtActual = GridView.CalculateSplitPaneScrollbarChrome(
                GridViewTestHelpers.CreateTwoByTwoViewport() with
                {
                    SplitPanes = new SplitPaneState(1, 1, [new RowMetric(1, 20, 0)], [new ColMetric(1, 60, 0)])
                },
                grid.ActualWidth,
                grid.ActualHeight);
            var chromeAtLogical = GridView.CalculateSplitPaneScrollbarChrome(
                GridViewTestHelpers.CreateTwoByTwoViewport() with
                {
                    SplitPanes = new SplitPaneState(1, 1, [new RowMetric(1, 20, 0)], [new ColMetric(1, 60, 0)])
                },
                logicalWidth,
                logicalHeight);

            chromeAtLogical.Should().Be(chromeAtActual,
                "at 100% zoom the fixed (logical-size) chrome computation must be identical to the pre-fix (actual-size) one");
        });
    }

    // R48-render-zoom-scaling-3-2: the autofill-handle drag and the selection-move drag both drive
    // edge-autoscroll from CalculateAutofillEdgeScrollIntent, and both call sites must pass the
    // zoom-adjusted logical viewport size, not the raw ActualWidth/ActualHeight, or the edge-scroll
    // trigger boundary silently desyncs from the pointer's actual (post-zoom) coordinate space.
    [Fact]
    public void AutofillAndSelectionMoveDragEdgeScroll_UseLogicalViewportNotActualSize()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Input.cs");
        const string marker = "var scrollRequest = CalculateAutofillEdgeScrollIntent(";

        var firstStart = source.IndexOf(marker, StringComparison.Ordinal);
        firstStart.Should().BeGreaterThanOrEqualTo(0, "the autofill drag call site must exist");
        var firstEnd = source.IndexOf(");", firstStart, StringComparison.Ordinal);
        var firstCallArgs = source[firstStart..firstEnd];

        var secondStart = source.IndexOf(marker, firstStart + marker.Length, StringComparison.Ordinal);
        secondStart.Should().BeGreaterThanOrEqualTo(0, "the selection-move drag call site must exist");
        var secondEnd = source.IndexOf(");", secondStart, StringComparison.Ordinal);
        var secondCallArgs = source[secondStart..secondEnd];

        foreach (var callArgs in new[] { firstCallArgs, secondCallArgs })
        {
            callArgs.Should().Contain("GetLogicalViewportWidth()");
            callArgs.Should().Contain("GetLogicalViewportHeight()");
            callArgs.Should().NotContain("ActualWidth");
            callArgs.Should().NotContain("ActualHeight");
        }
    }

    // Sibling no-regression case: the pure scroll-intent math itself (GridAutofillPlanner via the
    // GridView.CalculateAutofillEdgeScrollIntent wrapper) is untouched by this fix -- passing an
    // explicit width/height (as tests of the pure function already do) must keep behaving exactly as
    // before; only the two GridView.Input.cs call sites' choice of which dimension to pass changed.
    [Fact]
    public void CalculateAutofillEdgeScrollIntent_StillHonorsExplicitWidthAndHeight_NoRegression()
    {
        var scrollRequest = GridView.CalculateAutofillEdgeScrollIntent(
            pointerX: 195,
            pointerY: 120,
            width: 200,
            height: 300,
            rowHeaderWidth: 30,
            columnHeaderHeight: 18);

        scrollRequest.HasAnyDirection.Should().BeTrue(
            "a pointer within the edge threshold of the supplied width must still request an edge scroll");
    }

    // R48-render-zoom-scaling-3-3: the worksheet background picture fill rect must cover the
    // zoom-adjusted viewport (no blank right/bottom gap once the grid is zoomed below 100%).
    [Fact]
    public void WorksheetBackgroundFillRect_UsesLogicalViewportNotActualSize()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.DrawingObjects.Pictures.cs");

        source.Should().Contain(
            "new Rect(ActualRowHeaderWidth, EffectiveColHeaderHeight, Math.Max(0, GetLogicalViewportWidth() - ActualRowHeaderWidth), Math.Max(0, GetLogicalViewportHeight() - EffectiveColHeaderHeight))",
            "the worksheet background fill rect must be sized against the zoom-adjusted logical viewport, matching the outer render clip and other full-viewport overlays");
        source.Should().NotContain(
            "new Rect(ActualRowHeaderWidth, EffectiveColHeaderHeight, Math.Max(0, ActualWidth - ActualRowHeaderWidth), Math.Max(0, ActualHeight - EffectiveColHeaderHeight))",
            "the raw (un-zoomed) background fill rect must no longer be used");
    }

    // Sibling no-regression case: at 100% zoom the background fill rect's size is unchanged, since
    // GetLogicalViewportWidth()/Height() reduce to ActualWidth/ActualHeight when ZoomFactor is 1.
    [Fact]
    public void WorksheetBackgroundFillRect_SizeUnchangedAtDefaultZoom_NoRegression()
    {
        WpfTestThread.Run(() =>
        {
            var grid = new GridView { Width = 300, Height = 200, ZoomFactor = 1.0, ShowHeaders = false };
            grid.Measure(new Size(300, 200));
            grid.Arrange(new Rect(0, 0, 300, 200));
            grid.UpdateLayout();

            var widthMethod = typeof(GridView).GetMethod("GetLogicalViewportWidth", BindingFlags.NonPublic | BindingFlags.Instance);
            var heightMethod = typeof(GridView).GetMethod("GetLogicalViewportHeight", BindingFlags.NonPublic | BindingFlags.Instance);

            var logicalWidth = (double)widthMethod!.Invoke(grid, null)!;
            var logicalHeight = (double)heightMethod!.Invoke(grid, null)!;

            logicalWidth.Should().BeApproximately(grid.ActualWidth, 0.01);
            logicalHeight.Should().BeApproximately(grid.ActualHeight, 0.01);
        });
    }
}

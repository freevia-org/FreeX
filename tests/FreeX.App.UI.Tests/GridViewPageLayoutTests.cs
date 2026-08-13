using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;
using System.Linq;
using System.Windows;

namespace FreeX.App.UI.Tests;

public sealed class GridViewPageLayoutTests
{
    [Fact]
    public void CalculatePageMarginRulerHandles_MapsMarginsToHorizontalAndVerticalRulerHandles()
    {
        var pageBounds = new Rect(30, 18, 850, 1100);

        var handles = GridView.CalculatePageMarginRulerHandles(
            pageBounds,
            WorksheetPaperSize.Letter,
            WorksheetPageOrientation.Portrait,
            WorksheetPageMargins.Normal);

        handles.Left.Should().Be(new Rect(100 - 4, 18 - 14, 8, 12));
        handles.Right.Should().Be(new Rect(810 - 4, 18 - 14, 8, 12));
        handles.Top.Should().Be(new Rect(30 - 14, 93 - 4, 12, 8));
        handles.Bottom.Should().Be(new Rect(30 - 14, 1043 - 4, 12, 8));
    }

    [Fact]
    public void HitTestPageMarginRulerHandles_ReturnsMarginEdgeForHandle()
    {
        var handles = new PageMarginRulerHandles(
            new Rect(126, 4, 8, 12),
            new Rect(776, 4, 8, 12),
            new Rect(16, 114, 12, 8),
            new Rect(16, 1014, 12, 8));

        GridView.HitTestPageMarginRulerHandles(handles, new Point(130, 10))
            .Should().Be(WorksheetPageMarginEdge.Left);
        GridView.HitTestPageMarginRulerHandles(handles, new Point(780, 10))
            .Should().Be(WorksheetPageMarginEdge.Right);
        GridView.HitTestPageMarginRulerHandles(handles, new Point(20, 118))
            .Should().Be(WorksheetPageMarginEdge.Top);
        GridView.HitTestPageMarginRulerHandles(handles, new Point(20, 1018))
            .Should().Be(WorksheetPageMarginEdge.Bottom);
        GridView.HitTestPageMarginRulerHandles(handles, new Point(200, 200))
            .Should().BeNull();
    }

    [Fact]
    public void HitTestPageMarginRulerHandles_IncludesHandleBoundary()
    {
        var handles = new PageMarginRulerHandles(
            new Rect(126, 4, 8, 12),
            new Rect(776, 4, 8, 12),
            new Rect(16, 114, 12, 8),
            new Rect(16, 1014, 12, 8));

        GridView.HitTestPageMarginRulerHandles(handles, new Point(134, 16))
            .Should().Be(WorksheetPageMarginEdge.Left);
        GridView.HitTestPageMarginRulerHandles(handles, new Point(784, 16))
            .Should().Be(WorksheetPageMarginEdge.Right);
        GridView.HitTestPageMarginRulerHandles(handles, new Point(28, 122))
            .Should().Be(WorksheetPageMarginEdge.Top);
        GridView.HitTestPageMarginRulerHandles(handles, new Point(28, 1022))
            .Should().Be(WorksheetPageMarginEdge.Bottom);
    }

    [Fact]
    public void HitTestPageMarginRulerHandles_ReturnsNullWhenRulersAreHidden()
    {
        var handles = new PageMarginRulerHandles(
            new Rect(126, 4, 8, 12),
            new Rect(776, 4, 8, 12),
            new Rect(16, 114, 12, 8),
            new Rect(16, 1014, 12, 8));

        GridView.HitTestPageMarginRulerHandles(handles, new Point(130, 10), showRulers: false)
            .Should().BeNull();
    }

    // The page-break-preview / page-layout geometry tests live with the portable planner in
    // FreeX.App.Presentation.Tests.PageLayout.PageBreakPreviewLayoutPlannerTests. This file keeps only
    // the WPF GridView wiring assertions.

    [Fact]
    public void GridView_RenderWorksheetViewOverlay_DrawsDistinctPageBreakAndPageLayoutVisuals()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Overlays.cs");

        // R91-render-frozen-print-titles-5-2: the single-range PrintArea/PagePreviewRange fallback
        // (used when no multi-area PrintAreas list is configured) now lives inside the extracted
        // ResolvePageBreakPreviewRanges helper rather than inline here.
        source.Should().Contain("printArea ?? pagePreviewRange");
        source.Should().Contain("ResolvePageBreakPreviewRanges(PrintAreas, PrintArea, PagePreviewRange)");
        source.Should().Contain("PageBreakPreviewLayoutPlanner.Calculate");
        source.Should().Contain("RenderPageBreakPreviewLayout(dc, layout)");
        source.Should().Contain("DrawPageBreakWatermark");
        source.Should().Contain("RenderPageLayoutPages(dc, layout)");
        source.Should().Contain("DrawPageLayoutBoundary(dc, page)");
        source.Should().Contain("DrawPageLayoutHeaderFooterCues(dc, page)");
        source.Should().Contain("drawClippedEdges: WorksheetViewMode != WorksheetViewMode.PageLayout");
        source.Should().Contain("page.VisibleEdges.Top");
        source.Should().Contain("page.VisibleEdges.Bottom");
        source.Should().NotContain("dc.DrawRectangle(PageLayoutPageSurfaceBrush, PageLayoutPen, page.Bounds)");
        source.Should().Contain("RenderPageMarginGuides(dc, pageRange)");
    }

    [Fact]
    public void GridView_PreSelectionCacheKeysPagePreviewAndPageSetupInputs()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.RenderSurfaceCache.cs");

        source.Should().Contain("GridRange? PagePreviewRange");
        source.Should().Contain("WorksheetPageOrder PageOrder");
        source.Should().Contain("WorksheetScaleToFit ScaleToFit");
        source.Should().Contain("WorksheetRepeatRange? PrintTitleRows");
        source.Should().Contain("WorksheetRepeatRange? PrintTitleColumns");
        source.Should().Contain("PagePreviewRange,");
    }

}

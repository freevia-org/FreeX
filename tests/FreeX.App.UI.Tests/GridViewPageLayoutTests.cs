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

        handles.Left.Should().Be(new Rect(130 - 4, 18 - 14, 8, 12));
        handles.Right.Should().Be(new Rect(780 - 4, 18 - 14, 8, 12));
        handles.Top.Should().Be(new Rect(30 - 14, 118 - 4, 12, 8));
        handles.Bottom.Should().Be(new Rect(30 - 14, 1018 - 4, 12, 8));
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

    [Fact]
    public void PageBreakPreviewLayoutPlanner_BuildsPagesAndAutomaticBreakLinesForPreviewRange()
    {
        var sheetId = SheetId.New();
        var viewport = new ViewportModel(
            [],
            Enumerable.Range(1, 70).Select(row => new RowMetric((uint)row, (row - 1) * 20, 20)).ToList(),
            Enumerable.Range(1, 20).Select(col => new ColMetric((uint)col, (col - 1) * 40, 40)).ToList(),
            null,
            []);
        var previewRange = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 70, 20));

        var layout = PageBreakPreviewLayoutPlanner.Calculate(
            viewport,
            previewRange,
            rowPageBreaks: null,
            columnPageBreaks: null,
            WorksheetPageOrder.DownThenOver,
            WorksheetScaleToFit.Default,
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.A4,
            WorksheetPageOrientation.Portrait,
            WorksheetPageMargins.Narrow,
            rowHeaderWidth: 30,
            columnHeaderHeight: 18,
            actualWidth: 900,
            actualHeight: 1500);

        layout.Pages.Should().HaveCount(4);
        layout.Pages.Select(page => page.PageNumber).Should().Equal(1, 3, 2, 4);
        layout.Pages.Should().OnlyContain(page => page.Bounds.Width > 0 && page.Bounds.Height > 0);
        layout.OutsidePrintAreaMasks.Should().NotBeEmpty();
    }

    [Fact]
    public void PageBreakPreviewLayoutPlanner_MarksOffscreenPageLayoutTopAndBottomEdgesNotVisible()
    {
        var sheetId = SheetId.New();
        var viewport = new ViewportModel(
            [],
            Enumerable.Range(10, 11).Select(row => new RowMetric((uint)row, (row - 10) * 20, 20)).ToList(),
            Enumerable.Range(1, 5).Select(col => new ColMetric((uint)col, (col - 1) * 40, 40)).ToList(),
            null,
            []);
        var printArea = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 40, 5));

        var layout = PageBreakPreviewLayoutPlanner.Calculate(
            viewport,
            printArea,
            rowPageBreaks: null,
            columnPageBreaks: null,
            WorksheetPageOrder.DownThenOver,
            WorksheetScaleToFit.Default,
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.A4,
            WorksheetPageOrientation.Portrait,
            WorksheetPageMargins.Narrow,
            rowHeaderWidth: 30,
            columnHeaderHeight: 18,
            actualWidth: 230,
            actualHeight: 238);

        var page = layout.Pages.Should().ContainSingle().Which;
        page.Bounds.Top.Should().Be(18);
        page.Bounds.Bottom.Should().Be(238);
        page.VisibleEdges.Top.Should().BeFalse();
        page.VisibleEdges.Bottom.Should().BeFalse();
        page.VisibleEdges.Left.Should().BeTrue();
        page.VisibleEdges.Right.Should().BeTrue();
    }

    [Fact]
    public void PageBreakPreviewLayoutPlanner_MarksPageLayoutBoundaryEdgesOnlyWhenDocumentRowsAreVisible()
    {
        var sheetId = SheetId.New();
        var printArea = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 40, 5));

        var topViewport = CreatePageLayoutViewport(firstRow: 1, rowCount: 12);
        var topPage = CalculateSinglePage(topViewport, printArea);
        topPage.VisibleEdges.Top.Should().BeTrue();
        topPage.VisibleEdges.Bottom.Should().BeFalse();

        var bottomViewport = CreatePageLayoutViewport(firstRow: 30, rowCount: 11);
        var bottomPage = CalculateSinglePage(bottomViewport, printArea);
        bottomPage.VisibleEdges.Top.Should().BeFalse();
        bottomPage.VisibleEdges.Bottom.Should().BeTrue();
    }

    [Fact]
    public void GridView_RenderWorksheetViewOverlay_DrawsDistinctPageBreakAndPageLayoutVisuals()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Overlays.cs");

        source.Should().Contain("PrintArea ?? PagePreviewRange");
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

    private static ViewportModel CreatePageLayoutViewport(int firstRow, int rowCount) =>
        new(
            [],
            Enumerable.Range(firstRow, rowCount)
                .Select(row => new RowMetric((uint)row, (row - firstRow) * 20, 20))
                .ToList(),
            Enumerable.Range(1, 5)
                .Select(col => new ColMetric((uint)col, (col - 1) * 40, 40))
                .ToList(),
            null,
            []);

    private static PageBreakPreviewPageLayout CalculateSinglePage(ViewportModel viewport, GridRange printArea) =>
        PageBreakPreviewLayoutPlanner.Calculate(
                viewport,
                printArea,
                rowPageBreaks: null,
                columnPageBreaks: null,
                WorksheetPageOrder.DownThenOver,
                WorksheetScaleToFit.Default,
                printTitleRows: null,
                printTitleColumns: null,
                WorksheetPaperSize.A4,
                WorksheetPageOrientation.Portrait,
                WorksheetPageMargins.Narrow,
                rowHeaderWidth: 30,
                columnHeaderHeight: 18,
                actualWidth: 230,
                actualHeight: 238)
            .Pages
            .Should()
            .ContainSingle()
            .Which;

}

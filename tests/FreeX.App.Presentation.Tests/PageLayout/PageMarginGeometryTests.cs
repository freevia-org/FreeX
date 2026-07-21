using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

public sealed class PageMarginGeometryTests
{
    [Fact]
    public void CalculateGuide_MapsPrintAreaToMarginGuidePixels()
    {
        var sheetId = SheetId.New();
        var viewport = new ViewportModel(
            [],
            [new RowMetric(2, 20, 10), new RowMetric(3, 30, 30)],
            [new ColMetric(4, 80, 15), new ColMetric(5, 120, 95)],
            null,
            []);
        var printArea = new GridRange(
            new CellAddress(sheetId, 2, 4),
            new CellAddress(sheetId, 3, 5));

        var guide = PageMarginGuideLayoutPlanner.CalculateGuide(
            viewport,
            printArea,
            rowHeaderWidth: 30,
            columnHeaderHeight: 18,
            WorksheetPaperSize.Letter,
            WorksheetPageOrientation.Portrait,
            WorksheetPageMargins.Normal);

        // Excel's real Normal margins (0.7in left/right, 0.75in top/bottom) narrow the guide band
        // slightly compared to the old (incorrect) 1.0in-all-around constants.
        guide.Should().Be(new PageMarginGuideLayout(
            Top: 28,
            Left: 45,
            Bottom: 78,
            Right: 245,
            MarginLeft: 61.470588235294116,
            MarginRight: 228.52941176470588,
            MarginTop: 31.40909090909091,
            MarginBottom: 74.5909090909091));
    }

    [Fact]
    public void CalculateGuide_ReturnsNullWhenPrintAreaEdgeIsNotVisible()
    {
        var sheetId = SheetId.New();
        var viewport = new ViewportModel(
            [],
            [new RowMetric(2, 20, 10)],
            [new ColMetric(4, 80, 15)],
            null,
            []);
        var printArea = new GridRange(
            new CellAddress(sheetId, 2, 4),
            new CellAddress(sheetId, 3, 4));

        PageMarginGuideLayoutPlanner.CalculateGuide(
                viewport,
                printArea,
                rowHeaderWidth: 30,
                columnHeaderHeight: 18,
                WorksheetPaperSize.Letter,
                WorksheetPageOrientation.Portrait,
                WorksheetPageMargins.Normal)
            .Should().BeNull();
    }

    [Fact]
    public void CalculateHandles_MapsMarginsToHorizontalAndVerticalRulerHandles()
    {
        var pageBounds = new LayoutRect(30, 18, 850, 1100);

        var handles = PageMarginRulerLayoutPlanner.CalculateHandles(
            pageBounds,
            WorksheetPaperSize.Letter,
            WorksheetPageOrientation.Portrait,
            WorksheetPageMargins.Normal);

        // Letter is 8.5in x 11in. Excel's real Normal margins are 0.7in left/right, 0.75in top/bottom.
        // marginLeft = 30 + 850*(0.7/8.5) = 30 + 70 = 100; marginRight = 30 + 850*(1 - 0.7/8.5) = 810.
        // marginTop = 18 + 1100*(0.75/11) = 18 + 75 = 93; marginBottom = 18 + 1100*(1 - 0.75/11) = 1043.
        handles.Left.Should().Be(new LayoutRect(100 - 4, 18 - 14, 8, 12));
        handles.Right.Should().Be(new LayoutRect(810 - 4, 18 - 14, 8, 12));
        handles.Top.Should().Be(new LayoutRect(30 - 14, 93 - 4, 12, 8));
        handles.Bottom.Should().Be(new LayoutRect(30 - 14, 1043 - 4, 12, 8));
    }

    [Fact]
    public void HitTestHandles_ReturnsMarginEdgeForHandle()
    {
        var handles = new PageMarginRulerHandles(
            new LayoutRect(126, 4, 8, 12),
            new LayoutRect(776, 4, 8, 12),
            new LayoutRect(16, 114, 12, 8),
            new LayoutRect(16, 1014, 12, 8));

        PageMarginRulerLayoutPlanner.HitTestHandles(handles, new LayoutPoint(130, 10))
            .Should().Be(WorksheetPageMarginEdge.Left);
        PageMarginRulerLayoutPlanner.HitTestHandles(handles, new LayoutPoint(780, 10))
            .Should().Be(WorksheetPageMarginEdge.Right);
        PageMarginRulerLayoutPlanner.HitTestHandles(handles, new LayoutPoint(20, 118))
            .Should().Be(WorksheetPageMarginEdge.Top);
        PageMarginRulerLayoutPlanner.HitTestHandles(handles, new LayoutPoint(20, 1018))
            .Should().Be(WorksheetPageMarginEdge.Bottom);
        PageMarginRulerLayoutPlanner.HitTestHandles(handles, new LayoutPoint(200, 200))
            .Should().BeNull();
    }

    [Fact]
    public void HitTestHandles_IncludesHandleBoundary()
    {
        var handles = new PageMarginRulerHandles(
            new LayoutRect(126, 4, 8, 12),
            new LayoutRect(776, 4, 8, 12),
            new LayoutRect(16, 114, 12, 8),
            new LayoutRect(16, 1014, 12, 8));

        PageMarginRulerLayoutPlanner.HitTestHandles(handles, new LayoutPoint(134, 16))
            .Should().Be(WorksheetPageMarginEdge.Left);
        PageMarginRulerLayoutPlanner.HitTestHandles(handles, new LayoutPoint(784, 16))
            .Should().Be(WorksheetPageMarginEdge.Right);
        PageMarginRulerLayoutPlanner.HitTestHandles(handles, new LayoutPoint(28, 122))
            .Should().Be(WorksheetPageMarginEdge.Top);
        PageMarginRulerLayoutPlanner.HitTestHandles(handles, new LayoutPoint(28, 1022))
            .Should().Be(WorksheetPageMarginEdge.Bottom);
    }

    [Fact]
    public void HitTestHandles_ReturnsNullWhenRulersAreHidden()
    {
        var handles = new PageMarginRulerHandles(
            new LayoutRect(126, 4, 8, 12),
            new LayoutRect(776, 4, 8, 12),
            new LayoutRect(16, 114, 12, 8),
            new LayoutRect(16, 1014, 12, 8));

        PageMarginRulerLayoutPlanner.HitTestHandles(handles, new LayoutPoint(130, 10), showRulers: false)
            .Should().BeNull();
    }

    [Fact]
    public void HitTestGuide_HitTestsGuidesAndRulerHandles()
    {
        var guide = new PageMarginGuideLayout(
            Top: 18,
            Left: 30,
            Bottom: 1118,
            Right: 880,
            MarginLeft: 130,
            MarginRight: 780,
            MarginTop: 118,
            MarginBottom: 1018);
        var handles = new PageMarginRulerHandles(
            new LayoutRect(126, 4, 8, 12),
            new LayoutRect(776, 4, 8, 12),
            new LayoutRect(16, 114, 12, 8),
            new LayoutRect(16, 1014, 12, 8));

        PageMarginGuideLayoutPlanner.HitTestGuide(guide, new LayoutPoint(130, 10), handles, showRulers: true, guideHitZone: 5)
            .Should().Be(WorksheetPageMarginEdge.Left);
        PageMarginGuideLayoutPlanner.HitTestGuide(guide, new LayoutPoint(782, 400), handles, showRulers: true, guideHitZone: 5)
            .Should().Be(WorksheetPageMarginEdge.Right);
        PageMarginGuideLayoutPlanner.HitTestGuide(guide, new LayoutPoint(400, 118), handles, showRulers: true, guideHitZone: 5)
            .Should().Be(WorksheetPageMarginEdge.Top);
        PageMarginGuideLayoutPlanner.HitTestGuide(guide, new LayoutPoint(400, 1018), handles, showRulers: true, guideHitZone: 5)
            .Should().Be(WorksheetPageMarginEdge.Bottom);
        PageMarginGuideLayoutPlanner.HitTestGuide(guide, new LayoutPoint(10, 10), handles, showRulers: true, guideHitZone: 5)
            .Should().BeNull();
    }

    [Fact]
    public void HitTestGuide_PrefersRulerHandleOverGuideLine()
    {
        var guide = new PageMarginGuideLayout(
            Top: 18,
            Left: 30,
            Bottom: 1118,
            Right: 880,
            MarginLeft: 400,
            MarginRight: 780,
            MarginTop: 118,
            MarginBottom: 1018);
        var handles = new PageMarginRulerHandles(
            new LayoutRect(126, 4, 8, 12),
            new LayoutRect(776, 4, 8, 12),
            new LayoutRect(16, 114, 12, 8),
            new LayoutRect(16, 1014, 12, 8));

        // Pointer sits on the right ruler handle even though it is far from the left guide line.
        PageMarginGuideLayoutPlanner.HitTestGuide(guide, new LayoutPoint(780, 10), handles, showRulers: true, guideHitZone: 5)
            .Should().Be(WorksheetPageMarginEdge.Right);
    }

    [Fact]
    public void CalculateDraggedMargins_MapsPointerToGuideFraction()
    {
        var guide = new PageMarginGuideLayout(
            Top: 18,
            Left: 30,
            Bottom: 1118,
            Right: 880,
            MarginLeft: 130,
            MarginRight: 780,
            MarginTop: 118,
            MarginBottom: 1018);

        var margins = PageMarginGuideLayoutPlanner.CalculateDraggedMargins(
            WorksheetPaperSize.Letter,
            WorksheetPageOrientation.Portrait,
            WorksheetPageMargins.Normal,
            WorksheetPageMarginEdge.Left,
            guide,
            new LayoutPoint(115, 300));

        margins.Left.Should().BeApproximately(0.85, 0.001);
    }
}

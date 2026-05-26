using FluentAssertions;
using Freexcel.App.UI;
using Freexcel.Core.Model;
using System.Windows;

namespace Freexcel.App.UI.Tests;

public sealed class GridViewPageLayoutTests
{
    [Fact]
    public void PageMarginGuideLayoutPlanner_MapsPrintAreaToMarginGuidePixels()
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

        guide.Should().Be(new PageMarginGuideLayout(
            Top: 28,
            Left: 45,
            Bottom: 78,
            Right: 245,
            MarginLeft: 68.529411764705884,
            MarginRight: 221.47058823529412,
            MarginTop: 32.545454545454547,
            MarginBottom: 73.454545454545453));
    }

    [Fact]
    public void PageMarginGuideLayoutPlanner_ReturnsNullWhenPrintAreaEdgeIsNotVisible()
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
}

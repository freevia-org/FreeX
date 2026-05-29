using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;
using System.IO;
using System.Windows;

namespace FreeX.App.UI.Tests;

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
    public void PageMarginGuideLayoutPlanner_CalculatesVisibleEdgesWithSingleMetricPasses()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "PageMarginGuideLayoutPlanner.cs"));
        var calculateGuide = source[
            source.IndexOf("public static PageMarginGuideLayout? CalculateGuide", StringComparison.Ordinal)..
            source.IndexOf("public static WorksheetPageMarginEdge? HitTestGuide", StringComparison.Ordinal)];

        calculateGuide.Should().Contain("TryFindRowMetrics(viewport.RowMetrics");
        calculateGuide.Should().Contain("TryFindColumnMetrics(viewport.ColMetrics");
        calculateGuide.Should().Contain("foreach (var metric in metrics)");
        calculateGuide.Should().NotContain("FirstOrDefault");
        calculateGuide.Should().NotContain(".Where(");
        calculateGuide.Should().NotContain(".ToList()");
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
    public void PageMarginGuideLayoutPlanner_HitTestsGuidesAndRulerHandles()
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
            new Rect(126, 4, 8, 12),
            new Rect(776, 4, 8, 12),
            new Rect(16, 114, 12, 8),
            new Rect(16, 1014, 12, 8));

        PageMarginGuideLayoutPlanner.HitTestGuide(guide, new Point(130, 10), handles, showRulers: true, guideHitZone: 5)
            .Should().Be(WorksheetPageMarginEdge.Left);
        PageMarginGuideLayoutPlanner.HitTestGuide(guide, new Point(782, 400), handles, showRulers: true, guideHitZone: 5)
            .Should().Be(WorksheetPageMarginEdge.Right);
        PageMarginGuideLayoutPlanner.HitTestGuide(guide, new Point(400, 118), handles, showRulers: true, guideHitZone: 5)
            .Should().Be(WorksheetPageMarginEdge.Top);
        PageMarginGuideLayoutPlanner.HitTestGuide(guide, new Point(400, 1018), handles, showRulers: true, guideHitZone: 5)
            .Should().Be(WorksheetPageMarginEdge.Bottom);
        PageMarginGuideLayoutPlanner.HitTestGuide(guide, new Point(10, 10), handles, showRulers: true, guideHitZone: 5)
            .Should().BeNull();
    }

    [Fact]
    public void PageMarginGuideLayoutPlanner_CalculatesDraggedMarginsFromGuideFraction()
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
            new Point(115, 300));

        margins.Left.Should().BeApproximately(0.85, 0.001);
    }

    private static string FindWorkspaceFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. relativeParts]);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate workspace file.", Path.Combine(relativeParts));
    }
}

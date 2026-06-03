using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;
using System.IO;
using System.Windows;

namespace FreeX.App.UI.Tests;

public sealed partial class GridViewSplitPaneLayoutTests
{
    [Fact]
    public void HitTestViewportCell_UsesPinnedSplitPaneQuadrantsBeforeMainViewport()
    {
        var sheetId = SheetId.New();
        var viewport = new ViewportModel(
            [],
            [new RowMetric(20, 18, 0), new RowMetric(21, 18, 18)],
            [new ColMetric(10, 64, 0), new ColMetric(11, 64, 64)],
            SplitPanes: new SplitPaneState(
                4,
                4,
                [new RowMetric(1, 18, 0), new RowMetric(2, 22, 18), new RowMetric(3, 18, 40)],
                [new ColMetric(1, 64, 0), new ColMetric(2, 80, 64), new ColMetric(3, 64, 144)]));

        GridView.HitTestViewportCell(viewport, sheetId, new Point(GridView.RowHeaderWidth + 5, GridView.ColHeaderHeight + 5))
            .Should().Be(new CellAddress(sheetId, 1, 1));
        GridView.HitTestViewportCell(viewport, sheetId, new Point(GridView.RowHeaderWidth + 208 + 5, GridView.ColHeaderHeight + 5))
            .Should().Be(new CellAddress(sheetId, 1, 10));
        GridView.HitTestViewportCell(viewport, sheetId, new Point(GridView.RowHeaderWidth + 5, GridView.ColHeaderHeight + 58 + 5))
            .Should().Be(new CellAddress(sheetId, 20, 1));
        GridView.HitTestViewportCell(viewport, sheetId, new Point(GridView.RowHeaderWidth + 208 + 5, GridView.ColHeaderHeight + 58 + 5))
            .Should().Be(new CellAddress(sheetId, 20, 10));
    }

    [Fact]
    public void HitTestViewportCell_UsesIndependentTopRightAndBottomLeftMetrics()
    {
        var sheetId = SheetId.New();
        var viewport = new ViewportModel(
            [],
            [new RowMetric(20, 18, 0), new RowMetric(21, 18, 18)],
            [new ColMetric(10, 64, 0), new ColMetric(11, 64, 64)],
            SplitPanes: new SplitPaneState(
                4,
                4,
                [new RowMetric(1, 18, 0), new RowMetric(2, 22, 18), new RowMetric(3, 18, 40)],
                [new ColMetric(1, 64, 0), new ColMetric(2, 80, 64), new ColMetric(3, 64, 144)],
                [],
                [new ColMetric(12, 64, 0), new ColMetric(13, 64, 64)],
                [new RowMetric(30, 18, 0), new RowMetric(31, 18, 18)]));

        GridView.HitTestViewportCell(viewport, sheetId, new Point(GridView.RowHeaderWidth + 208 + 5, GridView.ColHeaderHeight + 5))
            .Should().Be(new CellAddress(sheetId, 1, 12));
        GridView.HitTestViewportCell(viewport, sheetId, new Point(GridView.RowHeaderWidth + 5, GridView.ColHeaderHeight + 58 + 5))
            .Should().Be(new CellAddress(sheetId, 30, 1));
        GridView.HitTestViewportCell(viewport, sheetId, new Point(GridView.RowHeaderWidth + 208 + 5, GridView.ColHeaderHeight + 58 + 5))
            .Should().Be(new CellAddress(sheetId, 20, 10));
    }

    [Fact]
    public void HitTestViewportCell_StopsMetricScansOnceSortedEdgesPassPointer()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "GridView.SplitPanes.cs"));
        var hitTestMetrics = source[
            source.IndexOf("private static CellAddress? HitTestMetrics", StringComparison.Ordinal)..
            source.IndexOf("public static SplitPaneClipRects CalculateSplitPaneClipRects", StringComparison.Ordinal)];

        hitTestMetrics.Should().Contain("foreach (var rm in rows)");
        hitTestMetrics.Should().Contain("if (pos.Y < top)");
        hitTestMetrics.Should().Contain("foreach (var cm in cols)");
        hitTestMetrics.Should().Contain("if (pos.X < left)");
        hitTestMetrics.Should().Contain("break;");
    }

    [Fact]
    public void HitTestViewportCell_ReusesRowHeaderWidthWithinHitTest()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "GridView.SplitPanes.cs"));
        var hitTestViewportCell = source[
            source.IndexOf("public static CellAddress? HitTestViewportCell", StringComparison.Ordinal)..
            source.IndexOf("public static SplitPaneRegion HitTestSplitPaneRegion", StringComparison.Ordinal)];

        hitTestViewportCell.Should().Contain("var rowHeaderWidth = CalculateRowHeaderWidth(viewport);");
        hitTestViewportCell.Should().Contain("if (pos.X < rowHeaderWidth || pos.Y < ColHeaderHeight)");
        hitTestViewportCell.Should().Contain(": rowHeaderWidth;");
        hitTestViewportCell.Should().Contain("ColHeaderHeight, rowHeaderWidth)");
        hitTestViewportCell.IndexOf("var rowHeaderWidth = CalculateRowHeaderWidth(viewport);", StringComparison.Ordinal)
            .Should()
            .BeLessThan(hitTestViewportCell.IndexOf("if (viewport.SplitPanes is { } splitPanes)", StringComparison.Ordinal));
        hitTestViewportCell[
            hitTestViewportCell.IndexOf("if (viewport.SplitPanes is { } splitPanes)", StringComparison.Ordinal)..]
            .Should()
            .NotContain("CalculateRowHeaderWidth(viewport)");
    }

    [Fact]
    public void HitTestViewportCell_ReusesSplitDividerLayoutForRegionClassification()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "GridView.SplitPanes.cs"));
        var hitTestViewportCell = source[
            source.IndexOf("public static CellAddress? HitTestViewportCell", StringComparison.Ordinal)..
            source.IndexOf("public static SplitPaneRegion HitTestSplitPaneRegion", StringComparison.Ordinal)];
        var splitPaneRegionMethods = source[
            source.IndexOf("public static SplitPaneRegion HitTestSplitPaneRegion", StringComparison.Ordinal)..
            source.IndexOf("public static SplitDividerHandle HitTestSplitDividerHandle", StringComparison.Ordinal)];

        hitTestViewportCell.Should().Contain("var dividerLayout = CalculateSplitDividerLayout(viewport);");
        hitTestViewportCell.Should().Contain("var region = HitTestSplitPaneRegion(dividerLayout, pos);");
        hitTestViewportCell[
            hitTestViewportCell.IndexOf("var dividerLayout = CalculateSplitDividerLayout(viewport);", StringComparison.Ordinal)..]
            .Should()
            .NotContain("HitTestSplitPaneRegion(viewport, pos)");
        splitPaneRegionMethods.Should().Contain("private static SplitPaneRegion HitTestSplitPaneRegion(SplitDividerLayout dividerLayout, Point pos)");
        splitPaneRegionMethods.Should().Contain("return HitTestSplitPaneRegion(dividerLayout, pos);");
    }

    [Fact]
    public void HitTestSplitPaneRegion_ClassifiesSplitQuadrants()
    {
        var viewport = SplitViewport();

        GridView.HitTestSplitPaneRegion(viewport, new Point(GridView.RowHeaderWidth + 5, GridView.ColHeaderHeight + 5))
            .Should().Be(SplitPaneRegion.TopLeft);
        GridView.HitTestSplitPaneRegion(viewport, new Point(GridView.RowHeaderWidth + 208 + 5, GridView.ColHeaderHeight + 5))
            .Should().Be(SplitPaneRegion.TopRight);
        GridView.HitTestSplitPaneRegion(viewport, new Point(GridView.RowHeaderWidth + 5, GridView.ColHeaderHeight + 58 + 5))
            .Should().Be(SplitPaneRegion.BottomLeft);
        GridView.HitTestSplitPaneRegion(viewport, new Point(GridView.RowHeaderWidth + 208 + 5, GridView.ColHeaderHeight + 58 + 5))
            .Should().Be(SplitPaneRegion.BottomRight);
    }

    [Fact]
    public void HitTestSplitDividerHandle_DetectsHorizontalVerticalAndIntersectionHandles()
    {
        var viewport = SplitViewport();

        GridView.HitTestSplitDividerHandle(
                viewport,
                new Point(GridView.RowHeaderWidth + 20, GridView.ColHeaderHeight + 58 + 2))
            .Should().Be(SplitDividerHandle.Horizontal);
        GridView.HitTestSplitDividerHandle(
                viewport,
                new Point(GridView.RowHeaderWidth + 208 + 2, GridView.ColHeaderHeight + 20))
            .Should().Be(SplitDividerHandle.Vertical);
        GridView.HitTestSplitDividerHandle(
                viewport,
                new Point(GridView.RowHeaderWidth + 208 + 2, GridView.ColHeaderHeight + 58 + 2))
            .Should().Be(SplitDividerHandle.Intersection);
        GridView.HitTestSplitDividerHandle(
                viewport,
                new Point(GridView.RowHeaderWidth + 20, GridView.ColHeaderHeight + 30))
            .Should().Be(SplitDividerHandle.None);
    }

    [Fact]
    public void HitTestSplitDividerHandle_StaysInsideRenderedControlBounds()
    {
        var viewport = SplitViewport();
        const double actualWidth = 500;
        const double actualHeight = 300;

        GridView.HitTestSplitDividerHandle(
                viewport,
                new Point(actualWidth + 10, GridView.ColHeaderHeight + 58),
                actualWidth,
                actualHeight)
            .Should().Be(SplitDividerHandle.None);
        GridView.HitTestSplitDividerHandle(
                viewport,
                new Point(GridView.RowHeaderWidth + 208, actualHeight + 10),
                actualWidth,
                actualHeight)
            .Should().Be(SplitDividerHandle.None);
        GridView.HitTestSplitDividerHandle(
                viewport,
                new Point(actualWidth, GridView.ColHeaderHeight + 58),
                actualWidth,
                actualHeight)
            .Should().Be(SplitDividerHandle.Horizontal);
        GridView.HitTestSplitDividerHandle(
                viewport,
                new Point(GridView.RowHeaderWidth + 208, actualHeight),
                actualWidth,
                actualHeight)
            .Should().Be(SplitDividerHandle.Vertical);
    }

    [Fact]
    public void CalculateSplitDividerDragTarget_MapsReleasePositionToSplitRowAndColumn()
    {
        var viewport = SplitViewport();

        GridView.CalculateSplitDividerDragTarget(
                viewport,
                SplitDividerHandle.Horizontal,
                new Point(GridView.RowHeaderWidth + 5, GridView.ColHeaderHeight + 18 + 22 + 5))
            .Should().Be(new SplitDividerDragTarget(4, null));
        GridView.CalculateSplitDividerDragTarget(
                viewport,
                SplitDividerHandle.Vertical,
                new Point(GridView.RowHeaderWidth + 64 + 80 + 5, GridView.ColHeaderHeight + 5))
            .Should().Be(new SplitDividerDragTarget(null, 4));
        GridView.CalculateSplitDividerDragTarget(
                viewport,
                SplitDividerHandle.Intersection,
                new Point(GridView.RowHeaderWidth + 64 + 80 + 5, GridView.ColHeaderHeight + 18 + 22 + 5))
            .Should().Be(new SplitDividerDragTarget(4, 4));
    }

    [Fact]
    public void CalculateSplitDividerDragTarget_ClampsPinnedEdgeAtWorksheetLimits()
    {
        var viewport = new ViewportModel(
            [],
            [new RowMetric(CellAddress.MaxRow, 18, 0)],
            [new ColMetric(CellAddress.MaxCol, 64, 0)],
            SplitPanes: new SplitPaneState(
                CellAddress.MaxRow,
                CellAddress.MaxCol,
                [new RowMetric(CellAddress.MaxRow, 18, 0)],
                [new ColMetric(CellAddress.MaxCol, 64, 0)]));

        GridView.CalculateSplitDividerDragTarget(
                viewport,
                SplitDividerHandle.Intersection,
                new Point(GridView.RowHeaderWidth + 64, GridView.ColHeaderHeight + 5))
            .Should().Be(new SplitDividerDragTarget(CellAddress.MaxRow, CellAddress.MaxCol));
    }
}

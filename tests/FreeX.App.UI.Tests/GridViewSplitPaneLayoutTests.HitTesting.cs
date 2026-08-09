using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;
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

    // R49-render-header-frozen-corner-3-1: once a column outline group exists, the render path
    // (GridView.Rendering.Headers.cs / GridView.CalculateColumnHeaderHeight) draws the column
    // header -- and therefore row 1's real top -- at ColHeaderHeight PLUS the outline gutter
    // height, not at the bare ColHeaderHeight constant. Before the fix, HitTestViewportCell bucketed
    // clicks against the bare constant, so every row landed 1-gutter-height too high once columns
    // were grouped, and clicks inside the still-visible gutter strip fell through to row 1 instead
    // of hitting nothing.
    [Fact]
    public void HitTestViewportCell_WithColumnOutlineGutter_AccountsForGutterHeightNotBareConstant()
    {
        var sheetId = SheetId.New();
        var viewport = new ViewportModel(
            [],
            [new RowMetric(1, 20, 0), new RowMetric(2, 20, 20)],
            [new ColMetric(1, 64, 0)],
            ColumnOutlineGroups: [new OutlineGroupRange(1, 1, 3, 0, false)]);

        var effectiveHeaderHeight = GridView.CalculateColumnHeaderHeight(viewport);
        var gutterHeight = effectiveHeaderHeight - GridView.ColHeaderHeight;
        gutterHeight.Should().BeGreaterThan(0, "a level-1 column outline group must add a non-zero gutter");

        // A click inside the still-visible outline-gutter strip (below the bare 18px header but
        // above the true effective header) must hit nothing -- there is no cell there, only chrome
        // -- rather than falling through to row 1 as it did when hit-testing used the bare constant.
        GridView.HitTestViewportCell(
                viewport,
                sheetId,
                new Point(GridView.RowHeaderWidth + 5, GridView.ColHeaderHeight + gutterHeight / 2))
            .Should().BeNull();

        // A click just below the true effective header height must land on row 1 (TopOffset 0) --
        // matching what the render path actually draws at that offset.
        GridView.HitTestViewportCell(
                viewport,
                sheetId,
                new Point(GridView.RowHeaderWidth + 5, effectiveHeaderHeight + 5))
            .Should().Be(new CellAddress(sheetId, 1, 1));

        // And row 2 (TopOffset 20) must still land correctly, anchored off the effective header
        // height rather than 26px (the gutter height in this scenario) too early.
        GridView.HitTestViewportCell(
                viewport,
                sheetId,
                new Point(GridView.RowHeaderWidth + 5, effectiveHeaderHeight + 20 + 5))
            .Should().Be(new CellAddress(sheetId, 2, 1));
    }

    [Fact]
    public void HitTestViewportCell_DelegatesMetricScanningToPortablePlanner()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.SplitPanes.cs");

        source.Should().Contain("ViewportGeometryPlanner.HitTestCell(");
        source.Should().NotContain("private static CellAddress? HitTestMetrics");
    }

    [Fact]
    public void HitTestViewportCell_ReusesRowHeaderWidthWithinHitTest()
    {
        // r49 outline-gutter fix: hit-testing used to test/bucket against the bare ColHeaderHeight
        // constant, which ignores the column-outline gutter the render path adds above the header
        // row once columns are grouped — misaligning every click by the gutter's height. The fix
        // computes an effective column-header height (CalculateColumnHeaderHeight(viewport), the
        // same helper the render path already uses) once up front, alongside rowHeaderWidth, and
        // reuses it everywhere ColHeaderHeight used to appear directly. This test's intent is
        // unchanged from before the fix — the hit-test must compute rowHeaderWidth/colHeaderHeight
        // once and reuse them rather than recomputing (or falling back to the stale constant)
        // inside the split-pane branch — only the pinned substrings below were updated to match.
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.SplitPanes.cs");
        var hitTestViewportCell = source[
            source.IndexOf("public static CellAddress? HitTestViewportCell", StringComparison.Ordinal)..
            source.IndexOf("public static SplitPaneRegion HitTestSplitPaneRegion", StringComparison.Ordinal)];

        hitTestViewportCell.Should().Contain("var rowHeaderWidth = CalculateRowHeaderWidth(viewport);");
        hitTestViewportCell.Should().Contain("var colHeaderHeight = CalculateColumnHeaderHeight(viewport);");
        hitTestViewportCell.Should().Contain("ViewportGeometryPlanner.HitTestCell(");
        hitTestViewportCell.Should().Contain("rowHeaderWidth,");
        hitTestViewportCell.Should().Contain("colHeaderHeight,");
        hitTestViewportCell.Should().Contain("SplitColumnHeaderHeight: ColHeaderHeight");
        hitTestViewportCell.Should().NotContain("pos.Y < ColHeaderHeight");
        hitTestViewportCell.IndexOf("var rowHeaderWidth = CalculateRowHeaderWidth(viewport);", StringComparison.Ordinal)
            .Should()
            .BeLessThan(hitTestViewportCell.IndexOf("ViewportGeometryPlanner.HitTestCell(", StringComparison.Ordinal));
        hitTestViewportCell.IndexOf("var colHeaderHeight = CalculateColumnHeaderHeight(viewport);", StringComparison.Ordinal)
            .Should()
            .BeLessThan(hitTestViewportCell.IndexOf("ViewportGeometryPlanner.HitTestCell(", StringComparison.Ordinal));
    }

    [Fact]
    public void HitTestViewportCell_LeavesSplitRegionClassificationInPortablePlanner()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.SplitPanes.cs");
        var hitTestViewportCell = source[
            source.IndexOf("public static CellAddress? HitTestViewportCell", StringComparison.Ordinal)..
            source.IndexOf("public static SplitPaneRegion HitTestSplitPaneRegion", StringComparison.Ordinal)];

        hitTestViewportCell.Should().Contain("ViewportGeometryPlanner.HitTestCell(");
        hitTestViewportCell.Should().NotContain("CalculateSplitDividerLayout(viewport)");
        hitTestViewportCell.Should().NotContain("HitTestSplitPaneRegion(");
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

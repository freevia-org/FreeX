using FluentAssertions;
using Freexcel.App.UI;
using Freexcel.Core.Model;
using System.Windows;

namespace Freexcel.App.UI.Tests;

public sealed class GridViewSplitPaneLayoutTests
{
    [Fact]
    public void CalculateSplitDividerLayout_UsesPinnedPaneMetricsWhenMainViewportIsScrolledPastSplit()
    {
        var viewport = new ViewportModel(
            [],
            [new RowMetric(20, 18, 0), new RowMetric(21, 18, 18)],
            [new ColMetric(10, 64, 0), new ColMetric(11, 64, 64)],
            SplitPanes: new SplitPaneState(
                4,
                4,
                [new RowMetric(1, 18, 0), new RowMetric(2, 22, 18), new RowMetric(3, 18, 40)],
                [new ColMetric(1, 64, 0), new ColMetric(2, 80, 64), new ColMetric(3, 64, 144)]));

        var layout = GridView.CalculateSplitDividerLayout(viewport);

        layout.HorizontalY.Should().Be(GridView.ColHeaderHeight + 58);
        layout.VerticalX.Should().Be(GridView.RowHeaderWidth + 208);
    }

    [Fact]
    public void CalculateSplitPaneCellLayouts_MapsPinnedCellsToPinnedQuadrants()
    {
        var viewport = new ViewportModel(
            [],
            [new RowMetric(20, 18, 0), new RowMetric(21, 18, 18)],
            [new ColMetric(10, 64, 0), new ColMetric(11, 64, 64)],
            SplitPanes: new SplitPaneState(
                4,
                4,
                [new RowMetric(1, 18, 0), new RowMetric(2, 22, 18), new RowMetric(3, 18, 40)],
                [new ColMetric(1, 64, 0), new ColMetric(2, 80, 64), new ColMetric(3, 64, 144)],
                [
                    Cell(1, 1, "top-left"),
                    Cell(1, 10, "top"),
                    Cell(20, 1, "left")
                ]));

        var layouts = GridView.CalculateSplitPaneCellLayouts(viewport);

        layouts.Select(layout => (layout.Cell.Row, layout.Cell.Col, layout.Rect.X, layout.Rect.Y, layout.Rect.Width, layout.Rect.Height))
            .Should().Equal(
                (1u, 1u, GridView.RowHeaderWidth, GridView.ColHeaderHeight, 64, 18),
                (1u, 10u, GridView.RowHeaderWidth + 208, GridView.ColHeaderHeight, 64, 18),
                (20u, 1u, GridView.RowHeaderWidth, GridView.ColHeaderHeight + 58, 64, 18));
    }

    [Fact]
    public void SplitPaneCellLayoutPlanner_MapsPinnedCellsOutsideGridView()
    {
        var viewport = new ViewportModel(
            [],
            [new RowMetric(20, 18, 0), new RowMetric(21, 18, 18)],
            [new ColMetric(10, 64, 0), new ColMetric(11, 64, 64)],
            SplitPanes: new SplitPaneState(
                4,
                4,
                [new RowMetric(1, 18, 0), new RowMetric(2, 22, 18), new RowMetric(3, 18, 40)],
                [new ColMetric(1, 64, 0), new ColMetric(2, 80, 64), new ColMetric(3, 64, 144)],
                [
                    Cell(1, 1, "top-left"),
                    Cell(1, 10, "top"),
                    Cell(20, 1, "left")
                ]));

        var layouts = SplitPaneCellLayoutPlanner.CalculateLayouts(viewport);

        layouts.Select(layout => (layout.Cell.Row, layout.Cell.Col, layout.Rect.X, layout.Rect.Y, layout.Rect.Width, layout.Rect.Height))
            .Should().Equal(
                (1u, 1u, GridView.RowHeaderWidth, GridView.ColHeaderHeight, 64, 18),
                (1u, 10u, GridView.RowHeaderWidth + 208, GridView.ColHeaderHeight, 64, 18),
                (20u, 1u, GridView.RowHeaderWidth, GridView.ColHeaderHeight + 58, 64, 18));
    }

    [Fact]
    public void CalculateSplitPaneCellLayouts_UsesIndependentTopRightAndBottomLeftMetrics()
    {
        var viewport = new ViewportModel(
            [],
            [new RowMetric(20, 18, 0), new RowMetric(21, 18, 18)],
            [new ColMetric(10, 64, 0), new ColMetric(11, 64, 64)],
            SplitPanes: new SplitPaneState(
                4,
                4,
                [new RowMetric(1, 18, 0), new RowMetric(2, 22, 18), new RowMetric(3, 18, 40)],
                [new ColMetric(1, 64, 0), new ColMetric(2, 80, 64), new ColMetric(3, 64, 144)],
                [
                    Cell(1, 12, "top-offset"),
                    Cell(30, 1, "left-offset")
                ],
                [new ColMetric(12, 64, 0), new ColMetric(13, 64, 64)],
                [new RowMetric(30, 18, 0), new RowMetric(31, 18, 18)]));

        var layouts = GridView.CalculateSplitPaneCellLayouts(viewport);

        layouts.Select(layout => (layout.Cell.Row, layout.Cell.Col, layout.Rect.X, layout.Rect.Y, layout.Rect.Width, layout.Rect.Height))
            .Should().Equal(
                (1u, 12u, GridView.RowHeaderWidth + 208, GridView.ColHeaderHeight, 64, 18),
                (30u, 1u, GridView.RowHeaderWidth, GridView.ColHeaderHeight + 58, 64, 18));
    }

    [Fact]
    public void CalculateSplitPaneCellLayouts_ExpandsMergedAnchorWithinSplitPaneMetrics()
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
                [
                    Cell(1, 1, "merged"),
                    Cell(1, 2, "covered"),
                    Cell(20, 1, "left")
                ]));
        var mergedRegions = new[]
        {
            new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 1, 2))
        };

        var layouts = GridView.CalculateSplitPaneCellLayouts(viewport, mergedRegions);

        layouts.Select(layout => (layout.Cell.Row, layout.Cell.Col, layout.Rect.X, layout.Rect.Y, layout.Rect.Width, layout.Rect.Height))
            .Should().Equal(
                (1u, 1u, GridView.RowHeaderWidth, GridView.ColHeaderHeight, 144, 18),
                (20u, 1u, GridView.RowHeaderWidth, GridView.ColHeaderHeight + 58, 64, 18));
    }

    [Fact]
    public void CalculateSplitPaneCellLayouts_SuppressesCoveredMergeCellWhenAnchorIsOutsideVisiblePaneMetrics()
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
                [
                    Cell(20, 1, "covered"),
                    Cell(21, 1, "visible")
                ]));
        var mergedRegions = new[]
        {
            new GridRange(
                new CellAddress(sheetId, 19, 1),
                new CellAddress(sheetId, 20, 1))
        };

        var layouts = GridView.CalculateSplitPaneCellLayouts(viewport, mergedRegions);

        layouts.Select(layout => (layout.Cell.Row, layout.Cell.Col, layout.Cell.DisplayText))
            .Should().Equal((21u, 1u, "visible"));
    }

    [Fact]
    public void CalculateSplitPaneCellLayouts_AllowsTextOverflowAcrossEmptyCellsWithinSamePane()
    {
        var viewport = new ViewportModel(
            [],
            [new RowMetric(20, 18, 0), new RowMetric(21, 18, 18)],
            [new ColMetric(10, 64, 0), new ColMetric(11, 64, 64)],
            SplitPanes: new SplitPaneState(
                4,
                4,
                [new RowMetric(1, 18, 0), new RowMetric(2, 22, 18), new RowMetric(3, 18, 40)],
                [new ColMetric(1, 64, 0), new ColMetric(2, 80, 64), new ColMetric(3, 64, 144)],
                [
                    Cell(1, 1, "overflow"),
                    Cell(1, 3, "stop")
                ]));

        var layouts = GridView.CalculateSplitPaneCellLayouts(viewport);

        layouts.Single(layout => layout.Cell.Col == 1).TextClipRect
            .Should().Be(new Rect(GridView.RowHeaderWidth, GridView.ColHeaderHeight, 144, 18));
    }

    [Fact]
    public void CalculateSplitPaneCellLayouts_DoesNotOverflowShrinkToFitTextAcrossEmptyCells()
    {
        var viewport = new ViewportModel(
            [],
            [new RowMetric(20, 18, 0), new RowMetric(21, 18, 18)],
            [new ColMetric(10, 64, 0), new ColMetric(11, 64, 64)],
            SplitPanes: new SplitPaneState(
                4,
                4,
                [new RowMetric(1, 18, 0), new RowMetric(2, 22, 18), new RowMetric(3, 18, 40)],
                [new ColMetric(1, 64, 0), new ColMetric(2, 80, 64), new ColMetric(3, 64, 144)],
                [
                    Cell(1, 1, "shrink text", new CellStyle { ShrinkToFit = true }),
                    Cell(1, 3, "stop")
                ]));

        var layouts = GridView.CalculateSplitPaneCellLayouts(viewport);

        layouts.Single(layout => layout.Cell.Col == 1).TextClipRect
            .Should().Be(new Rect(GridView.RowHeaderWidth, GridView.ColHeaderHeight, 64, 18));
    }

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
    public void CalculateSplitPaneScrollbarChrome_AddsIndependentPaneTracksAndThumbs()
    {
        var viewport = SplitViewport();

        var chrome = GridView.CalculateSplitPaneScrollbarChrome(viewport, actualWidth: 500, actualHeight: 300);

        chrome.HorizontalTopRight.Should().NotBeNull();
        chrome.HorizontalTopRight!.Track.Should().Be(new Rect(GridView.RowHeaderWidth + 208, GridView.ColHeaderHeight + 58 - 10, 262, 10));
        chrome.HorizontalTopRight.Thumb.Width.Should().BeGreaterThanOrEqualTo(24);
        chrome.HorizontalTopRight.Thumb.Y.Should().Be(chrome.HorizontalTopRight.Track.Y + 1);
        chrome.VerticalBottomLeft.Should().NotBeNull();
        chrome.VerticalBottomLeft!.Track.Should().Be(new Rect(GridView.RowHeaderWidth + 208 - 10, GridView.ColHeaderHeight + 58, 10, 224));
        chrome.VerticalBottomLeft.Thumb.Height.Should().BeGreaterThanOrEqualTo(24);
        chrome.VerticalBottomLeft.Thumb.X.Should().Be(chrome.VerticalBottomLeft.Track.X + 1);
    }

    [Fact]
    public void SplitPaneViewportChrome_CalculatesScrollbarChromeOutsideGridView()
    {
        var viewport = SplitViewport();

        var chrome = SplitPaneViewportChrome.CalculateScrollbarChrome(viewport, actualWidth: 500, actualHeight: 300);

        chrome.HorizontalTopRight!.Track.Should().Be(new Rect(GridView.RowHeaderWidth + 208, GridView.ColHeaderHeight + 58 - 10, 262, 10));
        chrome.VerticalBottomLeft!.Track.Should().Be(new Rect(GridView.RowHeaderWidth + 208 - 10, GridView.ColHeaderHeight + 58, 10, 224));
    }

    [Fact]
    public void CalculateSplitPaneScrollbarChrome_SizesThumbsFromVisibleSpan()
    {
        var viewport = SplitViewport();

        var chrome = GridView.CalculateSplitPaneScrollbarChrome(viewport, actualWidth: 500, actualHeight: 300);

        var horizontalAvailable = chrome.HorizontalTopRight!.Track.Width - 2;
        var verticalAvailable = chrome.VerticalBottomLeft!.Track.Height - 2;
        chrome.HorizontalTopRight.Thumb.Width.Should()
            .Be(Math.Max(24, horizontalAvailable * 2 / CellAddress.MaxCol));
        chrome.VerticalBottomLeft.Thumb.Height.Should()
            .Be(Math.Max(24, verticalAvailable * 2 / CellAddress.MaxRow));
    }

    [Fact]
    public void HitTestSplitPaneScrollbar_DetectsThumbTrackAndEmptySpace()
    {
        var viewport = SplitViewport();
        var chrome = GridView.CalculateSplitPaneScrollbarChrome(viewport, actualWidth: 500, actualHeight: 300);

        GridView.HitTestSplitPaneScrollbar(chrome, chrome.HorizontalTopRight!.Thumb.TopLeft + new Vector(2, 2))
            .Should().Be(new SplitPaneScrollbarHit(SplitPaneScrollbarPart.Thumb, SplitPaneScrollbarOrientation.Horizontal, SplitPaneRegion.TopRight));
        GridView.HitTestSplitPaneScrollbar(chrome, chrome.VerticalBottomLeft!.Thumb.TopLeft + new Vector(2, 2))
            .Should().Be(new SplitPaneScrollbarHit(SplitPaneScrollbarPart.Thumb, SplitPaneScrollbarOrientation.Vertical, SplitPaneRegion.BottomLeft));
        GridView.HitTestSplitPaneScrollbar(chrome, new Point(chrome.HorizontalTopRight.Track.Right - 2, chrome.HorizontalTopRight.Track.Top + 2))
            .Should().Be(new SplitPaneScrollbarHit(SplitPaneScrollbarPart.Track, SplitPaneScrollbarOrientation.Horizontal, SplitPaneRegion.TopRight));
        GridView.HitTestSplitPaneScrollbar(chrome, new Point(5, 5))
            .Should().BeNull();
    }

    [Fact]
    public void CalculateSplitPaneScrollbarScrollTarget_MapsTrackPositionToGridIndex()
    {
        var viewport = SplitViewport();
        var chrome = GridView.CalculateSplitPaneScrollbarChrome(viewport, actualWidth: 500, actualHeight: 300);

        GridView.CalculateSplitPaneScrollbarScrollTarget(
                chrome,
                new Point(chrome.HorizontalTopRight!.Track.Left + 1, chrome.HorizontalTopRight.Track.Top + 2))
            .Should().Be(new SplitPaneScrollbarScrollTarget(SplitPaneRegion.TopRight, SplitPaneScrollbarOrientation.Horizontal, 1));
        GridView.CalculateSplitPaneScrollbarScrollTarget(
                chrome,
                new Point(chrome.HorizontalTopRight.Track.Right - 1, chrome.HorizontalTopRight.Track.Top + 2))
            .Should().Be(new SplitPaneScrollbarScrollTarget(SplitPaneRegion.TopRight, SplitPaneScrollbarOrientation.Horizontal, CellAddress.MaxCol - 1));
        GridView.CalculateSplitPaneScrollbarScrollTarget(
                chrome,
                new Point(chrome.VerticalBottomLeft!.Track.Left + 2, chrome.VerticalBottomLeft.Track.Bottom - 1))
            .Should().Be(new SplitPaneScrollbarScrollTarget(SplitPaneRegion.BottomLeft, SplitPaneScrollbarOrientation.Vertical, CellAddress.MaxRow - 1));
    }

    [Fact]
    public void CalculateSplitPaneScrollbarScrollTarget_ClampsToLastValidFirstVisibleIndex()
    {
        var viewport = SplitViewport();
        var chrome = GridView.CalculateSplitPaneScrollbarChrome(viewport, actualWidth: 500, actualHeight: 300);

        GridView.CalculateSplitPaneScrollbarScrollTarget(
                chrome,
                new Point(chrome.HorizontalTopRight!.Track.Right - 1, chrome.HorizontalTopRight.Track.Top + 2))
            .Should().Be(new SplitPaneScrollbarScrollTarget(SplitPaneRegion.TopRight, SplitPaneScrollbarOrientation.Horizontal, CellAddress.MaxCol - 1));
        GridView.CalculateSplitPaneScrollbarScrollTarget(
                chrome,
                new Point(chrome.VerticalBottomLeft!.Track.Left + 2, chrome.VerticalBottomLeft.Track.Bottom - 1))
            .Should().Be(new SplitPaneScrollbarScrollTarget(SplitPaneRegion.BottomLeft, SplitPaneScrollbarOrientation.Vertical, CellAddress.MaxRow - 1));
    }

    [Fact]
    public void CalculateSplitPaneScrollbarInteractionTarget_PagesTrackClicksByVisiblePaneSpan()
    {
        var viewport = SplitViewport();
        var chrome = GridView.CalculateSplitPaneScrollbarChrome(viewport, actualWidth: 500, actualHeight: 300);

        GridView.CalculateSplitPaneScrollbarInteractionTarget(
                viewport,
                chrome,
                new Point(chrome.HorizontalTopRight!.Thumb.Right + 12, chrome.HorizontalTopRight.Track.Top + 2))
            .Should().Be(new SplitPaneScrollbarScrollTarget(SplitPaneRegion.TopRight, SplitPaneScrollbarOrientation.Horizontal, 12));
        GridView.CalculateSplitPaneScrollbarInteractionTarget(
                viewport,
                chrome,
                new Point(chrome.VerticalBottomLeft!.Track.Left + 2, chrome.VerticalBottomLeft.Thumb.Bottom + 12))
            .Should().Be(new SplitPaneScrollbarScrollTarget(SplitPaneRegion.BottomLeft, SplitPaneScrollbarOrientation.Vertical, 22));
    }

    [Fact]
    public void CalculateSplitPaneScrollbarThumbDragTarget_PreservesPointerOffsetInsideThumb()
    {
        var viewport = SplitViewport();
        var chrome = GridView.CalculateSplitPaneScrollbarChrome(viewport, actualWidth: 500, actualHeight: 300);
        var horizontal = chrome.HorizontalTopRight!;
        var vertical = chrome.VerticalBottomLeft!;

        GridView.CalculateSplitPaneScrollbarThumbDragTarget(
                horizontal,
                new Point(horizontal.Thumb.Left + horizontal.Thumb.Width / 2, horizontal.Thumb.Top + 2),
                horizontal.Thumb.Width / 2)
            .Should().Be(new SplitPaneScrollbarScrollTarget(SplitPaneRegion.TopRight, SplitPaneScrollbarOrientation.Horizontal, 10));
        GridView.CalculateSplitPaneScrollbarThumbDragTarget(
                vertical,
                new Point(vertical.Thumb.Left + 2, vertical.Thumb.Top + vertical.Thumb.Height / 2),
                vertical.Thumb.Height / 2)
            .Should().Be(new SplitPaneScrollbarScrollTarget(SplitPaneRegion.BottomLeft, SplitPaneScrollbarOrientation.Vertical, 20));
    }

    [Fact]
    public void CalculateSplitPaneScrollbarWheelTarget_ClampsToLastValidFirstVisibleIndex()
    {
        var viewport = SplitViewport();
        var chrome = GridView.CalculateSplitPaneScrollbarChrome(viewport, actualWidth: 500, actualHeight: 300);

        GridView.CalculateSplitPaneScrollbarWheelTarget(
                chrome.HorizontalTopRight!,
                CellAddress.MaxCol - 2,
                notches: -1)
            .Should().Be(new SplitPaneScrollbarScrollTarget(SplitPaneRegion.TopRight, SplitPaneScrollbarOrientation.Horizontal, CellAddress.MaxCol - 1));
        GridView.CalculateSplitPaneScrollbarWheelTarget(
                chrome.VerticalBottomLeft!,
                CellAddress.MaxRow - 2,
                notches: -1)
            .Should().Be(new SplitPaneScrollbarScrollTarget(SplitPaneRegion.BottomLeft, SplitPaneScrollbarOrientation.Vertical, CellAddress.MaxRow - 1));
    }

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

    [Fact]
    public void CalculateFormulaTraceArrowLayouts_ReturnsCenterPointsForVisibleSameSheetCells()
    {
        var sheetId = SheetId.New();
        var viewport = new ViewportModel(
            [],
            [new RowMetric(1, 20, 0), new RowMetric(2, 20, 20)],
            [new ColMetric(1, 64, 0), new ColMetric(2, 64, 64)],
            null,
            []);

        var arrows = GridView.CalculateFormulaTraceArrowLayouts(
            viewport,
            [new FormulaTraceArrow(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2))],
            sheetId);

        arrows.Should().ContainSingle().Which.Should().Be(
            new FormulaTraceArrowLayout(
                new Point(GridView.RowHeaderWidth + 32, GridView.ColHeaderHeight + 10),
                new Point(GridView.RowHeaderWidth + 64 + 32, GridView.ColHeaderHeight + 20 + 10)));
    }

    [Fact]
    public void FormulaTraceLayoutPlanner_ReturnsCenterPointsOutsideGridView()
    {
        var sheetId = SheetId.New();
        var viewport = new ViewportModel(
            [],
            [new RowMetric(1, 20, 0), new RowMetric(2, 20, 20)],
            [new ColMetric(1, 64, 0), new ColMetric(2, 64, 64)],
            null,
            []);

        var arrows = FormulaTraceLayoutPlanner.CalculateLayouts(
            viewport,
            [new FormulaTraceArrow(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2))],
            sheetId);

        arrows.Should().ContainSingle().Which.Should().Be(
            new FormulaTraceArrowLayout(
                new Point(GridView.RowHeaderWidth + 32, GridView.ColHeaderHeight + 10),
                new Point(GridView.RowHeaderWidth + 64 + 32, GridView.ColHeaderHeight + 20 + 10)));
    }

    [Fact]
    public void CalculateFormulaTraceArrowLayouts_ReturnsMarkersForCrossSheetAndOffscreenCells()
    {
        var sheetId = SheetId.New();
        var otherSheetId = SheetId.New();
        var viewport = new ViewportModel(
            [],
            [new RowMetric(1, 20, 0)],
            [new ColMetric(1, 64, 0)],
            null,
            []);

        var arrows = GridView.CalculateFormulaTraceArrowLayouts(
            viewport,
            [
                new FormulaTraceArrow(new CellAddress(otherSheetId, 1, 1), new CellAddress(sheetId, 1, 1)),
                new FormulaTraceArrow(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 1))
            ],
            sheetId);

        arrows.Should().Equal(
            new FormulaTraceArrowLayout(
                new Point(GridView.RowHeaderWidth + 32, GridView.ColHeaderHeight + 10),
                new Point(GridView.RowHeaderWidth + 32, GridView.ColHeaderHeight + 10),
                FormulaTraceArrowLayoutKind.CrossSheetMarker,
                new CellAddress(otherSheetId, 1, 1)),
            new FormulaTraceArrowLayout(
                new Point(GridView.RowHeaderWidth + 32, GridView.ColHeaderHeight + 10),
                new Point(GridView.RowHeaderWidth + 32, GridView.ColHeaderHeight + 10),
                FormulaTraceArrowLayoutKind.OffscreenMarker,
                new CellAddress(sheetId, 2, 1)));
    }

    [Fact]
    public void HitTestFormulaTraceMarker_ReturnsHiddenCellNavigationTarget()
    {
        var sheetId = SheetId.New();
        var otherSheetId = SheetId.New();
        var viewport = new ViewportModel(
            [],
            [new RowMetric(1, 20, 0)],
            [new ColMetric(1, 64, 0)],
            null,
            []);
        var visible = new CellAddress(sheetId, 1, 1);
        var offscreen = new CellAddress(sheetId, 2, 1);
        var crossSheet = new CellAddress(otherSheetId, 1, 1);
        var markerPoint = new Point(GridView.RowHeaderWidth + 32, GridView.ColHeaderHeight + 10);

        GridView.HitTestFormulaTraceMarker(
                viewport,
                [new FormulaTraceArrow(visible, offscreen)],
                sheetId,
                markerPoint)
            .Should().Be(offscreen);

        GridView.HitTestFormulaTraceMarker(
                viewport,
                [new FormulaTraceArrow(crossSheet, visible)],
                sheetId,
                markerPoint)
            .Should().Be(crossSheet);

        GridView.HitTestFormulaTraceMarker(
                viewport,
                [new FormulaTraceArrow(visible, visible)],
                sheetId,
                markerPoint)
            .Should().BeNull();
    }

    private static DisplayCell Cell(uint row, uint col, string text, CellStyle? style = null) =>
        new(row, col, new TextValue(text), text, null, StyleId.Default, null, style);

    private static ViewportModel SplitViewport() =>
        new(
            [],
            [new RowMetric(20, 18, 0), new RowMetric(21, 18, 18)],
            [new ColMetric(10, 64, 0), new ColMetric(11, 64, 64)],
            SplitPanes: new SplitPaneState(
                4,
                4,
                [new RowMetric(1, 18, 0), new RowMetric(2, 22, 18), new RowMetric(3, 18, 40)],
                [new ColMetric(1, 64, 0), new ColMetric(2, 80, 64), new ColMetric(3, 64, 144)]));
}

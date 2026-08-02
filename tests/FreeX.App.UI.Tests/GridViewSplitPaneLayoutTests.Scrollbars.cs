using FluentAssertions;
using FreeX.App.Presentation.GridInteraction;
using FreeX.App.UI;
using FreeX.Core.Model;
using System.Windows;

namespace FreeX.App.UI.Tests;

public sealed partial class GridViewSplitPaneLayoutTests
{
    [Fact]
    public void WpfSplitChromeAndDividerAreAdaptersOfThePortablePlanner()
    {
        var viewport = SplitViewport();
        var rowHeaderWidth = GridView.CalculateRowHeaderWidth(viewport);
        var sharedDivider = SplitPanePointerPlanner.CalculateDividerLayout(
            viewport,
            rowHeaderWidth,
            GridView.ColHeaderHeight);
        var wpfDivider = GridView.CalculateSplitDividerLayout(viewport);
        var sharedChrome = SplitPanePointerPlanner.CalculateScrollbarChrome(
            viewport,
            actualWidth: 500,
            actualHeight: 300,
            rowHeaderWidth: rowHeaderWidth,
            columnHeaderHeight: GridView.ColHeaderHeight);
        var wpfChrome = GridView.CalculateSplitPaneScrollbarChrome(viewport, 500, 300);

        wpfDivider.HorizontalY.Should().Be(sharedDivider.HorizontalY);
        wpfDivider.VerticalX.Should().Be(sharedDivider.VerticalX);
        wpfChrome.HorizontalTopRight!.Value.Track.Should().Be(ToRect(sharedChrome.HorizontalTopRight!.Value.Track));
        wpfChrome.HorizontalTopRight.Value.Thumb.Should().Be(ToRect(sharedChrome.HorizontalTopRight.Value.Thumb));
        wpfChrome.VerticalBottomLeft!.Value.Track.Should().Be(ToRect(sharedChrome.VerticalBottomLeft!.Value.Track));
        wpfChrome.VerticalBottomLeft.Value.Thumb.Should().Be(ToRect(sharedChrome.VerticalBottomLeft.Value.Thumb));
    }

    [Fact]
    public void CalculateSplitPaneScrollbarChrome_AddsIndependentPaneTracksAndThumbs()
    {
        var viewport = SplitViewport();

        var chrome = GridView.CalculateSplitPaneScrollbarChrome(viewport, actualWidth: 500, actualHeight: 300);

        chrome.HorizontalTopRight.Should().NotBeNull();
        var horizontal = chrome.HorizontalTopRight!.Value;
        horizontal.Track.Should().Be(new Rect(GridView.RowHeaderWidth + 208, GridView.ColHeaderHeight + 58 - 10, 262, 10));
        horizontal.Thumb.Width.Should().BeGreaterThanOrEqualTo(24);
        horizontal.Thumb.Y.Should().Be(horizontal.Track.Y + 1);
        chrome.VerticalBottomLeft.Should().NotBeNull();
        var vertical = chrome.VerticalBottomLeft!.Value;
        vertical.Track.Should().Be(new Rect(GridView.RowHeaderWidth + 208 - 10, GridView.ColHeaderHeight + 58, 10, 224));
        vertical.Thumb.Height.Should().BeGreaterThanOrEqualTo(24);
        vertical.Thumb.X.Should().Be(vertical.Track.X + 1);
    }

    private static Rect ToRect(GridRect rect) => new(rect.Left, rect.Top, rect.Width, rect.Height);

    [Fact]
    public void SplitPaneViewportChrome_CalculatesScrollbarChromeOutsideGridView()
    {
        var viewport = SplitViewport();

        var chrome = SplitPaneViewportChrome.CalculateScrollbarChrome(viewport, actualWidth: 500, actualHeight: 300);

        chrome.HorizontalTopRight!.Value.Track.Should().Be(new Rect(GridView.RowHeaderWidth + 208, GridView.ColHeaderHeight + 58 - 10, 262, 10));
        chrome.VerticalBottomLeft!.Value.Track.Should().Be(new Rect(GridView.RowHeaderWidth + 208 - 10, GridView.ColHeaderHeight + 58, 10, 224));
    }

    [Fact]
    public void CalculateSplitPaneScrollbarChrome_SuppressesCollapsedTracks()
    {
        var viewport = SplitViewport();

        var chrome = SplitPaneViewportChrome.CalculateScrollbarChrome(
            viewport,
            actualWidth: GridView.RowHeaderWidth + 208,
            actualHeight: GridView.ColHeaderHeight + 58);

        chrome.HorizontalTopRight.Should().BeNull();
        chrome.VerticalBottomLeft.Should().BeNull();
    }

    [Fact]
    public void SplitPaneScrollbarLayoutPlanner_MapsThumbHitAndDragMath()
    {
        var scrollbar = new SplitPaneScrollbar(
            SplitPaneScrollbarOrientation.Horizontal,
            SplitPaneRegion.TopRight,
            new Rect(100, 20, 200, 10),
            SplitPaneScrollbarLayoutPlanner.CalculateThumb(
                SplitPaneScrollbarOrientation.Horizontal,
                new Rect(100, 20, 200, 10),
                firstVisibleIndex: 50,
                visibleCount: 10,
                maxIndex: 200),
            VisibleSpan: 10,
            MaxStartIndex: 191);

        SplitPaneScrollbarLayoutPlanner.HitTestScrollbar(scrollbar, scrollbar.Thumb.TopLeft + new Vector(2, 2))
            .Should().Be(new SplitPaneScrollbarHit(SplitPaneScrollbarPart.Thumb, SplitPaneScrollbarOrientation.Horizontal, SplitPaneRegion.TopRight));
        SplitPaneScrollbarLayoutPlanner.CalculateScrollTarget(scrollbar, new Point(scrollbar.Track.Right - 1, scrollbar.Track.Top + 2))
            .Should().Be(new SplitPaneScrollbarScrollTarget(SplitPaneRegion.TopRight, SplitPaneScrollbarOrientation.Horizontal, 191));
        SplitPaneScrollbarLayoutPlanner.CalculatePageTarget(scrollbar, currentIndex: 50, new Point(scrollbar.Thumb.Left - 4, scrollbar.Track.Top + 2))
            .Should().Be(new SplitPaneScrollbarScrollTarget(SplitPaneRegion.TopRight, SplitPaneScrollbarOrientation.Horizontal, 40));
        SplitPaneScrollbarLayoutPlanner.CalculateThumbDragTarget(
                scrollbar,
                new Point(scrollbar.Track.Left + 1 + 99 + scrollbar.Thumb.Width / 2, scrollbar.Track.Top + 2),
                scrollbar.Thumb.Width / 2)
            .Should().Be(new SplitPaneScrollbarScrollTarget(SplitPaneRegion.TopRight, SplitPaneScrollbarOrientation.Horizontal, 109));
    }

    [Fact]
    public void SplitPaneScrollbarLayoutPlanner_IncludesThumbAndTrackHitBoundaries()
    {
        var scrollbar = new SplitPaneScrollbar(
            SplitPaneScrollbarOrientation.Horizontal,
            SplitPaneRegion.TopRight,
            new Rect(100, 20, 200, 10),
            new Rect(120, 21, 30, 8),
            VisibleSpan: 10,
            MaxStartIndex: 191);

        SplitPaneScrollbarLayoutPlanner.HitTestScrollbar(scrollbar, new Point(scrollbar.Thumb.Right, scrollbar.Thumb.Bottom))
            .Should().Be(new SplitPaneScrollbarHit(SplitPaneScrollbarPart.Thumb, SplitPaneScrollbarOrientation.Horizontal, SplitPaneRegion.TopRight));
        SplitPaneScrollbarLayoutPlanner.HitTestScrollbar(scrollbar, new Point(scrollbar.Track.Right, scrollbar.Track.Bottom))
            .Should().Be(new SplitPaneScrollbarHit(SplitPaneScrollbarPart.Track, SplitPaneScrollbarOrientation.Horizontal, SplitPaneRegion.TopRight));
        SplitPaneScrollbarLayoutPlanner.CalculateScrollTarget(scrollbar, new Point(scrollbar.Track.Right, scrollbar.Track.Bottom))
            .Should().Be(new SplitPaneScrollbarScrollTarget(SplitPaneRegion.TopRight, SplitPaneScrollbarOrientation.Horizontal, 191));
    }

    [Fact]
    public void SplitPaneScrollbarLayoutPlanner_IgnoresCollapsedTracks()
    {
        var scrollbar = new SplitPaneScrollbar(
            SplitPaneScrollbarOrientation.Horizontal,
            SplitPaneRegion.TopRight,
            new Rect(100, 20, 0, 10),
            new Rect(100, 21, 0, 8),
            VisibleSpan: 10,
            MaxStartIndex: 191);
        var point = new Point(scrollbar.Track.Left, scrollbar.Track.Top + 2);

        SplitPaneScrollbarLayoutPlanner.HitTestScrollbar(scrollbar, point)
            .Should().BeNull();
        SplitPaneScrollbarLayoutPlanner.CalculateScrollTarget(scrollbar, point)
            .Should().BeNull();
    }

    [Fact]
    public void SplitPaneScrollbarLayoutPlanner_ClampsThumbToTrackWhenFirstVisibleExceedsLastStart()
    {
        var track = new Rect(100, 20, 200, 10);

        var thumb = SplitPaneScrollbarLayoutPlanner.CalculateThumb(
            SplitPaneScrollbarOrientation.Horizontal,
            track,
            firstVisibleIndex: 500,
            visibleCount: 10,
            maxIndex: 200);

        thumb.Left.Should().BeGreaterThanOrEqualTo(track.Left + 1);
        thumb.Right.Should().BeLessThanOrEqualTo(track.Right - 1);
    }

    [Fact]
    public void SplitPaneScrollbarLayoutPlanner_ClampsVisibleSpanToRangeWhenCalculatingThumb()
    {
        var track = new Rect(100, 20, 200, 10);

        var thumb = SplitPaneScrollbarLayoutPlanner.CalculateThumb(
            SplitPaneScrollbarOrientation.Horizontal,
            track,
            firstVisibleIndex: 50,
            visibleCount: 500,
            maxIndex: 200);

        thumb.Should().Be(new Rect(track.Left + 1, track.Top + 1, track.Width - 2, track.Height - 2));
    }

    [Fact]
    public void CalculateSplitPaneScrollbarChrome_SizesThumbsFromVisibleSpan()
    {
        var viewport = SplitViewport();

        var chrome = GridView.CalculateSplitPaneScrollbarChrome(viewport, actualWidth: 500, actualHeight: 300);

        var horizontal = chrome.HorizontalTopRight!.Value;
        var vertical = chrome.VerticalBottomLeft!.Value;
        var horizontalAvailable = horizontal.Track.Width - 2;
        var verticalAvailable = vertical.Track.Height - 2;
        horizontal.Thumb.Width.Should()
            .Be(Math.Max(24, horizontalAvailable * 2 / CellAddress.MaxCol));
        vertical.Thumb.Height.Should()
            .Be(Math.Max(24, verticalAvailable * 2 / CellAddress.MaxRow));
    }

    [Fact]
    public void HitTestSplitPaneScrollbar_DetectsThumbTrackAndEmptySpace()
    {
        var viewport = SplitViewport();
        var chrome = GridView.CalculateSplitPaneScrollbarChrome(viewport, actualWidth: 500, actualHeight: 300);

        var horizontal = chrome.HorizontalTopRight!.Value;
        var vertical = chrome.VerticalBottomLeft!.Value;

        GridView.HitTestSplitPaneScrollbar(chrome, horizontal.Thumb.TopLeft + new Vector(2, 2))
            .Should().Be(new SplitPaneScrollbarHit(SplitPaneScrollbarPart.Thumb, SplitPaneScrollbarOrientation.Horizontal, SplitPaneRegion.TopRight));
        GridView.HitTestSplitPaneScrollbar(chrome, vertical.Thumb.TopLeft + new Vector(2, 2))
            .Should().Be(new SplitPaneScrollbarHit(SplitPaneScrollbarPart.Thumb, SplitPaneScrollbarOrientation.Vertical, SplitPaneRegion.BottomLeft));
        GridView.HitTestSplitPaneScrollbar(chrome, new Point(horizontal.Track.Right - 2, horizontal.Track.Top + 2))
            .Should().Be(new SplitPaneScrollbarHit(SplitPaneScrollbarPart.Track, SplitPaneScrollbarOrientation.Horizontal, SplitPaneRegion.TopRight));
        GridView.HitTestSplitPaneScrollbar(chrome, new Point(5, 5))
            .Should().BeNull();
    }

    [Fact]
    public void HitTestSplitPaneScrollbar_IncludesRenderedThumbAndTrackBoundaries()
    {
        var viewport = SplitViewport();
        var chrome = GridView.CalculateSplitPaneScrollbarChrome(viewport, actualWidth: 500, actualHeight: 300);
        var horizontal = chrome.HorizontalTopRight!.Value;
        var vertical = chrome.VerticalBottomLeft!.Value;

        GridView.HitTestSplitPaneScrollbar(chrome, horizontal.Thumb.BottomRight)
            .Should().Be(new SplitPaneScrollbarHit(SplitPaneScrollbarPart.Thumb, SplitPaneScrollbarOrientation.Horizontal, SplitPaneRegion.TopRight));
        GridView.HitTestSplitPaneScrollbar(chrome, vertical.Thumb.BottomRight)
            .Should().Be(new SplitPaneScrollbarHit(SplitPaneScrollbarPart.Thumb, SplitPaneScrollbarOrientation.Vertical, SplitPaneRegion.BottomLeft));
        GridView.HitTestSplitPaneScrollbar(chrome, horizontal.Track.BottomRight)
            .Should().Be(new SplitPaneScrollbarHit(SplitPaneScrollbarPart.Track, SplitPaneScrollbarOrientation.Horizontal, SplitPaneRegion.TopRight));
        GridView.HitTestSplitPaneScrollbar(chrome, vertical.Track.BottomRight)
            .Should().Be(new SplitPaneScrollbarHit(SplitPaneScrollbarPart.Track, SplitPaneScrollbarOrientation.Vertical, SplitPaneRegion.BottomLeft));
    }

    [Fact]
    public void CalculateSplitPaneScrollbarScrollTarget_MapsTrackPositionToGridIndex()
    {
        var viewport = SplitViewport();
        var chrome = GridView.CalculateSplitPaneScrollbarChrome(viewport, actualWidth: 500, actualHeight: 300);

        GridView.CalculateSplitPaneScrollbarScrollTarget(
                chrome,
                new Point(chrome.HorizontalTopRight!.Value.Track.Left + 1, chrome.HorizontalTopRight.Value.Track.Top + 2))
            .Should().Be(new SplitPaneScrollbarScrollTarget(SplitPaneRegion.TopRight, SplitPaneScrollbarOrientation.Horizontal, 1));
        GridView.CalculateSplitPaneScrollbarScrollTarget(
                chrome,
                new Point(chrome.HorizontalTopRight.Value.Track.Right - 1, chrome.HorizontalTopRight.Value.Track.Top + 2))
            .Should().Be(new SplitPaneScrollbarScrollTarget(SplitPaneRegion.TopRight, SplitPaneScrollbarOrientation.Horizontal, CellAddress.MaxCol - 1));
        GridView.CalculateSplitPaneScrollbarScrollTarget(
                chrome,
                new Point(chrome.VerticalBottomLeft!.Value.Track.Left + 2, chrome.VerticalBottomLeft.Value.Track.Bottom - 1))
            .Should().Be(new SplitPaneScrollbarScrollTarget(SplitPaneRegion.BottomLeft, SplitPaneScrollbarOrientation.Vertical, CellAddress.MaxRow - 1));
    }

    [Fact]
    public void CalculateSplitPaneScrollbarScrollTarget_ClampsToLastValidFirstVisibleIndex()
    {
        var viewport = SplitViewport();
        var chrome = GridView.CalculateSplitPaneScrollbarChrome(viewport, actualWidth: 500, actualHeight: 300);

        GridView.CalculateSplitPaneScrollbarScrollTarget(
                chrome,
                new Point(chrome.HorizontalTopRight!.Value.Track.Right - 1, chrome.HorizontalTopRight.Value.Track.Top + 2))
            .Should().Be(new SplitPaneScrollbarScrollTarget(SplitPaneRegion.TopRight, SplitPaneScrollbarOrientation.Horizontal, CellAddress.MaxCol - 1));
        GridView.CalculateSplitPaneScrollbarScrollTarget(
                chrome,
                new Point(chrome.VerticalBottomLeft!.Value.Track.Left + 2, chrome.VerticalBottomLeft.Value.Track.Bottom - 1))
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
                new Point(chrome.HorizontalTopRight!.Value.Thumb.Right + 12, chrome.HorizontalTopRight.Value.Track.Top + 2))
            .Should().Be(new SplitPaneScrollbarScrollTarget(SplitPaneRegion.TopRight, SplitPaneScrollbarOrientation.Horizontal, 12));
        GridView.CalculateSplitPaneScrollbarInteractionTarget(
                viewport,
                chrome,
                new Point(chrome.VerticalBottomLeft!.Value.Track.Left + 2, chrome.VerticalBottomLeft.Value.Thumb.Bottom + 12))
            .Should().Be(new SplitPaneScrollbarScrollTarget(SplitPaneRegion.BottomLeft, SplitPaneScrollbarOrientation.Vertical, 22));
    }

    [Fact]
    public void CalculateSplitPaneScrollbarInteractionTarget_DoesNotJumpScrollOnThumbMouseDown()
    {
        var viewport = SplitViewport();
        var chrome = GridView.CalculateSplitPaneScrollbarChrome(viewport, actualWidth: 500, actualHeight: 300);

        GridView.CalculateSplitPaneScrollbarInteractionTarget(
                viewport,
                chrome,
                chrome.HorizontalTopRight!.Value.Thumb.TopLeft + new Vector(2, 2))
            .Should().BeNull();
        GridView.CalculateSplitPaneScrollbarInteractionTarget(
                viewport,
                chrome,
                chrome.VerticalBottomLeft!.Value.Thumb.TopLeft + new Vector(2, 2))
            .Should().BeNull();
    }

    [Fact]
    public void CalculateSplitPaneScrollbarInteractionTarget_ReusesKnownTrackHit()
    {
        var viewport = SplitViewport();
        var chrome = GridView.CalculateSplitPaneScrollbarChrome(viewport, actualWidth: 500, actualHeight: 300);
        var horizontal = chrome.HorizontalTopRight!.Value;
        var pos = new Point(horizontal.Thumb.Right + 12, horizontal.Track.Top + 2);
        var hit = new SplitPaneScrollbarHit(
            SplitPaneScrollbarPart.Track,
            SplitPaneScrollbarOrientation.Horizontal,
            SplitPaneRegion.TopRight);

        GridView.CalculateSplitPaneScrollbarInteractionTarget(viewport, chrome, hit, pos)
            .Should().Be(new SplitPaneScrollbarScrollTarget(SplitPaneRegion.TopRight, SplitPaneScrollbarOrientation.Horizontal, 12));
    }

    [Fact]
    public void CalculateSplitPaneScrollbarThumbDragTarget_PreservesPointerOffsetInsideThumb()
    {
        var viewport = SplitViewport();
        var chrome = GridView.CalculateSplitPaneScrollbarChrome(viewport, actualWidth: 500, actualHeight: 300);
        var horizontal = chrome.HorizontalTopRight!.Value;
        var vertical = chrome.VerticalBottomLeft!.Value;

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
                chrome.HorizontalTopRight!.Value,
                CellAddress.MaxCol - 2,
                notches: -1)
            .Should().Be(new SplitPaneScrollbarScrollTarget(SplitPaneRegion.TopRight, SplitPaneScrollbarOrientation.Horizontal, CellAddress.MaxCol - 1));
        GridView.CalculateSplitPaneScrollbarWheelTarget(
                chrome.VerticalBottomLeft!.Value,
                CellAddress.MaxRow - 2,
                notches: -1)
            .Should().Be(new SplitPaneScrollbarScrollTarget(SplitPaneRegion.BottomLeft, SplitPaneScrollbarOrientation.Vertical, CellAddress.MaxRow - 1));
    }

    [Fact]
    public void ResolveSplitPaneWheelTarget_PrefersMiniScrollbarAxisOverCellRegionFallback()
    {
        var sheetId = SheetId.New();
        var viewport = SplitViewport();
        var chrome = GridView.CalculateSplitPaneScrollbarChrome(viewport, actualWidth: 500, actualHeight: 300);

        GridView.ResolveSplitPaneWheelTarget(
                viewport,
                sheetId,
                new Point(chrome.HorizontalTopRight!.Value.Track.Left + 2, chrome.HorizontalTopRight.Value.Track.Top + 2),
                actualWidth: 500,
                actualHeight: 300,
                requestedHorizontal: false)
            .Should().Be(new SplitPaneWheelTarget(SplitPaneRegion.TopRight, Horizontal: true));

        GridView.ResolveSplitPaneWheelTarget(
                viewport,
                sheetId,
                new Point(chrome.VerticalBottomLeft!.Value.Track.Left + 2, chrome.VerticalBottomLeft.Value.Track.Top + 2),
                actualWidth: 500,
                actualHeight: 300,
                requestedHorizontal: true)
            .Should().Be(new SplitPaneWheelTarget(SplitPaneRegion.BottomLeft, Horizontal: false));
    }
}

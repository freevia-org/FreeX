using Freexcel.Core.Model;
using System.Windows;

namespace Freexcel.App.UI;

public static class SplitPaneScrollbarLayoutPlanner
{
    public const double Thickness = 10;
    public const double MinThumbLength = 24;

    public static Rect CalculateThumb(
        SplitPaneScrollbarOrientation orientation,
        Rect track,
        uint firstVisibleIndex,
        int visibleCount,
        uint maxIndex)
    {
        var trackLength = Math.Max(0, TrackLength(orientation, track) - 2);
        var thumbLength = Math.Min(
            trackLength,
            Math.Max(MinThumbLength, trackLength * Math.Max(1, visibleCount) / maxIndex));
        var available = Math.Max(0, TrackLength(orientation, track) - thumbLength - 2);
        var maxStartIndex = Math.Max(1, maxIndex - (uint)Math.Max(1, visibleCount) + 1);
        var ratio = maxStartIndex <= 1
            ? 0
            : (double)(Math.Max(1, firstVisibleIndex) - 1) / (maxStartIndex - 1);

        return orientation == SplitPaneScrollbarOrientation.Horizontal
            ? new Rect(track.X + 1 + available * ratio, track.Y + 1, thumbLength, Math.Max(0, track.Height - 2))
            : new Rect(track.X + 1, track.Y + 1 + available * ratio, Math.Max(0, track.Width - 2), thumbLength);
    }

    public static SplitPaneScrollbarHit? HitTestScrollbar(SplitPaneScrollbar? scrollbar, Point pos)
    {
        if (scrollbar is null || !scrollbar.Track.Contains(pos))
            return null;

        var part = scrollbar.Thumb.Contains(pos)
            ? SplitPaneScrollbarPart.Thumb
            : SplitPaneScrollbarPart.Track;
        return new SplitPaneScrollbarHit(part, scrollbar.Orientation, scrollbar.Region);
    }

    public static SplitPaneScrollbarScrollTarget? CalculateScrollTarget(
        SplitPaneScrollbar? scrollbar,
        Point pos)
    {
        if (scrollbar is null || !scrollbar.Track.Contains(pos))
            return null;

        var index = IndexFromTrackPosition(scrollbar, TrackPosition(scrollbar.Orientation, pos));
        return new SplitPaneScrollbarScrollTarget(scrollbar.Region, scrollbar.Orientation, index);
    }

    public static SplitPaneScrollbarScrollTarget CalculateThumbDragTarget(
        SplitPaneScrollbar scrollbar,
        Point pos,
        double pointerOffset)
    {
        var index = IndexFromTrackPosition(
            scrollbar,
            TrackPosition(scrollbar.Orientation, pos) - pointerOffset);
        return new SplitPaneScrollbarScrollTarget(scrollbar.Region, scrollbar.Orientation, index);
    }

    public static SplitPaneScrollbarScrollTarget CalculateWheelTarget(
        SplitPaneScrollbar scrollbar,
        uint currentIndex,
        int notches,
        uint step = 3)
    {
        var next = (long)Math.Max(1, currentIndex) - (long)notches * step;
        var clamped = (uint)Math.Max(1, Math.Min(scrollbar.MaxStartIndex, next));
        return new SplitPaneScrollbarScrollTarget(scrollbar.Region, scrollbar.Orientation, clamped);
    }

    public static SplitPaneScrollbarScrollTarget CalculatePageTarget(
        SplitPaneScrollbar scrollbar,
        uint currentIndex,
        Point pos)
    {
        var page = (uint)Math.Max(1, scrollbar.VisibleSpan);
        var beforeThumb = TrackPosition(scrollbar.Orientation, pos) < TrackStart(scrollbar.Orientation, scrollbar.Thumb);
        var next = beforeThumb
            ? currentIndex > page ? currentIndex - page : 1
            : Math.Min(scrollbar.MaxStartIndex, currentIndex + page);
        return new SplitPaneScrollbarScrollTarget(scrollbar.Region, scrollbar.Orientation, next);
    }

    private static uint IndexFromTrackPosition(SplitPaneScrollbar scrollbar, double position)
    {
        var available = Math.Max(1, TrackLength(scrollbar.Orientation, scrollbar.Track) - TrackLength(scrollbar.Orientation, scrollbar.Thumb) - 2);
        var ratio = Math.Max(0, Math.Min(1, (position - TrackStart(scrollbar.Orientation, scrollbar.Track) - 1) / available));
        return (uint)Math.Max(1, Math.Min(scrollbar.MaxStartIndex, 1 + Math.Round(ratio * (scrollbar.MaxStartIndex - 1))));
    }

    private static double TrackStart(SplitPaneScrollbarOrientation orientation, Rect rect) =>
        orientation == SplitPaneScrollbarOrientation.Horizontal ? rect.Left : rect.Top;

    private static double TrackLength(SplitPaneScrollbarOrientation orientation, Rect rect) =>
        orientation == SplitPaneScrollbarOrientation.Horizontal ? rect.Width : rect.Height;

    private static double TrackPosition(SplitPaneScrollbarOrientation orientation, Point pos) =>
        orientation == SplitPaneScrollbarOrientation.Horizontal ? pos.X : pos.Y;
}

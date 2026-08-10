using FreeX.App.Presentation.GridInteraction;
using FreeX.Core.Model;
using System.Windows;

namespace FreeX.App.UI;

public static class SplitPaneScrollbarLayoutPlanner
{
    public const double Thickness = SplitPanePointerPlanner.ScrollbarThickness;
    public const double MinThumbLength = SplitPanePointerPlanner.ScrollbarMinThumbLength;

    /// <summary>
    /// R76-render-freeze-scroll-4-2: default rows/cols scrolled per wheel notch when no explicit
    /// step is supplied. Named (rather than a bare literal) so it stays in lockstep with the main
    /// scrollbar's own default (WorkbookViewportScrollPlanner.DefaultWheelScrollLinesPerNotch in the
    /// WPF host, which additionally honors the live OS "wheel scroll lines" setting where this
    /// split-pane-scrollbar-specific path currently does not).
    /// </summary>
    public const uint DefaultWheelScrollStep = 3;

    public static Rect CalculateThumb(
        SplitPaneScrollbarOrientation orientation,
        Rect track,
        uint firstVisibleIndex,
        int visibleCount,
        uint maxIndex)
    {
        return ToRect(SplitPanePointerPlanner.CalculateThumb(
            ToGridRect(track),
            orientation == SplitPaneScrollbarOrientation.Horizontal,
            firstVisibleIndex,
            visibleCount,
            maxIndex));
    }

    public static SplitPaneScrollbarHit? HitTestScrollbar(SplitPaneScrollbar? scrollbar, Point pos)
    {
        if (scrollbar is not { } value || !IsRenderableTrack(value.Track))
            return null;

        var hit = SplitPanePointerPlanner.HitTestScrollbar(
            ToChrome(value),
            new GridPoint(pos.X, pos.Y));
        return hit is { } sharedHit
            ? new SplitPaneScrollbarHit(
                (SplitPaneScrollbarPart)sharedHit.Part,
                (SplitPaneScrollbarOrientation)sharedHit.Orientation,
                (SplitPaneRegion)sharedHit.Region)
            : null;
    }

    public static SplitPaneScrollbarScrollTarget? CalculateScrollTarget(
        SplitPaneScrollbar? scrollbar,
        Point pos)
    {
        if (scrollbar is not { } value ||
            !IsRenderableTrack(value.Track) ||
            !RectHitTest.ContainsInclusive(value.Track, pos))
            return null;

        var target = SplitPanePointerPlanner.CalculateTrackTarget(
            ToShared(value),
            new GridPoint(pos.X, pos.Y));
        return ToWpf(target);
    }

    public static SplitPaneScrollbarScrollTarget CalculateThumbDragTarget(
        SplitPaneScrollbar scrollbar,
        Point pos,
        double pointerOffset)
    {
        return ToWpfRequired(SplitPanePointerPlanner.CalculateThumbDragTarget(
            ToShared(scrollbar),
            new GridPoint(pos.X, pos.Y),
            pointerOffset));
    }

    public static SplitPaneScrollbarScrollTarget CalculateWheelTarget(
        SplitPaneScrollbar scrollbar,
        uint currentIndex,
        int notches,
        uint step = DefaultWheelScrollStep)
    {
        return ToWpfRequired(SplitPanePointerPlanner.CalculateWheelTarget(
            ToShared(scrollbar), currentIndex, notches, step));
    }

    public static SplitPaneScrollbarScrollTarget CalculatePageTarget(
        SplitPaneScrollbar scrollbar,
        uint currentIndex,
        Point pos)
    {
        return ToWpfRequired(SplitPanePointerPlanner.CalculatePageTarget(
            ToShared(scrollbar),
            currentIndex,
            new GridPoint(pos.X, pos.Y)));
    }

    private static SplitPanePointerScrollbarChrome ToChrome(SplitPaneScrollbar scrollbar) =>
        scrollbar.Orientation == SplitPaneScrollbarOrientation.Horizontal
            ? new SplitPanePointerScrollbarChrome(ToShared(scrollbar), null)
            : new SplitPanePointerScrollbarChrome(null, ToShared(scrollbar));

    private static SplitPanePointerScrollbar ToShared(SplitPaneScrollbar scrollbar) =>
        new(
            (SplitPanePointerScrollbarOrientation)scrollbar.Orientation,
            (SplitPanePointerRegion)scrollbar.Region,
            ToGridRect(scrollbar.Track),
            ToGridRect(scrollbar.Thumb),
            scrollbar.VisibleSpan,
            scrollbar.MaxStartIndex);

    private static SplitPaneScrollbarScrollTarget? ToWpf(SplitPanePointerScrollTarget? target) =>
        target is { } value
            ? new SplitPaneScrollbarScrollTarget(
                (SplitPaneRegion)value.Region,
                (SplitPaneScrollbarOrientation)value.Orientation,
                value.Index)
            : null;

    private static SplitPaneScrollbarScrollTarget ToWpfRequired(SplitPanePointerScrollTarget target) =>
        new(
            (SplitPaneRegion)target.Region,
            (SplitPaneScrollbarOrientation)target.Orientation,
            target.Index);

    private static Rect ToRect(GridRect rect) => new(rect.Left, rect.Top, rect.Width, rect.Height);

    private static GridRect ToGridRect(Rect rect) => new(rect.Left, rect.Top, rect.Width, rect.Height);

    private static bool IsRenderableTrack(Rect rect) =>
        rect.Width > 0 && rect.Height > 0;

}

using System.Globalization;

using FreeX.App.Presentation.Charts;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.SlicerTimeline;

/// <summary>
/// Pure, UI-free glue that turns a pointer hit on a laid-out slicer or timeline into the matching
/// Core.Commands mutation. No renderer types: the shell supplies the portable layout model plus the
/// hit point (in the same pixel space the layout was built in) and consumes the returned
/// <see cref="IWorkbookCommand"/> through the session's command path. The tests exercise the
/// tile-toggle and range-hit mappings without a running UI.
/// </summary>
public static class SlicerTimelineInteractionPlanner
{
    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>
    /// Hit-tests <paramref name="point"/> against a slicer's tiles and, when a non-preview tile is
    /// hit, builds the <see cref="SetSlicerSelectionCommand"/> that toggles it. Returns null when the
    /// point misses every tile or lands on the synthetic "all" preview tile (which has no item to
    /// toggle). The new selection is computed by the portable <see cref="SlicerLayoutBuilder.Toggle"/>.
    /// <paramref name="additive"/> mirrors Excel's Ctrl+click semantics: false (the default, a plain
    /// click) replaces the whole selection with just the clicked tile; true toggles the tile's
    /// membership in the existing selection instead.
    /// </summary>
    public static SetSlicerSelectionCommand? BuildSlicerToggleCommand(
        SlicerModel slicer,
        IEnumerable<string> availableItems,
        SlicerLayoutModel layout,
        LayoutPoint point,
        bool additive = false)
    {
        ArgumentNullException.ThrowIfNull(slicer);
        ArgumentNullException.ThrowIfNull(availableItems);
        ArgumentNullException.ThrowIfNull(layout);

        var items = availableItems as IReadOnlyCollection<string> ?? availableItems.ToList();
        if (SlicerLayoutBuilder.HitTest(layout, point) is not { IsAllPreview: false } tile)
            return null;

        var toggle = SlicerLayoutBuilder.Toggle(slicer, items, tile.Caption, additive);
        return new SetSlicerSelectionCommand(slicer.Name, toggle.SelectedItems.ToList());
    }

    /// <summary>
    /// Hit-tests <paramref name="point"/> against a timeline and builds the
    /// <see cref="SetTimelineRangeCommand"/> that applies the resulting range. A hit on a handle moves
    /// just that boundary (keeping the other end of the current selection); a hit on the track or
    /// selection jumps the whole range to the bucket under the pointer. Returns null when the point
    /// misses the timeline or no date can be mapped (the full range is unknown).
    /// </summary>
    public static SetTimelineRangeCommand? BuildTimelineRangeCommand(
        TimelineModel timeline,
        TimelineLayoutModel layout,
        LayoutPoint point)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(layout);

        var hit = TimelineLayoutBuilder.HitTest(layout, point);
        if (hit.Kind == TimelineHitKind.None || hit.Date is not { } date)
            return null;

        var (start, end) = ResolveRange(layout, hit.Kind, date);
        if (start > end)
            (start, end) = (end, start);

        return new SetTimelineRangeCommand(timeline.Name, Format(start), Format(end));
    }

    private static (DateOnly Start, DateOnly End) ResolveRange(
        TimelineLayoutModel layout,
        TimelineHitKind kind,
        DateOnly date)
    {
        // Delegate to the shared Presentation-layer resolver so every renderer uses the same math.
        var (start, end) = SlicerTimelineHitDateResolver.ResolveRange(layout, kind, date);
        // The resolver can return null only when hitDate was null — we pass a concrete date, so
        // both are always non-null here. Fall back to (date, date) as a defensive guard.
        return (start ?? date, end ?? date);
    }

    // ── Clear-filter ────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a <see cref="SetSlicerSelectionCommand"/> that clears the slicer's filter (selects
    /// all items), or <c>null</c> when the point does not hit <see cref="SlicerLayoutModel.ClearFilterIconRect"/>
    /// or when the slicer has no active filter (nothing to clear). Use this before the tile hit-test
    /// so clicking the clear icon does not also toggle a tile.
    /// </summary>
    public static SetSlicerSelectionCommand? BuildSlicerClearFilterCommand(
        SlicerModel slicer,
        SlicerLayoutModel layout,
        LayoutPoint point)
    {
        ArgumentNullException.ThrowIfNull(slicer);
        ArgumentNullException.ThrowIfNull(layout);

        if (!layout.HasActiveFilter)
            return null;
        if (!Contains(layout.ClearFilterIconRect, point))
            return null;

        // Empty selected-items list = "no filter" in SetSlicerSelectionCommand.Apply.
        return new SetSlicerSelectionCommand(slicer.Name, []);
    }

    /// <summary>
    /// Builds a <see cref="SetTimelineRangeCommand"/> that clears the timeline's date filter (both
    /// bounds to null), or <c>null</c> when the point does not hit
    /// <see cref="TimelineLayoutModel.ClearFilterIconRect"/> or there is no active filter.
    /// </summary>
    public static SetTimelineRangeCommand? BuildTimelineClearFilterCommand(
        TimelineModel timeline,
        TimelineLayoutModel layout,
        LayoutPoint point)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(layout);

        if (!layout.HasActiveFilter)
            return null;
        if (!Contains(layout.ClearFilterIconRect, point))
            return null;

        return new SetTimelineRangeCommand(timeline.Name, null, null);
    }

    // ── Granularity dropdown ─────────────────────────────────────────────────────

    /// <summary>
    /// Builds a <see cref="SetTimelineGranularityCommand"/> that cycles the timeline's display
    /// granularity one step (Years → Quarters → Months → Days → Years), or <c>null</c> when the
    /// point does not hit <see cref="TimelineLayoutModel.GranularityDropdownRect"/>.
    /// A simple cycle-on-click is used: no popup is opened.
    /// </summary>
    public static SetTimelineGranularityCommand? BuildTimelineGranularityCommand(
        TimelineModel timeline,
        TimelineLayoutModel layout,
        LayoutPoint point)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(layout);

        if (!Contains(layout.GranularityDropdownRect, point))
            return null;

        var nextLevel = SetTimelineGranularityCommand.CycleLevel(timeline.Level);
        return new SetTimelineGranularityCommand(timeline.Name, nextLevel);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static bool Contains(LayoutRect rect, LayoutPoint point) =>
        rect.Width > 0 && rect.Height > 0 &&
        point.X >= rect.Left && point.X <= rect.Right &&
        point.Y >= rect.Top && point.Y <= rect.Bottom;

    private static string Format(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);
}

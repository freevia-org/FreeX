using System.Globalization;

using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.SlicerTimeline;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// Pure, UI-free glue that turns a pointer hit on a laid-out slicer or timeline into the matching
/// Core.Commands mutation. No Avalonia types: the shell supplies the portable layout model plus the
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
    /// </summary>
    public static SetSlicerSelectionCommand? BuildSlicerToggleCommand(
        SlicerModel slicer,
        IEnumerable<string> availableItems,
        SlicerLayoutModel layout,
        LayoutPoint point)
    {
        ArgumentNullException.ThrowIfNull(slicer);
        ArgumentNullException.ThrowIfNull(availableItems);
        ArgumentNullException.ThrowIfNull(layout);

        var items = availableItems as IReadOnlyCollection<string> ?? availableItems.ToList();
        if (SlicerLayoutBuilder.HitTest(layout, point) is not { IsAllPreview: false } tile)
            return null;

        var toggle = SlicerLayoutBuilder.Toggle(slicer, items, tile.Caption);
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
        var g = layout.Granularity;
        var selectedStart = layout.SelectedStart ?? layout.RangeStart ?? date;
        var selectedEnd = layout.SelectedEnd ?? layout.RangeEnd ?? date;

        return kind switch
        {
            // QQ2: snap the start handle to the BEGINNING of the period under the pointer.
            TimelineHitKind.StartHandle => (PeriodBounds(date, g).Start, selectedEnd),
            // QQ2: snap the end handle to the END of the period under the pointer.
            TimelineHitKind.EndHandle => (selectedStart, PeriodBounds(date, g).End),
            // QQ1: a track or selection click jumps the whole range to the full clicked period.
            _ => PeriodBounds(date, g),
        };
    }

    /// <summary>
    /// Returns the first and last day of the granularity period that contains <paramref name="date"/>.
    /// Days: (date, date) — unchanged.
    /// Months: (first-of-month, last-of-month).
    /// Quarters: (first day of Q start month, last day of Q end month).
    /// Years: (Jan 1, Dec 31) of that year.
    /// </summary>
    private static (DateOnly Start, DateOnly End) PeriodBounds(DateOnly date, TimelineGranularity g)
    {
        return g switch
        {
            TimelineGranularity.Month =>
                (new DateOnly(date.Year, date.Month, 1),
                 new DateOnly(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month))),

            TimelineGranularity.Quarter =>
                QuarterBounds(date),

            TimelineGranularity.Year =>
                (new DateOnly(date.Year, 1, 1),
                 new DateOnly(date.Year, 12, 31)),

            // Day (default): single-day range, unchanged.
            _ => (date, date),
        };
    }

    private static (DateOnly Start, DateOnly End) QuarterBounds(DateOnly date)
    {
        // Quarter number 1..4
        var q = ((date.Month - 1) / 3) + 1;
        var startMonth = ((q - 1) * 3) + 1;   // Q1→1, Q2→4, Q3→7, Q4→10
        var endMonth = startMonth + 2;          // Q1→3, Q2→6, Q3→9, Q4→12
        var start = new DateOnly(date.Year, startMonth, 1);
        var end = new DateOnly(date.Year, endMonth, DateTime.DaysInMonth(date.Year, endMonth));
        return (start, end);
    }

    private static string Format(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);
}

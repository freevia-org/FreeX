namespace FreeX.App.Presentation.SlicerTimeline;

/// <summary>
/// Shared, framework-free helper that maps a <see cref="TimelineHitKind"/> + a hit date to the
/// concrete (Start, End) date range a timeline filter command should apply. Extracted from the
/// shell interaction planner so both desktop renderers reuse the same period-snap math without
/// depending on any UI-framework assembly.
/// </summary>
public static class SlicerTimelineHitDateResolver
{
    /// <summary>
    /// Given a timeline layout and a hit result (kind + date), computes the new (Start, End) date
    /// range using the same period-snapping logic as the shell interaction planner:
    /// <list type="bullet">
    ///   <item>Handle drags: snap the moved boundary to the period edge (start→first day, end→last day).</item>
    ///   <item>Track / selection clicks: snap the whole range to the clicked period's full extent.</item>
    /// </list>
    /// Returns <c>(null, null)</c> when <paramref name="hitDate"/> is <c>null</c>.
    /// </summary>
    public static (DateOnly? Start, DateOnly? End) ResolveRange(
        TimelineLayoutModel layout,
        TimelineHitKind kind,
        DateOnly hitDate)
    {
        var g = layout.Granularity;
        var selectedStart = layout.SelectedStart ?? layout.RangeStart ?? hitDate;
        var selectedEnd = layout.SelectedEnd ?? layout.RangeEnd ?? hitDate;

        var (start, end) = kind switch
        {
            TimelineHitKind.StartHandle => (PeriodBounds(hitDate, g).Start, selectedEnd),
            TimelineHitKind.EndHandle   => (selectedStart, PeriodBounds(hitDate, g).End),
            _                           => PeriodBounds(hitDate, g),
        };

        if (start > end)
            (start, end) = (end, start);

        return (start, end);
    }

    /// <summary>
    /// Returns the first and last day of the granularity period that contains <paramref name="date"/>.
    /// </summary>
    public static (DateOnly Start, DateOnly End) PeriodBounds(DateOnly date, TimelineGranularity g) =>
        g switch
        {
            TimelineGranularity.Month =>
                (new DateOnly(date.Year, date.Month, 1),
                 new DateOnly(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month))),

            TimelineGranularity.Quarter => QuarterBounds(date),

            TimelineGranularity.Year =>
                (new DateOnly(date.Year, 1, 1),
                 new DateOnly(date.Year, 12, 31)),

            _ => (date, date), // Day: single-day range
        };

    private static (DateOnly Start, DateOnly End) QuarterBounds(DateOnly date)
    {
        var q = ((date.Month - 1) / 3) + 1;
        var startMonth = ((q - 1) * 3) + 1;
        var endMonth = startMonth + 2;
        var start = new DateOnly(date.Year, startMonth, 1);
        var end = new DateOnly(date.Year, endMonth, DateTime.DaysInMonth(date.Year, endMonth));
        return (start, end);
    }
}

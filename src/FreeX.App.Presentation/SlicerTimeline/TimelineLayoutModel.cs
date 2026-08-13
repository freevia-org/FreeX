using System.Globalization;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.SlicerTimeline;

/// <summary>
/// The grouping granularity a timeline buckets dates into, controlling the date-label text the
/// renderers show (day / month / quarter / year). Mirrors the source pivot field grouping used when
/// timeline keys are generated.
/// </summary>
public enum TimelineGranularity
{
    Day,
    Month,
    Quarter,
    Year
}

/// <summary>
/// One handle on the timeline's range bar that the desktop hosts let users drag to resize the
/// selected range. The rectangle is in pixel space; <see cref="IsStart"/> distinguishes the left
/// (range start) handle from the right (range end) handle.
/// </summary>
public readonly record struct TimelineHandleLayout(LayoutRect Rect, bool IsStart);

/// <summary>
/// Identifies which part of the timeline a point falls on during hit-testing.
/// </summary>
public enum TimelineHitKind
{
    None,
    StartHandle,
    EndHandle,
    Selection,
    Track
}

/// <summary>
/// The result of hit-testing a point against a timeline: which region was hit and, for points on the
/// track, the date that position maps to (so the renderers can begin a drag or jump the range there).
/// </summary>
public readonly record struct TimelineHitResult(TimelineHitKind Kind, DateOnly? Date);

/// <summary>
/// The portable, framework-free layout of a timeline filter inside a bounds rectangle. Carries the
/// header bar, the date label rectangle and text, the year banner rectangle, the period tick-label
/// rectangle, the range-bar (track) rectangle, the selection overlay rectangle, the drag handles,
/// the scrollbar rectangle, the selection ratios, and the granularity. The geometry is faithful to
/// Excel: a header capped at 22px, a date label band at +22px, a year banner at +34px, period tick
/// labels at +48px, the track at +62px, and a scrollbar at the bottom of the widget.
/// <para>
/// <see cref="RangeStart"/>/<see cref="RangeEnd"/> hold the full data range (used for scrollbar
/// thumb positioning). <see cref="WindowStart"/>/<see cref="WindowEnd"/> hold the visible period
/// window (the subset of the range that the track renders — used for tick and selection mapping).
/// When no scroll window is active, Window == Range.
/// </para>
/// <para>
/// <see cref="GranularityDropdownRect"/> is the affordance Excel shows at the top-right of the
/// header: "MONTHS ▾" (or YEARS/QUARTERS/DAYS) label + chevron. <see cref="ClearFilterIconRect"/>
/// is the clear-filter (×) slot immediately to the right of the dropdown, only present when
/// <see cref="HasActiveFilter"/> is true. Both rects are in the same pixel space as
/// <see cref="HeaderRect"/>. The actual granularity-change popup is not yet interactive —
/// renderers should draw the affordances but interactivity is deferred.
/// </para>
/// </summary>
public sealed record TimelineLayoutModel(
    string Name,
    string Caption,
    string? SourceFieldName,
    bool HasActiveFilter,
    string DateLabel,
    TimelineGranularity Granularity,
    string GranularityLabel,
    string ClearFilterGlyph,
    LayoutRect Bounds,
    LayoutRect HeaderRect,
    LayoutRect CaptionRect,
    LayoutRect DateLabelRect,
    LayoutRect YearBannerRect,
    LayoutRect TickLabelRect,
    LayoutRect TrackRect,
    LayoutRect SelectionRect,
    LayoutRect ScrollbarRect,
    double SelectionLeftRatio,
    double SelectionWidthRatio,
    TimelineHandleLayout StartHandle,
    TimelineHandleLayout EndHandle,
    DateOnly? RangeStart,
    DateOnly? RangeEnd,
    DateOnly? SelectedStart,
    DateOnly? SelectedEnd,
    DateOnly? WindowStart,
    DateOnly? WindowEnd,
    double ScrollThumbLeftRatio,
    double ScrollThumbWidthRatio,
    LayoutRect GranularityDropdownRect,
    LayoutRect ClearFilterIconRect);

/// <summary>
/// Builds <see cref="TimelineLayoutModel"/> layouts, formats date labels per granularity, and
/// performs point hit-testing against the selection handles and track. Pure geometry and date math;
/// the desktop renderers turn the returned rectangles into their own drawing primitives and wire the
/// hit result into the range command.
/// </summary>
public static class TimelineLayoutBuilder
{
    public const string ClearFilterGlyph = "\u00D7";

    // Faithful to the source desktop renderer's timeline math + new structural rows.
    private const double HeaderMaxHeight = 22;
    private const double DateLabelTopInset = 22;
    private const double DateLabelHeight = 12;
    private const double LabelHorizontalInset = 6;
    // Year banner sits below the date label (top+34) and is 13px tall.
    private const double YearBannerTopInset = 34;
    private const double YearBannerHeight = 13;
    // Tick labels sit below the year banner (top+48) and are 12px tall.
    private const double TickTopInset = 48;
    private const double TickHeight = 12;
    // Track sits below the tick labels (top+62).
    private const double TrackTopInset = 62;
    private const double TrackHorizontalInset = 8;
    // Scrollbar is 13px at the very bottom; the track reserves space for it.
    private const double ScrollbarHeight = 13;
    private const double ScrollbarBottomInset = 0;
    // Total space below the track needed: scrollbar + small gap.
    private const double TrackBottomReserve = ScrollbarHeight + 4;
    private const double TrackMinHeight = 6;
    private const double TrackMaxHeight = 14;
    private const double PreviewSelectionLeftRatio = 0.18;
    private const double PreviewSelectionWidthRatio = 0.56;
    private const double HandleWidth = 6;

    // Header chrome: granularity dropdown label ("MONTHS ▾") + clear-filter (×) icon.
    // The dropdown sits to the right of the caption, and the clear-filter is the rightmost slot.
    // Geometry: clear icon is GranClearIconSize px at far right with GranClearIconMargin inset;
    // the dropdown label is GranDropdownWidth px to the left of the clear icon.
    private const double GranDropdownWidth = 72;
    private const double GranDropdownHeight = 10;
    private const double GranClearIconSize = 10;
    private const double GranClearIconMargin = 4;
    private const double GranDropdownRightMargin = 4;

    // OOXML timeline level → granularity mapping (date hierarchy: 0=years, 1=quarters, 2=months, 3=days).
    // Confirmed: fixture has level="2" and Excel shows MONTHS.
    private static readonly TimelineGranularity[] LevelToGranularity =
        [TimelineGranularity.Year, TimelineGranularity.Quarter, TimelineGranularity.Month, TimelineGranularity.Day];

    // Fixed per-period pixel width (at 96 DPI) that Excel uses internally for the timeline track.
    // Derived from the reference image: timeline cx=3302000 EMU ≈ 346px total, track ≈ 330px, 8 months
    // visible → 330/8 ≈ 41px/month. Using 36px gives floor(330/36)=9 periods; 41px gives 8. Use 41px
    // to match the observed 8-month window.
    private const double MonthWidthPx = 41.0;
    private const double QuarterWidthPx = 50.0;
    private const double YearWidthPx = 60.0;
    private const double DayWidthPx = 4.0;

    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>
    /// Builds a layout for <paramref name="timeline"/> within <paramref name="bounds"/>.
    /// <paramref name="granularity"/> is the fallback granularity when the timeline's <c>Level</c>
    /// attribute is absent; when <c>Level</c> is present the OOXML date-hierarchy mapping is used.
    /// When no sub-range is selected the overlay falls back to the source renderer's fixed preview
    /// ratios (18% inset, 56% width); when a range is selected within a known full range, the overlay
    /// is derived proportionally from the selected dates.
    /// </summary>
    public static TimelineLayoutModel Build(
        TimelineModel timeline,
        LayoutRect bounds,
        TimelineGranularity granularity = TimelineGranularity.Month)
    {
        ArgumentNullException.ThrowIfNull(timeline);

        // Override granularity from OOXML level attribute when present (0=years,1=quarters,2=months,3=days).
        if (timeline.Level is { } level && (uint)level < (uint)LevelToGranularity.Length)
            granularity = LevelToGranularity[level];

        var rangeStart = ParseDate(timeline.StartDate);
        var rangeEnd = ParseDate(timeline.EndDate);
        var selectedStart = ParseDate(timeline.SelectedStartDate);
        var selectedEnd = ParseDate(timeline.SelectedEndDate);

        var headerRect = new LayoutRect(bounds.X, bounds.Y, bounds.Width, Math.Min(HeaderMaxHeight, bounds.Height));
        var dateLabelRect = new LayoutRect(
            bounds.Left + LabelHorizontalInset,
            bounds.Top + DateLabelTopInset,
            Math.Max(1, bounds.Width - (LabelHorizontalInset * 2)),
            DateLabelHeight);

        // Year banner and tick label rows — only present when the widget is tall enough.
        var hasStructuralRows = bounds.Height >= TrackTopInset + TrackMinHeight + ScrollbarHeight + 4;
        var yearBannerRect = hasStructuralRows
            ? new LayoutRect(bounds.Left + TrackHorizontalInset, bounds.Top + YearBannerTopInset,
                Math.Max(1, bounds.Width - (TrackHorizontalInset * 2)), YearBannerHeight)
            : new LayoutRect(bounds.Left + TrackHorizontalInset, bounds.Top + YearBannerTopInset, 1, 0);
        var tickLabelRect = hasStructuralRows
            ? new LayoutRect(bounds.Left + TrackHorizontalInset, bounds.Top + TickTopInset,
                Math.Max(1, bounds.Width - (TrackHorizontalInset * 2)), TickHeight)
            : new LayoutRect(bounds.Left + TrackHorizontalInset, bounds.Top + TickTopInset, 1, 0);

        // When widget is too short to show the new rows, fall back to the old compact layout
        // (track at top+34 like the wave1b renderer) so the band is still visible.
        // Track height = available space minus everything above and below the track.
        // Structural: available = bounds.Height - TrackTopInset - ScrollbarHeight - 2px gap
        // Compact:    available = bounds.Height - 42 (original reserve, matches wave1b)
        var effectiveTrackTopInset = hasStructuralRows ? TrackTopInset : 34.0;
        var effectiveTrackHeight = hasStructuralRows
            ? Math.Max(TrackMinHeight, Math.Min(TrackMaxHeight, bounds.Height - TrackTopInset - ScrollbarHeight - 2))
            : Math.Max(TrackMinHeight, Math.Min(TrackMaxHeight, bounds.Height - 42));

        var trackRect = new LayoutRect(
            bounds.Left + TrackHorizontalInset,
            bounds.Top + effectiveTrackTopInset,
            Math.Max(1, bounds.Width - (TrackHorizontalInset * 2)),
            effectiveTrackHeight);

        var scrollbarRect = hasStructuralRows
            ? new LayoutRect(bounds.Left + TrackHorizontalInset, bounds.Bottom - ScrollbarHeight,
                Math.Max(1, bounds.Width - (TrackHorizontalInset * 2)), ScrollbarHeight)
            : new LayoutRect(bounds.Left + TrackHorizontalInset, bounds.Bottom, 1, 0);

        // Compute the visible scroll window from scrollPosition + granularity.
        // When scrollPosition is present, the track renders only the visible window rather than the
        // full data range. The number of visible periods is derived from the track width divided by
        // the fixed per-period pixel width Excel uses for each granularity.
        var (windowStart, windowEnd) = ComputeScrollWindow(
            timeline.ScrollPosition, granularity, rangeStart, rangeEnd, trackRect.Width);

        // Scrollbar thumb ratios: position/size of the visible window within the full range.
        var (thumbLeft, thumbWidth) = ComputeScrollThumbRatios(rangeStart, rangeEnd, windowStart, windowEnd);

        // Selection ratios are computed within the visible window (not the full range) so the
        // selection band stays aligned with the visible tick marks.
        var (leftRatio, widthRatio) = ComputeSelectionRatios(windowStart, windowEnd, selectedStart, selectedEnd);
        var selectionRect = new LayoutRect(
            trackRect.Left + (trackRect.Width * leftRatio),
            trackRect.Top,
            Math.Max(HandleWidth, trackRect.Width * widthRatio),
            trackRect.Height);

        var startHandle = new TimelineHandleLayout(
            new LayoutRect(selectionRect.Left - (HandleWidth / 2), trackRect.Top, HandleWidth, trackRect.Height),
            IsStart: true);
        var endHandle = new TimelineHandleLayout(
            new LayoutRect(selectionRect.Right - (HandleWidth / 2), trackRect.Top, HandleWidth, trackRect.Height),
            IsStart: false);

        var hasActiveFilter = HasActiveFilter(timeline);
        var (granDropdownRect, clearFilterRect) = BuildHeaderChromeRects(headerRect, hasActiveFilter);
        var captionRect = BuildCaptionRect(headerRect, granDropdownRect, clearFilterRect);

        return new TimelineLayoutModel(
            Name: timeline.Name,
            Caption: ResolveCaption(timeline),
            SourceFieldName: timeline.SourceFieldName,
            HasActiveFilter: hasActiveFilter,
            DateLabel: FormatDateLabel(timeline, granularity),
            Granularity: granularity,
            GranularityLabel: FormatGranularityLabel(granularity),
            ClearFilterGlyph: ClearFilterGlyph,
            Bounds: bounds,
            HeaderRect: headerRect,
            CaptionRect: captionRect,
            DateLabelRect: dateLabelRect,
            YearBannerRect: yearBannerRect,
            TickLabelRect: tickLabelRect,
            TrackRect: trackRect,
            SelectionRect: selectionRect,
            ScrollbarRect: scrollbarRect,
            SelectionLeftRatio: leftRatio,
            SelectionWidthRatio: widthRatio,
            StartHandle: startHandle,
            EndHandle: endHandle,
            RangeStart: rangeStart,
            RangeEnd: rangeEnd,
            SelectedStart: selectedStart,
            SelectedEnd: selectedEnd,
            WindowStart: windowStart,
            WindowEnd: windowEnd,
            ScrollThumbLeftRatio: thumbLeft,
            ScrollThumbWidthRatio: thumbWidth,
            GranularityDropdownRect: granDropdownRect,
            ClearFilterIconRect: clearFilterRect);
    }

    public static string FormatGranularityLabel(TimelineGranularity granularity) =>
        granularity switch
        {
            TimelineGranularity.Year => "YEARS \u25BE",
            TimelineGranularity.Quarter => "QUARTERS \u25BE",
            TimelineGranularity.Month => "MONTHS \u25BE",
            TimelineGranularity.Day => "DAYS \u25BE",
            _ => throw new ArgumentOutOfRangeException(nameof(granularity), granularity, null)
        };

    // Computes the header chrome rects for the timeline:
    // - GranularityDropdownRect: "MONTHS ▾" label area, right-of-center in the header.
    // - ClearFilterIconRect: × icon slot, rightmost, only non-zero when hasActiveFilter is true.
    // Geometry: clearIcon is 10px at the far right
    // with GranClearIconMargin inset; the dropdown is labelWidth px to the left of the clear icon.
    private static (LayoutRect Dropdown, LayoutRect ClearFilter) BuildHeaderChromeRects(
        LayoutRect headerRect,
        bool hasActiveFilter)
    {
        if (headerRect.Height <= 0)
        {
            var empty = new LayoutRect(headerRect.Right, headerRect.Top, 0, 0);
            return (empty, empty);
        }

        var iconCenterY = headerRect.Top + (headerRect.Height - GranClearIconSize) / 2;
        // Clear-filter × slot (rightmost).
        var clearFilterLeft = headerRect.Right - GranClearIconSize - GranClearIconMargin;
        var clearFilterRect = hasActiveFilter
            ? new LayoutRect(clearFilterLeft, iconCenterY, GranClearIconSize, GranClearIconSize)
            : new LayoutRect(headerRect.Right, headerRect.Top, 0, 0);

        // Granularity dropdown label sits to the left of the clear-filter icon.
        var dropdownRight = hasActiveFilter
            ? clearFilterLeft - GranDropdownRightMargin
            : headerRect.Right - GranClearIconMargin;
        var dropdownLeft = dropdownRight - GranDropdownWidth;
        var dropdownCenterY = headerRect.Top + (headerRect.Height - GranDropdownHeight) / 2;
        var dropdownRect = dropdownLeft > headerRect.Left + 5
            ? new LayoutRect(dropdownLeft, dropdownCenterY, GranDropdownWidth, GranDropdownHeight)
            : new LayoutRect(headerRect.Right, headerRect.Top, 0, 0); // too narrow to show

        return (dropdownRect, clearFilterRect);
    }

    private static LayoutRect BuildCaptionRect(
        LayoutRect headerRect,
        LayoutRect dropdownRect,
        LayoutRect clearFilterRect)
    {
        if (headerRect.Height <= 0 || headerRect.Width <= 0)
            return new LayoutRect(headerRect.X, headerRect.Y, 0, 0);

        const double CaptionPaddingLeft = 6;
        const double CaptionChromeGap = 4;
        var firstChromeLeft = FirstChromeLeft(dropdownRect, clearFilterRect);
        var right = firstChromeLeft is { } left
            ? Math.Max(headerRect.Left + CaptionPaddingLeft, left - CaptionChromeGap)
            : headerRect.Right - CaptionPaddingLeft;

        return new LayoutRect(
            headerRect.Left + CaptionPaddingLeft,
            headerRect.Top,
            Math.Max(0, right - headerRect.Left - CaptionPaddingLeft),
            headerRect.Height);
    }

    private static double? FirstChromeLeft(LayoutRect dropdownRect, LayoutRect clearFilterRect)
    {
        double? left = null;
        if (dropdownRect.Width > 0)
            left = dropdownRect.Left;
        if (clearFilterRect.Width > 0)
            left = left is { } current ? Math.Min(current, clearFilterRect.Left) : clearFilterRect.Left;
        return left;
    }

    // Computes the visible window [windowStart, windowEnd) from the OOXML scrollPosition and
    // granularity. When scrollPosition is absent (or unparseable), returns the full data range.
    // The number of visible periods = floor(trackWidth / perPeriodPx); window end is snapped to
    // the nearest period boundary at or after the last visible period.
    private static (DateOnly? WindowStart, DateOnly? WindowEnd) ComputeScrollWindow(
        string? scrollPositionDate,
        TimelineGranularity granularity,
        DateOnly? rangeStart,
        DateOnly? rangeEnd,
        double trackWidth)
    {
        // No scroll data → full range is the window.
        if (string.IsNullOrWhiteSpace(scrollPositionDate) || rangeStart is null || rangeEnd is null)
            return (rangeStart, rangeEnd);

        var viewStart = ParseDate(scrollPositionDate);
        if (viewStart is null)
            return (rangeStart, rangeEnd);

        // Clamp viewStart to [rangeStart, rangeEnd).
        if (viewStart < rangeStart)
            viewStart = rangeStart;
        if (viewStart >= rangeEnd)
            return (rangeStart, rangeEnd);

        // Per-period pixel width for this granularity (at 96 DPI logical pixels).
        var periodPx = granularity switch
        {
            TimelineGranularity.Year => YearWidthPx,
            TimelineGranularity.Quarter => QuarterWidthPx,
            TimelineGranularity.Day => DayWidthPx,
            _ => MonthWidthPx
        };

        // Number of full periods that fit in the track width.
        var visibleCount = Math.Max(1, (int)Math.Floor(trackWidth / periodPx));

        // Compute windowEnd: viewStart + visibleCount periods.
        var viewEnd = granularity switch
        {
            TimelineGranularity.Year => viewStart.Value.AddYears(visibleCount),
            TimelineGranularity.Quarter => viewStart.Value.AddMonths(visibleCount * 3),
            TimelineGranularity.Day => viewStart.Value.AddDays(visibleCount),
            _ => viewStart.Value.AddMonths(visibleCount) // Month
        };

        // Clamp to rangeEnd.
        if (viewEnd > rangeEnd.Value)
            viewEnd = rangeEnd.Value;

        return (viewStart, viewEnd);
    }

    // Computes the scrollbar thumb position/size as ratios within the full data range.
    private static (double Left, double Width) ComputeScrollThumbRatios(
        DateOnly? rangeStart,
        DateOnly? rangeEnd,
        DateOnly? windowStart,
        DateOnly? windowEnd)
    {
        if (rangeStart is null || rangeEnd is null || windowStart is null || windowEnd is null)
            return (0, 1);

        var totalDays = rangeEnd.Value.DayNumber - rangeStart.Value.DayNumber;
        if (totalDays <= 0)
            return (0, 1);

        var thumbLeft = Math.Clamp(
            (windowStart.Value.DayNumber - rangeStart.Value.DayNumber) / (double)totalDays, 0, 1);
        var thumbRight = Math.Clamp(
            (windowEnd.Value.DayNumber - rangeStart.Value.DayNumber) / (double)totalDays, 0, 1);
        return (thumbLeft, Math.Max(0, thumbRight - thumbLeft));
    }

    /// <summary>
    /// Hit-tests <paramref name="point"/> against the timeline. The drag handles take priority,
    /// then the selection overlay, then the track; for points on the track or selection the mapped
    /// date is reported when the full range is known.
    /// </summary>
    public static TimelineHitResult HitTest(TimelineLayoutModel layout, LayoutPoint point)
    {
        ArgumentNullException.ThrowIfNull(layout);

        if (Contains(layout.StartHandle.Rect, point))
            return new TimelineHitResult(TimelineHitKind.StartHandle, DateAt(layout, point.X));
        if (Contains(layout.EndHandle.Rect, point))
            return new TimelineHitResult(TimelineHitKind.EndHandle, DateAt(layout, point.X));
        if (Contains(layout.SelectionRect, point))
            return new TimelineHitResult(TimelineHitKind.Selection, DateAt(layout, point.X));
        if (Contains(layout.TrackRect, point))
            return new TimelineHitResult(TimelineHitKind.Track, DateAt(layout, point.X));

        return new TimelineHitResult(TimelineHitKind.None, null);
    }

    /// <summary>
    /// Maps a horizontal pixel position to a date within the timeline's visible window (or full range
    /// when no window is set), or <c>null</c> when the range is unknown or empty. Positions are
    /// clamped to the track extent.
    /// </summary>
    public static DateOnly? DateAt(TimelineLayoutModel layout, double x)
    {
        ArgumentNullException.ThrowIfNull(layout);
        // Map within the visible window when present, otherwise the full range.
        var start = layout.WindowStart ?? layout.RangeStart;
        var end = layout.WindowEnd ?? layout.RangeEnd;
        if (start is null || end is null)
            return null;

        var totalDays = end.Value.DayNumber - start.Value.DayNumber;
        if (totalDays <= 0 || layout.TrackRect.Width <= 0)
            return start;

        var ratio = (x - layout.TrackRect.Left) / layout.TrackRect.Width;
        ratio = Math.Clamp(ratio, 0, 1);
        var dayOffset = (int)Math.Round(ratio * totalDays);
        return start.Value.AddDays(dayOffset);
    }

    /// <summary>
    /// True when the timeline has a selected start or end date (an active range filter). Mirrors the
    /// source filter-state check.
    /// </summary>
    public static bool HasActiveFilter(TimelineModel timeline)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        return !string.IsNullOrWhiteSpace(timeline.SelectedStartDate) ||
            !string.IsNullOrWhiteSpace(timeline.SelectedEndDate);
    }

    /// <summary>
    /// Formats the date label the renderers show under the header. When no sub-range is selected and no
    /// bounds are known this is the field name; otherwise it is the selected (or full) range formatted
    /// per <paramref name="granularity"/>. For the Month granularity, Excel shows abbreviated month names
    /// with a shared trailing year (e.g. "Feb – Apr 2026"); other granularities use a simple separator.
    /// </summary>
    public static string FormatDateLabel(TimelineModel timeline, TimelineGranularity granularity)
    {
        ArgumentNullException.ThrowIfNull(timeline);

        var start = NullIfEmpty(timeline.SelectedStartDate) ?? NullIfEmpty(timeline.StartDate);
        var end = NullIfEmpty(timeline.SelectedEndDate) ?? NullIfEmpty(timeline.EndDate);
        if (start is null && end is null)
            return timeline.SourceFieldName ?? timeline.CacheName ?? "";

        if (granularity == TimelineGranularity.Month)
            return FormatMonthRangeLabel(start, end);

        var startLabel = FormatBoundary(start, granularity);
        var endLabel = FormatBoundary(end, granularity);
        return $"{startLabel} – {endLabel}".Trim();
    }

    // Formats a month-granularity range label matching Excel's style:
    // "Feb – Apr 2026" (same year) or "Dec 2025 – Feb 2026" (cross-year).
    private static string FormatMonthRangeLabel(string? startRaw, string? endRaw)
    {
        var startDate = ParseDate(startRaw);
        var endDate = ParseDate(endRaw);

        if (startDate is null && endDate is null)
            return "";

        if (startDate is { } s && endDate is { } e)
        {
            if (s.Year == e.Year)
            {
                // Same year: "Feb – Apr 2026"
                var startMon = s.ToString("MMM", CultureInfo.InvariantCulture);
                var endMon = e.ToString("MMM", CultureInfo.InvariantCulture);
                return $"{startMon} – {endMon} {s.Year}";
            }
            else
            {
                // Cross-year: "Dec 2025 – Feb 2026"
                var startFmt = s.ToString("MMM yyyy", CultureInfo.InvariantCulture);
                var endFmt = e.ToString("MMM yyyy", CultureInfo.InvariantCulture);
                return $"{startFmt} – {endFmt}";
            }
        }

        // Only one bound available — format as "MMM yyyy"
        var date = startDate ?? endDate;
        return date!.Value.ToString("MMM yyyy", CultureInfo.InvariantCulture);
    }

    /// <summary>Formats a single date string per <paramref name="granularity"/> (day/month/quarter/year).</summary>
    public static string FormatBoundary(string? value, TimelineGranularity granularity)
    {
        var date = ParseDate(value);
        if (date is not { } parsed)
            return value?.Trim() ?? "";

        return granularity switch
        {
            TimelineGranularity.Year => parsed.Year.ToString(CultureInfo.InvariantCulture),
            TimelineGranularity.Quarter => $"{parsed.Year}-Q{((parsed.Month - 1) / 3) + 1}",
            TimelineGranularity.Month => parsed.ToString("MMM yyyy", CultureInfo.InvariantCulture),
            _ => parsed.ToString(DateFormat, CultureInfo.InvariantCulture)
        };
    }

    /// <summary>
    /// Enumerates the period tick labels for the timeline's visible range at the given
    /// <paramref name="granularity"/>. Each entry is (label, centerX) where centerX is the horizontal
    /// pixel position of that period's midpoint within the track, using the same date→x mapping as the
    /// selection band so ticks and band are aligned. Only periods whose center falls within the track
    /// are returned.
    /// </summary>
    public static IReadOnlyList<(string Label, double CenterX)> GetTickLabels(TimelineLayoutModel layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        // Use the visible window for tick generation; fall back to full range when no window is set.
        if ((layout.WindowStart ?? layout.RangeStart) is not { } rangeStart ||
            (layout.WindowEnd ?? layout.RangeEnd) is not { } rangeEnd)
            return [];

        var totalDays = rangeEnd.DayNumber - rangeStart.DayNumber;
        if (totalDays <= 0 || layout.TrackRect.Width <= 0)
            return [];

        var result = new List<(string, double)>();

        switch (layout.Granularity)
        {
            case TimelineGranularity.Month:
                // One label per calendar month that has any day within [rangeStart, rangeEnd).
                // rangeEnd is treated as exclusive (e.g. 2027-01-01 means "up to end of Dec 2026").
                var current = new DateOnly(rangeStart.Year, rangeStart.Month, 1);
                while (current < rangeEnd)
                {
                    var periodEnd = current.AddMonths(1);
                    // midpoint of the visible part of this month
                    var midDay = (current.DayNumber + Math.Min(periodEnd.DayNumber, rangeEnd.DayNumber)) / 2.0;
                    var midRatio = Math.Clamp((midDay - rangeStart.DayNumber) / totalDays, 0, 1);
                    var centerX = layout.TrackRect.Left + midRatio * layout.TrackRect.Width;
                    result.Add((current.ToString("MMM", System.Globalization.CultureInfo.InvariantCulture).ToUpperInvariant(), centerX));
                    current = current.AddMonths(1);
                }
                break;

            case TimelineGranularity.Year:
                // One label per year that has any day within [rangeStart, rangeEnd).
                var yearCurrent = new DateOnly(rangeStart.Year, 1, 1);
                while (yearCurrent < rangeEnd)
                {
                    var yearPeriodEnd = yearCurrent.AddYears(1);
                    var midDay = (yearCurrent.DayNumber + Math.Min(yearPeriodEnd.DayNumber, rangeEnd.DayNumber)) / 2.0;
                    var midRatio = Math.Clamp((midDay - rangeStart.DayNumber) / totalDays, 0, 1);
                    var centerX = layout.TrackRect.Left + midRatio * layout.TrackRect.Width;
                    result.Add((yearCurrent.Year.ToString(System.Globalization.CultureInfo.InvariantCulture), centerX));
                    yearCurrent = yearCurrent.AddYears(1);
                }
                break;

            case TimelineGranularity.Quarter:
                // One label per calendar quarter that has any day within [rangeStart, rangeEnd).
                var qMonth = ((rangeStart.Month - 1) / 3) * 3 + 1;
                var qCurrent = new DateOnly(rangeStart.Year, qMonth, 1);
                while (qCurrent < rangeEnd)
                {
                    var qNum = ((qCurrent.Month - 1) / 3) + 1;
                    var qPeriodEnd = qCurrent.AddMonths(3);
                    var midDay = (qCurrent.DayNumber + Math.Min(qPeriodEnd.DayNumber, rangeEnd.DayNumber)) / 2.0;
                    var midRatio = Math.Clamp((midDay - rangeStart.DayNumber) / totalDays, 0, 1);
                    var centerX = layout.TrackRect.Left + midRatio * layout.TrackRect.Width;
                    result.Add(($"Q{qNum}", centerX));
                    qCurrent = qCurrent.AddMonths(3);
                }
                break;

            case TimelineGranularity.Day:
                // Label every day but skip if too dense (more than 1 per 12px).
                var dayStep = Math.Max(1, (int)Math.Ceiling(totalDays / (layout.TrackRect.Width / 12.0)));
                var dayCurrent = rangeStart;
                while (dayCurrent <= rangeEnd)
                {
                    var ratio = Math.Clamp((dayCurrent.DayNumber - rangeStart.DayNumber) / (double)totalDays, 0, 1);
                    var centerX = layout.TrackRect.Left + ratio * layout.TrackRect.Width;
                    result.Add((dayCurrent.Day.ToString(System.Globalization.CultureInfo.InvariantCulture), centerX));
                    dayCurrent = dayCurrent.AddDays(dayStep);
                }
                break;
        }

        return result;
    }

    /// <summary>
    /// Enumerates the year spans for the year banner row. Each entry is (year, startX, width) giving
    /// the left edge and pixel width of that year's span within the track. Uses the same date→x
    /// mapping as <see cref="DateAt"/>.
    /// </summary>
    public static IReadOnlyList<(int Year, double StartX, double SpanWidth)> GetYearBannerSpans(TimelineLayoutModel layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        // Use the visible window for year banner spans; fall back to full range when no window is set.
        if ((layout.WindowStart ?? layout.RangeStart) is not { } rangeStart ||
            (layout.WindowEnd ?? layout.RangeEnd) is not { } rangeEnd)
            return [];

        var totalDays = rangeEnd.DayNumber - rangeStart.DayNumber;
        if (totalDays <= 0 || layout.TrackRect.Width <= 0)
            return [];

        var result = new List<(int, double, double)>();
        var yearStart = new DateOnly(rangeStart.Year, 1, 1);
        // Loop while the year period has at least one day before rangeEnd (exclusive).
        while (yearStart < rangeEnd)
        {
            var yearEnd = yearStart.AddYears(1);
            // Clamp to the visible range
            var clampedStart = yearStart < rangeStart ? rangeStart : yearStart;
            var clampedEnd = yearEnd > rangeEnd ? rangeEnd : yearEnd;
            var leftRatio = Math.Clamp((clampedStart.DayNumber - rangeStart.DayNumber) / (double)totalDays, 0, 1);
            var rightRatio = Math.Clamp((clampedEnd.DayNumber - rangeStart.DayNumber) / (double)totalDays, 0, 1);
            var startX = layout.TrackRect.Left + leftRatio * layout.TrackRect.Width;
            var spanWidth = (rightRatio - leftRatio) * layout.TrackRect.Width;
            if (spanWidth > 0)
                result.Add((yearStart.Year, startX, spanWidth));
            yearStart = yearStart.AddYears(1);
        }

        return result;
    }

    private static (double Left, double Width) ComputeSelectionRatios(
        DateOnly? rangeStart,
        DateOnly? rangeEnd,
        DateOnly? selectedStart,
        DateOnly? selectedEnd)
    {
        // With no selected sub-range, or no full range to measure against, fall back to the source
        // renderer's fixed preview overlay (18% inset, 56% width).
        if (rangeStart is not { } start || rangeEnd is not { } end)
            return (PreviewSelectionLeftRatio, PreviewSelectionWidthRatio);
        if (selectedStart is null && selectedEnd is null)
            return (PreviewSelectionLeftRatio, PreviewSelectionWidthRatio);

        var totalDays = end.DayNumber - start.DayNumber;
        if (totalDays <= 0)
            return (PreviewSelectionLeftRatio, PreviewSelectionWidthRatio);

        var selStart = selectedStart ?? start;
        var selEnd = selectedEnd ?? end;
        if (selEnd < selStart)
            (selStart, selEnd) = (selEnd, selStart);

        var leftRatio = Math.Clamp((selStart.DayNumber - start.DayNumber) / (double)totalDays, 0, 1);
        var rightRatio = Math.Clamp((selEnd.DayNumber - start.DayNumber) / (double)totalDays, 0, 1);
        var widthRatio = Math.Max(0, rightRatio - leftRatio);
        return (leftRatio, widthRatio);
    }

    private static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return DateOnly.TryParseExact(
            value.Trim(),
            DateFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? parsed
            : null;
    }

    private static string ResolveCaption(TimelineModel timeline)
    {
        if (!string.IsNullOrWhiteSpace(timeline.Caption))
            return timeline.Caption.Trim();
        if (!string.IsNullOrWhiteSpace(timeline.Name))
            return timeline.Name.Trim();
        return string.IsNullOrWhiteSpace(timeline.DrawingShapeName) ? "Filter" : timeline.DrawingShapeName.Trim();
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static bool Contains(LayoutRect rect, LayoutPoint point) =>
        point.X >= rect.Left && point.X <= rect.Right &&
        point.Y >= rect.Top && point.Y <= rect.Bottom;
}

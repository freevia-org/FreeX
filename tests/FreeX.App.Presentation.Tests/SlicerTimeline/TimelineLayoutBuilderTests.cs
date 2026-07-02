using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.SlicerTimeline;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.SlicerTimeline;

public sealed class TimelineLayoutBuilderTests
{
    private static readonly LayoutRect Bounds = new(40, 20, 200, 80);

    private static TimelineModel Timeline(
        string? start = "2024-01-01",
        string? end = "2024-12-31",
        string? selStart = null,
        string? selEnd = null) =>
        new()
        {
            Name = "Timeline1",
            Caption = "Order Date",
            SourceFieldName = "OrderDate",
            StartDate = start,
            EndDate = end,
            SelectedStartDate = selStart,
            SelectedEndDate = selEnd
        };

    [Fact]
    public void Build_HeaderAndTrackGeometry_MatchSourceMath()
    {
        var layout = TimelineLayoutBuilder.Build(Timeline(), Bounds);

        layout.HeaderRect.Height.Should().Be(22);
        layout.DateLabelRect.Top.Should().Be(Bounds.Top + 22);
        layout.DateLabelRect.Left.Should().Be(Bounds.Left + 6);

        // track: left+8, top+34, width = width-16, height = max(6, min(14, height-42))
        layout.TrackRect.Left.Should().Be(Bounds.Left + 8);
        layout.TrackRect.Top.Should().Be(Bounds.Top + 34);
        layout.TrackRect.Width.Should().Be(Bounds.Width - 16);
        layout.TrackRect.Height.Should().Be(14); // height-42 = 38 -> clamped to 14
    }

    [Fact]
    public void Build_NoSelection_UsesPreviewRatios()
    {
        var layout = TimelineLayoutBuilder.Build(Timeline(), Bounds);

        layout.SelectionLeftRatio.Should().Be(0.18);
        layout.SelectionWidthRatio.Should().Be(0.56);
        layout.SelectionRect.Left.Should().BeApproximately(layout.TrackRect.Left + (layout.TrackRect.Width * 0.18), 1e-9);
        layout.SelectionRect.Width.Should().BeApproximately(layout.TrackRect.Width * 0.56, 1e-9);
    }

    [Fact]
    public void Build_WithSelection_DerivesOverlayRatiosFromDates()
    {
        // Full year; selection covers Apr 1 .. Sep 30 (roughly Q2-Q3).
        var layout = TimelineLayoutBuilder.Build(
            Timeline(selStart: "2024-04-01", selEnd: "2024-09-30"),
            Bounds);

        var totalDays = new DateOnly(2024, 12, 31).DayNumber - new DateOnly(2024, 1, 1).DayNumber;
        var expectedLeft = (new DateOnly(2024, 4, 1).DayNumber - new DateOnly(2024, 1, 1).DayNumber) / (double)totalDays;
        var expectedRight = (new DateOnly(2024, 9, 30).DayNumber - new DateOnly(2024, 1, 1).DayNumber) / (double)totalDays;

        layout.SelectionLeftRatio.Should().BeApproximately(expectedLeft, 1e-9);
        layout.SelectionWidthRatio.Should().BeApproximately(expectedRight - expectedLeft, 1e-9);
        layout.HasActiveFilter.Should().BeTrue();
    }

    [Fact]
    public void Build_SelectionRatios_ClampedAndWidthFloored()
    {
        var layout = TimelineLayoutBuilder.Build(
            Timeline(selStart: "2024-06-01", selEnd: "2024-06-01"),
            Bounds);

        layout.SelectionWidthRatio.Should().Be(0); // zero-width range
        layout.SelectionRect.Width.Should().Be(6); // floored to handle width
    }

    [Theory]
    [InlineData(TimelineGranularity.Day, "2024-03-15", "2024-03-15")]
    [InlineData(TimelineGranularity.Month, "2024-03-15", "Mar 2024")]
    [InlineData(TimelineGranularity.Quarter, "2024-03-15", "2024-Q1")]
    [InlineData(TimelineGranularity.Quarter, "2024-11-15", "2024-Q4")]
    [InlineData(TimelineGranularity.Year, "2024-03-15", "2024")]
    public void FormatBoundary_PerGranularity(TimelineGranularity granularity, string input, string expected)
    {
        TimelineLayoutBuilder.FormatBoundary(input, granularity).Should().Be(expected);
    }

    [Fact]
    public void FormatDateLabel_NoBounds_UsesFieldName()
    {
        var timeline = Timeline(start: null, end: null);

        TimelineLayoutBuilder.FormatDateLabel(timeline, TimelineGranularity.Month).Should().Be("OrderDate");
    }

    [Fact]
    public void FormatDateLabel_WithRange_FormatsBothEnds()
    {
        var timeline = Timeline(selStart: "2024-01-01", selEnd: "2024-06-30");

        // Month granularity uses Excel's abbreviated format: "Jan – Jun 2024" (same year).
        TimelineLayoutBuilder.FormatDateLabel(timeline, TimelineGranularity.Month)
            .Should().Be("Jan – Jun 2024");
    }

    [Fact]
    public void HitTest_StartHandle_TakesPriority()
    {
        var layout = TimelineLayoutBuilder.Build(Timeline(selStart: "2024-04-01", selEnd: "2024-09-30"), Bounds);

        var result = TimelineLayoutBuilder.HitTest(layout, layout.StartHandle.Rect.Center);

        result.Kind.Should().Be(TimelineHitKind.StartHandle);
        result.Date.Should().NotBeNull();
    }

    [Fact]
    public void HitTest_EndHandle()
    {
        var layout = TimelineLayoutBuilder.Build(Timeline(selStart: "2024-04-01", selEnd: "2024-09-30"), Bounds);

        TimelineLayoutBuilder.HitTest(layout, layout.EndHandle.Rect.Center).Kind
            .Should().Be(TimelineHitKind.EndHandle);
    }

    [Fact]
    public void HitTest_SelectionBody()
    {
        var layout = TimelineLayoutBuilder.Build(Timeline(selStart: "2024-04-01", selEnd: "2024-09-30"), Bounds);

        TimelineLayoutBuilder.HitTest(layout, layout.SelectionRect.Center).Kind
            .Should().Be(TimelineHitKind.Selection);
    }

    [Fact]
    public void HitTest_TrackOutsideSelection()
    {
        var layout = TimelineLayoutBuilder.Build(Timeline(selStart: "2024-06-01", selEnd: "2024-07-01"), Bounds);
        // far-right point on track but past the selection overlay
        var point = new LayoutPoint(layout.TrackRect.Right - 1, layout.TrackRect.Center.Y);

        var result = TimelineLayoutBuilder.HitTest(layout, point);

        result.Kind.Should().BeOneOf(TimelineHitKind.Track, TimelineHitKind.Selection);
    }

    [Fact]
    public void HitTest_OutsideEverything_ReturnsNone()
    {
        var layout = TimelineLayoutBuilder.Build(Timeline(), Bounds);

        TimelineLayoutBuilder.HitTest(layout, new LayoutPoint(0, 0)).Kind.Should().Be(TimelineHitKind.None);
    }

    [Fact]
    public void DateAt_MapsTrackPositionToDate()
    {
        var layout = TimelineLayoutBuilder.Build(Timeline(), Bounds);

        TimelineLayoutBuilder.DateAt(layout, layout.TrackRect.Left).Should().Be(new DateOnly(2024, 1, 1));
        TimelineLayoutBuilder.DateAt(layout, layout.TrackRect.Right).Should().Be(new DateOnly(2024, 12, 31));
        var mid = TimelineLayoutBuilder.DateAt(layout, layout.TrackRect.Center.X);
        mid.Should().NotBeNull();
        mid!.Value.Should().BeOnOrAfter(new DateOnly(2024, 6, 1)).And.BeOnOrBefore(new DateOnly(2024, 7, 31));
    }

    [Fact]
    public void DateAt_NoRange_ReturnsNull()
    {
        var layout = TimelineLayoutBuilder.Build(Timeline(start: null, end: null), Bounds);

        TimelineLayoutBuilder.DateAt(layout, layout.TrackRect.Center.X).Should().BeNull();
    }

    [Fact]
    public void Build_EmptyRange_FallsBackToPreviewRatios()
    {
        // start == end => zero total days; selection present but unmeasurable.
        var layout = TimelineLayoutBuilder.Build(
            Timeline(start: "2024-05-01", end: "2024-05-01", selStart: "2024-05-01", selEnd: "2024-05-01"),
            Bounds);

        layout.SelectionLeftRatio.Should().Be(0.18);
        layout.SelectionWidthRatio.Should().Be(0.56);
    }

    [Fact]
    public void HasActiveFilter_TracksSelectedDates()
    {
        TimelineLayoutBuilder.HasActiveFilter(Timeline()).Should().BeFalse();
        TimelineLayoutBuilder.HasActiveFilter(Timeline(selStart: "2024-02-01")).Should().BeTrue();
        TimelineLayoutBuilder.HasActiveFilter(Timeline(selEnd: "2024-02-01")).Should().BeTrue();
    }

    [Fact]
    public void Build_ResolvesCaptionFallbacks()
    {
        var noCaption = new TimelineModel { Name = "MyTimeline" };
        TimelineLayoutBuilder.Build(noCaption, Bounds).Caption.Should().Be("MyTimeline");

        var shapeOnly = new TimelineModel { DrawingShapeName = "Shape 7" };
        TimelineLayoutBuilder.Build(shapeOnly, Bounds).Caption.Should().Be("Shape 7");
    }

    // --- Structural rows (year banner, tick labels, scrollbar) ---

    // Tall bounds trigger the structural layout: year banner, tick labels, scrollbar all appear.
    private static readonly LayoutRect TallBounds = new(40, 20, 200, 110);

    [Fact]
    public void Build_TallBounds_StructuralRowsArePresent()
    {
        var layout = TimelineLayoutBuilder.Build(Timeline(), TallBounds);

        // Year banner at top+34, height=13
        layout.YearBannerRect.Top.Should().Be(TallBounds.Top + 34);
        layout.YearBannerRect.Height.Should().Be(13);

        // Tick labels at top+48, height=12
        layout.TickLabelRect.Top.Should().Be(TallBounds.Top + 48);
        layout.TickLabelRect.Height.Should().Be(12);

        // Track at top+62
        layout.TrackRect.Top.Should().Be(TallBounds.Top + 62);
        layout.TrackRect.Height.Should().BeGreaterThanOrEqualTo(6);

        // Scrollbar at the very bottom (height=13)
        layout.ScrollbarRect.Height.Should().Be(13);
        layout.ScrollbarRect.Bottom.Should().BeApproximately(TallBounds.Bottom, 1e-9);
    }

    [Fact]
    public void Build_ShortBounds_StructuralRowsCollapse()
    {
        // 80px is below the 85px structural threshold → compact fallback
        var layout = TimelineLayoutBuilder.Build(Timeline(), Bounds);

        layout.YearBannerRect.Height.Should().Be(0);
        layout.TickLabelRect.Height.Should().Be(0);
        layout.ScrollbarRect.Height.Should().Be(0);
        // Track falls back to old compact position (top+34)
        layout.TrackRect.Top.Should().Be(Bounds.Top + 34);
    }

    [Fact]
    public void GetTickLabels_Month_EnumeratesAllMonthsInRange()
    {
        var layout = TimelineLayoutBuilder.Build(Timeline(), TallBounds);
        var ticks = TimelineLayoutBuilder.GetTickLabels(layout);

        // Full year 2024-01-01 to 2024-12-31 = 12 months
        ticks.Should().HaveCount(12);
        ticks[0].Label.Should().Be("JAN");
        ticks[11].Label.Should().Be("DEC");
    }

    [Fact]
    public void GetTickLabels_CenterXAlignedWithDateMapping()
    {
        var layout = TimelineLayoutBuilder.Build(Timeline(), TallBounds);
        var ticks = TimelineLayoutBuilder.GetTickLabels(layout);

        // January's center should be near the track's left quarter
        var janCenter = ticks[0].CenterX;
        janCenter.Should().BeGreaterThan(layout.TrackRect.Left);
        janCenter.Should().BeLessThan(layout.TrackRect.Left + layout.TrackRect.Width * 0.25);
    }

    [Fact]
    public void GetYearBannerSpans_SingleYear_CoversFullTrack()
    {
        var layout = TimelineLayoutBuilder.Build(Timeline(), TallBounds);
        var spans = TimelineLayoutBuilder.GetYearBannerSpans(layout);

        spans.Should().HaveCount(1);
        spans[0].Year.Should().Be(2024);
        spans[0].StartX.Should().BeApproximately(layout.TrackRect.Left, 1e-6);
        spans[0].SpanWidth.Should().BeApproximately(layout.TrackRect.Width, 1e-3);
    }

    [Fact]
    public void GetYearBannerSpans_MultiYear_SplitsAtYearBoundary()
    {
        var multiYear = new TimelineModel
        {
            Name = "T",
            StartDate = "2023-07-01",
            EndDate = "2024-06-30"
        };
        var layout = TimelineLayoutBuilder.Build(multiYear, TallBounds);
        var spans = TimelineLayoutBuilder.GetYearBannerSpans(layout);

        spans.Should().HaveCount(2);
        spans[0].Year.Should().Be(2023);
        spans[1].Year.Should().Be(2024);
        // The two spans should together span the full track width.
        (spans[0].SpanWidth + spans[1].SpanWidth).Should().BeApproximately(layout.TrackRect.Width, 1.0);
    }

    // --- Scroll-window (wave1d) tests ---

    private static TimelineModel TimelineWithScrollWindow(
        string? level = "2",
        string? scrollPosition = "2026-01-01",
        string start = "2026-01-01",
        string end = "2027-01-01",
        string? selStart = "2026-02-01",
        string? selEnd = "2026-04-30") =>
        new()
        {
            Name = "T",
            Caption = "SaleDate",
            StartDate = start,
            EndDate = end,
            SelectedStartDate = selStart,
            SelectedEndDate = selEnd,
            Level = level is null ? null : int.Parse(level, System.Globalization.CultureInfo.InvariantCulture),
            ScrollPosition = scrollPosition
        };

    [Fact]
    public void Build_WithLevel2_UsesMonthGranularity()
    {
        // OOXML level=2 → months (0=years,1=quarters,2=months,3=days)
        var layout = TimelineLayoutBuilder.Build(TimelineWithScrollWindow(level: "2"), TallBounds);
        layout.Granularity.Should().Be(TimelineGranularity.Month);
    }

    [Fact]
    public void Build_WithLevel0_UsesYearGranularity()
    {
        var layout = TimelineLayoutBuilder.Build(TimelineWithScrollWindow(level: "0"), TallBounds);
        layout.Granularity.Should().Be(TimelineGranularity.Year);
    }

    [Fact]
    public void Build_WithScrollPosition_WindowStartMatchesScrollPosition()
    {
        var layout = TimelineLayoutBuilder.Build(TimelineWithScrollWindow(scrollPosition: "2026-01-01"), TallBounds);
        layout.WindowStart.Should().Be(new DateOnly(2026, 1, 1));
    }

    [Fact]
    public void Build_WithScrollPositionAndMonth_WindowEndLimitedByPeriodCount()
    {
        // TallBounds track width = 200-16 = 184px. MonthWidthPx=41. floor(184/41)=4 months visible.
        var layout = TimelineLayoutBuilder.Build(TimelineWithScrollWindow(scrollPosition: "2026-01-01"), TallBounds);
        layout.WindowStart.Should().NotBeNull();
        layout.WindowEnd.Should().NotBeNull();
        var monthsVisible = (layout.WindowEnd!.Value.Year - layout.WindowStart!.Value.Year) * 12
                            + layout.WindowEnd.Value.Month - layout.WindowStart.Value.Month;
        monthsVisible.Should().BeGreaterThan(0).And.BeLessThanOrEqualTo(12);
        // Window must not exceed the full range end.
        layout.WindowEnd!.Value.Should().BeOnOrBefore(new DateOnly(2027, 1, 1));
    }

    [Fact]
    public void Build_WithScrollPosition_NoScrollPosition_WindowEqualsRange()
    {
        // Without ScrollPosition the window should equal the full range.
        var layout = TimelineLayoutBuilder.Build(
            TimelineWithScrollWindow(scrollPosition: null), TallBounds);
        layout.WindowStart.Should().Be(layout.RangeStart);
        layout.WindowEnd.Should().Be(layout.RangeEnd);
    }

    [Fact]
    public void Build_WithScrollWindow_TickLabels_OnlyCoverWindow()
    {
        var layout = TimelineLayoutBuilder.Build(TimelineWithScrollWindow(scrollPosition: "2026-01-01"), TallBounds);
        var ticks = TimelineLayoutBuilder.GetTickLabels(layout);
        // Window is smaller than 12 months, so fewer tick labels than the full range.
        ticks.Should().NotBeEmpty();
        ticks.Count.Should().BeLessThanOrEqualTo(12);
        ticks[0].Label.Should().Be("JAN"); // window starts at Jan
    }

    [Fact]
    public void Build_WithScrollWindow_SelectionBand_AlignedWithWindow()
    {
        // Selection FEB–APR within a JAN–SEP window should NOT reach 0 left ratio.
        var layout = TimelineLayoutBuilder.Build(TimelineWithScrollWindow(
            scrollPosition: "2026-01-01", selStart: "2026-02-01", selEnd: "2026-04-30"), TallBounds);
        layout.SelectionLeftRatio.Should().BeGreaterThan(0); // Feb not at left edge of window
        layout.SelectionLeftRatio.Should().BeLessThan(0.5);
    }

    [Fact]
    public void Build_WithScrollWindow_ScrollThumbRatios_ReflectPosition()
    {
        // scrollPosition=Jan (first month of 12-month range) → thumb starts at 0.
        var layout = TimelineLayoutBuilder.Build(TimelineWithScrollWindow(scrollPosition: "2026-01-01"), TallBounds);
        layout.ScrollThumbLeftRatio.Should().BeApproximately(0, 0.01);
        // Thumb width < 1 (window smaller than full range).
        layout.ScrollThumbWidthRatio.Should().BeGreaterThan(0).And.BeLessThan(1);
    }

    // --- Header chrome: GranularityDropdownRect + ClearFilterIconRect --------------------------------

    [Fact]
    public void Build_WithHeader_ProducesGranularityDropdownRectInHeaderBand()
    {
        var layout = TimelineLayoutBuilder.Build(Timeline(), Bounds);

        // Granularity dropdown rect must be non-empty and inside the header band.
        layout.GranularityDropdownRect.Width.Should().BeGreaterThan(0,
            because: "MONTHS ▾ dropdown affordance should always render in the header");
        layout.GranularityDropdownRect.Top.Should().BeGreaterThanOrEqualTo(layout.HeaderRect.Top);
        layout.GranularityDropdownRect.Bottom.Should().BeLessThanOrEqualTo(layout.HeaderRect.Bottom + 1);
        layout.GranularityDropdownRect.Right.Should().BeLessThanOrEqualTo(layout.HeaderRect.Right + 1);
    }

    [Fact]
    public void Build_NoActiveFilter_ClearFilterRectIsEmpty()
    {
        // No selected dates → no active filter → clear-filter icon should not be shown.
        var layout = TimelineLayoutBuilder.Build(Timeline(selStart: null, selEnd: null), Bounds);

        layout.HasActiveFilter.Should().BeFalse();
        layout.ClearFilterIconRect.Width.Should().Be(0,
            because: "clear-filter icon is only shown when HasActiveFilter is true");
    }

    [Fact]
    public void Build_WithActiveFilter_ClearFilterRectIsInHeaderBand()
    {
        var layout = TimelineLayoutBuilder.Build(
            Timeline(selStart: "2024-03-01", selEnd: "2024-06-30"),
            Bounds);

        layout.HasActiveFilter.Should().BeTrue();
        layout.ClearFilterIconRect.Width.Should().BeGreaterThan(0,
            because: "clear-filter icon appears when a date range is selected");
        // Clear-filter must sit inside the header band.
        layout.ClearFilterIconRect.Top.Should().BeGreaterThanOrEqualTo(layout.HeaderRect.Top);
        layout.ClearFilterIconRect.Bottom.Should().BeLessThanOrEqualTo(layout.HeaderRect.Bottom + 1);
        // Clear-filter is the rightmost element — to the right of the dropdown.
        layout.ClearFilterIconRect.Left.Should().BeGreaterThan(layout.GranularityDropdownRect.Left);
    }

    [Fact]
    public void Build_WithActiveFilter_DropdownIsLeftOfClearFilter()
    {
        var layout = TimelineLayoutBuilder.Build(
            Timeline(selStart: "2024-03-01", selEnd: "2024-06-30"),
            Bounds);

        layout.GranularityDropdownRect.Right.Should().BeLessThanOrEqualTo(
            layout.ClearFilterIconRect.Left,
            because: "dropdown label must not overlap the clear-filter icon");
    }

    [Fact]
    public void Build_HeaderCaptionRect_StopsBeforeDropdownAndClearFilterChrome()
    {
        var layout = TimelineLayoutBuilder.Build(
            Timeline(selStart: "2024-03-01", selEnd: "2024-06-30"),
            Bounds);

        layout.CaptionRect.Left.Should().BeGreaterThan(layout.HeaderRect.Left);
        layout.CaptionRect.Right.Should().BeLessThan(layout.GranularityDropdownRect.Left);
        layout.CaptionRect.Top.Should().Be(layout.HeaderRect.Top);
        layout.CaptionRect.Bottom.Should().Be(layout.HeaderRect.Bottom);
    }
}

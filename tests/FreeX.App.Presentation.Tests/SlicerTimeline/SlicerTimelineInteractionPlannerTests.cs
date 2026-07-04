using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.SlicerTimeline;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.SlicerTimeline;

/// <summary>
/// Tests for the non-UI slicer/timeline interaction glue: turning a pointer hit on a laid-out
/// slicer or timeline into the matching Core.Commands mutation, via the portable layout builders.
/// No running UI.
/// </summary>
public sealed class SlicerTimelineInteractionPlannerTests
{
    private static readonly string[] AvailableItems = ["East", "North", "South", "West"];

    // ── Slicer tile click → SetSlicerSelectionCommand ────────────────────────

    [Fact]
    public void BuildSlicerToggleCommand_PlainClick_ReplacesSelectionWithClickedTile()
    {
        // H45: a plain click (additive: false, the default) replaces the whole selection with just
        // the clicked item, matching Excel — it does not toggle membership against the old selection.
        var slicer = new SlicerModel { Name = "Region", SourceFieldName = "Region" };
        slicer.SelectedItems.AddRange(["North", "South"]);
        var bounds = new LayoutRect(0, 0, 120, 160);
        var layout = SlicerLayoutBuilder.Build(slicer, AvailableItems, bounds);

        var tile = layout.Tiles.First(t => !t.IsAllPreview && t.Caption == "North");
        var command = SlicerTimelineInteractionPlanner.BuildSlicerToggleCommand(slicer, AvailableItems, layout, tile.Rect.Center);

        command.Should().NotBeNull().And.BeOfType<SetSlicerSelectionCommand>();

        var toggle = SlicerLayoutBuilder.Toggle(slicer, AvailableItems, "North");
        toggle.SelectedItems.Should().BeEquivalentTo("North");
    }

    [Fact]
    public void BuildSlicerToggleCommand_AdditiveClick_TogglesItemOff()
    {
        // Ctrl+click (additive: true) preserves the old toggle-membership behavior.
        var slicer = new SlicerModel { Name = "Region", SourceFieldName = "Region" };
        slicer.SelectedItems.AddRange(["North", "South"]);
        var bounds = new LayoutRect(0, 0, 120, 160);
        var layout = SlicerLayoutBuilder.Build(slicer, AvailableItems, bounds);

        var tile = layout.Tiles.First(t => !t.IsAllPreview && t.Caption == "North");
        var command = SlicerTimelineInteractionPlanner.BuildSlicerToggleCommand(slicer, AvailableItems, layout, tile.Rect.Center, additive: true);

        command.Should().NotBeNull().And.BeOfType<SetSlicerSelectionCommand>();

        var toggle = SlicerLayoutBuilder.Toggle(slicer, AvailableItems, "North", additive: true);
        toggle.SelectedItems.Should().BeEquivalentTo("South");
    }

    [Fact]
    public void BuildSlicerToggleCommand_AllPreviewTile_ReturnsNull()
    {
        // No selected items → a single synthetic "all" preview tile, which has no item to toggle.
        var slicer = new SlicerModel { Name = "Region", SourceFieldName = "Region" };
        var bounds = new LayoutRect(0, 0, 120, 160);
        var layout = SlicerLayoutBuilder.Build(slicer, AvailableItems, bounds);

        var preview = layout.Tiles.Single();
        preview.IsAllPreview.Should().BeTrue();

        SlicerTimelineInteractionPlanner.BuildSlicerToggleCommand(slicer, AvailableItems, layout, preview.Rect.Center)
            .Should().BeNull();
    }

    [Fact]
    public void BuildSlicerToggleCommand_PointMissesEveryTile_ReturnsNull()
    {
        var slicer = new SlicerModel { Name = "Region", SourceFieldName = "Region" };
        slicer.SelectedItems.AddRange(["North"]);
        var bounds = new LayoutRect(0, 0, 120, 160);
        var layout = SlicerLayoutBuilder.Build(slicer, AvailableItems, bounds);

        // A point far outside the body misses all tiles.
        SlicerTimelineInteractionPlanner.BuildSlicerToggleCommand(slicer, AvailableItems, layout, new LayoutPoint(1000, 1000))
            .Should().BeNull();
    }

    // ── Timeline hit → SetTimelineRangeCommand ───────────────────────────────

    [Fact]
    public void BuildTimelineRangeCommand_TrackHit_JumpsRangeToClickedBucket()
    {
        var timeline = new TimelineModel
        {
            Name = "OrderDate",
            SourceFieldName = "OrderDate",
            StartDate = "2024-01-01",
            EndDate = "2024-12-31",
        };
        var bounds = new LayoutRect(0, 0, 240, 80);
        var layout = TimelineLayoutBuilder.Build(timeline, bounds, TimelineGranularity.Month);

        // Click the far-left edge of the track → maps to January 2024.
        // At Month granularity, the period-snapped range must be (Jan-1, Jan-31).
        var leftEdge = new LayoutPoint(layout.TrackRect.Left, layout.TrackRect.Center.Y);
        var command = SlicerTimelineInteractionPlanner.BuildTimelineRangeCommand(timeline, layout, leftEdge);

        command.Should().NotBeNull().And.BeOfType<SetTimelineRangeCommand>();

        var hit = TimelineLayoutBuilder.HitTest(layout, leftEdge);
        hit.Kind.Should().Be(TimelineHitKind.Track);
        hit.Date.Should().Be(new DateOnly(2024, 1, 1));

        // QQ1: the command must span the whole month, not just a single day.
        command!.SelectedStartDate.Should().Be("2024-01-01");
        command!.SelectedEndDate.Should().Be("2024-01-31");
    }

    // ── QQ1: period-snapping for track/selection clicks ──────────────────────

    [Fact]
    public void BuildTimelineRangeCommand_MonthGranularity_TrackClick_SnapsToWholeMonth()
    {
        // Click somewhere in the middle of March 2024 → expects (Mar-1, Mar-31).
        var timeline = new TimelineModel
        {
            Name = "OrderDate",
            SourceFieldName = "OrderDate",
            StartDate = "2024-01-01",
            EndDate = "2024-12-31",
        };
        var bounds = new LayoutRect(0, 0, 480, 80);
        var layout = TimelineLayoutBuilder.Build(timeline, bounds, TimelineGranularity.Month);

        // Map a date in March 2024 back to a pixel X position on the track.
        var marchDate = new DateOnly(2024, 3, 15);
        var marchX = DateToX(layout, marchDate);
        var point = new LayoutPoint(marchX, layout.TrackRect.Center.Y);

        var command = SlicerTimelineInteractionPlanner.BuildTimelineRangeCommand(timeline, layout, point);

        command.Should().NotBeNull();
        command!.SelectedStartDate.Should().Be("2024-03-01");
        command!.SelectedEndDate.Should().Be("2024-03-31");
    }

    [Fact]
    public void BuildTimelineRangeCommand_MonthGranularity_FebruaryLeapYear_SnapsToFeb29()
    {
        // 2024 is a leap year → Feb ends on the 29th.
        var timeline = new TimelineModel
        {
            Name = "OrderDate",
            SourceFieldName = "OrderDate",
            StartDate = "2024-01-01",
            EndDate = "2024-12-31",
        };
        var bounds = new LayoutRect(0, 0, 480, 80);
        var layout = TimelineLayoutBuilder.Build(timeline, bounds, TimelineGranularity.Month);

        var febDate = new DateOnly(2024, 2, 10);
        var febX = DateToX(layout, febDate);
        var point = new LayoutPoint(febX, layout.TrackRect.Center.Y);

        var command = SlicerTimelineInteractionPlanner.BuildTimelineRangeCommand(timeline, layout, point);

        command.Should().NotBeNull();
        command!.SelectedStartDate.Should().Be("2024-02-01");
        command!.SelectedEndDate.Should().Be("2024-02-29");
    }

    [Fact]
    public void BuildTimelineRangeCommand_MonthGranularity_FebruaryNonLeapYear_SnapsToFeb28()
    {
        // 2023 is NOT a leap year → Feb ends on the 28th.
        var timeline = new TimelineModel
        {
            Name = "OrderDate",
            SourceFieldName = "OrderDate",
            StartDate = "2023-01-01",
            EndDate = "2023-12-31",
        };
        var bounds = new LayoutRect(0, 0, 480, 80);
        var layout = TimelineLayoutBuilder.Build(timeline, bounds, TimelineGranularity.Month);

        var febDate = new DateOnly(2023, 2, 14);
        var febX = DateToX(layout, febDate);
        var point = new LayoutPoint(febX, layout.TrackRect.Center.Y);

        var command = SlicerTimelineInteractionPlanner.BuildTimelineRangeCommand(timeline, layout, point);

        command.Should().NotBeNull();
        command!.SelectedStartDate.Should().Be("2023-02-01");
        command!.SelectedEndDate.Should().Be("2023-02-28");
    }

    [Fact]
    public void BuildTimelineRangeCommand_QuarterGranularity_ClickInQ3_SnapsToJul1_Sep30()
    {
        var timeline = new TimelineModel
        {
            Name = "OrderDate",
            SourceFieldName = "OrderDate",
            StartDate = "2024-01-01",
            EndDate = "2024-12-31",
        };
        var bounds = new LayoutRect(0, 0, 480, 80);
        var layout = TimelineLayoutBuilder.Build(timeline, bounds, TimelineGranularity.Quarter);

        // Mid-August falls in Q3.
        var augDate = new DateOnly(2024, 8, 15);
        var augX = DateToX(layout, augDate);
        var point = new LayoutPoint(augX, layout.TrackRect.Center.Y);

        var command = SlicerTimelineInteractionPlanner.BuildTimelineRangeCommand(timeline, layout, point);

        command.Should().NotBeNull();
        command!.SelectedStartDate.Should().Be("2024-07-01");
        command!.SelectedEndDate.Should().Be("2024-09-30");
    }

    [Fact]
    public void BuildTimelineRangeCommand_YearGranularity_ClickInYear_SnapsToJan1_Dec31()
    {
        var timeline = new TimelineModel
        {
            Name = "OrderDate",
            SourceFieldName = "OrderDate",
            StartDate = "2022-01-01",
            EndDate = "2025-12-31",
        };
        var bounds = new LayoutRect(0, 0, 480, 80);
        var layout = TimelineLayoutBuilder.Build(timeline, bounds, TimelineGranularity.Year);

        var midYear = new DateOnly(2023, 6, 15);
        var midX = DateToX(layout, midYear);
        var point = new LayoutPoint(midX, layout.TrackRect.Center.Y);

        var command = SlicerTimelineInteractionPlanner.BuildTimelineRangeCommand(timeline, layout, point);

        command.Should().NotBeNull();
        command!.SelectedStartDate.Should().Be("2023-01-01");
        command!.SelectedEndDate.Should().Be("2023-12-31");
    }

    [Fact]
    public void BuildTimelineRangeCommand_DayGranularity_TrackClick_KeepsSingleDay()
    {
        var timeline = new TimelineModel
        {
            Name = "OrderDate",
            SourceFieldName = "OrderDate",
            StartDate = "2024-03-01",
            EndDate = "2024-03-31",
        };
        var bounds = new LayoutRect(0, 0, 480, 80);
        var layout = TimelineLayoutBuilder.Build(timeline, bounds, TimelineGranularity.Day);

        var dayDate = new DateOnly(2024, 3, 15);
        var dayX = DateToX(layout, dayDate);
        var point = new LayoutPoint(dayX, layout.TrackRect.Center.Y);

        var command = SlicerTimelineInteractionPlanner.BuildTimelineRangeCommand(timeline, layout, point);

        command.Should().NotBeNull();
        command!.SelectedStartDate.Should().Be("2024-03-15");
        command!.SelectedEndDate.Should().Be("2024-03-15");
    }

    // ── QQ2: handle-drag snapping ─────────────────────────────────────────────

    [Fact]
    public void BuildTimelineRangeCommand_EndHandleDrag_MidSeptember_Month_SnapsToSep30()
    {
        // Existing selection: Mar–Jun. Drag end handle into mid-September.
        // At Month granularity, end handle must snap to Sep-30.
        var timeline = new TimelineModel
        {
            Name = "OrderDate",
            SourceFieldName = "OrderDate",
            StartDate = "2024-01-01",
            EndDate = "2024-12-31",
            SelectedStartDate = "2024-03-01",
            SelectedEndDate = "2024-06-30",
        };
        var bounds = new LayoutRect(0, 0, 480, 80);
        var layout = TimelineLayoutBuilder.Build(timeline, bounds, TimelineGranularity.Month);

        // Simulate end-handle drag: create a layout that puts the end handle somewhere in September.
        // We test PeriodBounds directly via BuildTimelineRangeCommand: construct a layout where the
        // end handle center maps to a September date, then call the planner.
        // Strategy: build a layout with selection ending at Sep-14 → end handle is at Sep-14.
        var dragTimeline = new TimelineModel
        {
            Name = "OrderDate",
            SourceFieldName = "OrderDate",
            StartDate = "2024-01-01",
            EndDate = "2024-12-31",
            SelectedStartDate = "2024-03-01",
            SelectedEndDate = "2024-09-14",   // end handle sits at Sep-14
        };
        var dragLayout = TimelineLayoutBuilder.Build(dragTimeline, bounds, TimelineGranularity.Month);

        // Click the end handle center: kind=EndHandle, date=Sep-14 (or close).
        var command = SlicerTimelineInteractionPlanner.BuildTimelineRangeCommand(
            dragTimeline, dragLayout, dragLayout.EndHandle.Rect.Center);

        command.Should().NotBeNull();
        // End handle drag snaps the end boundary to the LAST day of Sep.
        command!.SelectedEndDate.Should().Be("2024-09-30");
        // Start boundary stays at what was already selected (Mar-1).
        command!.SelectedStartDate.Should().Be("2024-03-01");
    }

    [Fact]
    public void BuildTimelineRangeCommand_StartHandleDrag_MidMarch_Quarter_SnapsToJan1()
    {
        // At Quarter granularity, dragging start handle to mid-March (Q1) snaps start to Jan-1.
        var dragTimeline = new TimelineModel
        {
            Name = "OrderDate",
            SourceFieldName = "OrderDate",
            StartDate = "2024-01-01",
            EndDate = "2024-12-31",
            SelectedStartDate = "2024-03-15",  // start handle sits at Mar-15 (within Q1)
            SelectedEndDate = "2024-09-30",
        };
        var bounds = new LayoutRect(0, 0, 480, 80);
        var layout = TimelineLayoutBuilder.Build(dragTimeline, bounds, TimelineGranularity.Quarter);

        var command = SlicerTimelineInteractionPlanner.BuildTimelineRangeCommand(
            dragTimeline, layout, layout.StartHandle.Rect.Center);

        command.Should().NotBeNull();
        // Start handle drag snaps the start boundary to the FIRST day of the Q1 period.
        command!.SelectedStartDate.Should().Be("2024-01-01");
        // End boundary stays as-is.
        command!.SelectedEndDate.Should().Be("2024-09-30");
    }

    [Fact]
    public void BuildTimelineRangeCommand_StartHandleHit_MovesOnlyStartBoundary()
    {
        var timeline = new TimelineModel
        {
            Name = "OrderDate",
            SourceFieldName = "OrderDate",
            StartDate = "2024-01-01",
            EndDate = "2024-12-31",
            SelectedStartDate = "2024-03-01",
            SelectedEndDate = "2024-09-30",
        };
        var bounds = new LayoutRect(0, 0, 240, 80);
        var layout = TimelineLayoutBuilder.Build(timeline, bounds, TimelineGranularity.Month);

        var command = SlicerTimelineInteractionPlanner.BuildTimelineRangeCommand(
            timeline, layout, layout.StartHandle.Rect.Center);

        command.Should().NotBeNull().And.BeOfType<SetTimelineRangeCommand>();

        var hit = TimelineLayoutBuilder.HitTest(layout, layout.StartHandle.Rect.Center);
        hit.Kind.Should().Be(TimelineHitKind.StartHandle);
        hit.Date.Should().NotBeNull();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Converts a date to the approximate X pixel position within the layout track,
    /// using the same linear day-ratio math as <see cref="TimelineLayoutBuilder.DateAt"/> in reverse.
    /// </summary>
    private static double DateToX(TimelineLayoutModel layout, DateOnly date)
    {
        var start = layout.WindowStart ?? layout.RangeStart;
        var end = layout.WindowEnd ?? layout.RangeEnd;
        if (start is null || end is null)
            return layout.TrackRect.Left;

        var totalDays = end.Value.DayNumber - start.Value.DayNumber;
        if (totalDays <= 0)
            return layout.TrackRect.Left;

        var ratio = Math.Clamp(
            (date.DayNumber - start.Value.DayNumber) / (double)totalDays, 0, 1);
        return layout.TrackRect.Left + ratio * layout.TrackRect.Width;
    }

    [Fact]
    public void BuildTimelineRangeCommand_MissesTimeline_ReturnsNull()
    {
        var timeline = new TimelineModel
        {
            Name = "OrderDate",
            SourceFieldName = "OrderDate",
            StartDate = "2024-01-01",
            EndDate = "2024-12-31",
        };
        var bounds = new LayoutRect(0, 0, 240, 80);
        var layout = TimelineLayoutBuilder.Build(timeline, bounds, TimelineGranularity.Month);

        // A point below the track (in the reserved bottom band) hits nothing.
        SlicerTimelineInteractionPlanner.BuildTimelineRangeCommand(timeline, layout, new LayoutPoint(120, 79))
            .Should().BeNull();
    }

    [Fact]
    public void BuildTimelineRangeCommand_UnknownFullRange_ReturnsNull()
    {
        // No start/end bounds → no date can be mapped from a track position.
        var timeline = new TimelineModel { Name = "OrderDate", SourceFieldName = "OrderDate" };
        var bounds = new LayoutRect(0, 0, 240, 80);
        var layout = TimelineLayoutBuilder.Build(timeline, bounds, TimelineGranularity.Month);

        SlicerTimelineInteractionPlanner.BuildTimelineRangeCommand(timeline, layout, layout.TrackRect.Center)
            .Should().BeNull();
    }

    // ── ClearFilter ─────────────────────────────────────────────────────────────

    [Fact]
    public void BuildSlicerClearFilterCommand_HitsClearFilterRect_ReturnsEmptySelectionCommand()
    {
        var slicer = new SlicerModel { Name = "Region", SourceFieldName = "Region", ShowCaption = true };
        slicer.SelectedItems.AddRange(["North", "South"]);
        var bounds = new LayoutRect(0, 0, 160, 80);
        var layout = SlicerLayoutBuilder.BuildFull(slicer, AvailableItems, bounds);

        // The layout must have an active filter and a non-zero ClearFilterIconRect.
        layout.HasActiveFilter.Should().BeTrue();
        layout.ClearFilterIconRect.Width.Should().BeGreaterThan(0);

        var command = SlicerTimelineInteractionPlanner.BuildSlicerClearFilterCommand(
            slicer, layout, layout.ClearFilterIconRect.Center);

        command.Should().NotBeNull().And.BeOfType<SetSlicerSelectionCommand>();
    }

    [Fact]
    public void BuildSlicerClearFilterCommand_NoActiveFilter_ReturnsNull()
    {
        // No selection → no active filter → clear should not be triggered.
        var slicer = new SlicerModel { Name = "Region", SourceFieldName = "Region", ShowCaption = true };
        var bounds = new LayoutRect(0, 0, 160, 80);
        var layout = SlicerLayoutBuilder.BuildFull(slicer, AvailableItems, bounds);

        layout.HasActiveFilter.Should().BeFalse();
        SlicerTimelineInteractionPlanner.BuildSlicerClearFilterCommand(
                slicer, layout, layout.ClearFilterIconRect.Center)
            .Should().BeNull();
    }

    [Fact]
    public void BuildSlicerClearFilterCommand_PointMissesClearFilterRect_ReturnsNull()
    {
        var slicer = new SlicerModel { Name = "Region", SourceFieldName = "Region", ShowCaption = true };
        slicer.SelectedItems.AddRange(["North"]);
        var bounds = new LayoutRect(0, 0, 160, 80);
        var layout = SlicerLayoutBuilder.BuildFull(slicer, AvailableItems, bounds);

        // A point in the tile body, far from the icon.
        SlicerTimelineInteractionPlanner.BuildSlicerClearFilterCommand(
                slicer, layout, layout.Tiles[0].Rect.Center)
            .Should().BeNull();
    }

    [Fact]
    public void BuildTimelineClearFilterCommand_HitsClearFilterRect_ReturnsClearRangeCommand()
    {
        var timeline = new TimelineModel
        {
            Name = "OrderDate",
            SourceFieldName = "OrderDate",
            StartDate = "2024-01-01",
            EndDate = "2024-12-31",
            SelectedStartDate = "2024-03-01",
            SelectedEndDate = "2024-06-30",
        };
        var bounds = new LayoutRect(0, 0, 300, 100);
        var layout = TimelineLayoutBuilder.Build(timeline, bounds, TimelineGranularity.Month);

        layout.HasActiveFilter.Should().BeTrue();
        layout.ClearFilterIconRect.Width.Should().BeGreaterThan(0);

        var command = SlicerTimelineInteractionPlanner.BuildTimelineClearFilterCommand(
            timeline, layout, layout.ClearFilterIconRect.Center);

        command.Should().NotBeNull().And.BeOfType<SetTimelineRangeCommand>();
        command!.SelectedStartDate.Should().BeNull();
        command.SelectedEndDate.Should().BeNull();
    }

    [Fact]
    public void BuildTimelineClearFilterCommand_NoActiveFilter_ReturnsNull()
    {
        var timeline = new TimelineModel
        {
            Name = "OrderDate",
            SourceFieldName = "OrderDate",
            StartDate = "2024-01-01",
            EndDate = "2024-12-31",
        };
        var bounds = new LayoutRect(0, 0, 300, 100);
        var layout = TimelineLayoutBuilder.Build(timeline, bounds, TimelineGranularity.Month);

        layout.HasActiveFilter.Should().BeFalse();
        SlicerTimelineInteractionPlanner.BuildTimelineClearFilterCommand(
                timeline, layout, layout.ClearFilterIconRect.Center)
            .Should().BeNull();
    }

    [Fact]
    public void BuildTimelineClearFilterCommand_PointMissesClearFilterRect_ReturnsNull()
    {
        var timeline = new TimelineModel
        {
            Name = "OrderDate",
            SourceFieldName = "OrderDate",
            StartDate = "2024-01-01",
            EndDate = "2024-12-31",
            SelectedStartDate = "2024-03-01",
            SelectedEndDate = "2024-06-30",
        };
        var bounds = new LayoutRect(0, 0, 300, 100);
        var layout = TimelineLayoutBuilder.Build(timeline, bounds, TimelineGranularity.Month);

        // Point on the track — not on the clear icon.
        SlicerTimelineInteractionPlanner.BuildTimelineClearFilterCommand(
                timeline, layout, layout.TrackRect.Center)
            .Should().BeNull();
    }

    // ── Granularity dropdown ─────────────────────────────────────────────────────

    [Fact]
    public void BuildTimelineGranularityCommand_HitsDropdownRect_ReturnsCycledLevel()
    {
        // Level=2 (Months) → cycling should yield 3 (Days).
        var timeline = new TimelineModel
        {
            Name = "OrderDate",
            SourceFieldName = "OrderDate",
            StartDate = "2024-01-01",
            EndDate = "2024-12-31",
            Level = 2, // Months
        };
        var bounds = new LayoutRect(0, 0, 300, 100);
        var layout = TimelineLayoutBuilder.Build(timeline, bounds, TimelineGranularity.Month);

        layout.GranularityDropdownRect.Width.Should().BeGreaterThan(0);

        var command = SlicerTimelineInteractionPlanner.BuildTimelineGranularityCommand(
            timeline, layout, layout.GranularityDropdownRect.Center);

        command.Should().NotBeNull().And.BeOfType<SetTimelineGranularityCommand>();
    }

    [Fact]
    public void BuildTimelineGranularityCommand_PointMissesDropdownRect_ReturnsNull()
    {
        var timeline = new TimelineModel
        {
            Name = "OrderDate",
            SourceFieldName = "OrderDate",
            StartDate = "2024-01-01",
            EndDate = "2024-12-31",
            Level = 2,
        };
        var bounds = new LayoutRect(0, 0, 300, 100);
        var layout = TimelineLayoutBuilder.Build(timeline, bounds, TimelineGranularity.Month);

        SlicerTimelineInteractionPlanner.BuildTimelineGranularityCommand(
                timeline, layout, layout.TrackRect.Center)
            .Should().BeNull();
    }

    // ── SetTimelineGranularityCommand.CycleLevel ──────────────────────────────

    [Theory]
    [InlineData(0, 1)] // Years → Quarters
    [InlineData(1, 2)] // Quarters → Months
    [InlineData(2, 3)] // Months → Days
    [InlineData(3, 0)] // Days → Years (wraps)
    [InlineData(null, 3)] // null (default Month=2) → Days
    public void CycleLevel_CyclesCorrectly(int? current, int expected)
    {
        SetTimelineGranularityCommand.CycleLevel(current).Should().Be(expected);
    }

    // ── Hit-test predicate: ClearFilter vs Tile — no overlap ────────────────────

    [Fact]
    public void SlicerHitTest_ClearFilterRect_DoesNotOverlapTiles()
    {
        // Verify that the ClearFilterIconRect sits entirely within the header band and does NOT
        // overlap any tile rectangle, so the priority ordering is moot in practice.
        var slicer = new SlicerModel { Name = "Region", SourceFieldName = "Region", ShowCaption = true };
        slicer.SelectedItems.AddRange(["North", "South"]);
        var bounds = new LayoutRect(0, 0, 160, 120);
        var layout = SlicerLayoutBuilder.BuildFull(slicer, AvailableItems, bounds);

        var clearRect = layout.ClearFilterIconRect;
        foreach (var tile in layout.Tiles)
        {
            // Rectangles must NOT intersect.
            var xOverlap = clearRect.Left < tile.Rect.Right && clearRect.Right > tile.Rect.Left;
            var yOverlap = clearRect.Top < tile.Rect.Bottom && clearRect.Bottom > tile.Rect.Top;
            (xOverlap && yOverlap).Should().BeFalse(
                $"ClearFilterIconRect overlaps tile '{tile.Caption}'");
        }
    }
}

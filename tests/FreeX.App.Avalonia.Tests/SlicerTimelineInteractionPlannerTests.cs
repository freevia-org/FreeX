using FluentAssertions;
using FreeX.App.Avalonia;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.SlicerTimeline;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

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
    public void BuildSlicerToggleCommand_TileHit_TogglesItemOff()
    {
        var slicer = new SlicerModel { Name = "Region", SourceFieldName = "Region" };
        slicer.SelectedItems.AddRange(["North", "South"]);
        var bounds = new LayoutRect(0, 0, 120, 160);
        var layout = SlicerLayoutBuilder.Build(slicer, AvailableItems, bounds);

        // Click the first selected tile (North) → toggling it off leaves {South}.
        var tile = layout.Tiles.First(t => !t.IsAllPreview && t.Caption == "North");
        var command = SlicerTimelineInteractionPlanner.BuildSlicerToggleCommand(slicer, AvailableItems, layout, tile.Rect.Center);

        command.Should().NotBeNull().And.BeOfType<SetSlicerSelectionCommand>();

        // The carried selection is the portable toggle's result.
        var toggle = SlicerLayoutBuilder.Toggle(slicer, AvailableItems, "North");
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

        // Click the far-left edge of the track → maps to the range start; a track click jumps the
        // whole range to that single bucket.
        var leftEdge = new LayoutPoint(layout.TrackRect.Left, layout.TrackRect.Center.Y);
        var command = SlicerTimelineInteractionPlanner.BuildTimelineRangeCommand(timeline, layout, leftEdge);

        command.Should().NotBeNull().And.BeOfType<SetTimelineRangeCommand>();

        var hit = TimelineLayoutBuilder.HitTest(layout, leftEdge);
        hit.Kind.Should().Be(TimelineHitKind.Track);
        hit.Date.Should().Be(new DateOnly(2024, 1, 1));
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
}

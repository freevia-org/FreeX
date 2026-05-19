using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class SlicerTimelinePlannerTests
{
    [Fact]
    public void BuildSlicerTiles_UsesSourceItemsWhenPresentAndTreatsEmptySelectionAsAllSelected()
    {
        var slicer = new SlicerModel
        {
            Name = "Region Slicer",
            SourceFieldName = "Region"
        };

        var tiles = SlicerTimelinePlanner.BuildSlicerTiles(slicer, ["West", "East", "west"]);

        tiles.Select(tile => tile.Caption).Should().Equal("East", "West");
        tiles.Should().OnlyContain(tile => tile.SlicerName == "Region Slicer");
        tiles.Should().OnlyContain(tile => tile.IsSelected);
    }

    [Fact]
    public void BuildSlicerTiles_FallsBackToSelectedItemsWhenSourceItemsAreUnavailable()
    {
        var slicer = new SlicerModel { Name = "Category Slicer" };
        slicer.SelectedItems.AddRange(["B", "A"]);

        var tiles = SlicerTimelinePlanner.BuildSlicerTiles(slicer, []);

        tiles.Select(tile => tile.Caption).Should().Equal("A", "B");
        tiles.Should().OnlyContain(tile => tile.IsSelected);
    }

    [Fact]
    public void ToggleSlicerSelection_MatchesExcelAllItemsClearBehavior()
    {
        SlicerTimelinePlanner.ToggleSlicerSelection(["A", "B"], [], "A")
            .Should()
            .Equal("B");

        SlicerTimelinePlanner.ToggleSlicerSelection(["A", "B"], ["A"], "B")
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void BuildTimelineItem_UsesSelectedDatesThenCacheDateBounds()
    {
        var timeline = new TimelineModel
        {
            Name = "Order Date Timeline",
            SourceFieldName = "Order Date",
            CacheName = "Fallback",
            StartDate = "2026-01-01",
            EndDate = "2026-12-31",
            SelectedStartDate = "2026-05-01"
        };

        var item = SlicerTimelinePlanner.BuildTimelineItem(timeline);

        item.Name.Should().Be("Order Date Timeline");
        item.FieldName.Should().Be("Order Date");
        item.SelectedStartDate.Should().Be("2026-05-01");
        item.SelectedEndDate.Should().Be("2026-12-31");
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(" 2026-05-19 ", "2026-05-19")]
    public void NormalizeTimelineDateInput_TrimsAndConvertsBlankToNull(string? value, string? expected)
    {
        SlicerTimelinePlanner.NormalizeTimelineDateInput(value).Should().Be(expected);
    }
}

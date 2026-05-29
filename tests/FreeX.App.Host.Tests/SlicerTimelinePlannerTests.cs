using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

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

    [Fact]
    public void NativeVisualFilters_ReturnAnchoredControlsForPivotsOnActiveSheet()
    {
        var workbook = new Workbook("NativeVisualFilters");
        var activeSheet = workbook.AddSheet("Pivot");
        var otherSheet = workbook.AddSheet("Other");
        activeSheet.PivotTables.Add(new PivotTableModel { Name = "PivotTable1" });
        otherSheet.PivotTables.Add(new PivotTableModel { Name = "PivotTable2" });
        var anchor = new DrawingAnchorRange(
            new DrawingAnchorPoint(1, 0, 1, 0),
            new DrawingAnchorPoint(4, 0, 8, 0));
        workbook.Slicers.Add(new SlicerModel
        {
            Name = "Region Slicer",
            SourcePivotTableName = "PivotTable1",
            DrawingAnchor = anchor
        });
        workbook.Slicers.Add(new SlicerModel
        {
            Name = "Other Slicer",
            SourcePivotTableName = "PivotTable2",
            DrawingAnchor = anchor
        });
        workbook.Timelines.Add(new TimelineModel
        {
            Name = "Date Timeline",
            SourcePivotTableName = "PivotTable1",
            DrawingAnchor = anchor
        });

        SlicerTimelinePlanner.GetNativeVisualSlicers(workbook, activeSheet)
            .Select(slicer => slicer.Name)
            .Should()
            .Equal("Region Slicer");
        SlicerTimelinePlanner.GetNativeVisualTimelines(workbook, activeSheet)
            .Select(timeline => timeline.Name)
            .Should()
            .Equal("Date Timeline");
    }

    [Fact]
    public void NativeVisualFilters_UsePivotNameLookupForLargeWorkbooks()
    {
        var workbook = new Workbook("NativeVisualFiltersLarge");
        var activeSheet = workbook.AddSheet("Pivot");
        var anchor = new DrawingAnchorRange(
            new DrawingAnchorPoint(1, 0, 1, 0),
            new DrawingAnchorPoint(4, 0, 8, 0));

        for (var index = 0; index < 6000; index++)
        {
            activeSheet.PivotTables.Add(new PivotTableModel { Name = $"Pivot{index}" });
            workbook.Slicers.Add(new SlicerModel
            {
                Name = $"Slicer{index}",
                SourcePivotTableName = $"Pivot{index}",
                DrawingAnchor = anchor
            });
            workbook.Timelines.Add(new TimelineModel
            {
                Name = $"Timeline{index}",
                SourcePivotTableName = $"Pivot{index}",
                DrawingAnchor = anchor
            });
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var slicers = SlicerTimelinePlanner.GetNativeVisualSlicers(workbook, activeSheet);
        var timelines = SlicerTimelinePlanner.GetNativeVisualTimelines(workbook, activeSheet);
        stopwatch.Stop();

        slicers.Should().HaveCount(6000);
        timelines.Should().HaveCount(6000);
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(750);
    }

    [Fact]
    public void NativeVisualFilters_AvoidNestedPivotScans()
    {
        var source = System.IO.File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "SlicerTimelinePlanner.cs"));

        source.Should().Contain("BuildActivePivotNameSet(activeSheet)");
        source.Should().Contain("activePivotNames.Contains(pivotTableName)");
        source.Should().NotContain("activeSheet.PivotTables.Any");
    }
}

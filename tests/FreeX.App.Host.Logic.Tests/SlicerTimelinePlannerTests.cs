using System.IO;

using FluentAssertions;
using FreeX.Core.Model;
using SlicerTimelinePlanner = FreeX.App.Presentation.SlicerTimeline.SlicerTimelinePanePlanner;

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
    public void HasActiveSlicerFilter_UsesStoredSelectedItemsLikeExcelClearFilterState()
    {
        var slicer = new SlicerModel { Name = "Region Slicer" };

        SlicerTimelinePlanner.HasActiveSlicerFilter(slicer).Should().BeFalse();

        slicer.SelectedItems.Add("East");

        SlicerTimelinePlanner.HasActiveSlicerFilter(slicer).Should().BeTrue();
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
        item.HasActiveFilter.Should().BeTrue();
    }

    [Fact]
    public void BuildTimelineItem_TreatsDisplayedCacheBoundsAsUnfilteredClearState()
    {
        var timeline = new TimelineModel
        {
            Name = "Order Date Timeline",
            CacheName = "Fallback",
            StartDate = "2026-01-01",
            EndDate = "2026-12-31"
        };

        var item = SlicerTimelinePlanner.BuildTimelineItem(timeline);

        item.SelectedStartDate.Should().Be("2026-01-01");
        item.SelectedEndDate.Should().Be("2026-12-31");
        item.HasActiveFilter.Should().BeFalse();
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

        var filters = SlicerTimelinePlanner.GetNativeVisualFilters(workbook, activeSheet);
        filters.Slicers.Select(slicer => slicer.Name).Should().Equal("Region Slicer");
        filters.Timelines.Select(timeline => timeline.Name).Should().Equal("Date Timeline");
    }

    [Fact]
    public void NativeVisualSlicers_IncludeTableSlicersAnchoredOnActiveSheetWithoutPivot()
    {
        var workbook = new Workbook("TableSlicerGate");
        var activeSheet = workbook.AddSheet("Tasks");
        var otherSheet = workbook.AddSheet("Other");
        var anchor = new DrawingAnchorRange(
            new DrawingAnchorPoint(6, 0, 0, 0),
            new DrawingAnchorPoint(8, 0, 4, 0));

        // Table slicer: no SourcePivotTableName, anchored on the active sheet => visible.
        workbook.Slicers.Add(new SlicerModel
        {
            Name = "Category",
            DrawingAnchor = anchor,
            SourceSheetName = "Tasks",
            SourceTableId = 1,
            SourceTableColumnId = 5,
        });
        // Table slicer anchored on a different sheet => hidden on the active sheet.
        workbook.Slicers.Add(new SlicerModel
        {
            Name = "OtherTableSlicer",
            DrawingAnchor = anchor,
            SourceSheetName = "Other",
        });

        SlicerTimelinePlanner.GetNativeVisualSlicers(workbook, activeSheet)
            .Select(slicer => slicer.Name)
            .Should()
            .Equal("Category");

        SlicerTimelinePlanner.GetNativeVisualSlicers(workbook, otherSheet)
            .Select(slicer => slicer.Name)
            .Should()
            .Equal("OtherTableSlicer");
    }

    [Fact]
    public void NativeVisualTimelines_IncludeAnchoredTimelineOnActiveSheetWhenPivotConnectionIsMissing()
    {
        var workbook = new Workbook("TimelineSheetAnchorGate");
        var activeSheet = workbook.AddSheet("Pivot");
        var otherSheet = workbook.AddSheet("Other");
        activeSheet.PivotTables.Add(new PivotTableModel { Name = "NativePivotSlicerTimeline" });
        otherSheet.PivotTables.Add(new PivotTableModel { Name = "OtherPivot" });
        var anchor = new DrawingAnchorRange(
            new DrawingAnchorPoint(6, 0, 0, 0),
            new DrawingAnchorPoint(9, 0, 4, 0));

        workbook.Timelines.Add(new TimelineModel
        {
            Name = "SaleDate",
            DrawingAnchor = anchor,
            SourceSheetName = "Pivot"
        });
        workbook.Timelines.Add(new TimelineModel
        {
            Name = "OtherDate",
            DrawingAnchor = anchor,
            SourceSheetName = "Other"
        });

        SlicerTimelinePlanner.GetNativeVisualTimelines(workbook, activeSheet)
            .Select(timeline => timeline.Name)
            .Should()
            .Equal("SaleDate");

        SlicerTimelinePlanner.GetNativeVisualTimelines(workbook, otherSheet)
            .Select(timeline => timeline.Name)
            .Should()
            .Equal("OtherDate");
    }

    [Fact]
    public void NativeVisualSlicers_ExcludeTableSlicerWithoutDrawingAnchor()
    {
        var workbook = new Workbook("TableSlicerNoAnchor");
        var activeSheet = workbook.AddSheet("Tasks");
        workbook.Slicers.Add(new SlicerModel
        {
            Name = "Category",
            SourceSheetName = "Tasks",
            SourceTableId = 1,
        });

        SlicerTimelinePlanner.GetNativeVisualSlicers(workbook, activeSheet).Should().BeEmpty();
    }

    [Fact]
    public void NativeVisualFilters_ReturnSharedEmptyCollectionsForFastPaths()
    {
        var workbook = new Workbook("NativeVisualFiltersEmpty");
        var activeSheet = workbook.AddSheet("Sheet1");

        var firstSlicers = SlicerTimelinePlanner.GetNativeVisualSlicers(workbook, activeSheet);
        var secondSlicers = SlicerTimelinePlanner.GetNativeVisualSlicers(workbook, activeSheet);
        var firstTimelines = SlicerTimelinePlanner.GetNativeVisualTimelines(workbook, activeSheet);
        var secondTimelines = SlicerTimelinePlanner.GetNativeVisualTimelines(workbook, activeSheet);

        firstSlicers.Should().BeEmpty();
        secondSlicers.Should().BeSameAs(firstSlicers);
        firstTimelines.Should().BeEmpty();
        secondTimelines.Should().BeSameAs(firstTimelines);

        var filters = SlicerTimelinePlanner.GetNativeVisualFilters(workbook, activeSheet);
        filters.Slicers.Should().BeEmpty();
        filters.Timelines.Should().BeEmpty();
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
    public void NativeVisualFilters_RebuildCachedPivotLookupWhenNamesChange()
    {
        var workbook = new Workbook("NativeVisualFiltersCacheInvalidation");
        var activeSheet = workbook.AddSheet("Pivot");
        activeSheet.PivotTables.Add(new PivotTableModel { Name = "PivotA" });
        var anchor = new DrawingAnchorRange(
            new DrawingAnchorPoint(1, 0, 1, 0),
            new DrawingAnchorPoint(4, 0, 8, 0));
        workbook.Slicers.Add(new SlicerModel
        {
            Name = "Region Slicer",
            SourcePivotTableName = "PivotB",
            DrawingAnchor = anchor
        });

        SlicerTimelinePlanner.GetNativeVisualFilters(workbook, activeSheet)
            .Slicers
            .Should()
            .BeEmpty();

        activeSheet.PivotTables[0].Name = "PivotB";

        SlicerTimelinePlanner.GetNativeVisualFilters(workbook, activeSheet)
            .Slicers
            .Select(slicer => slicer.Name)
            .Should()
            .Equal("Region Slicer");
    }

    [Fact]
    public void NativeVisualFilters_ReusesCachedPairedResultForUnchangedWorkbookState()
    {
        var (workbook, activeSheet) = CreateLargeNativeVisualFilterWorkbook(visibleControlCount: 4);

        var first = SlicerTimelinePlanner.GetNativeVisualFilters(workbook, activeSheet);
        var second = SlicerTimelinePlanner.GetNativeVisualFilters(workbook, activeSheet);

        second.Should().BeSameAs(first);
        second.Slicers.Should().BeSameAs(first.Slicers);
        second.Timelines.Should().BeSameAs(first.Timelines);
    }

    [Fact]
    public void NativeVisualFilters_RebuildCachedPairedResultWhenControlConnectionChanges()
    {
        var workbook = new Workbook("NativeVisualFiltersConnectionInvalidation");
        var activeSheet = workbook.AddSheet("Pivot");
        activeSheet.PivotTables.Add(new PivotTableModel { Name = "PivotA" });
        var anchor = new DrawingAnchorRange(
            new DrawingAnchorPoint(1, 0, 1, 0),
            new DrawingAnchorPoint(4, 0, 8, 0));
        var slicer = new SlicerModel
        {
            Name = "Region Slicer",
            SourcePivotTableName = "PivotA",
            DrawingAnchor = anchor
        };
        workbook.Slicers.Add(slicer);

        var visible = SlicerTimelinePlanner.GetNativeVisualFilters(workbook, activeSheet);
        visible.Slicers.Should().ContainSingle();

        slicer.SourcePivotTableName = "PivotB";

        var hidden = SlicerTimelinePlanner.GetNativeVisualFilters(workbook, activeSheet);
        hidden.Should().NotBeSameAs(visible);
        hidden.Slicers.Should().BeEmpty();
    }

    [BenchmarkFact]
    public void Benchmark_NativeVisualFiltersEmptyWorkbookFastPath_ReportsTiming()
    {
        const int iterations = 20_000;
        var workbook = new Workbook("NativeVisualFiltersEmpty");
        var activeSheet = workbook.AddSheet("Sheet1");

        for (var i = 0; i < 100; i++)
        {
            SlicerTimelinePlanner.GetNativeVisualSlicers(workbook, activeSheet).Should().BeEmpty();
            SlicerTimelinePlanner.GetNativeVisualTimelines(workbook, activeSheet).Should().BeEmpty();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        IReadOnlyList<SlicerModel>? slicers = null;
        IReadOnlyList<TimelineModel>? timelines = null;
        for (var i = 0; i < iterations; i++)
        {
            slicers = SlicerTimelinePlanner.GetNativeVisualSlicers(workbook, activeSheet);
            timelines = SlicerTimelinePlanner.GetNativeVisualTimelines(workbook, activeSheet);
        }

        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Console.WriteLine(
            "PERF NATIVE_VISUAL_FILTERS_EMPTY " +
            $"steps={iterations} total_ms={stopwatch.Elapsed.TotalMilliseconds:F2} " +
            $"mean_us={stopwatch.Elapsed.TotalMicroseconds / iterations:F2} " +
            $"allocated_bytes={allocatedBytes:N0}");

        slicers.Should().BeEmpty();
        timelines.Should().BeEmpty();
        stopwatch.Elapsed.TotalMilliseconds.Should().BeGreaterThan(0);
    }

    [BenchmarkFact]
    public void Benchmark_NativeVisualFiltersLargeWorkbookPairedCalls_ReportsTiming()
    {
        const int iterations = 100;
        var (workbook, activeSheet) = CreateLargeNativeVisualFilterWorkbook(visibleControlCount: 6000);

        for (var i = 0; i < 5; i++)
        {
            SlicerTimelinePlanner.GetNativeVisualFilters(workbook, activeSheet).Slicers.Should().HaveCount(6000);
            SlicerTimelinePlanner.GetNativeVisualFilters(workbook, activeSheet).Timelines.Should().HaveCount(6000);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        IReadOnlyList<SlicerModel>? slicers = null;
        IReadOnlyList<TimelineModel>? timelines = null;
        for (var i = 0; i < iterations; i++)
        {
            var filters = SlicerTimelinePlanner.GetNativeVisualFilters(workbook, activeSheet);
            slicers = filters.Slicers;
            timelines = filters.Timelines;
        }

        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Console.WriteLine(
            "PERF NATIVE_VISUAL_FILTERS_LARGE_PAIRED " +
            $"steps={iterations} controls=6000 total_ms={stopwatch.Elapsed.TotalMilliseconds:F2} " +
            $"mean_ms={stopwatch.Elapsed.TotalMilliseconds / iterations:F2} " +
            $"allocated_bytes={allocatedBytes:N0}");

        slicers.Should().HaveCount(6000);
        timelines.Should().HaveCount(6000);
        allocatedBytes.Should().BeLessThan(1_000_000);
        stopwatch.Elapsed.TotalMilliseconds.Should().BeGreaterThan(0);
    }

    [Fact]
    public void NativeVisualFilters_AvoidNestedPivotScans()
    {
        var hostRoot = DialogSourceTestSupport.FindHostSourceDirectory("MainWindow.xaml.cs");
        var source = DialogSourceTestSupport.ReadPresentationSources("SlicerTimeline", "SlicerTimelinePanePlanner.cs");

        File.Exists(Path.Combine(hostRoot, "SlicerTimelinePlanner.cs"))
            .Should().BeFalse("WPF should call the Presentation planner without a host facade");
        source.Should().Contain("BuildActivePivotNameSet(activeSheet)");
        source.Should().Contain("public static NativeVisualFilters GetNativeVisualFilters(Workbook workbook, Sheet activeSheet)");
        source.Should().Contain("return Array.Empty<SlicerModel>();");
        source.Should().Contain("return Array.Empty<TimelineModel>();");
        source.Should().Contain("List<SlicerModel>? visible = null;");
        source.Should().Contain("List<TimelineModel>? visible = null;");
        source.Should().Contain("visible ??= new List<SlicerModel>(slicers.Count);");
        source.Should().Contain("visible ??= new List<TimelineModel>(timelines.Count);");
        source.Should().Contain("ConditionalWeakTable<Sheet, ActivePivotNameSetCache>");
        source.Should().Contain("ConditionalWeakTable<Sheet, NativeVisualFilterCache>");
        source.Should().Contain("ReferenceEquals(_activePivotNames, activePivotNames)");
        source.Should().Contain("SlicersMatch(workbook.Slicers)");
        source.Should().Contain("TimelinesMatch(workbook.Timelines)");
        source.Should().Contain("new HashSet<string>(pivotTables.Count");
        source.Should().Contain("Matches(pivotTables)");
        source.Should().Contain("activePivotNames.Contains(pivotTableName)");
        source.Should().NotContain("activeSheet.PivotTables.Any");
    }

    [Fact]
    public void BuildSlicerTiles_AvoidsLinqMaterializationScaffolding()
    {
        var hostRoot = DialogSourceTestSupport.FindHostSourceDirectory("MainWindow.xaml.cs");
        var source = DialogSourceTestSupport.ReadPresentationSources("SlicerTimeline", "SlicerTimelinePanePlanner.cs");
        var buildSlicerTiles = source[
            source.IndexOf("public static IReadOnlyList<SlicerTileItem> BuildSlicerTiles", StringComparison.Ordinal)..
            source.IndexOf("public static IReadOnlyList<string> ToggleSlicerSelection", StringComparison.Ordinal)];

        File.Exists(Path.Combine(hostRoot, "SlicerTimelinePlanner.cs"))
            .Should().BeFalse("the shared planner should be the only implementation");
        buildSlicerTiles.Should().Contain("new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase)");
        buildSlicerTiles.Should().Contain("new List<SlicerTileItem>(items.Count)");
        buildSlicerTiles.Should().NotContain(".ToList()");
        buildSlicerTiles.Should().NotContain(".Distinct(");
        buildSlicerTiles.Should().NotContain(".OrderBy(");
        buildSlicerTiles.Should().NotContain(".Select(");
    }

    private static (Workbook Workbook, Sheet ActiveSheet) CreateLargeNativeVisualFilterWorkbook(int visibleControlCount)
    {
        var workbook = new Workbook("NativeVisualFiltersLarge");
        var activeSheet = workbook.AddSheet("Pivot");
        var anchor = new DrawingAnchorRange(
            new DrawingAnchorPoint(1, 0, 1, 0),
            new DrawingAnchorPoint(4, 0, 8, 0));

        for (var index = 0; index < visibleControlCount; index++)
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

        return (workbook, activeSheet);
    }
}

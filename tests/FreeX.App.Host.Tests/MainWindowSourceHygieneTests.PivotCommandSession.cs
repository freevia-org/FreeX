using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowSourceHygieneTests
{
    [Fact]
    public void PivotEditorsAndSlicerTimelineAdapters_DoNotConstructPortableCommands()
    {
        var source = DialogSourceTestSupport.ReadHostSources(
            "MainWindow.PivotCommands.cs",
            "MainWindow.PivotAdvancedCommands.cs",
            "MainWindow.PivotDesignCommands.cs",
            "MainWindow.PivotSlicerTimeline.cs");

        source.Should().NotContain("new ConfigurePivotTableFieldFiltersCommand(");
        source.Should().NotContain("new ConfigurePivotTableViewCommand(");
        source.Should().NotContain("new ConfigurePivotTableCalculatedItemsCommand(");
        source.Should().NotContain("new ConfigurePivotTableOptionsCommand(");
        source.Should().NotContain("new AddSlicerCommand(");
        source.Should().NotContain("new AddTimelineCommand(");
        source.Should().NotContain("new SetSlicerSelectionCommand(");
        source.Should().NotContain("new SetTimelineRangeCommand(");
        source.Should().NotContain("new SetTimelineGranularityCommand(");
        source.Should().Contain("PivotApplication.ReadSourceHeaders(");
        source.Should().Contain("PivotApplication.ReadSourceItems(");
        source.Should().Contain("PivotApplication.PlanFieldItemSelection(");
        source.Should().Contain("PivotApplication.PlanFieldView(");
        source.Should().Contain("PivotSortPlanner.ReplaceQuickSort(");
        source.Should().Contain("PivotApplication.PlanCalculatedConfiguration(");
        source.Should().Contain("PivotApplication.PlanDialogOptions(");
        source.Should().Contain("PivotApplication.PlanSlicerSelection(");
        source.Should().Contain("PivotApplication.PlanTimelineRange(");
    }
}

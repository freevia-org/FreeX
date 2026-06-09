using FluentAssertions;
using static FreeX.App.Host.Tests.LocalizedXamlTestSupport;

namespace FreeX.App.Host.Tests;

public sealed class DataCommandSourceTests
{
    [Theory]
    [InlineData("Sort A to Z", "SA", "SortAscButton_Click")]
    [InlineData("Sort Z to A", "SD", "SortDescButton_Click")]
    [InlineData("Filter", "T", "FilterButton_Click")]
    [InlineData("Clear", "C", "ClearFilterButton_Click")]
    [InlineData("Advanced", "A", "AdvancedFilterBtn_Click")]
    [InlineData("Reapply", "R", "FilterReapplyMenuItem_Click")]
    public void DataSortAndFilterCommands_ExposeExpectedTitlesKeyTipsAndHandlers(
        string title,
        string keyTip,
        string handler)
    {
        var button = ReadMainWindowXaml().ExtractButtonElementByInvariantCommandName(title, $"Click=\"{handler}\"");

        button.ShouldContainInvariantCommandName(title);
        button.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        button.Should().Contain($"Click=\"{handler}\"");
    }

    [Fact]
    public void DataSortAndFilterHandlers_RouteThroughExpectedCommandsAndPlanners()
    {
        var filterSource = ReadHostSourceFile("MainWindow.DataFilterCommands.cs");
        var dataSource = ReadHostSourceFile("MainWindow.DataCommands.cs");
        var editingDropdownSource = ReadHostSourceFile("MainWindow.EditingDropdowns.cs");

        filterSource.Should().Contain("new SortCommand(_currentSheetId, currentRange, sortByColOffset: 0, ascending: true)");
        filterSource.Should().Contain("new SortCommand(_currentSheetId, currentRange, sortByColOffset: 0, ascending: false)");
        filterSource.Should().Contain("new SortDialog(");
        var filterButtonHandler = SourceMethodExtractor.ExtractMethodSource(filterSource, "private void FilterButton_Click(");
        filterButtonHandler.Should().Contain("AutoFilterToggleRangePlanner.Create(sheet, selectedRange)");
        filterButtonHandler.Should().Contain("new ToggleWorksheetAutoFilterCommand(_currentSheetId, plannedRange)");
        filterButtonHandler.Should().NotContain("AutoFilterDialog");
        filterButtonHandler.Should().NotContain("ApplyFilterPrompt");
        filterSource.Should().NotContain("private void ApplyFilterPrompt(");
        editingDropdownSource.Should().Contain("AutoFilterDropdownPlanner.CreateMenuPlan(");
        filterSource.Should().Contain("FilterPromptPlanner.TryPlan(value, out var promptPlan, out var promptError)");
        filterSource.Should().Contain("new FilterCommand(_currentSheetId, currentRange, filterColOffset, allowedValues: allowedValues)");
        filterSource.Should().Contain("private void ClearFilterButton_Click(object sender, RoutedEventArgs e)");
        filterSource.Should().Contain("ClearFilterRangePlanner.Create(sheet, selectedRange)");
        filterSource.Should().Contain("ClearRememberedAutoFilterCommand();");
        filterSource.Should().Contain("private void ReapplyAutoFilter()");

        dataSource.Should().Contain("new AdvancedFilterDialog(");
        dataSource.Should().Contain("() => new AdvancedFilterCommand(");
        dataSource.Should().Contain("ApplyAdvancedFilterRangeSelection(dialog, request)");
    }

    [Fact]
    public void DataRibbonFilterButton_TogglesAutoFilterWithoutOpeningColumnDropdown()
    {
        var filterButton = ReadMainWindowXaml()
            .ExtractButtonElementByInvariantCommandName("Filter", "Click=\"FilterButton_Click\"");
        var filterSource = ReadHostSourceFile("MainWindow.DataFilterCommands.cs");
        var editingDropdownSource = ReadHostSourceFile("MainWindow.EditingDropdowns.cs");
        var viewportSource = ReadHostSourceFile("MainWindow.Viewport.cs");

        filterButton.Should().Contain("Click=\"FilterButton_Click\"");
        filterButton.Should().NotContain("ContextMenu");
        SourceMethodExtractor
            .ExtractMethodSource(filterSource, "private void FilterButton_Click(")
            .Should()
            .Contain("new ToggleWorksheetAutoFilterCommand")
            .And.NotContain("AutoFilterDialog");
        viewportSource.Should().Contain("SheetGrid.AutoFilterRange = sheet is not null &&");
        viewportSource.Should().Contain("AutoFilterDropdownPlanner.TryGetAutoFilterRange(sheet, out var autoFilterRange)");
        editingDropdownSource.Should().Contain("private AutoFilterDialog? CreateAutoFilterFlyoutDialog");
    }

    [Fact]
    public void DataQueriesAndConnectionsUnsupportedCommand_IsNotSurfacedAsDisabledRibbonButton()
    {
        var xaml = ReadMainWindowXaml();

        xaml.ShouldContainLocalizedAttribute("Text", "Queries &amp; Connections");
        xaml.ShouldContainInvariantCommandName("Refresh All");
        xaml.Should().NotContain("local:RibbonMetadata.CommandName=\"Queries &amp; Connections\"");
    }

}

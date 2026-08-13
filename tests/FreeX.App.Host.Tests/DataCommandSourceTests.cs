using FluentAssertions;
using static FreeX.App.Host.Tests.LocalizedXamlTestSupport;

namespace FreeX.App.Host.Tests;

public sealed class DataCommandSourceTests
{

    [Fact]
    public void DataSortAndFilterHandlers_RouteThroughExpectedCommandsAndPlanners()
    {
        var filterSource = ReadHostSourceFile("MainWindow.DataFilterCommands.cs");
        var dataSource = ReadHostSourceFile("MainWindow.DataCommands.cs");
        var editingDropdownSource = ReadHostSourceFile("MainWindow.EditingDropdowns.cs");

        filterSource.Should().Contain("currentRange => CreateQuickSortCommand(currentRange, ascending: true)");
        filterSource.Should().Contain("currentRange => CreateQuickSortCommand(currentRange, ascending: false)");
        filterSource.Should().Contain("QuickSortRangePlanner.Create(sheet, range, SheetGrid.ActiveCell)");
        filterSource.Should().Contain("QuickSortRangePlanner.HasLikelyHeaderRow(sheet, range)");
        filterSource.Should().NotContain("QuickAnalysisSelectionReader.Describe(");
        filterSource.Should().Contain("new SortDialog(");
        var filterButtonHandler = SourceMethodExtractor.ExtractMethodSource(filterSource, "private void FilterButton_Click(");
        filterButtonHandler.Should().Contain("AutoFilterToggleRangePlanner.Create(sheet, selectedRange)");
        filterButtonHandler.Should().Contain("new ToggleWorksheetAutoFilterCommand(_currentSheetId, range)");
        filterButtonHandler.Should().NotContain("AutoFilterToggleRangePlanner.Create(currentSheet, currentRange)");
        filterButtonHandler.Should().NotContain("AutoFilterDialog");
        filterButtonHandler.Should().NotContain("ApplyFilterPrompt");
        filterSource.Should().NotContain("private void ApplyFilterPrompt(");
        editingDropdownSource.Should().Contain("AutoFilterDropdownMenuPlanner.CreateMenuPlan(");
        editingDropdownSource.Should().Contain("WpfResourceKeyTextResolver.Resources.AutoFilter");
        filterSource.Should().Contain("_filterWorkflowSession.PlanDialogResult(");
        filterSource.Should().Contain("WorksheetFilterMessagePlanner.GetPlanErrorResourceKey(plan)");
        filterSource.Should().NotContain("new FilterCommand(_currentSheetId, currentRange, filterColOffset");
        filterSource.Should().Contain("private void ClearFilterButton_Click(object sender, RoutedEventArgs e)");
        filterSource.Should().Contain("AutoFilterToggleRangePlanner.Create(sheet, selectedRange)");
        filterSource.Should().Contain("AutoFilterDropdownMenuPlanner.HasActiveFilter(sheet, range)");
        filterSource.Should().NotContain("ClearFilterRangePlanner.");
        filterSource.Should().Contain("_filterWorkflowSession.CreateClearAllPlan(sheet, range)");
        filterSource.Should().Contain("private void ReapplyAutoFilter()");

        dataSource.Should().Contain("new AdvancedFilterDialog(");
        dataSource.Should().Contain("AdvancedFilterPlanner.CreateDefaultListRange(sheet, selected)");
        dataSource.Should().NotContain("AdvancedFilterDefaultListRangePlanner.");
        dataSource.Should().Contain("() => new AdvancedFilterCommand(");
        dataSource.Should().Contain("ApplyAdvancedFilterRangeSelection(dialog, request)");
        dataSource.Should().Contain("_filterWorkflowSession.RememberAdvancedFilter(");
    }

}

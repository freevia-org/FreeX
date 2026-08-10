using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class MainWindowPivotHeaderDropdownSourceTests
{
    [Fact]
    public void MainWindow_WiresRenderedPivotHeaderDropdownsToPivotFieldMenu()
    {
        var constructorSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");
        var viewportSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Viewport.cs");
        var handlerSource = DialogSourceTestSupport.ReadHostSources("MainWindow.PivotHeaderDropdowns.cs");
        var menuSource = DialogSourceTestSupport.ReadHostSources("MainWindow.PivotChartCommands.cs");
        var pivotSource = DialogSourceTestSupport.ReadHostSources("MainWindow.PivotCommands.cs");

        constructorSource.Should().Contain("SheetGrid.PivotHeaderDropdownRequested += OnPivotHeaderDropdownRequested;");
        viewportSource.Should().Contain("PivotHeaderDropdownPlanner.BuildTargets(_workbook, sheet)");
        viewportSource.Should().Contain("SheetGrid.PivotHeaderDropdowns = pivotHeaderDropdownTargets");
        handlerSource.Should().Contain("_pivotFieldMenuContextCaption = target.FieldCaption;");
        handlerSource.Should().Contain("PivotHeaderDropdownAxis.Page => PivotFieldDropZone.Filters");
        handlerSource.Should().Contain("SetActiveCell(headerCell);");
        handlerSource.Should().Contain("CreatePivotFieldContextMenu();");
        pivotSource.Should().Contain("return _pivotFieldMenuContextCaption;");
        pivotSource.Should().Contain("ShowPivotFieldFilterDialog(PivotFieldFilterDialogTab.SelectItems)");
        pivotSource.Should().Contain("ShowPivotFieldFilterDialog(PivotFieldFilterDialogTab.LabelFilters)");
        pivotSource.Should().Contain("ShowPivotFieldFilterDialog(PivotFieldFilterDialogTab.ValueFilters)");
        pivotSource.Should().Contain("new ConfigurePivotTableFieldFiltersCommand(");
        pivotSource.Should().Contain(".CreateFieldSelectionState(");
        pivotSource.Should().Contain("PivotFieldFilterPlanner.ResolveItemSelection(");
        pivotSource.Should().Contain("ToPivotHeaderArea(context.Zone)");
        pivotSource.Should().NotContain("SetFieldSelectedItems(pivotTable.RowFields");
        pivotSource.Should().NotContain("SetFieldSelectedItems(pivotTable.ColumnFields");
        pivotSource.Should().NotContain("SetFieldSelectedItems(pivotTable.PageFields");
        pivotSource.Should().Contain("new PivotSortOptionsDialog(");
        pivotSource.Should().Contain("ResolveValueFieldSettingsIndex(");
        menuSource.Should().Contain("PivotChartFieldContextMenuPlanner.BuildCommands(BuildPivotChartFieldContextMenuState())");
        menuSource.Should().Contain("PivotFieldMoreSortOptionsMenuItem_Click");
        menuSource.Should().Contain("PivotFieldFilterSummary.FormatClearFilterHeader(filterState)");
        menuSource.Should().Contain("PivotUiPlanner.ResolvePivotChartFieldArea(");
        menuSource.Should().NotContain("pivotTable.PageFields.Any(field => field.SourceFieldIndex");
        menuSource.Should().NotContain("pivotTable.ColumnFields.Any(field => field.SourceFieldIndex");
        PivotChartFieldContextMenuPlanner.MoreSortOptionsHeader.Should().Be("More Sort Options...");
    }
}

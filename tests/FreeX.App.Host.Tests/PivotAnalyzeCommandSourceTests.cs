using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class PivotAnalyzeCommandSourceTests
{

    [Fact]
    public void PivotAnalyzeHandlers_RouteThroughExpectedPivotCommandsDialogsAndPanes()
    {
        var pivotSource = DialogSourceTestSupport.ReadHostSources("MainWindow.PivotCommands.cs");
        var advancedSource = DialogSourceTestSupport.ReadHostSources("MainWindow.PivotAdvancedCommands.cs");
        var chartSource = DialogSourceTestSupport.ReadHostSources("MainWindow.PivotChartCommands.cs");

        pivotSource.Should().Contain("new RefreshPivotTableCommand(_currentSheetId, pivotTable.Name)");
        pivotSource.Should().Contain("new DrillDownPivotTableCommand(_currentSheetId, target.PivotTableName, target.PivotCell)");
        pivotSource.Should().Contain("PivotFieldListPane.Visibility = PivotFieldListPane.Visibility == Visibility.Visible");
        pivotSource.Should().Contain("new PivotTableDataSourceDialog(");
        pivotSource.Should().Contain("new ChangePivotTableSourceCommand(_currentSheetId, pivotTable.Name, sourceRange)");
        pivotSource.Should().Contain("new PivotTableNameDialog(pivotTable.Name)");
        pivotSource.Should().Contain("new RenamePivotTableCommand(sheet.Id, pivotTable.Name, dialog.Result.Name)");
        pivotSource.Should().Contain("ShowPivotTableOptionsDialog();");
        pivotSource.Should().Contain("new ClearPivotTableViewCommand(sheet.Id, pivotTable.Name)");
        pivotSource.Should().Contain("PivotUiPlanner.ResolvePivotTableSelectionRange(pivotTable)");
        pivotSource.Should().Contain("new MovePivotTableDialog(");
        pivotSource.Should().Contain("new MovePivotTableCommand(sheet.Id, pivotTable.Name, targetRange.Start)");
        pivotSource.Should().Contain("new InsertSlicerDialog(headers, fieldName)");
        pivotSource.Should().Contain("new AddSlicerCommand(dialog.Result.SlicerName, pivotTable.Name, dialog.Result.FieldName)");
        pivotSource.Should().Contain("new InsertTimelineDialog(headers, fieldName)");
        pivotSource.Should().Contain("new AddTimelineCommand(dialog.Result.TimelineName, pivotTable.Name, dialog.Result.DateFieldName)");
        pivotSource.Should().Contain("new PivotValueFieldSettingsDialog(current, context.Headers)");

        var designSource = DialogSourceTestSupport.ReadHostSources("MainWindow.PivotDesignCommands.cs");
        designSource.Should().Contain("private void PivotExpandCollapseButtonsBtn_Click(object sender, RoutedEventArgs e)");
        designSource.Should().Contain("showExpandCollapseButtons: !pivotTable.ShowExpandCollapseButtons");
        designSource.Should().Contain("private void PivotFieldHeadersBtn_Click(object sender, RoutedEventArgs e)");
        designSource.Should().Contain("showFieldHeaders: !pivotTable.ShowFieldHeaders");

        advancedSource.Should().Contain("new PivotFieldGroupingDialog(headers, currentField)");
        advancedSource.Should().Contain("PivotFieldGroupingDialog.CreateResult(");
        advancedSource.Should().Contain("new PivotCalculatedFieldDialog");
        advancedSource.Should().Contain("new PivotCalculatedItemDialog(headers, sourceIndex)");
        advancedSource.Should().Contain("new ConfigurePivotTableCalculatedItemsCommand(");

        chartSource.Should().Contain("new PivotChartTypeDialog(ChartType.Column)");
        chartSource.Should().Contain("ChartCommandWorkflowPlanner.BuildAddPivotChartCommand(");
        chartSource.Should().NotContain("new AddPivotChartCommand(");
        chartSource.Should().Contain("ChartCommandWorkflowPlanner.BuildChangePivotChartTypeCommand(");
        chartSource.Should().NotContain("new ChangePivotChartTypeCommand(");
        chartSource.Should().Contain("new PivotChartOptionsDialog(chart)");
        chartSource.Should().Contain("ChartCommandWorkflowPlanner.BuildPivotChartOptionsCommand(");
        chartSource.Should().NotContain("new ConfigurePivotChartOptionsCommand(");
    }

    [Fact]
    public void PivotAnalyzeContextualHandlers_RequireSelectionInsidePivotTable()
    {
        var pivotSource = DialogSourceTestSupport.ReadHostSources("MainWindow.PivotCommands.cs");
        var getActiveStart = pivotSource.IndexOf("private bool TryGetActivePivotTable", StringComparison.Ordinal);
        var getSelectedStart = pivotSource.IndexOf("private bool TryGetSelectedPivotTable", StringComparison.Ordinal);
        var getActiveSource = pivotSource[getActiveStart..getSelectedStart];
        var fieldListStart = pivotSource.IndexOf("private void PivotFieldListBtn_Click", StringComparison.Ordinal);
        var insertSlicerStart = pivotSource.IndexOf("private void PivotInsertSlicerBtn_Click", StringComparison.Ordinal);
        var contextualSource = pivotSource[fieldListStart..insertSlicerStart];

        getActiveSource.Should().Contain(
            "FindPivotTableContainingSelection(sheet, SheetGrid.SelectedRange)",
            "Excel enables PivotTable Analyze/Design commands only while the selection is inside the PivotTable");
        getActiveSource.Should().NotContain(
            "FindPivotTableForSelection(sheet, SheetGrid.SelectedRange)",
            "falling back to the first PivotTable would let contextual commands operate from ordinary cells");
        contextualSource.Should().Contain(
            "FindPivotTableContainingSelection(sheet, SheetGrid.SelectedRange)",
            "Field List and Change Data Source are PivotTable contextual commands");
        contextualSource.Should().NotContain(
            "FindPivotTableForSelection(sheet, SheetGrid.SelectedRange)",
            "these handlers should not use the non-contextual workbook fallback");
    }

    private static string ReadPivotAnalyzeTabXaml()
    {
        var xaml = LocalizedXamlTestSupport.ReadMainWindowXaml();
        var start = xaml.IndexOf("Header=\"{local:Loc Key=MainWindow_Header_PivotTableAnalyze}\"", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "the PivotTable Analyze contextual tab should be present");

        var end = xaml.IndexOf("x:Name=\"PivotTableDesignTab\"", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, "the PivotTable Design contextual tab should follow Analyze");
        return xaml[start..end];
    }

}

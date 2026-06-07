using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class PivotAnalyzeCommandSourceTests
{
    [Theory]
    [InlineData("PivotTable Name", "PivotTable Name", "N", "PivotTableNameBtn_Click")]
    [InlineData("PivotTable Options", "Options", "O", "PivotTableOptionsBtn_Click")]
    [InlineData("Show Details", "Show Details", "D", "PivotTableShowDetailsBtn_Click")]
    [InlineData("Field Settings", "Field Settings", "FS", "PivotFieldValueSettingsMenuItem_Click")]
    [InlineData("Group Field", "Group Field", "GF", "PivotGroupFieldBtn_Click")]
    [InlineData("Ungroup", "Ungroup", "UG", "PivotUngroupFieldBtn_Click")]
    [InlineData("Insert Slicer", "Insert Slicer", "IS", "PivotInsertSlicerBtn_Click")]
    [InlineData("Insert Timeline", "Insert Timeline", "IT", "PivotInsertTimelineBtn_Click")]
    [InlineData("Refresh", "Refresh", "R", "RefreshPivotTableBtn_Click")]
    [InlineData("Change Data Source", "Change Data Source", "CD", "PivotChangeDataSourceBtn_Click")]
    [InlineData("Clear", "Clear", "CL", "PivotTableClearBtn_Click")]
    [InlineData("Select", "Select", "SE", "PivotTableSelectBtn_Click")]
    [InlineData("Move PivotTable", "Move PivotTable", "M", "PivotTableMoveBtn_Click")]
    [InlineData("Calculated Field", "Calc Field", "CF", "PivotCalculatedFieldBtn_Click")]
    [InlineData("Calculated Item", "Calc Item", "CI", "PivotCalculatedItemBtn_Click")]
    [InlineData("PivotChart", "PivotChart", "PC", "PivotChartBtn_Click")]
    [InlineData("Change Chart Type", "Change Chart", "CT", "PivotChartChangeTypeBtn_Click")]
    [InlineData("PivotChart Options", "Chart Options", "CO", "PivotChartOptionsBtn_Click")]
    [InlineData("Field List", "Field List", "FL", "PivotFieldListBtn_Click")]
    [InlineData("+/- Buttons", "+/- Buttons", "PB", "PivotExpandCollapseButtonsBtn_Click")]
    [InlineData("Field Headers", "Field Headers", "FH", "PivotFieldHeadersBtn_Click")]
    public void PivotAnalyzeCommands_ExposeExpectedTitlesKeyTipsAndHandlers(
        string title,
        string content,
        string keyTip,
        string handler)
    {
        var button = ReadPivotAnalyzeTabXaml().ExtractButtonElementByInvariantCommandName(title);

        button.ShouldContainLocalizedAttribute("Content", content);
        button.ShouldContainInvariantCommandName(title);
        button.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        button.Should().Contain($"Click=\"{handler}\"");
    }

    [Fact]
    public void PivotAnalyzeRequestedBreadthCommands_AreEnabledAndRouted()
    {
        var xaml = ReadPivotAnalyzeTabXaml();

        foreach (var title in new[] { "PivotTable Name", "PivotTable Options", "Clear", "Select", "Move PivotTable" })
        {
            var button = xaml.ExtractButtonElementByInvariantCommandName(title);
            button.Should().NotContain("IsEnabled=\"False\"");
            button.Should().NotContain("Deferred");
            button.Should().Contain("Click=");
        }
    }

    [Theory]
    [InlineData("PivotTable Name", "MainWindow_TooltipDescription_RenameTheSelectedPivotTable")]
    [InlineData("Clear", "MainWindow_TooltipDescription_ClearFiltersSortStateAndItemSelectionsFromTheSelectedPivotTable")]
    [InlineData("Select", "MainWindow_TooltipDescription_SelectTheSelectedPivotTableReportRange")]
    [InlineData("Move PivotTable", "MainWindow_TooltipDescription_MoveTheSelectedPivotTableToAnotherLocationOnTheCurrentWorksheet")]
    public void PivotAnalyzeSupportedActionCommands_UseNonDeferredTooltipDescriptions(
        string title,
        string descriptionKey)
    {
        var button = ReadPivotAnalyzeTabXaml().ExtractButtonElementByInvariantCommandName(title);
        var resources = DialogSourceTestSupport.ReadHostSources("Resources\\Strings.resx");

        button.Should().Contain($"local:RibbonTooltip.Description=\"{{local:Loc Key={descriptionKey}}}\"");
        descriptionKey.Should().NotContain("Deferred");
        resources.Should().Contain($"<data name=\"{descriptionKey}\"");
    }

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
        pivotSource.Should().Contain("new PivotValueFieldSettingsDialog(current, headers)");

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
        chartSource.Should().Contain("new AddPivotChartCommand(_currentSheetId, pivotTable.Name, dialog.Result.ChartType");
        chartSource.Should().Contain("new ChangePivotChartTypeCommand(_currentSheetId, chart.Id, dialog.Result.ChartType)");
        chartSource.Should().Contain("new PivotChartOptionsDialog(chart)");
        chartSource.Should().Contain("new ConfigurePivotChartOptionsCommand(");
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

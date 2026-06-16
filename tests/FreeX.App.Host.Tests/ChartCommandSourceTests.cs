using FluentAssertions;
using static FreeX.App.Host.Tests.LocalizedXamlTestSupport;

namespace FreeX.App.Host.Tests;

public sealed class ChartCommandSourceTests
{

    [Fact]
    public void ChartHandlers_RouteThroughExpectedDialogsCommandsAndDeferredPath()
    {
        var source = ReadHostSourceFile("MainWindow.ChartCommands.cs");

        source.Should().Contain("private void InsertChartPickerBtn_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("new InsertChartDialog { Owner = this }");
        source.Should().Contain("InsertChartOfType(dialog.Result.ChartType)");
        source.Should().Contain("private void InsertChartOfType(ChartType type)");
        source.Should().Contain("ChartAuthoringPlanner.CanAuthor(type)");
        source.Should().Contain("ShowDeferredChartFamilyMessage();");
        source.Should().Contain("ChartDataSourcePlanner.ResolveInsertionRange(sheet, currentRange)");
        source.Should().Contain("new AddChartCommand(");
        source.Should().Contain("UiText.Get(\"MainWindowMessage_ChartFamilyDeferred\")");
        source.Should().Contain("private void ChangeChartTypeBtn_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("new ChangeChartTypeDialog(chart.Type)");
        source.Should().Contain("new ChangeChartTypeCommand(_currentSheetId, chart.Id, dialog.Result.ChartType)");
        source.Should().Contain("private void SelectChartDataSourceBtn_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("new SelectDataSourceDialog(");
        source.Should().Contain("resolveSheetId: ResolveSheetIdByName");
        source.Should().Contain("ChartInputParser.TryParseDataRange(dialog.Result.SourceRangeText, _currentSheetId, ResolveSheetIdByName, out var dataRange)");
        source.Should().Contain("new ChangeChartSourceCommand(");
        source.Should().Contain("private void MoveChartBtn_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("new MoveChartDialog(currentSheet.Name)");
        source.Should().Contain("private void ResizeSelectedChartObject()");
        source.Should().Contain("new ObjectSizeDialog(chart.Width, chart.Height, UiText.Get(\"MainWindowMessage_ObjectSizeTitle\"))");
        source.Should().Contain("new SetChartBoundsCommand(");
    }

    [Fact]
    public void InsertChartCommand_UsesVisiblePlacementAndSelectsInsertedChart()
    {
        var source = ReadHostSourceFile("MainWindow.ChartCommands.cs");

        source.Should().Contain("ChartInsertionPlacementPlanner.CreatePlacement(");
        source.Should().Contain("command = new AddChartCommand(");
        source.Should().Contain("SelectInsertedChart(command.ChartId)");
        source.Should().Contain("SheetGrid.SelectedObjectKind = FreeX.App.UI.ObjectKind.Chart");
    }

    [Fact]
    public void ChartCommands_PreferSelectedChartForCommandTargets()
    {
        var chartSource = ReadHostSourceFile("MainWindow.ChartCommands.cs");
        var commandExecutionSource = ReadHostSourceFile("MainWindow.CommandExecution.cs");

        chartSource.Should().Contain("GetSelectedChartOnCurrentSheet() is { } selectedChart");
        chartSource.Should().Contain("IsChartContextualRibbonTarget(selectedChart)");
        commandExecutionSource.Should().Contain("private ChartModel? GetSelectedChartOnCurrentSheet()");
        commandExecutionSource.Should().Contain("SheetGrid.SelectedObjectKind != FreeX.App.UI.ObjectKind.Chart");
        commandExecutionSource.Should().Contain("chart.Id == SheetGrid.SelectedObjectId");
    }

}

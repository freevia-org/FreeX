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
        source.Should().Contain("ChartInsertionPlanner.CreateEmbeddedChartPlan(");
        source.Should().Contain("ChartInsertionPlanner.BuildChartSheetCommand(");
        source.Should().NotContain("new AddChartCommand(");
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

        source.Should().Contain("new ChartInsertionViewport(");
        source.Should().Contain("command = plan.Command;");
        source.Should().Contain("SelectInsertedChart(command.ChartId)");
        source.Should().Contain("SheetGrid.SelectedObjectKind = FreeX.App.UI.ObjectKind.Chart");
    }

    [Fact]
    public void ChartCommands_PreferSelectedChartForCommandTargets()
    {
        var chartSource = ReadHostSourceFile("MainWindow.ChartCommands.cs");
        var commandExecutionSource = ReadHostSourceFile("MainWindow.CommandExecution.cs");

        chartSource.Should().Contain("ChartWorkflowTargetPlanner.FindSelectedOrFirstChart(sheet, GetSelectedChartIdOnCurrentSheet())");
        chartSource.Should().Contain("ChartWorkflowTargetPlanner.HasSelectedChart(sheet, GetSelectedChartIdOnCurrentSheet())");
        commandExecutionSource.Should().Contain("private ChartModel? GetSelectedChartOnCurrentSheet()");
        commandExecutionSource.Should().Contain("ChartWorkflowTargetPlanner.FindSelectedChart(sheet, GetSelectedChartIdOnCurrentSheet())");
        commandExecutionSource.Should().Contain("private Guid? GetSelectedChartIdOnCurrentSheet()");
        commandExecutionSource.Should().Contain("SheetGrid.SelectedObjectKind != FreeX.App.UI.ObjectKind.Chart");
        commandExecutionSource.Should().NotContain("chart.Id == SheetGrid.SelectedObjectId");
    }

    [Fact]
    public void ChartQuickFormatHandlers_UseSharedCyclers()
    {
        var chartSource = ReadHostSourceFile("MainWindow.ChartCommands.cs");
        var axisSource = ReadHostSourceFile("MainWindow.ChartAxisCommands.cs");
        var cyclerSource = DialogSourceTestSupport.ReadPresentationSources("Charts", "Editing", "ChartOptionCycler.cs");

        chartSource.Should().Contain("ChartQuickFormatCycler.NextSeriesColor(");
        chartSource.Should().Contain("ChartQuickFormatCycler.NextChartTitleFontSize(");
        chartSource.Should().Contain("ChartQuickFormatCycler.NextAxisTitleFontSize(");
        chartSource.Should().Contain("ChartQuickFormatCycler.NextLegendFontSize(");
        chartSource.Should().Contain("ChartQuickFormatCycler.NextDataLabelBorderThickness(");
        chartSource.Should().Contain("ChartQuickFormatCycler.NextPointDataLabelBorderThickness(");
        chartSource.Should().Contain("ChartQuickFormatCycler.NextPlotAreaBorderThickness(");
        chartSource.Should().Contain("ChartQuickFormatCycler.NextLegendBorderThickness(");
        chartSource.Should().Contain("ChartQuickFormatCycler.NextTrendlineThickness(");
        chartSource.Should().Contain("ChartQuickFormatCycler.NextComboLineSeries(chart)");
        chartSource.Should().Contain("ChartQuickFormatCycler.ReadFirstSeriesFormat(chart)");
        chartSource.Should().Contain("ChartQuickFormatCycler.MergeFirstSeriesFormat(chart, updated)");
        chartSource.Should().Contain("ChartQuickFormatCycler.NextSeriesDash(");
        chartSource.Should().Contain("ChartQuickFormatCycler.NextMarkerSize(");
        chartSource.Should().NotContain("IndexOfSeriesFormat");
        chartSource.Should().NotContain("PlotAreaBorderThickness: chart.PlotAreaBorderThickness >= 3");
        chartSource.Should().NotContain("LegendBorderThickness: chart.LegendBorderThickness >= 3");
        chartSource.Should().NotContain("TrendlineThickness: chart.TrendlineThickness >= 3");

        axisSource.Should().Contain("ChartQuickFormatCycler.NextSeriesColor(");
        axisSource.Should().Contain("ChartQuickFormatCycler.NextGridlineState(");
        axisSource.Should().Contain("ChartOptionCycler.NextAxisTickState(");
        axisSource.Should().Contain("ChartOptionCycler.TryGetAxisBounds(");
        chartSource.Should().Contain("ChartOptionCycler.GetNextSecondaryAxisSeries(");
        cyclerSource.Should().NotContain("public static CellColor NextSeriesColor(");
        cyclerSource.Should().NotContain("NextDataLabelPosition(");
        cyclerSource.Should().NotContain("NextGridlineState(");
        cyclerSource.Should().NotContain("GetNextComboLineSeries(");
    }

}

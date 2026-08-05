using FluentAssertions;
using static FreeX.App.Host.Tests.LocalizedXamlTestSupport;

namespace FreeX.App.Host.Tests;

public sealed class ChartCommandSourceTests
{

    [Fact]
    public void ChartHandlers_RouteThroughExpectedDialogsCommandsAndDeferredPath()
    {
        var source = ReadHostSourceFile("MainWindow.ChartCommands.cs");
        var workflowSource = DialogSourceTestSupport.ReadPresentationSources("Charts", "Editing", "ChartCommandWorkflowPlanner.cs");

        source.Should().Contain("private void InsertChartPickerBtn_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("new InsertChartDialog { Owner = this }");
        source.Should().Contain("InsertChartOfType(dialog.Result.ChartType)");
        source.Should().Contain("private void InsertChartOfType(ChartType type)");
        source.Should().Contain("ChartAuthoringPlanner.CanAuthor(type)");
        source.Should().Contain("ShowDeferredChartFamilyMessage();");
        source.Should().Contain("ChartCommandWorkflowPlanner.CreateEmbeddedChartPlan(");
        source.Should().Contain("ChartCommandWorkflowPlanner.BuildChartSheetCommand(");
        source.Should().NotContain("new AddChartCommand(");
        source.Should().Contain("UiText.Get(\"MainWindowMessage_ChartFamilyDeferred\")");
        source.Should().Contain("private void ChangeChartTypeBtn_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("new ChangeChartTypeDialog(chart.Type)");
        source.Should().Contain("ChartCommandWorkflowPlanner.BuildChangeTypeCommand(");
        source.Should().NotContain("new ChangeChartTypeCommand(");
        source.Should().Contain("private void SelectChartDataSourceBtn_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("new SelectDataSourceDialog(");
        source.Should().Contain("resolveSheetId: ResolveSheetIdByName");
        source.Should().Contain("ChartInputParser.TryParseDataRange(dialog.Result.SourceRangeText, _currentSheetId, ResolveSheetIdByName, out var dataRange)");
        source.Should().Contain("ChartCommandWorkflowPlanner.BuildChangeSourceCommand(");
        source.Should().NotContain("new ChangeChartSourceCommand(");
        source.Should().Contain("ChartCommandWorkflowPlanner.BuildRemoveSeriesCommand(");
        source.Should().NotContain("new RemoveChartSeriesCommand(");
        source.Should().Contain("ChartCommandWorkflowPlanner.BuildHiddenEmptyCellsCommand(");
        source.Should().NotContain("new ConfigureChartHiddenEmptyCellsCommand(");
        // The dialog's Switch Row/Column checkbox must reach the command (and reflect the
        // chart's current orientation when the dialog opens) — not be a silent no-op.
        source.Should().Contain("switchRowColumn: chart.SeriesInRows");
        source.Should().Contain("dialog.Result.SwitchRowColumn");
        workflowSource.Should().Contain("seriesInRows: switchRowColumn");
        source.Should().Contain("private void MoveChartBtn_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("new MoveChartDialog(currentSheet.Name)");
        source.Should().Contain("ChartCommandWorkflowPlanner.PlanMoveCommand(");
        source.Should().NotContain("new MoveChartCommand(");
        source.Should().NotContain("new MoveChartToNewSheetCommand(");
        source.Should().Contain("private void ResizeSelectedChartObject()");
        source.Should().Contain("new ObjectSizeDialog(chart.Width, chart.Height, UiText.Get(\"MainWindowMessage_ObjectSizeTitle\"))");
        source.Should().Contain("ChartCommandWorkflowPlanner.BuildBoundsCommand(");
        source.Should().NotContain("new SetChartBoundsCommand(");
    }

    [Fact]
    public void ChartHandlers_UseSharedWorkflowCommandDescriptorsForCoreDialogFlows()
    {
        var source = ReadHostSourceFile("MainWindow.ChartCommands.cs");
        var chartDialogSource = ReadHostSourceFile("ChartFormatDialogs.cs");

        source.Should().Contain("ChartWorkflowCommandCatalog.ChangeChartType");
        source.Should().Contain("ChartWorkflowCommandCatalog.SelectDataSource");
        source.Should().Contain("ChartWorkflowCommandCatalog.MoveChart");
        source.Should().Contain("ChartWorkflowCommandCatalog.FormatChartArea");
        chartDialogSource.Should().Contain("Width = ChartAreaFormatPlanner.DialogWidth;");
        chartDialogSource.Should().Contain("Height = ChartAreaFormatPlanner.DialogHeight;");
        source.Should().Contain("TryGetActiveNormalChart(ChartWorkflowCommandDescriptor command");
        source.Should().Contain("TryGetFirstChartForDialog(ChartWorkflowCommandDescriptor command");
        source.Should().Contain("UiText.Get(command.HostMissingSelectionMessageResourceKey)");
        source.Should().NotContain("TryGetActiveNormalChart(\"Change Chart Type\"");
        source.Should().NotContain("TryGetActiveNormalChart(\"Select Data Source\"");
        source.Should().NotContain("TryGetActiveNormalChart(\"Move Chart\"");
        source.Should().NotContain("TryGetFirstChartForDialog(\"Format Chart Area\"");
        source.Should().NotContain("Insert or select a chart before formatting the chart area.");
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
    public void ChartQuickFormatHandlers_UseSharedQuickCommandPlanner()
    {
        var chartSource = ReadHostSourceFile("MainWindow.ChartCommands.cs");
        var axisSource = ReadHostSourceFile("MainWindow.ChartAxisCommands.cs");
        var cyclerSource = DialogSourceTestSupport.ReadPresentationSources("Charts", "Editing", "ChartOptionCycler.cs");
        var axisPlannerSource = DialogSourceTestSupport.ReadPresentationSources("Charts", "Editing", "ChartAxisPlanner.cs");
        var quickPlannerSource = DialogSourceTestSupport.ReadPresentationSources("Charts", "Editing", "ChartQuickCommandPlanner.cs");
        var quickCatalogSource = DialogSourceTestSupport.ReadPresentationSources("Charts", "Editing", "ChartQuickCommandCatalog.cs");
        var workflowSource = DialogSourceTestSupport.ReadPresentationSources("Charts", "Editing", "ChartCommandWorkflowPlanner.cs");
        var commandExecutionSource = ReadHostSourceFile("MainWindow.CommandExecution.cs");

        chartSource.Should().Contain("ExecuteChartQuickCommand(ChartQuickCommandCatalog.DataLabelCategoryName)");
        chartSource.Should().Contain("ExecuteChartQuickCommand(ChartQuickCommandCatalog.TrendlineMovingAveragePeriod)");
        chartSource.Should().Contain("ExecuteChartQuickCommand(ChartQuickCommandCatalog.ComboSeries)");
        chartSource.Should().Contain("ExecuteChartQuickCommand(ChartQuickCommandCatalog.SeriesMarkerSize)");
        chartSource.Should().Contain("private void ExecuteChartQuickCommand(ChartQuickCommandDescriptor command)");
        chartSource.Should().Contain("UiText.Get(command.HostMissingSelectionMessageResourceKey)");
        chartSource.Should().Contain("command.HostUnsupportedMessageResourceKey is null");
        chartSource.Should().Contain("TryExecuteRepeatableChartQuickCommand(");
        chartSource.Should().NotContain("ChartQuickCommandPlanner.CanApply(");
        chartSource.Should().NotContain("ChartQuickCommandPlanner.Plan(");
        commandExecutionSource.Should().Contain("ChartCommandWorkflowPlanner.PlanQuickCommand(");
        workflowSource.Should().Contain("ChartQuickCommandPlanner.CanApply(chart, command.Command)");
        workflowSource.Should().Contain("ChartQuickCommandPlanner.Plan(chart, command.Command)");
        chartSource.Should().NotContain("ChartQuickFormatCycler.");
        chartSource.Should().NotContain("ChartOptionCycler.GetNextSecondaryAxisSeries(");
        chartSource.Should().NotContain("IndexOfSeriesFormat");
        chartSource.Should().NotContain("IndexOfPointDataLabelFormat");
        chartSource.Should().NotContain("Func<ChartModel, ChartLayoutOptions>");
        chartSource.Should().NotContain("\"First Slice Angle\"");
        chartSource.Should().NotContain("\"Combo Chart Series\"");
        chartSource.Should().NotContain("\"MainWindowMessage_ChartSelectForDataLabelOptions\"");
        chartSource.Should().NotContain("PlotAreaBorderThickness: chart.PlotAreaBorderThickness >= 3");
        chartSource.Should().NotContain("LegendBorderThickness: chart.LegendBorderThickness >= 3");
        chartSource.Should().NotContain("TrendlineThickness: chart.TrendlineThickness >= 3");

        quickCatalogSource.Should().Contain("public static class ChartQuickCommandCatalog");
        quickCatalogSource.Should().Contain("public sealed record ChartQuickCommandDescriptor");
        quickCatalogSource.Should().Contain("\"First Slice Angle\"");
        quickCatalogSource.Should().Contain("\"MainWindowMessage_ChartSelectForDataLabelOptions\"");
        quickCatalogSource.Should().Contain("\"Combo Chart Series\"");
        quickCatalogSource.Should().Contain("\"MainWindowMessage_ChartComboUnsupported\"");

        quickPlannerSource.Should().Contain("ChartQuickFormatCycler.NextSeriesColor(");
        quickPlannerSource.Should().Contain("ChartQuickFormatCycler.NextChartTitleFontSize(");
        quickPlannerSource.Should().Contain("ChartQuickFormatCycler.NextAxisTitleFontSize(");
        quickPlannerSource.Should().Contain("ChartQuickFormatCycler.NextLegendFontSize(");
        quickPlannerSource.Should().Contain("ChartQuickFormatCycler.NextDataLabelBorderThickness(");
        quickPlannerSource.Should().Contain("ChartQuickFormatCycler.NextPointDataLabelBorderThickness(");
        quickPlannerSource.Should().Contain("ChartQuickFormatCycler.NextPlotAreaBorderThickness(");
        quickPlannerSource.Should().Contain("ChartQuickFormatCycler.NextLegendBorderThickness(");
        quickPlannerSource.Should().Contain("ChartQuickFormatCycler.NextTrendlineThickness(");
        quickPlannerSource.Should().Contain("ChartQuickFormatCycler.NextComboLineSeries(chart)");
        quickPlannerSource.Should().Contain("ChartQuickFormatCycler.ReadFirstSeriesFormat(chart)");
        quickPlannerSource.Should().Contain("ChartQuickFormatCycler.MergeFirstSeriesFormat(chart, updated)");
        quickPlannerSource.Should().Contain("ChartQuickFormatCycler.NextSeriesDash(");
        quickPlannerSource.Should().Contain("ChartQuickFormatCycler.NextMarkerSize(");
        quickPlannerSource.Should().Contain("ChartOptionCycler.GetNextSecondaryAxisSeries(");

        axisSource.Should().Contain("ChartAxisPlanner.PlanQuickCommand(");
        axisSource.Should().Contain("ChartAxisPlanner.PlanLogScaleToggle");
        axisSource.Should().Contain("ChartAxisPlanner.PlanBoundsToggle");
        axisSource.Should().NotContain("ChartQuickFormatCycler.");
        axisSource.Should().NotContain("ChartOptionCycler.");
        axisPlannerSource.Should().Contain("ChartQuickFormatCycler.NextSeriesColor(");
        axisPlannerSource.Should().Contain("ChartQuickFormatCycler.NextGridlineState(");
        axisPlannerSource.Should().Contain("ChartOptionCycler.NextAxisTickState(");
        axisPlannerSource.Should().Contain("ChartOptionCycler.TryGetAxisBounds(");
        cyclerSource.Should().NotContain("public static CellColor NextSeriesColor(");
        cyclerSource.Should().NotContain("NextDataLabelPosition(");
        cyclerSource.Should().NotContain("NextGridlineState(");
        cyclerSource.Should().NotContain("GetNextComboLineSeries(");
    }

    [Fact]
    public void DeclarativeWpfChartFormatSurface_ExposesLegacyAxisCommands()
    {
        var ribbonSource = DialogSourceTestSupport.ReadRibbonDefinitionSource("FreeXRibbonDefinition.cs");
        var handlerMap = ReadHostSourceFile("Ribbon\\FreeXRibbonHandlerMap.g.cs");

        foreach (var label in new[]
        {
            "X Axis Ticks", "Y Axis Ticks", "X Axis Label Font", "Y Axis Label Font",
            "X Axis Label Angle", "Y Axis Label Angle", "X Axis Line", "Y Axis Line",
            "X Axis Number Format", "Y Axis Number Format", "X Gridline Style", "Y Gridline Style",
            "X Log Scale", "Y Log Scale",
        })
        {
            ribbonSource.Should().Contain(label);
            handlerMap.Should().Contain(label);
        }

        ribbonSource.Should().Contain("ChartFormatLegacyAxesGroup");
        handlerMap.Should().Contain("ChartXAxisLabelAngleBtn_Click");
        handlerMap.Should().Contain("ChartYAxisLogBtn_Click");
    }

    [Fact]
    public void WpfComboChartButton_UsesTheImmediateSharedToggle()
    {
        var source = ReadHostSourceFile("MainWindow.ChartCommands.cs");

        source.Should().Contain("private void ChartComboBtn_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("ExecuteChartQuickCommand(ChartQuickCommandCatalog.ComboToggle)");
    }

}

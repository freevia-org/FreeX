using System;
using System.Windows;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Charts.Editing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private void InsertChartButton_Click(object sender, RoutedEventArgs e)
        => InsertChartOfType(ChartType.Column);

    private void InsertEmbeddedChart() => InsertChartOfType(ChartType.Column);

    private void InsertChartSheet()
    {
        if (SheetGrid.SelectedRange is not { } range) return;

        AddChartSheetCommand? command = null;
        IWorkbookCommand CreateCommand()
        {
            var currentRange = SheetGrid.SelectedRange ?? range;
            var sheet = _workbook.GetSheet(_currentSheetId);
            command = ChartInsertionPlanner.BuildChartSheetCommand(
                sheet,
                _currentSheetId,
                currentRange,
                ChartType.Column,
                "Chart");
            return command;
        }

        var outcome = _commandBus.ExecuteRepeatable(_workbook.Id, CreateCommand);
        if (!outcome.Success)
        {
            ShowCommandError(outcome, "Insert Chart Sheet");
            return;
        }

        _repeatPostAction = null;
        if (command?.CreatedSheetId is { } createdSheetId)
        {
            _currentSheetId = createdSheetId;
            _groupedSheetIds.Clear();
            _groupedSheetIds.Add(_currentSheetId);
            _sheetGroupAnchor = _currentSheetId;
        }

        RefreshSheetTabs();
        UpdateViewport();
    }

    private void InsertChartOfType(ChartType type)
    {
        if (!ChartAuthoringPlanner.CanAuthor(type))
        {
            ShowDeferredChartFamilyMessage();
            return;
        }

        if (SheetGrid.SelectedRange is not { } range) return;
        AddChartCommand? command = null;
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Insert Chart",
                range,
                currentRange =>
                {
                    var sheet = _workbook.GetSheet(_currentSheetId);
                    var plan = sheet is null
                        ? ChartInsertionPlanner.CreateEmbeddedChartPlan(
                            _currentSheetId,
                            currentRange,
                            type,
                            "Chart",
                            ChartInsertionPlanner.DefaultPlacement)
                        : ChartInsertionPlanner.CreateEmbeddedChartPlan(
                            sheet,
                            currentRange,
                            type,
                            new ChartInsertionViewport(
                                SheetGrid.Viewport,
                                CalculateViewportAvailableWidth(SheetGrid.ActualWidth, SheetGrid.ActualRowHeaderWidth, _zoomLevel),
                                Math.Max(0, (SheetGrid.ActualHeight - SheetGrid.EffectiveColHeaderHeight) / _zoomLevel)),
                            "Chart");
                    command = plan.Command;
                    return command;
                }))
            return;

        UpdateViewport();
        if (command is not null)
            SelectInsertedChart(command.ChartId);
    }

    private void SelectInsertedChart(Guid chartId)
    {
        SheetGrid.SelectedObjectId = chartId;
        SheetGrid.SelectedObjectKind = FreeX.App.UI.ObjectKind.Chart;
        SheetGrid.InvalidateVisual();
    }

    private void InsertChartPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new InsertChartDialog { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        InsertChartOfType(dialog.Result.ChartType);
    }

    private void ChangeChartTypeBtn_Click(object sender, RoutedEventArgs e)
    {
        var command = ChartWorkflowCommandCatalog.ChangeChartType;
        if (!TryGetActiveNormalChart(command, out var chart))
            return;

        var dialog = new ChangeChartTypeDialog(chart.Type) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (!TryExecuteCommand(new ChangeChartTypeCommand(_currentSheetId, chart.Id, dialog.Result.ChartType), command.Label))
            return;

        UpdateViewport();
    }

    private void SelectChartDataSourceBtn_Click(object sender, RoutedEventArgs e)
    {
        var command = ChartWorkflowCommandCatalog.SelectDataSource;
        if (!TryGetActiveNormalChart(command, out var chart))
            return;

        SelectDataSourceDialog? dialog = null;
        dialog = new SelectDataSourceDialog(
            FormatRangeReference(chart.DataRange.Start, chart.DataRange.End),
            chart.FirstColIsCategories,
            request => ApplySelectDataSourceRangeSelection(dialog, request),
            sheetId: _currentSheetId,
            resolveSheetId: ResolveSheetIdByName,
            switchRowColumn: chart.SeriesInRows)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
            return;

        if (!ChartInputParser.TryParseDataRange(dialog.Result.SourceRangeText, _currentSheetId, ResolveSheetIdByName, out var dataRange))
        {
            _messageService.ShowWarning(
                UiText.Get("MainWindowMessage_ChartInvalidDataRange"),
                UiText.Get("MainWindowMessage_SelectDataSourceTitle"));
            return;
        }

        if (!TryExecuteCommand(
                new ChangeChartSourceCommand(
                    _currentSheetId,
                    chart.Id,
                    dataRange,
                    firstRowIsHeader: chart.FirstRowIsHeader,
                    firstColIsCategories: dialog.Result.FirstColumnIsCategories,
                    seriesInRows: dialog.Result.SwitchRowColumn),
                command.Label))
            return;

        UpdateViewport();
    }

    private void ApplySelectDataSourceRangeSelection(
        SelectDataSourceDialog? dialog,
        SelectDataSourceRangeSelectionRequest request)
    {
        if (dialog is null)
            return;

        BeginDialogRangeSelection(
            dialog,
            request.CollapseDialog,
            selectedRange => dialog.ApplyRangeSelection(FormatWorkbookRange(selectedRange)));
    }

    private void MoveChartBtn_Click(object sender, RoutedEventArgs e)
    {
        var command = ChartWorkflowCommandCatalog.MoveChart;
        if (!TryGetActiveNormalChart(command, out var chart))
            return;

        var currentSheet = _workbook.GetSheet(_currentSheetId);
        if (currentSheet is null)
            return;

        var dialog = new MoveChartDialog(currentSheet.Name) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (dialog.Result.TargetKind == MoveChartTargetKind.NewChartSheet)
        {
            if (!TryExecuteCommand(new MoveChartToNewSheetCommand(_currentSheetId, chart.Id, dialog.Result.TargetName), command.Label))
                return;

            var createdSheet = _workbook.GetSheet(dialog.Result.TargetName);
            if (createdSheet is not null)
                _currentSheetId = createdSheet.Id;
        }
        else
        {
            var targetSheet = _workbook.GetSheet(dialog.Result.TargetName);
            if (targetSheet is null)
            {
                _messageService.ShowWarning(
                    UiText.Get("MainWindowMessage_ChartTargetSheetNotFound"),
                    UiText.Get("MainWindowMessage_MoveChartTitle"));
                return;
            }

            if (!TryExecuteCommand(new MoveChartCommand(_currentSheetId, chart.Id, targetSheet.Id), command.Label))
                return;

            _currentSheetId = targetSheet.Id;
        }

        _groupedSheetIds.Clear();
        _groupedSheetIds.Add(_currentSheetId);
        _sheetGroupAnchor = _currentSheetId;
        RefreshSheetTabs();
        UpdateViewport();
    }

    private void ChartStylesBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetFirstChartForDialog("Chart Styles", "Insert or select a chart before choosing a chart style.", out var chart))
            return;

        var dialog = new ChartStyleDialog(chart) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (!TryExecuteCommand(new SetChartStyleCommand(_currentSheetId, chart.Id, dialog.Result.ChartStyleId), "Chart Styles"))
            return;

        UpdateViewport();
    }

    private void ResizeSelectedChartObject()
    {
        if (!TryGetActiveNormalChart("Chart Size", out var chart))
            return;

        var dialog = new ObjectSizeDialog(chart.Width, chart.Height, UiText.Get("MainWindowMessage_ObjectSizeTitle")) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (!TryExecuteCommand(
                new SetChartBoundsCommand(
                    _currentSheetId,
                    chart.Id,
                    chart.Left,
                    chart.Top,
                    dialog.Result.Width,
                    dialog.Result.Height),
                "Chart Size"))
            return;

        SheetGrid.SelectedObjectId = chart.Id;
        SheetGrid.SelectedObjectKind = FreeX.App.UI.ObjectKind.Chart;
        UpdateViewport();
    }

    private void FormatChartAreaBtn_Click(object sender, RoutedEventArgs e)
    {
        var command = ChartWorkflowCommandCatalog.FormatChartArea;
        if (!TryGetFirstChartForDialog(command, out var chart))
            return;

        var dialog = new ChartAreaLegendDialog(chart) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (!ApplyChartLayoutDialogResult(ChartWorkflowCaption(command), chart, dialog.Result.ToOptions()))
            return;

        UpdateViewport();
    }

    private bool TryGetActiveNormalChart(ChartWorkflowCommandDescriptor command, out ChartModel chart) =>
        TryGetActiveNormalChart(ChartWorkflowCaption(command), out chart);

    private bool TryGetFirstChartForDialog(ChartWorkflowCommandDescriptor command, out ChartModel chart) =>
        TryGetFirstChartForDialog(ChartWorkflowCaption(command), UiText.Get(command.HostMissingSelectionMessageResourceKey), out chart);

    private static string ChartWorkflowCaption(ChartWorkflowCommandDescriptor command) =>
        command.TitleResourceKey is { } resourceKey ? UiText.Get(resourceKey) : command.Label;

    private void ShowUnsupportedChartWorkflow(ChartWorkflowCommandDescriptor command)
    {
        var message = command.HostUnsupportedMessageResourceKey is { } resourceKey
            ? UiText.Get(resourceKey)
            : UiText.Get(ChartWorkflowCommandCatalog.DefaultHostMissingSelectionMessageResourceKey);
        ShowCommandError(new CommandOutcome(false, message), ChartWorkflowCaption(command));
    }

    private bool TryGetActiveNormalChart(string caption, out ChartModel chart)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        chart = null!;
        chart = ChartWorkflowTargetPlanner.FindSelectedOrFirstChart(sheet, GetSelectedChartIdOnCurrentSheet())!;

        if (chart is not null)
            return true;

        _messageService.ShowInfo(UiText.Get("MainWindowMessage_ChartSelectBeforeCommand"), caption);
        return false;
    }

    private void RefreshChartContextualTabs()
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        SetChartContextualTabsVisible(ChartWorkflowTargetPlanner.HasSelectedChart(sheet, GetSelectedChartIdOnCurrentSheet()));
    }

    private void SetChartContextualTabsVisible(bool visible)
    {
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (ChartDesignTab is not null)
            ChartDesignTab.Visibility = visibility;
        if (ChartFormatTab is not null)
            ChartFormatTab.Visibility = visibility;

        if (!visible &&
            RibbonTabs is not null &&
            (ReferenceEquals(RibbonTabs.SelectedItem, ChartDesignTab) ||
             ReferenceEquals(RibbonTabs.SelectedItem, ChartFormatTab)))
        {
            RibbonTabs.SelectedIndex = 1;
        }

        InvalidateVisibleKeyTipElementCache();
    }

    private void ChartColumnMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Column);
    private void ChartStackedColumnMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.StackedColumn);
    private void ChartPercentStackedColumnMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.PercentStackedColumn);
    private void ChartLineMenuItem_Click(object sender, RoutedEventArgs e)   => InsertChartOfType(ChartType.Line);
    private void Chart3DLineMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.ThreeDLine);
    private void ChartPieMenuItem_Click(object sender, RoutedEventArgs e)    => InsertChartOfType(ChartType.Pie);
    private void Chart3DPieMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.ThreeDPie);
    private void ChartDoughnutMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Doughnut);
    private void ChartBarMenuItem_Click(object sender, RoutedEventArgs e)    => InsertChartOfType(ChartType.Bar);
    private void ChartStackedBarMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.StackedBar);
    private void ChartPercentStackedBarMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.PercentStackedBar);
    private void ChartAreaMenuItem_Click(object sender, RoutedEventArgs e)   => InsertChartOfType(ChartType.Area);
    private void Chart3DAreaMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.ThreeDArea);
    private void ChartScatterMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Scatter);
    private void ChartBubbleMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Bubble);
    private void ChartRadarMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Radar);
    private void ChartStockMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Stock);
    private void ChartSurfaceMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Surface);
    private void Chart3DSurfaceMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.ThreeDSurface);
    private void Chart3DColumnMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.ThreeDColumn);
    private void Chart3DBarMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.ThreeDBar);
    private void ChartTreemapMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Treemap);
    private void ChartSunburstMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Sunburst);
    private void ChartHistogramMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Histogram);
    private void ChartParetoMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Pareto);
    private void ChartBoxAndWhiskerMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.BoxAndWhisker);
    private void ChartWaterfallMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Waterfall);
    private void ChartFunnelMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Funnel);
    private void DeferredChartFamilyMenuItem_Click(object sender, RoutedEventArgs e) => ShowDeferredChartFamilyMessage();

    private void ShowDeferredChartFamilyMessage() =>
        _messageService.ShowInfo(
            UiText.Get("MainWindowMessage_ChartFamilyDeferred"),
            UiText.Get("MainWindowMessage_ChartFamilyDeferredTitle"));

    private void ChartFirstSliceAngleBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.FirstSliceAngle);
    }

    private void ChartDoughnutHoleSizeBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.DoughnutHoleSize);
    }

    private void ChartExplodedSliceBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.ExplodedSlice);
    }

    private void ChartBarFormatBtn_Click(object sender, RoutedEventArgs e)
    {
        var command = ChartWorkflowCommandCatalog.FormatBarColumn;
        var caption = ChartWorkflowCaption(command);
        if (!TryGetFirstChartForDialog(command, out var chart))
            return;

        if (!ChartWorkflowCommandCatalog.CanOpenDialog(chart, command))
        {
            ShowUnsupportedChartWorkflow(command);
            return;
        }

        var dialog = new ChartBarFormatDialog(chart) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        ApplyChartLayoutDialogResult(caption, chart, dialog.Result.ToOptions());
    }

    private void ChartBubbleFormatBtn_Click(object sender, RoutedEventArgs e)
    {
        var command = ChartWorkflowCommandCatalog.FormatBubbleChart;
        var caption = ChartWorkflowCaption(command);
        if (!TryGetFirstChartForDialog(command, out var chart))
            return;

        if (!ChartWorkflowCommandCatalog.CanOpenDialog(chart, command))
        {
            ShowUnsupportedChartWorkflow(command);
            return;
        }

        var dialog = new ChartBubbleFormatDialog(chart) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        ApplyChartLayoutDialogResult(caption, chart, dialog.Result.ToOptions());
    }

    private void ChartPieFormatBtn_Click(object sender, RoutedEventArgs e)
    {
        var command = ChartWorkflowCommandCatalog.FormatPieDoughnut;
        var caption = ChartWorkflowCaption(command);
        if (!TryGetFirstChartForDialog(command, out var chart))
            return;

        if (!ChartWorkflowCommandCatalog.CanOpenDialog(chart, command))
        {
            ShowUnsupportedChartWorkflow(command);
            return;
        }

        var dialog = new ChartPieFormatDialog(chart) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        ApplyChartLayoutDialogResult(caption, chart, dialog.Result.ToOptions());
    }

    private void ChartStockFormatBtn_Click(object sender, RoutedEventArgs e)
    {
        var command = ChartWorkflowCommandCatalog.FormatStockChart;
        var caption = ChartWorkflowCaption(command);
        if (!TryGetFirstChartForDialog(command, out var chart))
            return;

        if (!ChartWorkflowCommandCatalog.CanOpenDialog(chart, command))
        {
            ShowUnsupportedChartWorkflow(command);
            return;
        }

        var dialog = new ChartStockFormatDialog(chart) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        ApplyChartLayoutDialogResult(caption, chart, dialog.Result.ToOptions());
    }

    private void ChartDataLabelsBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowChartDataLabelsDialog();
    }

    private void ShowChartDataLabelsDialog()
    {
        var command = ChartWorkflowCommandCatalog.FormatDataLabels;
        var caption = ChartWorkflowCaption(command);
        if (!TryGetFirstChartForDialog(command, out var chart))
            return;

        var dialog = new ChartDataLabelsDialog(chart) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        ApplyChartLayoutDialogResult(caption, chart, dialog.Result.ToOptions());
    }

    private void ChartDataLabelPositionBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowChartDataLabelsDialog();
    }

    private void ChartDataLabelCategoryBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.DataLabelCategoryName);
    }

    private void ChartDataLabelSeriesBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.DataLabelSeriesName);
    }

    private void ChartDataLabelPercentageBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.DataLabelPercentage);
    }

    private void ChartDataLabelSeparatorBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.DataLabelSeparator);
    }

    private void ChartDataLabelNumberFormatBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.DataLabelNumberFormat);
    }

    private void ChartDataLabelCalloutBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.DataLabelCallout);
    }

    private void ChartDataLabelFillBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.DataLabelFill);
    }

    private void ChartDataLabelTextBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.DataLabelTextColor);
    }

    private void ChartDataLabelBorderBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.DataLabelBorder);
    }

    private void ChartDataLabelSizeBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.DataLabelFontSize);
    }

    private void ChartDataLabelAngleBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.DataLabelAngle);
    }

    private void ChartPointDataLabelBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.PointDataLabel);
    }

    private void ChartAreaFillBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.ChartAreaFill);
    }

    private void ChartTitleColorBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.ChartTitleColor);
    }

    private void ChartTitlesBtn_Click(object sender, RoutedEventArgs e)
    {
        var command = ChartWorkflowCommandCatalog.ChartTitles;
        var caption = ChartWorkflowCaption(command);
        if (!TryGetFirstChartForDialog(command, out var chart))
            return;

        var dialog = new ChartTitlesDialog(chart.Title, chart.XAxisTitle, chart.YAxisTitle) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        ApplyChartLayoutDialogResult(caption, chart, dialog.Result.ToOptions());
    }

    private void ChartTitleSizeBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.ChartTitleFontSize);
    }

    private void ChartAxisTitleColorBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.AxisTitleColor);
    }

    private void ChartAxisTitleSizeBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.AxisTitleFontSize);
    }

    private void ChartPlotAreaFillBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.PlotAreaFill);
    }

    private void ChartPlotAreaBorderBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.PlotAreaBorder);
    }

    private void ChartLegendTextBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.LegendTextColor);
    }

    private void ChartLegendFillBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.LegendFill);
    }

    private void ChartLegendBorderBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.LegendBorder);
    }

    private void ChartLegendSizeBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.LegendFontSize);
    }

    private void ChartLegendOverlayBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.LegendOverlay);
    }

    private void ChartTrendlineBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowChartTrendlineDialog();
    }

    private void ShowChartTrendlineDialog()
    {
        var command = ChartWorkflowCommandCatalog.FormatTrendline;
        var caption = ChartWorkflowCaption(command);
        if (!TryGetFirstChartForDialog(command, out var chart))
            return;

        if (!ChartWorkflowCommandCatalog.CanOpenDialog(chart, command))
        {
            ShowUnsupportedChartWorkflow(command);
            return;
        }

        var dialog = new ChartTrendlineOptionsDialog(chart) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        ApplyChartLayoutDialogResult(caption, chart, dialog.Result.ToOptions());
    }

    private void ChartTrendlineTypeBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowChartTrendlineDialog();
    }

    private void ChartTrendlinePeriodBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.TrendlineMovingAveragePeriod);
    }

    private void ChartTrendlineOrderBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.TrendlinePolynomialOrder);
    }

    private void ChartTrendlineEquationBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.TrendlineEquation);
    }

    private void ChartTrendlineRSquaredBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.TrendlineRSquared);
    }

    private void ChartTrendlineColorBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.TrendlineColor);
    }

    private void ChartTrendlineDashBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.TrendlineDash);
    }

    private void ChartTrendlineWidthBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.TrendlineThickness);
    }

    private void ChartErrorBarsBtn_Click(object sender, RoutedEventArgs e)
    {
        var command = ChartWorkflowCommandCatalog.FormatErrorBars;
        var caption = ChartWorkflowCaption(command);
        if (!TryGetFirstChartForDialog(command, out var chart))
            return;

        if (!ChartWorkflowCommandCatalog.CanOpenDialog(chart, command))
        {
            ShowUnsupportedChartWorkflow(command);
            return;
        }

        var dialog = new ChartErrorBarsDialog(chart) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (!ApplyChartLayoutDialogResult(caption, chart, dialog.Result.ToOptions()))
            return;

        UpdateViewport();
    }

    private void ChartSecondaryAxisSeriesBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.SecondaryAxisSeries);
    }

    private void ChartComboBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.ComboToggle);
    }

    private void ChartComboSeriesBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.ComboSeries);
    }

    private void ChartSeriesColorBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowChartSeriesFormatDialog();
    }

    private void ChartSeriesWidthBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.SeriesWidth);
    }

    private void ChartSeriesDashBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.SeriesDash);
    }

    private void ChartSeriesMarkerBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowChartSeriesFormatDialog();
    }

    private void ShowChartSeriesFormatDialog()
    {
        var command = ChartWorkflowCommandCatalog.FormatDataSeries;
        var caption = ChartWorkflowCaption(command);
        if (!TryGetFirstChartForDialog(command, out var chart))
            return;

        if (!ChartWorkflowCommandCatalog.CanOpenDialog(chart, command))
        {
            ShowUnsupportedChartWorkflow(command);
            return;
        }

        var seriesCount = ChartSeriesFormatPlanner.GetSeriesCount(chart);
        var dialog = new ChartSeriesFormatDialog(chart, seriesCount) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        ApplyChartLayoutDialogResult(caption, chart, dialog.Result.ToOptions(chart));
    }

    private void ChartSeriesMarkerSizeBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.SeriesMarkerSize);
    }

    private void ExecuteChartQuickCommand(ChartQuickCommandDescriptor command)
    {
        var unsupportedMessage = command.HostUnsupportedMessageResourceKey is null
            ? null
            : UiText.Get(command.HostUnsupportedMessageResourceKey);
        if (!TryExecuteRepeatableChartLayout(
                command.Label,
                UiText.Get(command.HostMissingSelectionMessageResourceKey),
                chart => ChartQuickCommandPlanner.CanApply(chart, command.Command),
                unsupportedMessage,
                chart => ChartQuickCommandPlanner.Plan(chart, command.Command)))
            return;

        UpdateViewport();
    }

    private void InsertChartOfType(string type)
    {
        InsertChartOfType(ChartOptionCycler.ParseChartType(type));
    }

}

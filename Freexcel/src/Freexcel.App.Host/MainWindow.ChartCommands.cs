using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

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
            command = new AddChartSheetCommand(_currentSheetId, currentRange, ChartType.Column, "Chart");
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
        if (!ChartTypeSupport.IsRenderable(type))
        {
            ShowDeferredChartFamilyMessage();
            return;
        }

        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Insert Chart",
                range,
                currentRange => new AddChartCommand(_currentSheetId, currentRange, type, "Chart")))
            return;

        UpdateViewport();
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
        if (!TryGetActiveNormalChart("Change Chart Type", out var chart))
            return;

        var dialog = new ChangeChartTypeDialog(chart.Type) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (!TryExecuteCommand(new ChangeChartTypeCommand(_currentSheetId, chart.Id, dialog.Result.ChartType), "Change Chart Type"))
            return;

        UpdateViewport();
    }

    private void SelectChartDataSourceBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetActiveNormalChart("Select Data Source", out var chart))
            return;

        var dialog = new SelectDataSourceDialog(
            FormatRangeReference(chart.DataRange.Start, chart.DataRange.End),
            chart.FirstColIsCategories)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
            return;

        if (!ChartInputParser.TryParseDataRange(dialog.Result.SourceRangeText, _currentSheetId, out var dataRange))
        {
            MessageBox.Show("Enter a valid chart data range.", "Select Data Source", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryExecuteCommand(
                new ChangeChartSourceCommand(
                    _currentSheetId,
                    chart.Id,
                    dataRange,
                    firstRowIsHeader: chart.FirstRowIsHeader,
                    firstColIsCategories: dialog.Result.FirstColumnIsCategories),
                "Select Data Source"))
            return;

        UpdateViewport();
    }

    private void MoveChartBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetActiveNormalChart("Move Chart", out var chart))
            return;

        var currentSheet = _workbook.GetSheet(_currentSheetId);
        if (currentSheet is null)
            return;

        var dialog = new MoveChartDialog(currentSheet.Name) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (dialog.Result.TargetKind == MoveChartTargetKind.NewChartSheet)
        {
            if (!TryExecuteCommand(new MoveChartToNewSheetCommand(_currentSheetId, chart.Id, dialog.Result.TargetName), "Move Chart"))
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
                MessageBox.Show("Target sheet was not found.", "Move Chart", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!TryExecuteCommand(new MoveChartCommand(_currentSheetId, chart.Id, targetSheet.Id), "Move Chart"))
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

    private void FormatChartAreaBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetFirstChartForDialog("Format Chart Area", "Insert or select a chart before formatting the chart area.", out var chart))
            return;

        var dialog = new ChartAreaLegendDialog(chart) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (!ApplyChartLayoutDialogResult("Format Chart Area", chart, dialog.Result.ToOptions()))
            return;

        UpdateViewport();
    }

    private bool TryGetActiveNormalChart(string caption, out ChartModel chart)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        chart = sheet?.Charts.FirstOrDefault(item => !item.IsPivotChart) ?? null!;
        if (chart is not null)
            return true;

        MessageBox.Show(
            "Insert or select a chart before using this command.",
            caption,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return false;
    }

    private void ChartColumnMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Column);
    private void ChartStackedColumnMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.StackedColumn);
    private void ChartPercentStackedColumnMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.PercentStackedColumn);
    private void ChartLineMenuItem_Click(object sender, RoutedEventArgs e)   => InsertChartOfType(ChartType.Line);
    private void ChartPieMenuItem_Click(object sender, RoutedEventArgs e)    => InsertChartOfType(ChartType.Pie);
    private void ChartDoughnutMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Doughnut);
    private void ChartBarMenuItem_Click(object sender, RoutedEventArgs e)    => InsertChartOfType(ChartType.Bar);
    private void ChartStackedBarMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.StackedBar);
    private void ChartPercentStackedBarMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.PercentStackedBar);
    private void ChartAreaMenuItem_Click(object sender, RoutedEventArgs e)   => InsertChartOfType(ChartType.Area);
    private void ChartScatterMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Scatter);
    private void ChartBubbleMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Bubble);
    private void ChartRadarMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Radar);
    private void ChartStockMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Stock);
    private void Chart3DColumnMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.ThreeDColumn);
    private void Chart3DBarMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.ThreeDBar);
    private void DeferredChartFamilyMenuItem_Click(object sender, RoutedEventArgs e) => ShowDeferredChartFamilyMessage();

    private static void ShowDeferredChartFamilyMessage() =>
        MessageBox.Show(
            "This chart family is retained when opening XLSX files, but authoring and rendering are deferred until its data model and renderer are implemented.",
            "Chart family deferred",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

    private void ChartFirstSliceAngleBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryExecuteRepeatableChartLayout(
                "First Slice Angle",
                "Insert or select a pie or doughnut chart before changing first-slice angle.",
                chart => chart.Type is ChartType.Pie or ChartType.Doughnut,
                "First-slice angle only applies to pie and doughnut charts.",
                chart => new ChartLayoutOptions(FirstSliceAngle: chart.FirstSliceAngle >= 270 ? 0 : chart.FirstSliceAngle + 90)))
            return;

        UpdateViewport();
    }

    private void ChartDoughnutHoleSizeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryExecuteRepeatableChartLayout(
                "Doughnut Hole Size",
                "Insert or select a doughnut chart before changing hole size.",
                chart => chart.Type == ChartType.Doughnut,
                "Doughnut hole size only applies to doughnut charts.",
                chart => new ChartLayoutOptions(
                    DoughnutHoleSize: chart.DoughnutHoleSize switch
                    {
                        < 0.45 => 0.55,
                        < 0.7 => 0.75,
                        _ => 0.35
                    })))
            return;

        UpdateViewport();
    }

    private void ChartExplodedSliceBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryExecuteRepeatableChartLayout(
                "Explode Slice",
                "Insert or select a pie or doughnut chart before exploding a slice.",
                chart => chart.Type is (ChartType.Pie or ChartType.Doughnut) && ChartTypeSupport.GetDataPointCount(chart) > 0,
                "Exploded slices require a pie or doughnut chart with chart data.",
                chart =>
                {
                    var sliceCount = ChartTypeSupport.GetDataPointCount(chart);
                    var nextIndex = chart.ExplodedSliceIndex < 0
                        ? 0
                        : chart.ExplodedSliceIndex + 1 >= sliceCount ? -1 : chart.ExplodedSliceIndex + 1;
                    var nextDistance = nextIndex < 0
                        ? 0.1
                        : chart.ExplodedSliceDistance >= 0.22 ? 0.1 : chart.ExplodedSliceDistance + 0.06;
                    return new ChartLayoutOptions(ExplodedSliceIndex: nextIndex, ExplodedSliceDistance: nextDistance);
                }))
            return;

        UpdateViewport();
    }

    private void ChartDataLabelsBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowChartDataLabelsDialog();
    }

    private void ShowChartDataLabelsDialog()
    {
        if (!TryGetFirstChartForDialog("Format Data Labels", "Insert or select a chart before changing data labels.", out var chart))
            return;

        var dialog = new ChartDataLabelsDialog(chart) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        ApplyChartLayoutDialogResult("Format Data Labels", chart, dialog.Result.ToOptions());
    }

    private void ChartDataLabelPositionBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowChartDataLabelsDialog();
    }

    private void ChartDataLabelCategoryBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleDataLabelOption(
            "Category Name",
            chart => new ChartLayoutOptions(
                ShowDataLabels: true,
                ShowDataLabelCategoryName: !chart.ShowDataLabelCategoryName));
    }

    private void ChartDataLabelSeriesBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleDataLabelOption(
            "Series Name",
            chart => new ChartLayoutOptions(
                ShowDataLabels: true,
                ShowDataLabelSeriesName: !chart.ShowDataLabelSeriesName));
    }

    private void ChartDataLabelPercentageBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleDataLabelOption(
            "Percentage",
            chart => new ChartLayoutOptions(
                ShowDataLabels: true,
                ShowDataLabelPercentage: !chart.ShowDataLabelPercentage));
    }

    private void ChartDataLabelSeparatorBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleDataLabelOption(
            "Label Separator",
            chart => new ChartLayoutOptions(
                ShowDataLabels: true,
                DataLabelSeparator: chart.DataLabelSeparator switch
                {
                    ChartDataLabelSeparator.Comma => ChartDataLabelSeparator.Semicolon,
                    ChartDataLabelSeparator.Semicolon => ChartDataLabelSeparator.NewLine,
                    ChartDataLabelSeparator.NewLine => ChartDataLabelSeparator.Space,
                    _ => ChartDataLabelSeparator.Comma
                }));
    }

    private void ChartDataLabelNumberFormatBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleDataLabelOption(
            "Label Number Format",
            chart => new ChartLayoutOptions(
                ShowDataLabels: true,
                DataLabelNumberFormat: ChartOptionCycler.NextDataLabelNumberFormat(chart.DataLabelNumberFormat)));
    }

    private void ChartDataLabelCalloutBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleDataLabelOption(
            "Data Callout",
            chart => new ChartLayoutOptions(
                ShowDataLabels: true,
                ShowDataLabelCallouts: !chart.ShowDataLabelCallouts));
    }

    private void ChartDataLabelFillBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleDataLabelOption(
            "Data Label Fill",
            chart => new ChartLayoutOptions(
                ShowDataLabels: true,
                DataLabelFillColor: ChartOptionCycler.NextSeriesColor(chart.DataLabelFillColor)));
    }

    private void ChartDataLabelTextBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleDataLabelOption(
            "Data Label Text",
            chart => new ChartLayoutOptions(
                ShowDataLabels: true,
                DataLabelTextColor: ChartOptionCycler.NextSeriesColor(chart.DataLabelTextColor)));
    }

    private void ChartDataLabelBorderBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleDataLabelOption(
            "Data Label Border",
            chart => new ChartLayoutOptions(
                ShowDataLabels: true,
                DataLabelBorderColor: ChartOptionCycler.NextSeriesColor(chart.DataLabelBorderColor),
                DataLabelBorderThickness: chart.DataLabelBorderThickness >= 3 ? 0.75 : chart.DataLabelBorderThickness + 0.75));
    }

    private void ChartDataLabelSizeBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleDataLabelOption(
            "Data Label Size",
            chart => new ChartLayoutOptions(
                ShowDataLabels: true,
                DataLabelFontSize: chart.DataLabelFontSize >= 16 ? 9 : chart.DataLabelFontSize + 1));
    }

    private void ChartDataLabelAngleBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleDataLabelOption(
            "Data Label Angle",
            chart => new ChartLayoutOptions(
                ShowDataLabels: true,
                DataLabelAngle: ChartOptionCycler.NextAxisLabelAngle(chart.DataLabelAngle)));
    }

    private void ChartPointDataLabelBtn_Click(object sender, RoutedEventArgs e)
    {
        const string caption = "Format Data Point Label";
        if (!TryExecuteRepeatableChartLayout(
                caption,
                "Insert or select a chart before changing point data-label formatting.",
                chart => ChartOptionCycler.GetSeriesCount(chart) > 0 && ChartTypeSupport.GetDataPointCount(chart) > 0,
                "Add chart data points before changing point data-label formatting.",
                chart =>
                {
                    var formats = chart.PointDataLabelFormats.ToList();
                    var existingIndex = formats.FindIndex(format => format.SeriesIndex == 0 && format.PointIndex == 0);
                    var current = existingIndex >= 0 ? formats[existingIndex] : new ChartPointDataLabelFormat(0, 0);
                    var updated = current with
                    {
                        FillColor = ChartOptionCycler.NextSeriesColor(current.FillColor),
                        BorderColor = ChartOptionCycler.NextSeriesColor(current.BorderColor ?? current.FillColor),
                        BorderThickness = current.BorderThickness is null or >= 3 ? 0.75 : current.BorderThickness.Value + 0.75,
                        TextColor = ChartOptionCycler.NextSeriesColor(current.TextColor),
                        FontSize = current.FontSize is null or >= 16 ? 9 : current.FontSize.Value + 1
                    };
                    if (existingIndex >= 0)
                        formats[existingIndex] = updated;
                    else
                        formats.Add(updated);
                    return new ChartLayoutOptions(
                        ShowDataLabels: true,
                        PointDataLabelFormats: formats);
                }))
            return;

        UpdateViewport();
    }

    private void ChartAreaFillBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAreaOption(
            "Chart Area Fill",
            chart => new ChartLayoutOptions(ChartAreaFillColor: ChartOptionCycler.NextSeriesColor(chart.ChartAreaFillColor)));
    }

    private void ChartTitleColorBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAreaOption(
            "Chart Title Color",
            chart => new ChartLayoutOptions(ChartTitleTextColor: ChartOptionCycler.NextSeriesColor(chart.ChartTitleTextColor)));
    }

    private void ChartTitlesBtn_Click(object sender, RoutedEventArgs e)
    {
        const string caption = "Chart Titles";
        if (!TryGetFirstChartForDialog(caption, "Insert or select a chart before editing chart titles.", out var chart))
            return;

        var dialog = new ChartTitlesDialog(chart.Title, chart.XAxisTitle, chart.YAxisTitle) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        ApplyChartLayoutDialogResult(caption, chart, dialog.Result.ToOptions());
    }

    private void ChartTitleSizeBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAreaOption(
            "Chart Title Size",
            chart => new ChartLayoutOptions(ChartTitleFontSize: chart.ChartTitleFontSize >= 24 ? 12 : chart.ChartTitleFontSize + 2));
    }

    private void ChartAxisTitleColorBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAreaOption(
            "Axis Title Color",
            chart => new ChartLayoutOptions(AxisTitleTextColor: ChartOptionCycler.NextSeriesColor(chart.AxisTitleTextColor)));
    }

    private void ChartAxisTitleSizeBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAreaOption(
            "Axis Title Size",
            chart => new ChartLayoutOptions(AxisTitleFontSize: chart.AxisTitleFontSize >= 18 ? 9 : chart.AxisTitleFontSize + 1));
    }

    private void ChartPlotAreaFillBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAreaOption(
            "Plot Area Fill",
            chart => new ChartLayoutOptions(PlotAreaFillColor: ChartOptionCycler.NextSeriesColor(chart.PlotAreaFillColor)));
    }

    private void ChartPlotAreaBorderBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAreaOption(
            "Plot Area Border",
            chart => new ChartLayoutOptions(
                PlotAreaBorderColor: ChartOptionCycler.NextSeriesColor(chart.PlotAreaBorderColor),
                PlotAreaBorderThickness: chart.PlotAreaBorderThickness >= 3 ? 1 : chart.PlotAreaBorderThickness + 0.75));
    }

    private void ChartLegendTextBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAreaOption(
            "Legend Text",
            chart => new ChartLayoutOptions(LegendTextColor: ChartOptionCycler.NextSeriesColor(chart.LegendTextColor)));
    }

    private void ChartLegendFillBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAreaOption(
            "Legend Fill",
            chart => new ChartLayoutOptions(LegendFillColor: ChartOptionCycler.NextSeriesColor(chart.LegendFillColor)));
    }

    private void ChartLegendBorderBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAreaOption(
            "Legend Border",
            chart => new ChartLayoutOptions(
                LegendBorderColor: ChartOptionCycler.NextSeriesColor(chart.LegendBorderColor),
                LegendBorderThickness: chart.LegendBorderThickness >= 3 ? 0.75 : chart.LegendBorderThickness + 0.75));
    }

    private void ChartLegendSizeBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAreaOption(
            "Legend Font Size",
            chart => new ChartLayoutOptions(LegendFontSize: chart.LegendFontSize >= 16 ? 9 : chart.LegendFontSize + 1));
    }

    private void ChartLegendOverlayBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAreaOption(
            "Legend Overlay",
            chart => new ChartLayoutOptions(ShowLegend: true, LegendOverlay: !chart.LegendOverlay));
    }

    private void ToggleDataLabelOption(string caption, Func<ChartModel, ChartLayoutOptions> optionsFactory)
    {
        if (!TryExecuteRepeatableChartLayout(
                caption,
                "Insert or select a chart before changing data label options.",
                null,
                null,
                optionsFactory))
            return;

        UpdateViewport();
    }

    private void ToggleChartAreaOption(string caption, Func<ChartModel, ChartLayoutOptions> optionsFactory)
    {
        if (!TryExecuteRepeatableChartLayout(
                caption,
                "Insert or select a chart before changing chart area formatting.",
                null,
                null,
                optionsFactory))
            return;

        UpdateViewport();
    }

    private void ChartTrendlineBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowChartTrendlineDialog();
    }

    private void ShowChartTrendlineDialog()
    {
        if (!TryGetFirstChartForDialog("Format Trendline", "Insert or select a chart before changing trendlines.", out var chart))
            return;

        if (!ChartTypeSupport.SupportsTrendlines(chart.Type))
        {
            ShowCommandError(new CommandOutcome(false, "Trendlines are currently supported for column, line, bar, scatter, bubble, and area charts."), "Format Trendline");
            return;
        }

        var dialog = new ChartTrendlineOptionsDialog(chart) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        ApplyChartLayoutDialogResult("Format Trendline", chart, dialog.Result.ToOptions());
    }

    private void ChartTrendlineTypeBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowChartTrendlineDialog();
    }

    private void ChartTrendlinePeriodBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryExecuteRepeatableChartLayout(
                "Moving Average Period",
                "Insert or select a chart before changing moving-average period.",
                chart => ChartTypeSupport.SupportsTrendlines(chart.Type),
                "Trendlines are currently supported for column, line, bar, scatter, bubble, and area charts.",
                chart => new ChartLayoutOptions(
                    ShowLinearTrendline: true,
                    TrendlineType: ChartTrendlineType.MovingAverage,
                    TrendlinePeriod: chart.TrendlinePeriod >= 6 ? 2 : chart.TrendlinePeriod + 1)))
            return;

        UpdateViewport();
    }

    private void ChartTrendlineOrderBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryExecuteRepeatableChartLayout(
                "Polynomial Order",
                "Insert or select a chart before changing polynomial order.",
                chart => ChartTypeSupport.SupportsTrendlines(chart.Type),
                "Trendlines are currently supported for column, line, bar, scatter, bubble, and area charts.",
                chart => new ChartLayoutOptions(
                    ShowLinearTrendline: true,
                    TrendlineType: ChartTrendlineType.Polynomial,
                    TrendlineOrder: chart.TrendlineOrder >= 6 ? 2 : chart.TrendlineOrder + 1)))
            return;

        UpdateViewport();
    }

    private void ChartTrendlineEquationBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleTrendlineInfo(
            "Trendline Equation",
            chart => new ChartLayoutOptions(
                ShowLinearTrendline: true,
                ShowTrendlineEquation: !chart.ShowTrendlineEquation));
    }

    private void ChartTrendlineRSquaredBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleTrendlineInfo(
            "R-squared",
            chart => new ChartLayoutOptions(
                ShowLinearTrendline: true,
                ShowTrendlineRSquared: !chart.ShowTrendlineRSquared));
    }

    private void ChartTrendlineColorBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleTrendlineInfo(
            "Trendline Color",
            chart => new ChartLayoutOptions(
                ShowLinearTrendline: true,
                TrendlineColor: ChartOptionCycler.NextTrendlineColor(chart.TrendlineColor)));
    }

    private void ChartTrendlineDashBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleTrendlineInfo(
            "Trendline Dash",
            chart => new ChartLayoutOptions(
                ShowLinearTrendline: true,
                TrendlineDashStyle: chart.TrendlineDashStyle switch
                {
                    ChartLineDashStyle.Dash => ChartLineDashStyle.Dot,
                    ChartLineDashStyle.Dot => ChartLineDashStyle.Solid,
                    _ => ChartLineDashStyle.Dash
                }));
    }

    private void ChartTrendlineWidthBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleTrendlineInfo(
            "Trendline Width",
            chart => new ChartLayoutOptions(
                ShowLinearTrendline: true,
                TrendlineThickness: chart.TrendlineThickness >= 3 ? 1.5 : chart.TrendlineThickness + 0.75));
    }

    private void ChartErrorBarsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetFirstChartForDialog("Format Error Bars", "Insert or select a chart before changing error bars.", out var chart))
            return;

        var dialog = new ChartErrorBarsDialog(chart) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (!ApplyChartLayoutDialogResult("Format Error Bars", chart, dialog.Result.ToOptions()))
            return;

        UpdateViewport();
    }

    private void ToggleTrendlineInfo(string caption, Func<ChartModel, ChartLayoutOptions> optionsFactory)
    {
        if (!TryExecuteRepeatableChartLayout(
                caption,
                "Insert or select a chart before changing trendline information.",
                chart => ChartTypeSupport.SupportsTrendlines(chart.Type),
                "Trendline information is currently supported for column, line, bar, scatter, bubble, and area charts.",
                optionsFactory))
            return;

        UpdateViewport();
    }

    private void ChartSecondaryAxisBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryExecuteRepeatableChartLayout(
                "Secondary Axis",
                "Insert or select a chart before changing secondary axes.",
                chart => ChartTypeSupport.SupportsSecondaryAxis(chart.Type) &&
                         (chart.ShowSecondaryAxis || ChartOptionCycler.GetSeriesCount(chart) >= 2),
                "Secondary value axes require a supported chart with at least two data series.",
                chart => new ChartLayoutOptions(
                    ShowSecondaryAxis: !chart.ShowSecondaryAxis,
                    SecondaryAxisSeriesIndexes: [])))
            return;

        UpdateViewport();
    }

    private void ChartXAxisBoundsBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowChartAxisFormatDialog(useXAxis: true);
    }

    private void ChartYAxisBoundsBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowChartAxisFormatDialog(useXAxis: false);
    }

    private void ChartXAxisLogBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowChartAxisFormatDialog(useXAxis: true);
    }

    private void ChartYAxisLogBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowChartAxisFormatDialog(useXAxis: false);
    }

    private void ChartXAxisNumberFormatBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowChartAxisFormatDialog(useXAxis: true);
    }

    private void ChartYAxisNumberFormatBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowChartAxisFormatDialog(useXAxis: false);
    }

    private void ChartXAxisGridlinesBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAxisGridlines(useXAxis: true);
    }

    private void ChartYAxisGridlinesBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAxisGridlines(useXAxis: false);
    }

    private void ChartXAxisGridlineStyleBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAxisGridlineStyle(useXAxis: true);
    }

    private void ChartYAxisGridlineStyleBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAxisGridlineStyle(useXAxis: false);
    }

    private void ChartXAxisTickBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowChartAxisFormatDialog(useXAxis: true);
    }

    private void ChartYAxisTickBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowChartAxisFormatDialog(useXAxis: false);
    }

    private void ChartXAxisLabelsBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAxisLabels(useXAxis: true);
    }

    private void ChartYAxisLabelsBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAxisLabels(useXAxis: false);
    }

    private void ChartXAxisLabelFontBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAxisLabelFont(useXAxis: true);
    }

    private void ChartXAxisLabelAngleBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAxisLabelAngle(useXAxis: true);
    }

    private void ChartYAxisLabelFontBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAxisLabelFont(useXAxis: false);
    }

    private void ChartYAxisLabelAngleBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAxisLabelAngle(useXAxis: false);
    }

    private void ChartXAxisLineBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowChartAxisFormatDialog(useXAxis: true);
    }

    private void ChartYAxisLineBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowChartAxisFormatDialog(useXAxis: false);
    }

    private void ShowChartAxisFormatDialog(bool useXAxis)
    {
        var caption = useXAxis ? "Format X Axis" : "Format Y Axis";
        if (!TryGetFirstChartForDialog(caption, "Insert or select a chart before changing axis options.", out var chart))
            return;

        var dialog = new ChartAxisFormatDialog(chart, useXAxis) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        ApplyChartLayoutDialogResult(useXAxis ? "Format X Axis" : "Format Y Axis", chart, dialog.Result.ToOptions());
    }

    private void ToggleChartAxisTicks(bool useXAxis)
    {
        var caption = useXAxis ? "X Axis Ticks" : "Y Axis Ticks";
        if (!TryExecuteRepeatableChartLayout(
                caption,
                "Insert or select a chart before changing axis ticks.",
                null,
                null,
                chart =>
                {
                    var (major, minor) = useXAxis
                        ? ChartOptionCycler.NextAxisTickState(chart.XAxisMajorTickStyle, chart.XAxisMinorTickStyle)
                        : ChartOptionCycler.NextAxisTickState(chart.YAxisMajorTickStyle, chart.YAxisMinorTickStyle);
                    return useXAxis
                        ? new ChartLayoutOptions(XAxisMajorTickStyle: major, XAxisMinorTickStyle: minor)
                        : new ChartLayoutOptions(YAxisMajorTickStyle: major, YAxisMinorTickStyle: minor);
                }))
            return;

        UpdateViewport();
    }

    private void ToggleChartAxisLabels(bool useXAxis)
    {
        var caption = useXAxis ? "X Axis Labels" : "Y Axis Labels";
        if (!TryExecuteRepeatableChartLayout(
                caption,
                "Insert or select a chart before changing axis labels.",
                null,
                null,
                chart => useXAxis
                    ? new ChartLayoutOptions(ShowXAxisLabels: !chart.ShowXAxisLabels)
                    : new ChartLayoutOptions(ShowYAxisLabels: !chart.ShowYAxisLabels)))
            return;

        UpdateViewport();
    }

    private void ToggleChartAxisLabelFont(bool useXAxis)
    {
        var caption = useXAxis ? "X Axis Label Font" : "Y Axis Label Font";
        if (!TryExecuteRepeatableChartLayout(
                caption,
                "Insert or select a chart before changing axis label formatting.",
                null,
                null,
                chart =>
                {
                    var currentColor = useXAxis ? chart.XAxisLabelTextColor : chart.YAxisLabelTextColor;
                    var currentSize = useXAxis ? chart.XAxisLabelFontSize : chart.YAxisLabelFontSize;
                    var nextColor = ChartOptionCycler.NextSeriesColor(currentColor);
                    var nextSize = currentSize >= 14 ? 9 : currentSize + 1;
                    return useXAxis
                        ? new ChartLayoutOptions(XAxisLabelTextColor: nextColor, XAxisLabelFontSize: nextSize)
                        : new ChartLayoutOptions(YAxisLabelTextColor: nextColor, YAxisLabelFontSize: nextSize);
                }))
            return;

        UpdateViewport();
    }

    private void ToggleChartAxisLabelAngle(bool useXAxis)
    {
        var caption = useXAxis ? "X Axis Label Angle" : "Y Axis Label Angle";
        if (!TryExecuteRepeatableChartLayout(
                caption,
                "Insert or select a chart before changing axis label rotation.",
                null,
                null,
                chart =>
                {
                    var currentAngle = useXAxis ? chart.XAxisLabelAngle : chart.YAxisLabelAngle;
                    var nextAngle = ChartOptionCycler.NextAxisLabelAngle(currentAngle);
                    return useXAxis
                        ? new ChartLayoutOptions(XAxisLabelAngle: nextAngle)
                        : new ChartLayoutOptions(YAxisLabelAngle: nextAngle);
                }))
            return;

        UpdateViewport();
    }

    private void ToggleChartAxisLine(bool useXAxis)
    {
        var caption = useXAxis ? "X Axis Line" : "Y Axis Line";
        if (!TryExecuteRepeatableChartLayout(
                caption,
                "Insert or select a chart before changing axis line formatting.",
                null,
                null,
                chart =>
                {
                    var currentColor = useXAxis ? chart.XAxisLineColor : chart.YAxisLineColor;
                    var currentThickness = useXAxis ? chart.XAxisLineThickness : chart.YAxisLineThickness;
                    var (nextColor, nextThickness) = ChartOptionCycler.NextAxisLineState(currentColor, currentThickness);
                    return useXAxis
                        ? new ChartLayoutOptions(XAxisLineColor: nextColor, XAxisLineThickness: nextThickness)
                        : new ChartLayoutOptions(YAxisLineColor: nextColor, YAxisLineThickness: nextThickness);
                }))
            return;

        UpdateViewport();
    }

    private void ToggleChartAxisGridlines(bool useXAxis)
    {
        var caption = useXAxis ? "X Axis Gridlines" : "Y Axis Gridlines";
        if (!TryExecuteRepeatableChartLayout(
                caption,
                "Insert or select a chart before changing axis gridlines.",
                null,
                null,
                chart =>
                {
                    var (showMajor, showMinor) = useXAxis
                        ? ChartOptionCycler.NextGridlineState(chart.ShowXAxisMajorGridlines, chart.ShowXAxisMinorGridlines)
                        : ChartOptionCycler.NextGridlineState(chart.ShowYAxisMajorGridlines, chart.ShowYAxisMinorGridlines);
                    return useXAxis
                        ? new ChartLayoutOptions(ShowXAxisMajorGridlines: showMajor, ShowXAxisMinorGridlines: showMinor)
                        : new ChartLayoutOptions(ShowYAxisMajorGridlines: showMajor, ShowYAxisMinorGridlines: showMinor);
                }))
            return;

        UpdateViewport();
    }

    private void ToggleChartAxisGridlineStyle(bool useXAxis)
    {
        var caption = useXAxis ? "X Gridline Style" : "Y Gridline Style";
        if (!TryExecuteRepeatableChartLayout(
                caption,
                "Insert or select a chart before changing gridline formatting.",
                null,
                null,
                chart =>
                {
                    var currentMajorColor = useXAxis ? chart.XAxisMajorGridlineColor : chart.YAxisMajorGridlineColor;
                    var currentMinorColor = useXAxis ? chart.XAxisMinorGridlineColor : chart.YAxisMinorGridlineColor;
                    var currentThickness = useXAxis ? chart.XAxisGridlineThickness : chart.YAxisGridlineThickness;
                    var nextMajorColor = ChartOptionCycler.NextSeriesColor(currentMajorColor);
                    var nextMinorColor = ChartOptionCycler.NextSeriesColor(currentMinorColor ?? currentMajorColor);
                    var nextThickness = currentThickness >= 3 ? 1 : currentThickness + 0.5;
                    return useXAxis
                        ? new ChartLayoutOptions(
                            XAxisMajorGridlineColor: nextMajorColor,
                            XAxisMinorGridlineColor: nextMinorColor,
                            XAxisGridlineThickness: nextThickness,
                            ShowXAxisMajorGridlines: true)
                        : new ChartLayoutOptions(
                            YAxisMajorGridlineColor: nextMajorColor,
                            YAxisMinorGridlineColor: nextMinorColor,
                            YAxisGridlineThickness: nextThickness,
                            ShowYAxisMajorGridlines: true);
                }))
            return;

        UpdateViewport();
    }

    private void ToggleChartAxisNumberFormat(bool useXAxis)
    {
        var caption = useXAxis ? "X Axis Number Format" : "Y Axis Number Format";
        if (!TryExecuteRepeatableChartLayout(
                caption,
                "Insert or select a chart before changing axis number formats.",
                null,
                null,
                chart =>
                {
                    var next = ChartOptionCycler.NextDataLabelNumberFormat(useXAxis ? chart.XAxisNumberFormat : chart.YAxisNumberFormat);
                    return useXAxis
                        ? new ChartLayoutOptions(XAxisNumberFormat: next)
                        : new ChartLayoutOptions(YAxisNumberFormat: next);
                }))
            return;

        UpdateViewport();
    }

    private void ToggleChartAxisLogScale(bool useXAxis)
    {
        var caption = useXAxis ? "X Log Scale" : "Y Log Scale";
        IWorkbookCommand CreateCommand()
        {
            var sheet = _workbook.GetSheet(_currentSheetId);
            var chart = sheet?.Charts.FirstOrDefault();
            if (sheet is null || chart is null)
                return new FailedWorkbookCommand("Insert or select a chart before changing axis scale.");

            if (useXAxis && !ChartTypeSupport.SupportsXAxisLogScale(chart.Type))
                return new FailedWorkbookCommand("X-axis log scale is currently supported for bar, scatter, and bubble charts with value X axes.");

            if (!useXAxis && !ChartTypeSupport.SupportsYAxisLogScale(chart.Type))
                return new FailedWorkbookCommand("Y-axis log scale is currently supported for column, line, area, scatter, and bubble charts with value Y axes.");

            var enableLog = useXAxis ? !chart.XAxisLogScale : !chart.YAxisLogScale;
            var options = useXAxis
                ? new ChartLayoutOptions(XAxisLogScale: enableLog)
                : new ChartLayoutOptions(YAxisLogScale: enableLog);

            if (enableLog && ChartOptionCycler.TryGetAxisBounds(sheet, chart, useXAxis, out var minimum, out var maximum))
            {
                var positiveMinimum = minimum > 0 ? minimum : 1;
                var positiveMaximum = maximum > positiveMinimum ? maximum : positiveMinimum * 10;
                options = useXAxis
                    ? options with { XAxisMinimum = positiveMinimum, XAxisMaximum = positiveMaximum }
                    : options with { YAxisMinimum = positiveMinimum, YAxisMaximum = positiveMaximum };
            }

            return new SetChartLayoutCommand(_currentSheetId, chart.Id, options);
        }

        var outcome = _commandBus.ExecuteRepeatable(_workbook.Id, CreateCommand);
        if (!outcome.Success)
        {
            ShowCommandError(outcome, caption);
            return;
        }

        _repeatPostAction = null;
        UpdateViewport();
    }

    private void ToggleChartAxisBounds(bool useXAxis)
    {
        var caption = useXAxis ? "X Axis Bounds" : "Y Axis Bounds";
        IWorkbookCommand CreateCommand()
        {
            var sheet = _workbook.GetSheet(_currentSheetId);
            var chart = sheet?.Charts.FirstOrDefault();
            if (sheet is null || chart is null)
                return new FailedWorkbookCommand("Insert or select a chart before changing axis bounds.");

            var hasBounds = useXAxis
                ? chart.XAxisMinimum is not null || chart.XAxisMaximum is not null
                : chart.YAxisMinimum is not null || chart.YAxisMaximum is not null;
            if (!hasBounds &&
                (useXAxis
                    ? !ChartTypeSupport.SupportsXAxisBounds(chart.Type)
                    : !ChartTypeSupport.SupportsYAxisBounds(chart.Type)))
                return new FailedWorkbookCommand("Axis bounds are currently supported for chart value axes only.");

            ChartLayoutOptions options;
            if (hasBounds)
            {
                options = useXAxis
                    ? new ChartLayoutOptions(ClearXAxisBounds: true)
                    : new ChartLayoutOptions(ClearYAxisBounds: true);
            }
            else if (ChartOptionCycler.TryGetAxisBounds(sheet, chart, useXAxis, out var minimum, out var maximum))
            {
                var majorUnit = Math.Max(double.Epsilon, (maximum - minimum) / 5);
                var minorUnit = Math.Max(double.Epsilon, majorUnit / 2);
                options = useXAxis
                    ? new ChartLayoutOptions(XAxisMinimum: minimum, XAxisMaximum: maximum, XAxisMajorUnit: majorUnit, XAxisMinorUnit: minorUnit)
                    : new ChartLayoutOptions(YAxisMinimum: minimum, YAxisMaximum: maximum, YAxisMajorUnit: majorUnit, YAxisMinorUnit: minorUnit);
            }
            else
            {
                return new FailedWorkbookCommand("Add numeric chart data before setting axis bounds.");
            }

            return new SetChartLayoutCommand(_currentSheetId, chart.Id, options);
        }

        var outcome = _commandBus.ExecuteRepeatable(_workbook.Id, CreateCommand);
        if (!outcome.Success)
        {
            ShowCommandError(outcome, caption);
            return;
        }

        _repeatPostAction = null;
        UpdateViewport();
    }

    private void ChartSecondaryAxisSeriesBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryExecuteRepeatableChartLayout(
                "Secondary Axis Series",
                "Insert or select a chart before changing secondary-axis series.",
                chart => ChartTypeSupport.SupportsSecondaryAxis(chart.Type) && ChartOptionCycler.GetSeriesCount(chart) >= 2,
                "Secondary value axes require a supported chart with at least two data series.",
                chart =>
                {
                    var next = ChartOptionCycler.GetNextSecondaryAxisSeries(chart, ChartOptionCycler.GetSeriesCount(chart));
                    return new ChartLayoutOptions(
                        ShowSecondaryAxis: next.ShowSecondaryAxis,
                        SecondaryAxisSeriesIndexes: next.SeriesIndexes);
                }))
            return;

        UpdateViewport();
    }

    private void ChartComboBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryExecuteRepeatableChartLayout(
                "Combo Chart",
                "Insert or select a chart before changing combo chart options.",
                chart => ChartTypeSupport.SupportsComboLineOverlay(chart.Type) &&
                         (chart.UseComboLineForSecondarySeries || ChartTypeSupport.SupportsComboLineOverlay(chart)),
                "Combo line overlays require a supported chart with at least two data series.",
                chart => new ChartLayoutOptions(
                    UseComboLineForSecondarySeries: !chart.UseComboLineForSecondarySeries,
                    ComboLineSeriesIndexes: !chart.UseComboLineForSecondarySeries ? chart.ComboLineSeriesIndexes : [])))
            return;

        UpdateViewport();
    }

    private void ChartComboSeriesBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryExecuteRepeatableChartLayout(
                "Combo Chart Series",
                "Insert or select a chart before changing combo chart series.",
                chart => ChartTypeSupport.SupportsComboLineOverlay(chart.Type) && ChartTypeSupport.SupportsComboLineOverlay(chart),
                "Combo line overlays require a supported chart with at least two data series.",
                chart =>
                {
                    var nextIndexes = ChartOptionCycler.GetNextComboLineSeries(chart, ChartOptionCycler.GetSeriesCount(chart));
                    return new ChartLayoutOptions(
                        UseComboLineForSecondarySeries: nextIndexes.Length > 0,
                        ComboLineSeriesIndexes: nextIndexes);
                }))
            return;

        UpdateViewport();
    }

    private void ChartSeriesColorBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowChartSeriesFormatDialog();
    }

    private void ChartSeriesWidthBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleSeriesFormat(
            "Series Width",
            format => format with
            {
                StrokeThickness = format.StrokeThickness is null or >= 4 ? 1.5 : format.StrokeThickness.Value + 0.75
            });
    }

    private void ChartSeriesDashBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleSeriesFormat(
            "Series Dash",
            format => format with
            {
                DashStyle = format.DashStyle switch
                {
                    null => ChartLineDashStyle.Dash,
                    ChartLineDashStyle.Dash => ChartLineDashStyle.Dot,
                    ChartLineDashStyle.Dot => ChartLineDashStyle.Solid,
                    _ => null
                }
            });
    }

    private void ChartSeriesMarkerBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowChartSeriesFormatDialog();
    }

    private void ShowChartSeriesFormatDialog()
    {
        if (!TryGetFirstChartForDialog("Format Data Series", "Insert or select a chart before changing series formatting.", out var chart))
            return;

        var seriesCount = ChartOptionCycler.GetSeriesCount(chart);
        if (seriesCount <= 0)
        {
            ShowCommandError(new CommandOutcome(false, "Add data series before changing series formatting."), "Format Data Series");
            return;
        }

        var dialog = new ChartSeriesFormatDialog(chart, ChartOptionCycler.GetSeriesCount(chart)) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        ApplyChartLayoutDialogResult("Format Data Series", chart, dialog.Result.ToOptions(chart.SeriesFormats));
    }

    private void ChartSeriesMarkerSizeBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleSeriesFormat(
            "Marker Size",
            format => format with
            {
                MarkerSize = format.MarkerSize is null or >= 12 ? 5 : format.MarkerSize.Value + 2
            },
            chart => ChartTypeSupport.SupportsSeriesMarkers(chart.Type),
            "Series marker shape and size are currently supported for line and scatter charts.");
    }

    private void ToggleSeriesFormat(
        string caption,
        Func<ChartSeriesFormat, ChartSeriesFormat> update,
        Func<ChartModel, bool>? canApply = null,
        string? unsupportedMessage = null)
    {
        if (!TryExecuteRepeatableChartLayout(
                caption,
                "Insert or select a chart before changing series formatting.",
                chart => ChartOptionCycler.GetSeriesCount(chart) > 0 && (canApply?.Invoke(chart) ?? true),
                unsupportedMessage ?? "Add data series before changing series formatting.",
                chart =>
                {
                    var formats = chart.SeriesFormats.ToList();
                    var existingIndex = formats.FindIndex(format => format.SeriesIndex == 0);
                    var current = existingIndex >= 0 ? formats[existingIndex] : new ChartSeriesFormat(0);
                    var updated = update(current);
                    if (existingIndex >= 0)
                        formats[existingIndex] = updated;
                    else
                        formats.Add(updated);
                    return new ChartLayoutOptions(SeriesFormats: formats);
                }))
            return;

        UpdateViewport();
    }

    private void InsertChartOfType(string type)
    {
        InsertChartOfType(ChartOptionCycler.ParseChartType(type));
    }

}

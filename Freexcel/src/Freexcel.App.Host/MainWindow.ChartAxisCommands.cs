using System;
using System.Linq;
using System.Windows;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public partial class MainWindow
{
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
}

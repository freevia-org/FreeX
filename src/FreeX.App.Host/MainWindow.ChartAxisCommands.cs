using System;
using System.Windows;
using FreeX.App.Presentation.Charts.Editing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private void ChartSecondaryAxisBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryExecuteRepeatableChartLayout(
                "Secondary Axis",
                UiText.Get("MainWindowMessage_ChartSecondaryAxisRequiresChart"),
                ChartAxisPlanner.CanToggleSecondaryAxis,
                UiText.Get("MainWindowMessage_ChartSecondaryAxisUnsupported"),
                ChartAxisPlanner.PlanSecondaryAxisToggle))
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
        var caption = useXAxis ? UiText.Get("ChartAxisFormat_XAxisTitle") : UiText.Get("ChartAxisFormat_YAxisTitle");
        if (!TryGetFirstChartForDialog(caption, UiText.Get("MainWindowMessage_ChartAxisOptionsRequiresChart"), out var chart))
            return;

        var dialog = new ChartAxisFormatDialog(chart, useXAxis) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        ApplyChartLayoutDialogResult(caption, chart, dialog.Result.ToOptions());
    }

    private void ToggleChartAxisTicks(bool useXAxis)
    {
        var caption = useXAxis ? "X Axis Ticks" : "Y Axis Ticks";
        ToggleChartAxisQuickCommand(
            useXAxis,
            ChartAxisQuickCommand.TickMarks,
            caption,
            UiText.Get("MainWindowMessage_ChartAxisTicksRequiresChart"));
    }

    private void ToggleChartAxisLabels(bool useXAxis)
    {
        var caption = useXAxis ? "X Axis Labels" : "Y Axis Labels";
        ToggleChartAxisQuickCommand(
            useXAxis,
            ChartAxisQuickCommand.Labels,
            caption,
            UiText.Get("MainWindowMessage_ChartAxisLabelsRequiresChart"));
    }

    private void ToggleChartAxisLabelFont(bool useXAxis)
    {
        var caption = useXAxis ? "X Axis Label Font" : "Y Axis Label Font";
        ToggleChartAxisQuickCommand(
            useXAxis,
            ChartAxisQuickCommand.LabelFont,
            caption,
            UiText.Get("MainWindowMessage_ChartAxisLabelFormattingRequiresChart"));
    }

    private void ToggleChartAxisLabelAngle(bool useXAxis)
    {
        var caption = useXAxis ? "X Axis Label Angle" : "Y Axis Label Angle";
        ToggleChartAxisQuickCommand(
            useXAxis,
            ChartAxisQuickCommand.LabelAngle,
            caption,
            UiText.Get("MainWindowMessage_ChartAxisLabelRotationRequiresChart"));
    }

    private void ToggleChartAxisLine(bool useXAxis)
    {
        var caption = useXAxis ? "X Axis Line" : "Y Axis Line";
        ToggleChartAxisQuickCommand(
            useXAxis,
            ChartAxisQuickCommand.AxisLine,
            caption,
            UiText.Get("MainWindowMessage_ChartAxisLineFormattingRequiresChart"));
    }

    private void ToggleChartAxisGridlines(bool useXAxis)
    {
        var caption = useXAxis ? "X Axis Gridlines" : "Y Axis Gridlines";
        ToggleChartAxisQuickCommand(
            useXAxis,
            ChartAxisQuickCommand.Gridlines,
            caption,
            UiText.Get("MainWindowMessage_ChartAxisGridlinesRequiresChart"));
    }

    private void ToggleChartAxisGridlineStyle(bool useXAxis)
    {
        var caption = useXAxis ? "X Gridline Style" : "Y Gridline Style";
        ToggleChartAxisQuickCommand(
            useXAxis,
            ChartAxisQuickCommand.GridlineStyle,
            caption,
            UiText.Get("MainWindowMessage_ChartGridlineFormattingRequiresChart"));
    }

    private void ToggleChartAxisNumberFormat(bool useXAxis)
    {
        var caption = useXAxis ? "X Axis Number Format" : "Y Axis Number Format";
        ToggleChartAxisQuickCommand(
            useXAxis,
            ChartAxisQuickCommand.NumberFormat,
            caption,
            UiText.Get("MainWindowMessage_ChartAxisNumberFormatRequiresChart"));
    }

    private void ToggleChartAxisLogScale(bool useXAxis)
    {
        var caption = useXAxis ? "X Log Scale" : "Y Log Scale";
        ToggleChartAxisPlannedCommand(
            useXAxis,
            caption,
            UiText.Get("MainWindowMessage_ChartAxisScaleRequiresChart"),
            ChartAxisPlanner.PlanLogScaleToggle);
    }

    private void ToggleChartAxisBounds(bool useXAxis)
    {
        var caption = useXAxis ? "X Axis Bounds" : "Y Axis Bounds";
        ToggleChartAxisPlannedCommand(
            useXAxis,
            caption,
            UiText.Get("MainWindowMessage_ChartAxisBoundsRequiresChart"),
            ChartAxisPlanner.PlanBoundsToggle);
    }

    private void ToggleChartAxisQuickCommand(
        bool useXAxis,
        ChartAxisQuickCommand command,
        string caption,
        string requiresChartMessage)
    {
        if (!TryExecuteRepeatableChartLayout(
                caption,
                requiresChartMessage,
                null,
                null,
                chart => ChartAxisPlanner.PlanQuickCommand(chart, useXAxis, command)))
            return;

        UpdateViewport();
    }

    private void ToggleChartAxisPlannedCommand(
        bool useXAxis,
        string caption,
        string requiresChartMessage,
        Func<Sheet, ChartModel, bool, ChartAxisCommandPlan> planner)
    {
        IWorkbookCommand CreateCommand()
        {
            var sheet = _workbook.GetSheet(_currentSheetId);
            var chart = FindFirstChart(sheet);
            if (sheet is null || chart is null)
                return new FailedWorkbookCommand(requiresChartMessage);

            var plan = planner(sheet, chart, useXAxis);
            if (plan.Options is not { } options)
                return new FailedWorkbookCommand(GetChartAxisCommandIssueMessage(plan.Issue, useXAxis));

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

    private static string GetChartAxisCommandIssueMessage(ChartAxisCommandIssue issue, bool useXAxis) =>
        issue switch
        {
            ChartAxisCommandIssue.UnsupportedLogScale => UiText.Get(useXAxis
                ? "MainWindowMessage_ChartXAxisLogScaleSupportedTypes"
                : "MainWindowMessage_ChartYAxisLogScaleSupportedTypes"),
            ChartAxisCommandIssue.UnsupportedBounds => UiText.Get("MainWindowMessage_ChartAxisBoundsSupportedTypes"),
            ChartAxisCommandIssue.NumericBoundsRequired => UiText.Get("MainWindowMessage_ChartAxisBoundsRequiresNumericData"),
            _ => UiText.Get("MainWindowMessage_ChartAxisOptionsRequiresChart"),
        };

    private static ChartModel? FindFirstChart(Sheet? sheet)
        => ChartWorkflowTargetPlanner.FindFirstChart(sheet);
}

using System;
using System.Windows;
using FreeX.App.Presentation.Charts.Editing;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private void ChartSecondaryAxisBtn_Click(object sender, RoutedEventArgs e)
    {
        var command = ChartWorkflowCommandCatalog.SecondaryAxis;
        if (!TryExecuteRepeatableChartLayout(
                ChartWorkflowCaption(command),
                UiText.Get(command.HostMissingSelectionMessageResourceKey),
                chart => ChartWorkflowCommandCatalog.CanOpenDialog(chart, command),
                UiText.Get(command.HostUnsupportedMessageResourceKey!),
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
        var command = ChartAxisWorkflowCommandCatalog.FormatAxis(useXAxis);
        var caption = useXAxis ? UiText.Get("ChartAxisFormat_XAxisTitle") : UiText.Get("ChartAxisFormat_YAxisTitle");
        if (!TryGetFirstChartForDialog(caption, UiText.Get(command.HostMissingSelectionMessageResourceKey), out var chart))
            return;

        var dialog = new ChartAxisFormatDialog(chart, useXAxis) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        ApplyChartLayoutDialogResult(caption, chart, ChartAxisPlanner.Plan(dialog.Result));
    }

    private void ToggleChartAxisTicks(bool useXAxis)
    {
        ToggleChartAxisQuickCommand(ChartAxisWorkflowCommandCatalog.TickMarks(useXAxis));
    }

    private void ToggleChartAxisLabels(bool useXAxis)
    {
        ToggleChartAxisQuickCommand(ChartAxisWorkflowCommandCatalog.Labels(useXAxis));
    }

    private void ToggleChartAxisLabelFont(bool useXAxis)
    {
        ToggleChartAxisQuickCommand(ChartAxisWorkflowCommandCatalog.LabelFont(useXAxis));
    }

    private void ToggleChartAxisLabelAngle(bool useXAxis)
    {
        ToggleChartAxisQuickCommand(ChartAxisWorkflowCommandCatalog.LabelAngle(useXAxis));
    }

    private void ToggleChartAxisLine(bool useXAxis)
    {
        ToggleChartAxisQuickCommand(ChartAxisWorkflowCommandCatalog.AxisLine(useXAxis));
    }

    private void ToggleChartAxisGridlines(bool useXAxis)
    {
        ToggleChartAxisQuickCommand(ChartAxisWorkflowCommandCatalog.Gridlines(useXAxis));
    }

    private void ToggleChartAxisGridlineStyle(bool useXAxis)
    {
        ToggleChartAxisQuickCommand(ChartAxisWorkflowCommandCatalog.GridlineStyle(useXAxis));
    }

    private void ToggleChartAxisNumberFormat(bool useXAxis)
    {
        ToggleChartAxisQuickCommand(ChartAxisWorkflowCommandCatalog.NumberFormat(useXAxis));
    }

    private void ToggleChartAxisLogScale(bool useXAxis)
    {
        ToggleChartAxisPlannedCommand(
            ChartAxisWorkflowCommandCatalog.LogScale(useXAxis),
            ChartAxisPlanner.PlanLogScaleToggle);
    }

    private void ToggleChartAxisBounds(bool useXAxis)
    {
        ToggleChartAxisPlannedCommand(
            ChartAxisWorkflowCommandCatalog.Bounds(useXAxis),
            ChartAxisPlanner.PlanBoundsToggle);
    }

    private void ToggleChartAxisQuickCommand(ChartAxisWorkflowCommandDescriptor command)
    {
        if (command.QuickCommand is not { } quickCommand)
            throw new ArgumentException("Axis command descriptor does not have a quick command.", nameof(command));

        if (!TryExecuteRepeatableChartLayout(
                UiText.Get(command.TitleResourceKey),
                UiText.Get(command.HostMissingSelectionMessageResourceKey),
                null,
                null,
                chart => ChartAxisPlanner.PlanQuickCommand(chart, command.UseXAxis, quickCommand)))
            return;

        UpdateViewport();
    }

    private void ToggleChartAxisPlannedCommand(
        ChartAxisWorkflowCommandDescriptor command,
        Func<Sheet, ChartModel, bool, ChartAxisCommandPlan> planner)
    {
        IWorkbookCommand CreateCommand()
        {
            var sheet = _workbook.GetSheet(_currentSheetId);
            var chart = FindFirstChart(sheet);
            if (sheet is null || chart is null)
                return new FailedWorkbookCommand(UiText.Get(command.HostMissingSelectionMessageResourceKey));

            var plan = planner(sheet, chart, command.UseXAxis);
            if (plan.Options is not { } options)
                return new FailedWorkbookCommand(GetChartAxisCommandIssueMessage(plan.Issue, command.UseXAxis));

            return ChartCommandWorkflowPlanner.BuildLayoutCommand(_currentSheetId, chart, options);
        }

        if (!TryExecuteRepeatableCommand(CreateCommand, UiText.Get(command.TitleResourceKey), out _))
            return;
        UpdateViewport();
    }

    private static string GetChartAxisCommandIssueMessage(ChartAxisCommandIssue issue, bool useXAxis) =>
        ChartValidationPresentationPlanner.DescribeAxisCommandIssue(issue, useXAxis).Resolve(UiText.Get, UiText.Format);

    private static ChartModel? FindFirstChart(Sheet? sheet)
        => ChartWorkflowTargetPlanner.FindFirstChart(sheet);
}

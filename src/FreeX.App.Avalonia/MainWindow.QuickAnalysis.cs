using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using FreeX.App.Presentation.QuickAnalysis;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    /// <summary>
    /// Opens the Quick Analysis popup for the current multi-cell selection. The UI-free
    /// <see cref="QuickAnalysisShellRequestPlanner"/> plans selection support, grouped display items,
    /// shell actions, and hover metadata. Each item is rendered as a native button; the few items
    /// without a shell command (PivotTable, running/percent totals) stay visible but report that they are
    /// not yet available.
    /// </summary>
    private async Task ShowQuickAnalysisDialogAsync()
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var selection = _session.SelectedRange;
        var request = QuickAnalysisShellRequestPlanner.Build(
            _session.ActiveSheet,
            selection,
            QuickAnalysisShellCapabilities.DirectApplyLimited);
        var openPlan = QuickAnalysisShellOpenPlanner.Plan(request);
        if (!openPlan.CanOpen || openPlan.Selection is not { } range)
        {
            ShowQuickAnalysisOpenIssue(openPlan);
            return;
        }

        var shellPlan = openPlan.ShellPlan;
        var dialog = new Window
        {
            Title = UiText.Get("TableLoc_QaDialogTitle"),
            Width = 420,
            Height = 460,
            MinWidth = 360,
            MinHeight = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "QuickAnalysisDialog");

        var groupsPanel = new StackPanel { Spacing = 14 };
        foreach (var group in shellPlan.Groups)
        {
            groupsPanel.Children.Add(new TextBlock
            {
                Text = UiText.Get(group.TitleResourceKey),
                Foreground = HeaderForeground,
                FontWeight = FontWeight.SemiBold,
            });

            var buttonRow = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var item in group.Items)
            {
                var captured = item;
                var button = new Button
                {
                    Content = item.Label,
                    MinWidth = 116,
                    Margin = new Thickness(0, 0, 8, 8),
                };
                AutomationProperties.SetAutomationId(button, item.AutomationId);
                button.Click += (_, _) =>
                {
                    dialog.Close();
                    ApplyQuickAnalysisItem(captured);
                };
                buttonRow.Children.Add(button);
            }

            groupsPanel.Children.Add(buttonRow);
        }

        var closeButton = new Button { Content = UiText.Get("TableLoc_Close"), IsCancel = true };
        ApplyDialogButtonChrome(closeButton, 84);
        AutomationProperties.SetAutomationId(closeButton, "QuickAnalysisCloseButton");
        closeButton.Click += (_, _) => dialog.Close();

        var buttonBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
            Children = { closeButton },
        };
        DockPanel.SetDock(buttonBar, Dock.Bottom);

        dialog.Content = new DockPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                buttonBar,
                new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = UiText.Format("TableLoc_QaSuggestionsFor", FormatRangeReference(range)),
                            Foreground = HeaderForeground,
                            TextWrapping = TextWrapping.Wrap,
                        },
                        new ScrollViewer { Content = groupsPanel },
                    },
                },
            },
        };

        await dialog.ShowDialog(this);
    }

    private void ShowQuickAnalysisOpenIssue(QuickAnalysisShellOpenPlan openPlan)
    {
        var issue = openPlan.Issue
            ?? throw new InvalidOperationException("Quick Analysis open issue was not planned.");

        if (issue.RequiresSelectionReference &&
            openPlan.Selection is { } range)
        {
            ShowEditIssue(UiText.Format(issue.DialogResourceKey, FormatRangeReference(range)));
            return;
        }

        ShowEditIssue(UiText.Get(issue.DialogResourceKey));
    }

    /// <summary>
    /// Executes a chosen Quick Analysis item by routing it to the matching existing shell command
    /// path. Conditional-format presets reuse the preset command path, Totals reuse AutoSum, Sparklines
    /// reuse the sparkline insert command, Charts reuse the add-chart command, Tables reuse the create-table
    /// command; the remaining deferred suggestions (PivotTable, running/percent totals) report a status note
    /// without changing the workbook.
    /// </summary>
    private void ApplyQuickAnalysisItem(QuickAnalysisShellItemPlan item)
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var operation = QuickAnalysisHostOperationPlanner.Plan(item);
        switch (operation.Kind)
        {
            case QuickAnalysisHostOperationKind.ApplyConditionalFormat
                when operation.ConditionalFormatPreset is { } preset:
                ApplyConditionalFormatPreset(preset);
                break;

            case QuickAnalysisHostOperationKind.InsertAggregateTotalFormula
                when operation.TotalFunction is { } function:
                InsertAutoSumFormula(function);
                break;

            case QuickAnalysisHostOperationKind.InsertSparkline
                when operation.SparklineKind is { } sparklineKind:
                InsertQuickAnalysisSparklines(sparklineKind);
                break;

            case QuickAnalysisHostOperationKind.InsertChart when operation.ChartType is { } chartType:
                InsertChartFromSelection(chartType);
                break;

            case QuickAnalysisHostOperationKind.CreateTable:
                _ = InsertTableFromSelectionAsync();
                break;

            case QuickAnalysisHostOperationKind.Deferred:
                RefreshShell(operation.DeferredNote ?? UiText.Get("TableLoc_QaSuggestionNotAvailable"));
                break;
        }
    }

    /// <summary>
    /// Inserts one sparkline per data row beside the selection through the shared session command path,
    /// reusing the Core <see cref="AddSparklineCommand"/> the sparkline renderer already paints.
    /// </summary>
    private void InsertQuickAnalysisSparklines(SparklineKind kind)
    {
        var range = _session.SelectedRange;
        var description = QuickAnalysisSelectionReader.Describe(_session.ActiveSheet, range);
        var commands = QuickAnalysisSparklinePlanner.BuildCommands(
            _session.ActiveSheet.Id, range, description.HasHeaderRow, kind);
        if (commands.Count == 0)
        {
            ShowEditIssue(UiText.Get("TableLoc_QaSparklinesNeedTwoColumns"));
            return;
        }

        foreach (var command in commands)
        {
            var result = _session.ExecuteReviewCommand(command);
            if (!result.Success)
            {
                ShowEditIssue(result.ErrorMessage ?? UiText.Get("TableLoc_QaInsertSparklineFailed"));
                return;
            }
        }

        RefreshShell(UiText.Format("TableLoc_QaInsertedSparklines", commands.Count, FormatRangeReference(range)));
    }
}

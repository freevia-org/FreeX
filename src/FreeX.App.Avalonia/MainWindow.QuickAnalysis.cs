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
    /// <summary>The display title for each Quick Analysis group, in model order.</summary>
    private static string QuickAnalysisGroupTitle(QuickAnalysisGroup group) =>
        group switch
        {
            QuickAnalysisGroup.Formatting => "Formatting",
            QuickAnalysisGroup.Charts => "Charts",
            QuickAnalysisGroup.Totals => "Totals",
            QuickAnalysisGroup.Tables => "Tables",
            QuickAnalysisGroup.Sparklines => "Sparklines",
            _ => group.ToString(),
        };

    /// <summary>
    /// Opens the Quick Analysis popup for the current multi-cell selection. The selection is described by
    /// the UI-free <see cref="QuickAnalysisSelectionReader"/>, then turned into grouped suggestions by the
    /// portable <see cref="QuickAnalysisModelBuilder"/>. Each suggestion is a button wired through
    /// <see cref="QuickAnalysisCommandRouter"/> to an existing shell command path; the few suggestions
    /// without a shell command (PivotTable, running/percent totals) stay visible but report that they are
    /// not yet available.
    /// </summary>
    private async Task ShowQuickAnalysisDialogAsync()
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var range = _session.SelectedRange;
        if (range.CellCount <= 1)
        {
            ShowEditIssue("Select more than one cell to use Quick Analysis.");
            return;
        }

        var description = QuickAnalysisSelectionReader.Describe(_session.ActiveSheet, range);
        var model = QuickAnalysisModelBuilder.Build(description);
        if (model.IsEmpty)
        {
            ShowEditIssue($"No Quick Analysis suggestions for {FormatRangeReference(range)}.");
            return;
        }

        var dialog = new Window
        {
            Title = "Quick Analysis",
            Width = 420,
            Height = 460,
            MinWidth = 360,
            MinHeight = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "QuickAnalysisDialog");

        var groupsPanel = new StackPanel { Spacing = 14 };
        foreach (var group in model.Groups)
        {
            groupsPanel.Children.Add(new TextBlock
            {
                Text = QuickAnalysisGroupTitle(group.Group),
                Foreground = HeaderForeground,
                FontWeight = FontWeight.SemiBold,
            });

            var buttonRow = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var suggestion in group.Suggestions)
            {
                var captured = suggestion;
                var button = new Button
                {
                    Content = suggestion.Label,
                    MinWidth = 116,
                    Margin = new Thickness(0, 0, 8, 8),
                };
                AutomationProperties.SetAutomationId(button, $"QuickAnalysis_{suggestion.Id}");
                button.Click += (_, _) =>
                {
                    dialog.Close();
                    ApplyQuickAnalysisSuggestion(captured);
                };
                buttonRow.Children.Add(button);
            }

            groupsPanel.Children.Add(buttonRow);
        }

        var closeButton = new Button { Content = "Close", IsCancel = true, MinWidth = 84 };
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
                            Text = $"Suggestions for {FormatRangeReference(range)}",
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

    /// <summary>
    /// Executes a chosen Quick Analysis suggestion by routing it to the matching existing shell command
    /// path. Conditional-format presets reuse the preset command path, Totals reuse AutoSum, Sparklines
    /// reuse the sparkline insert command, Charts reuse the add-chart command, Tables reuse the create-table
    /// command; the remaining deferred suggestions (PivotTable, running/percent totals) report a status note
    /// without changing the workbook.
    /// </summary>
    private void ApplyQuickAnalysisSuggestion(QuickAnalysisSuggestion suggestion)
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var route = QuickAnalysisCommandRouter.Route(suggestion);
        switch (route.Kind)
        {
            case QuickAnalysisCommandKind.ConditionalFormatPreset when route.Preset is { } preset:
                ApplyConditionalFormatPreset(preset);
                break;

            case QuickAnalysisCommandKind.AutoSum when route.AutoSumFunction is { } function:
                InsertAutoSumFormula(function);
                break;

            case QuickAnalysisCommandKind.Sparkline when route.SparklineKind is { } sparklineKind:
                InsertQuickAnalysisSparklines(sparklineKind);
                break;

            case QuickAnalysisCommandKind.InsertChart when route.ChartType is { } chartType:
                InsertChartFromSelection(chartType);
                break;

            case QuickAnalysisCommandKind.Table:
                InsertTableFromSelection();
                break;

            default:
                RefreshShell(route.DeferredNote ?? "This Quick Analysis suggestion is not yet available.");
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
            ShowEditIssue("Quick Analysis sparklines need at least two columns of data.");
            return;
        }

        foreach (var command in commands)
        {
            var result = _session.ExecuteReviewCommand(command);
            if (!result.Success)
            {
                ShowEditIssue(result.ErrorMessage ?? "Insert Sparkline failed.");
                return;
            }
        }

        RefreshShell($"Inserted {commands.Count} sparkline(s) beside {FormatRangeReference(range)}");
    }
}

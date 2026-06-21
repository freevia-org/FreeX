using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using FreeX.App.Avalonia.Dialogs;
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
            QuickAnalysisGroup.Formatting => UiText.Get("TableLoc_QaGroupFormatting"),
            QuickAnalysisGroup.Charts => UiText.Get("TableLoc_QaGroupCharts"),
            QuickAnalysisGroup.Totals => UiText.Get("TableLoc_QaGroupTotals"),
            QuickAnalysisGroup.Tables => UiText.Get("TableLoc_QaGroupTables"),
            QuickAnalysisGroup.Sparklines => UiText.Get("TableLoc_QaGroupSparklines"),
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
            ShowEditIssue(UiText.Get("TableLoc_QaSelectMoreThanOne"));
            return;
        }

        var description = QuickAnalysisSelectionReader.Describe(_session.ActiveSheet, range);
        var model = QuickAnalysisModelBuilder.Build(description);
        if (model.IsEmpty)
        {
            ShowEditIssue(UiText.Format("TableLoc_QaNoSuggestions", FormatRangeReference(range)));
            return;
        }

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

        var closeButton = new Button { Content = UiText.Get("TableLoc_Close"), IsCancel = true, MinWidth = 84 };
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
            case QuickAnalysisCommandKind.ConditionalFormat
                when route.ConditionalFormat is { } conditionalFormat &&
                     TryMapQuickAnalysisConditionalFormatPreset(conditionalFormat, out var preset):
                ApplyConditionalFormatPreset(preset);
                break;

            case QuickAnalysisCommandKind.InsertTotalFormula
                when route.TotalFormulaKind == QuickAnalysisTotalFormulaKind.Aggregate &&
                     route.TotalFunction is { } function &&
                     IsQuickAnalysisAutoSumFunction(function):
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

            case QuickAnalysisCommandKind.PivotTable:
                RefreshShell("Converting to a PivotTable is not yet available on macOS.");
                break;

            case QuickAnalysisCommandKind.InsertTotalFormula:
                RefreshShell("This total is not yet available on macOS.");
                break;

            default:
                RefreshShell(route.DeferredNote ?? UiText.Get("TableLoc_QaSuggestionNotAvailable"));
                break;
        }
    }

    private static bool TryMapQuickAnalysisConditionalFormatPreset(
        QuickAnalysisConditionalFormatCommand command,
        out ConditionalFormatPreset preset)
    {
        preset = command switch
        {
            QuickAnalysisConditionalFormatCommand.DataBar => ConditionalFormatPreset.DataBar,
            QuickAnalysisConditionalFormatCommand.ColorScale => ConditionalFormatPreset.ColorScale,
            QuickAnalysisConditionalFormatCommand.IconSet => ConditionalFormatPreset.IconSet,
            QuickAnalysisConditionalFormatCommand.GreaterThan => ConditionalFormatPreset.HighlightGreaterThan,
            QuickAnalysisConditionalFormatCommand.LessThan => ConditionalFormatPreset.HighlightLessThan,
            QuickAnalysisConditionalFormatCommand.Between => ConditionalFormatPreset.HighlightBetween,
            QuickAnalysisConditionalFormatCommand.EqualTo => ConditionalFormatPreset.HighlightEqualTo,
            QuickAnalysisConditionalFormatCommand.TextContains => ConditionalFormatPreset.HighlightTextContains,
            QuickAnalysisConditionalFormatCommand.DateOccurring => ConditionalFormatPreset.HighlightDateOccurring,
            QuickAnalysisConditionalFormatCommand.DuplicateValues => ConditionalFormatPreset.HighlightDuplicateValues,
            QuickAnalysisConditionalFormatCommand.Top10Items => ConditionalFormatPreset.Top10,
            QuickAnalysisConditionalFormatCommand.Top10Percent => ConditionalFormatPreset.Top10Percent,
            QuickAnalysisConditionalFormatCommand.Bottom10Items => ConditionalFormatPreset.Bottom10Items,
            QuickAnalysisConditionalFormatCommand.Bottom10Percent => ConditionalFormatPreset.Bottom10Percent,
            QuickAnalysisConditionalFormatCommand.AboveAverage => ConditionalFormatPreset.AboveAverage,
            QuickAnalysisConditionalFormatCommand.BelowAverage => ConditionalFormatPreset.BelowAverage,
            _ => default
        };

        return Enum.IsDefined(command);
    }

    private static bool IsQuickAnalysisAutoSumFunction(string function) =>
        string.Equals(function, "SUM", StringComparison.Ordinal) ||
        string.Equals(function, "AVERAGE", StringComparison.Ordinal) ||
        string.Equals(function, "COUNT", StringComparison.Ordinal) ||
        string.Equals(function, "MAX", StringComparison.Ordinal) ||
        string.Equals(function, "MIN", StringComparison.Ordinal);

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

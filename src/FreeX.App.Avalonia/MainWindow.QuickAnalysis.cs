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
    /// <summary>
    /// Opens the Quick Analysis popup for the current multi-cell selection. The selection is described by
    /// the UI-free <see cref="QuickAnalysisSelectionReader"/>, then turned into grouped display items by the
    /// portable <see cref="QuickAnalysisModelBuilder"/>. Each item is a button wired through
    /// <see cref="QuickAnalysisCommandRouter"/> to an existing shell command path; the few items
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
        var displayModel = model.ToDisplayModel();
        if (displayModel.IsEmpty)
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
        foreach (var group in displayModel.Groups)
        {
            groupsPanel.Children.Add(new TextBlock
            {
                Text = UiText.Get(QuickAnalysisShellPlanner.GroupTitleResourceKey(group.Group)),
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
                AutomationProperties.SetAutomationId(button, $"QuickAnalysis_{item.Id}");
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

    /// <summary>
    /// Executes a chosen Quick Analysis item by routing it to the matching existing shell command
    /// path. Conditional-format presets reuse the preset command path, Totals reuse AutoSum, Sparklines
    /// reuse the sparkline insert command, Charts reuse the add-chart command, Tables reuse the create-table
    /// command; the remaining deferred suggestions (PivotTable, running/percent totals) report a status note
    /// without changing the workbook.
    /// </summary>
    private void ApplyQuickAnalysisItem(QuickAnalysisDisplayItem item)
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var action = QuickAnalysisShellActionPlanner.Plan(item, QuickAnalysisShellCapabilities.DirectApplyLimited);
        switch (action.Kind)
        {
            case QuickAnalysisShellActionKind.ApplyConditionalFormat
                when action.ConditionalFormat is { } conditionalFormat &&
                     TryMapQuickAnalysisConditionalFormatPreset(conditionalFormat, out var preset):
                ApplyConditionalFormatPreset(preset);
                break;

            case QuickAnalysisShellActionKind.InsertAggregateTotalFormula
                when action.TotalFunction is { } function:
                InsertAutoSumFormula(function);
                break;

            case QuickAnalysisShellActionKind.InsertSparkline when action.SparklineKind is { } sparklineKind:
                InsertQuickAnalysisSparklines(sparklineKind);
                break;

            case QuickAnalysisShellActionKind.InsertChart when action.ChartType is { } chartType:
                InsertChartFromSelection(chartType);
                break;

            case QuickAnalysisShellActionKind.CreateTable:
                _ = InsertTableFromSelectionAsync();
                break;

            case QuickAnalysisShellActionKind.Deferred:
                RefreshShell(action.DeferredNote ?? UiText.Get("TableLoc_QaSuggestionNotAvailable"));
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

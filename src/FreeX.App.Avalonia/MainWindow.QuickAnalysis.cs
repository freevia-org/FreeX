using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.QuickAnalysis;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private Flyout? _quickAnalysisFlyout;

    /// <summary>
    /// Opens the Quick Analysis popup for the current multi-cell selection. The UI-free
    /// <see cref="QuickAnalysisShellRequestPlanner"/> plans selection support, grouped display items,
    /// shell actions, and hover metadata. Each item is rendered as a native button; the few items
    /// without a shell command (PivotTable, running/percent totals) stay visible but report that they are
    /// not yet available.
    /// </summary>
    private Task ShowQuickAnalysisDialogAsync()
    {
        if (!TryCommitPendingFormulaEdit())
            return Task.CompletedTask;

        var selection = _session.SelectedRange;
        var request = QuickAnalysisShellRequestPlanner.Build(
            _session.ActiveSheet,
            selection,
            QuickAnalysisShellCapabilities.DirectApplyLimited);
        var openPlan = QuickAnalysisShellOpenPlanner.Plan(request);
        if (!openPlan.CanOpen || openPlan.Selection is not { } range)
        {
            ShowQuickAnalysisOpenIssue(openPlan);
            return Task.CompletedTask;
        }

        var shellPlan = openPlan.ShellPlan;
        _quickAnalysisFlyout?.Hide();
        var flyout = new Flyout
        {
            Placement = PlacementMode.BottomEdgeAlignedRight,
            ShowMode = FlyoutShowMode.Standard,
        };
        _quickAnalysisFlyout = flyout;

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
                buttonRow.Children.Add(CreateQuickAnalysisItemButton(flyout, item));
            }

            groupsPanel.Children.Add(buttonRow);
        }

        var closeButton = new Button { Content = UiText.Get("TableLoc_Close") };
        ApplyDialogButtonChrome(closeButton, 84);
        AutomationProperties.SetAutomationId(closeButton, "QuickAnalysisCloseButton");
        closeButton.Click += (_, _) => flyout.Hide();

        var buttonBar = AvaloniaCompactDialogChrome.CreateActionRow([closeButton], new Thickness(0, 10, 0, 0));
        DockPanel.SetDock(buttonBar, Dock.Bottom);

        var content = new Border
        {
            Width = 500,
            MaxHeight = 460,
            Padding = new Thickness(16),
            Child = new DockPanel
            {
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
            },
        };
        AutomationProperties.SetAutomationId(content, "QuickAnalysisFlyout");
        flyout.Content = content;
        flyout.Closed += (_, _) =>
        {
            if (ReferenceEquals(_quickAnalysisFlyout, flyout))
                _quickAnalysisFlyout = null;
        };

        flyout.ShowAt((Control?)_activeCellBorder ?? _sheetGridHost);
        return Task.CompletedTask;
    }

    private Button CreateQuickAnalysisItemButton(Flyout flyout, QuickAnalysisShellItemPlan item)
    {
        var button = new Button
        {
            Content = CreateQuickAnalysisItemButtonContent(item),
            MinWidth = 150,
            Margin = new Thickness(0, 0, 8, 8),
            Padding = new Thickness(8, 5),
        };
        AutomationProperties.SetAutomationId(button, item.AutomationId);
        ToolTip.SetTip(button, item.ToolTip);
        button.Click += (_, _) =>
        {
            flyout.Hide();
            ApplyQuickAnalysisItem(item);
        };
        return button;
    }

    private static Control CreateQuickAnalysisItemButtonContent(QuickAnalysisShellItemPlan item) =>
        new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            Children =
            {
                QuickAnalysisPreviewIconFactory.Create(item.PreviewVisual),
                new TextBlock
                {
                    Text = item.Label,
                    VerticalAlignment = AvaloniaVerticalAlignment.Center,
                },
            },
        };

    private void ShowQuickAnalysisOpenIssue(QuickAnalysisShellOpenPlan openPlan)
    {
        ShowEditIssue(QuickAnalysisShellOpenPlanner.FormatIssueText(
            openPlan,
            QuickAnalysisShellOpenIssueTextTarget.Dialog,
            UiText.Get,
            (resourceKey, rangeReference) => UiText.Format(resourceKey, rangeReference),
            FormatRangeReference));
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
                when operation.SparklineKind is not null:
                InsertQuickAnalysisSparklines(operation);
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
    private void InsertQuickAnalysisSparklines(QuickAnalysisHostOperation operation)
    {
        var range = _session.SelectedRange;
        if (!QuickAnalysisHostOperationPlanner.TryBuildSparklineCommands(
            operation,
            _session.ActiveSheet,
            range,
            out var commands))
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

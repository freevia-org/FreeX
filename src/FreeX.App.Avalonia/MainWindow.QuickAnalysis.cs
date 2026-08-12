using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.QuickAnalysis;
using FreeX.App.Services;
using FreeX.Core.Model;

using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private Flyout? _quickAnalysisFlyout;
    private readonly QuickAnalysisShellSession _quickAnalysisSession = new();

    /// <summary>
    /// Opens the Quick Analysis popup for the current multi-cell selection. The UI-free
    /// <see cref="QuickAnalysisShellRequestPlanner"/> plans selection support, grouped display items,
    /// shell actions, and hover metadata. Each item is rendered as a native button and dispatched through
    /// the existing dialog and command paths shared with the rest of the Avalonia shell.
    /// </summary>
    private Task ShowQuickAnalysisDialogAsync()
    {
        if (!TryCommitPendingFormulaEdit())
            return Task.CompletedTask;

        _quickAnalysisFlyout?.Hide();
        var openPlan = _quickAnalysisSession.PlanOpen(
            _session.ActiveSheet,
            _session.SelectedRange,
            QuickAnalysisShellCapabilities.DialogBacked);
        if (!openPlan.CanOpen || openPlan.Selection is not { } range)
        {
            ShowQuickAnalysisOpenIssue(openPlan);
            return Task.CompletedTask;
        }

        var shellPlan = openPlan.ShellPlan;
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
            IsEnabled = item.IsEnabled,
        };
        AutomationProperties.SetAutomationId(button, item.AutomationId);
        ToolTip.SetTip(button, item.ToolTip);
        button.Click += async (_, _) =>
        {
            flyout.Hide();
            try
            {
                await ApplyQuickAnalysisItemAsync(item);
            }
            catch (Exception exception)
            {
                ShowEditIssue(exception.Message);
            }
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
                QuickAnalysisPreviewIconFactory.Create(item.PreviewIcon),
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
    /// path. Conditional-format dialogs and clear reuse the existing editor/command paths, Totals reuse the
    /// shared edit planner, Sparklines reuse the sparkline insert command, Charts reuse the add-chart command,
    /// Tables reuse the create-table command, and PivotTables reuse the existing create dialog.
    /// </summary>
    private async Task ApplyQuickAnalysisItemAsync(QuickAnalysisShellItemPlan item)
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        await _quickAnalysisSession.ExecuteSelectionAsync(
            item,
            CreateQuickAnalysisOperationHandlers());
    }

    private QuickAnalysisOperationHandlers CreateQuickAnalysisOperationHandlers() =>
        new(
            OpenConditionalFormatDialogAsync: ShowQuickAnalysisConditionalFormatDialogAsync,
            ApplyConditionalFormatAsync: preset =>
                ExecuteQuickAnalysisAction(() => ApplyConditionalFormatPreset(preset)),
            ClearConditionalFormattingAsync: () =>
                ExecuteQuickAnalysisAction(ClearConditionalFormatsFromSelection),
            InsertChartAsync: chartType =>
                ExecuteQuickAnalysisAction(() => InsertChartFromSelection(chartType)),
            OpenChartPickerAsync: OpenQuickAnalysisChartPickerAsync,
            ExecuteTotalAsync: ExecuteQuickAnalysisTotalAsync,
            CreateTableAsync: InsertTableFromSelectionAsync,
            CreatePivotTableAsync: ShowInsertPivotTableDialogAsync,
            InsertSparklineAsync: ExecuteQuickAnalysisSparklinesAsync,
            ShowDeferredAsync: note =>
                ExecuteQuickAnalysisAction(() => RefreshShell(note)));

    private static Task ExecuteQuickAnalysisAction(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    private async Task OpenQuickAnalysisChartPickerAsync()
    {
        if (await ShowChartTypePickerAsync(ChartType.Column) is { } pickedChartType)
            InsertChartFromSelection(pickedChartType);
    }

    private async Task ShowQuickAnalysisConditionalFormatDialogAsync(
        QuickAnalysisConditionalFormatDialogPlan dialogPlan)
    {
        Action<ConditionalFormatRuleDialogInspection>? inspection = null;
        ResolveQuickAnalysisConditionalFormatInspection(ref inspection);
        var built = await ShowConditionalFormatRuleEditorAsync(
            dialogPlan.Seed,
            inspection);
        if (built is null)
            return;

        RunConditionalFormatCommand(ConditionalFormatCommandPlanner.PlanApplyRule(
            _session.GetCurrentGroupedEditSheetIds(),
            ResolveConditionalFormatSelectionRanges(built.AppliesTo),
            built));
    }

    partial void ResolveQuickAnalysisConditionalFormatInspection(
        ref Action<ConditionalFormatRuleDialogInspection>? inspection);

    private Task ExecuteQuickAnalysisTotalAsync(QuickAnalysisHostOperation operation)
    {
        var result = _session.ExecuteQuickAnalysisTotal(operation);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? result.CommandTitle);
            return Task.CompletedTask;
        }

        if (!result.IsNoOp)
            RefreshShell(result.CommandTitle);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Inserts one sparkline per data row beside the selection through the shared session command path,
    /// reusing the Core <see cref="AddSparklineCommand"/> the sparkline renderer already paints.
    /// </summary>
    private Task ExecuteQuickAnalysisSparklinesAsync(QuickAnalysisHostOperation operation)
    {
        var result = _session.ExecuteQuickAnalysisSparklines(operation);
        if (result.Failure == QuickAnalysisWorkbookOperationFailure.InvalidSparklineSelection)
        {
            ShowEditIssue(UiText.Get("TableLoc_QaSparklinesNeedTwoColumns"));
            return Task.CompletedTask;
        }

        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? UiText.Get("TableLoc_QaInsertSparklineFailed"));
            return Task.CompletedTask;
        }

        RefreshShell(UiText.Format(
            "TableLoc_QaInsertedSparklines",
            result.AppliedItemCount,
            FormatRangeReference(result.SourceRange)));
        return Task.CompletedTask;
    }
}

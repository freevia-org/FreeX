using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using FreeX.App.Presentation.QuickAnalysis;
using FreeX.Core.Model;
using FreeX.App.UI;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private Popup? _quickAnalysisPopup;
    private readonly QuickAnalysisShellSession _quickAnalysisSession = new();

    internal void ShowQuickAnalysisMenu()
    {
        CloseQuickAnalysisMenu();
        var sheet = _workbook.GetSheet(_currentSheetId);
        var openPlan = _quickAnalysisSession.PlanOpen(
            sheet,
            SheetGrid.SelectedRange,
            QuickAnalysisShellCapabilities.DialogBacked);
        if (!openPlan.CanOpen || openPlan.Selection is not { } range)
        {
            ShowQuickAnalysisUnavailableStatus(openPlan);
            return;
        }

        var shellPlan = openPlan.ShellPlan;
        var popup = new Popup
        {
            PlacementTarget = SheetGrid,
            Placement = PlacementMode.RelativePoint,
            StaysOpen = false,
            AllowsTransparency = true,
        };
        _quickAnalysisPopup = popup;
        if (SheetGrid.Viewport is { } viewport)
        {
            var anchor = QuickAnalysisMenuPlacementPlanner.BuildAnchor(
                range,
                viewport,
                SheetGrid.ActualRowHeaderWidth,
                SheetGrid.EffectiveColHeaderHeight);
            popup.HorizontalOffset = anchor.X;
            popup.VerticalOffset = anchor.Y;
        }

        var tabs = new TabControl
        {
            Width = 380,
            MaxHeight = 420,
            Background = SystemColors.WindowBrush,
            BorderBrush = SystemColors.ActiveBorderBrush,
            BorderThickness = new Thickness(1),
        };
        AutomationProperties.SetAutomationId(tabs, "QuickAnalysisTabs");
        foreach (var group in shellPlan.Groups)
        {
            var optionPanel = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(8),
            };

            foreach (var item in group.Items)
            {
                var option = new Button
                {
                    Width = 112,
                    Height = 72,
                    Margin = new Thickness(3),
                    Padding = new Thickness(6),
                    Tag = item,
                    IsEnabled = item.IsEnabled,
                    ToolTip = item.ToolTip,
                    Content = CreateQuickAnalysisOptionContent(item),
                };
                AutomationProperties.SetName(option, item.Label);
                AutomationProperties.SetAutomationId(option, item.AutomationId);
                option.MouseEnter += QuickAnalysisMenuItem_MouseEnter;
                option.MouseLeave += QuickAnalysisMenuItem_MouseLeave;
                option.GotKeyboardFocus += QuickAnalysisMenuItem_GotKeyboardFocus;
                option.LostKeyboardFocus += QuickAnalysisMenuItem_LostKeyboardFocus;
                option.Click += QuickAnalysisMenuItem_Click;
                optionPanel.Children.Add(option);
            }

            tabs.Items.Add(new TabItem
            {
                Header = UiText.Get(group.TitleResourceKey),
                Content = new ScrollViewer
                {
                    MaxHeight = 350,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = optionPanel,
                },
            });
        }

        tabs.SelectionChanged += (_, _) => ClearQuickAnalysisPreview(resetStatus: false);
        var palette = new Border
        {
            Background = SystemColors.WindowBrush,
            BorderBrush = SystemColors.ActiveBorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4),
            Child = tabs,
        };
        palette.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape)
                return;
            args.Handled = true;
            popup.IsOpen = false;
            SheetGrid.Focus();
        };
        popup.Child = palette;
        popup.Opened += (_, _) =>
        {
            var firstOption = tabs.Items.OfType<TabItem>()
                .Select(tab => (tab.Content as ScrollViewer)?.Content as WrapPanel)
                .Where(panel => panel is not null)
                .SelectMany(panel => panel!.Children.OfType<Button>())
                .FirstOrDefault(button => button.IsEnabled);
            if (firstOption is not null)
            {
                firstOption.Focus();
                Keyboard.Focus(firstOption);
            }
        };
        popup.Closed += (_, _) =>
        {
            if (ReferenceEquals(_quickAnalysisPopup, popup))
                _quickAnalysisPopup = null;
            ClearQuickAnalysisPreview();
        };
        popup.IsOpen = true;
    }

    private void CloseQuickAnalysisMenu()
    {
        if (_quickAnalysisPopup is { IsOpen: true } popup)
            popup.IsOpen = false;
        _quickAnalysisPopup = null;
    }

    private void ShowQuickAnalysisUnavailableStatus(QuickAnalysisShellOpenPlan openPlan)
    {
        ClearQuickAnalysisPreview(resetStatus: false);
        StatusReadyText.Text = QuickAnalysisShellOpenPlanner.FormatIssueText(
            openPlan,
            QuickAnalysisShellOpenIssueTextTarget.Status,
            UiText.Get,
            (resourceKey, rangeReference) => UiText.Format(resourceKey, rangeReference),
            range => FormatRangeReference(range.Start, range.End));
    }

    private static FrameworkElement CreateQuickAnalysisOptionContent(QuickAnalysisShellItemPlan item)
    {
        var content = new StackPanel
        {
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
        };
        content.Children.Add(QuickAnalysisPreviewIconFactory.Create(item.PreviewIcon));
        content.Children.Add(new TextBlock
        {
            Text = item.Label,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Margin = new Thickness(0, 3, 0, 0),
        });
        return content;
    }

    private void QuickAnalysisMenuItem_Click(object sender, RoutedEventArgs e) =>
        RunGuardedUiCommand("Quick Analysis", () => ExecuteQuickAnalysisAsync(sender, e));

    private async Task ExecuteQuickAnalysisAsync(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: QuickAnalysisShellItemPlan item })
            return;

        CloseQuickAnalysisMenu();
        await _quickAnalysisSession.ExecuteSelectionAsync(
            item,
            CreateQuickAnalysisOperationHandlers(sender, e));
    }

    private QuickAnalysisOperationHandlers CreateQuickAnalysisOperationHandlers(
        object sender,
        RoutedEventArgs eventArgs) =>
        new(
            OpenConditionalFormatDialogAsync: dialogPlan =>
                ExecuteQuickAnalysisAction(() => ShowCfDialog(dialogPlan.Title)),
            ApplyConditionalFormatAsync: null,
            ClearConditionalFormattingAsync: () =>
                ExecuteQuickAnalysisAction(() => CfClearRulesMenuItem_Click(sender, eventArgs)),
            InsertChartAsync: chartType =>
                ExecuteQuickAnalysisAction(() => InsertChartOfType(chartType)),
            OpenChartPickerAsync: () =>
                ExecuteQuickAnalysisAction(() => InsertChartPickerBtn_Click(sender, eventArgs)),
            ExecuteTotalAsync: ExecuteQuickAnalysisTotalAsync,
            CreateTableAsync: () =>
                ExecuteQuickAnalysisAction(() => TableBtn_Click(sender, eventArgs)),
            CreatePivotTableAsync: () =>
                ExecuteQuickAnalysisAction(() => PivotTableBtn_Click(sender, eventArgs)),
            InsertSparklineAsync: operation =>
                string.IsNullOrWhiteSpace(operation.SparklineDialogKind)
                    ? Task.CompletedTask
                    : ExecuteQuickAnalysisAction(() => InsertSparkline(operation.SparklineDialogKind)),
            ShowDeferredAsync: note =>
                ExecuteQuickAnalysisAction(() => StatusReadyText.Text = note));

    private static Task ExecuteQuickAnalysisAction(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    private Task ExecuteQuickAnalysisTotalAsync(QuickAnalysisHostOperation operation)
    {
        SynchronizeWorkbookSessionSelection();
        var result = _session.ExecuteQuickAnalysisTotal(operation);
        RecordDiagnosticEvent("command_invoked", new Dictionary<string, string?>
        {
            ["command"] = result.CommandTitle,
            ["status"] = result.Success ? "succeeded" : "failed"
        });
        if (!result.Success)
        {
            ShowCommandError(ToCommandOutcome(result.EditResult), result.CommandTitle);
            return Task.CompletedTask;
        }

        if (result.IsNoOp)
            return Task.CompletedTask;

        InvalidateNavigationCaches();
        ApplyWorkbookSessionSelectionToRenderer();
        SyncWindowViewState([_currentSheetId]);
        NotifyOtherWindowsOfWorkbookChange();
        UpdateViewport();
        return Task.CompletedTask;
    }

    private void QuickAnalysisMenuItem_MouseEnter(object sender, MouseEventArgs e)
    {
        ShowQuickAnalysisPreview(sender);
    }

    private void QuickAnalysisMenuItem_MouseLeave(object sender, MouseEventArgs e)
    {
        ClearQuickAnalysisPreview();
    }

    private void QuickAnalysisMenuItem_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        ShowQuickAnalysisPreview(sender);
    }

    private void QuickAnalysisMenuItem_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        ClearQuickAnalysisPreview();
    }

    internal void ShowQuickAnalysisPreview(object sender)
    {
        if (sender is not Button { Tag: QuickAnalysisShellItemPlan item })
            return;

        var preview = _quickAnalysisSession.PlanPreview(item);
        ApplyQuickAnalysisPreview(
            preview.Range,
            preview.Visual);
        if (preview.StatusText is { } statusText)
            StatusReadyText.Text = statusText;
    }

    private void ClearQuickAnalysisPreview(bool resetStatus = true)
    {
        var preview = _quickAnalysisSession.PlanPreviewClear(resetStatus);
        ApplyQuickAnalysisPreview(preview.Range, preview.Visual);
        if (preview.ShouldResetStatus)
            StatusReadyText.Text = UiText.Get("MainWindow_Text_Ready");
    }

    private void ApplyQuickAnalysisPreview(GridRange? range, QuickAnalysisPreviewVisualKind visual)
    {
        if (SheetGrid.QuickAnalysisPreviewRange != range)
            SheetGrid.QuickAnalysisPreviewRange = range;
        if (SheetGrid.QuickAnalysisPreviewVisual != visual)
            SheetGrid.QuickAnalysisPreviewVisual = visual;
    }
}

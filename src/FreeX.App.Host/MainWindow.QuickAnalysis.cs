using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using FreeX.App.Presentation.QuickAnalysis;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using FreeX.App.UI;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private ContextMenu? _quickAnalysisMenu;
    private readonly QuickAnalysisShellSession _quickAnalysisSession = new();

    private void ShowQuickAnalysisMenu()
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
        var menu = new ContextMenu
        {
            PlacementTarget = SheetGrid,
            Placement = PlacementMode.RelativePoint
        };
        _quickAnalysisMenu = menu;
        if (SheetGrid.Viewport is { } viewport)
        {
            var anchor = QuickAnalysisMenuPlacementPlanner.BuildAnchor(
                range,
                viewport,
                SheetGrid.ActualRowHeaderWidth,
                SheetGrid.EffectiveColHeaderHeight);
            menu.HorizontalOffset = anchor.X;
            menu.VerticalOffset = anchor.Y;
        }

        menu.Opened += QuickAnalysisMenu_Opened;
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_quickAnalysisMenu, menu))
                _quickAnalysisMenu = null;
            ClearQuickAnalysisPreview();
        };

        foreach (var group in shellPlan.Groups)
        {
            if (menu.Items.Count > 0)
                menu.Items.Add(new Separator());

            menu.Items.Add(new MenuItem
            {
                Header = UiText.Get(group.TitleResourceKey),
                IsEnabled = false
            });

            foreach (var item in group.Items)
            {
                var menuItem = new MenuItem
                {
                    Header = item.Label,
                    Tag = item,
                    ToolTip = item.ToolTip,
                    IsEnabled = item.IsEnabled,
                    Icon = QuickAnalysisPreviewIconFactory.Create(item.PreviewIcon)
                };
                menuItem.MouseEnter += QuickAnalysisMenuItem_MouseEnter;
                menuItem.MouseLeave += QuickAnalysisMenuItem_MouseLeave;
                menuItem.GotKeyboardFocus += QuickAnalysisMenuItem_GotKeyboardFocus;
                menuItem.LostKeyboardFocus += QuickAnalysisMenuItem_LostKeyboardFocus;
                menuItem.Click += QuickAnalysisMenuItem_Click;
                menu.Items.Add(menuItem);
            }
        }

        MenuKeyTipAssigner.AssignUniqueKeyTips(menu.Items.OfType<MenuItem>().Where(item => item.IsEnabled));
        menu.IsOpen = true;
    }

    private void CloseQuickAnalysisMenu()
    {
        if (_quickAnalysisMenu is { IsOpen: true } menu)
            menu.IsOpen = false;
        _quickAnalysisMenu = null;
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

    private static void QuickAnalysisMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu)
            return;

        MenuItem? firstEnabledItem = null;
        foreach (var item in menu.Items)
        {
            if (item is not MenuItem menuItem || !menuItem.IsEnabled)
                continue;

            firstEnabledItem = menuItem;
            break;
        }

        if (firstEnabledItem is null)
            return;

        firstEnabledItem.Focus();
        Keyboard.Focus(firstEnabledItem);
    }

    private void QuickAnalysisMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: QuickAnalysisShellItemPlan item })
            return;

        var operation = _quickAnalysisSession.PlanSelection(item);
        if (operation is null)
            return;

        switch (operation.Kind)
        {
            case QuickAnalysisHostOperationKind.OpenConditionalFormatDialog
                when operation.ConditionalFormatDialogTitle is { } title:
                ShowCfDialog(title);
                break;
            case QuickAnalysisHostOperationKind.ClearConditionalFormatting:
                CfClearRulesMenuItem_Click(sender, e);
                break;
            case QuickAnalysisHostOperationKind.InsertChart when operation.ChartType is { } chartType:
                InsertChartOfType(chartType);
                break;
            case QuickAnalysisHostOperationKind.OpenChartPicker:
                InsertChartPickerBtn_Click(sender, e);
                break;
            case QuickAnalysisHostOperationKind.InsertAggregateTotalFormula:
            case QuickAnalysisHostOperationKind.InsertPercentTotalFormula:
            case QuickAnalysisHostOperationKind.InsertRunningTotalFormula:
                InsertQuickAnalysisTotalFormulas(operation);
                break;
            case QuickAnalysisHostOperationKind.CreateTable:
                TableBtn_Click(sender, e);
                break;
            case QuickAnalysisHostOperationKind.CreatePivotTable:
                PivotTableBtn_Click(sender, e);
                break;
            case QuickAnalysisHostOperationKind.InsertSparkline when operation.SparklineDialogKind is { } sparklineDialogKind:
                InsertQuickAnalysisSparkline(sparklineDialogKind);
                break;
            case QuickAnalysisHostOperationKind.Deferred when operation.DeferredNote is { } note:
                StatusReadyText.Text = note;
                break;
        }
    }

    private void InsertQuickAnalysisSparkline(string dialogKind)
    {
        InsertSparkline(dialogKind);
    }

    private void InsertQuickAnalysisTotalFormulas(QuickAnalysisHostOperation operation)
    {
        if (SheetGrid.SelectedRange is not { } range)
            return;

        if (!QuickAnalysisHostOperationPlanner.TryBuildTotalFormulaEdits(operation, range, out var edits))
            return;

        var title = operation.TotalCommandTitle ?? "Quick Analysis Total";
        var outcome = _commandBus.ExecuteRepeatable(
            _workbook.Id,
            () => new EditCellsCommand(_currentSheetId, edits));
        if (!outcome.Success)
        {
            ShowCommandError(outcome, title);
            return;
        }

        RecalculateIfAutomatic(outcome.AffectedCells ?? edits.Select(edit => edit.Address).ToList());
        SetActiveCell(edits[^1].Address);
        UpdateViewport();
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

    private void ShowQuickAnalysisPreview(object sender)
    {
        if (sender is not MenuItem { Tag: QuickAnalysisShellItemPlan item })
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

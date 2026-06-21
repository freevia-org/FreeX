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
    private bool _suppressNextQuickAnalysisClosedStatusReset;
    private bool _preserveQuickAnalysisUnsupportedStatus;

    private void ShowQuickAnalysisMenu()
    {
        if (SheetGrid.SelectedRange is not { } range)
        {
            ShowQuickAnalysisUnsupportedSelectionStatus();
            return;
        }

        var options = QuickAnalysisPlanner.BuildOptions(range);
        if (options.Count == 0)
        {
            ShowQuickAnalysisUnsupportedSelectionStatus();
            return;
        }

        _preserveQuickAnalysisUnsupportedStatus = false;
        CloseQuickAnalysisMenu();
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
            ClearQuickAnalysisPreview(resetStatus: !_suppressNextQuickAnalysisClosedStatusReset);
            _suppressNextQuickAnalysisClosedStatusReset = false;
        };

        string? currentGroup = null;
        foreach (var option in options)
        {
            if (currentGroup != option.Group)
            {
                if (currentGroup is not null)
                    menu.Items.Add(new Separator());

                menu.Items.Add(new MenuItem
                {
                    Header = option.Group,
                    IsEnabled = false
                });
                currentGroup = option.Group;
            }

            var item = new MenuItem
            {
                Header = option.Label,
                Tag = option,
                ToolTip = option.PreviewText,
                Icon = QuickAnalysisPreviewIconFactory.Create(option.PreviewVisual)
            };
            item.MouseEnter += QuickAnalysisMenuItem_MouseEnter;
            item.MouseLeave += QuickAnalysisMenuItem_MouseLeave;
            item.GotKeyboardFocus += QuickAnalysisMenuItem_GotKeyboardFocus;
            item.LostKeyboardFocus += QuickAnalysisMenuItem_LostKeyboardFocus;
            item.Click += QuickAnalysisMenuItem_Click;
            menu.Items.Add(item);
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

    private void ShowQuickAnalysisUnsupportedSelectionStatus()
    {
        _preserveQuickAnalysisUnsupportedStatus = true;
        _suppressNextQuickAnalysisClosedStatusReset = true;
        CloseQuickAnalysisMenu();
        ClearQuickAnalysisPreview(resetStatus: false);
        StatusReadyText.Text = UiText.Get("QuickAnalysis_SelectRangeStatus");
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
        if (sender is not MenuItem { Tag: QuickAnalysisOption option })
            return;

        var route = QuickAnalysisCommandRouter.Route(option);
        switch (route.Kind)
        {
            case QuickAnalysisCommandKind.ConditionalFormat when route.ConditionalFormat is { } conditionalFormat:
                ShowCfDialog(QuickAnalysisConditionalFormatDialogTitle(conditionalFormat));
                break;
            case QuickAnalysisCommandKind.ClearConditionalFormatting:
                CfClearRulesMenuItem_Click(sender, e);
                break;
            case QuickAnalysisCommandKind.InsertChart when route.ChartType is { } chartType:
                InsertChartOfType(chartType);
                break;
            case QuickAnalysisCommandKind.MoreCharts:
                InsertChartPickerBtn_Click(sender, e);
                break;
            case QuickAnalysisCommandKind.InsertTotalFormula
                when route.TotalFormulaKind == QuickAnalysisTotalFormulaKind.Aggregate &&
                     !string.IsNullOrWhiteSpace(route.TotalFunction):
                InsertQuickAnalysisTotalFormulas(
                    range => QuickAnalysisTotalsPlanner.BuildAggregateEdits(range, route.TotalFunction),
                    $"Quick Analysis {option.Label}");
                break;
            case QuickAnalysisCommandKind.InsertTotalFormula
                when route.TotalFormulaKind == QuickAnalysisTotalFormulaKind.PercentTotal:
                InsertQuickAnalysisTotalFormulas(QuickAnalysisTotalsPlanner.BuildPercentTotalEdits, "Quick Analysis % Total");
                break;
            case QuickAnalysisCommandKind.InsertTotalFormula
                when route.TotalFormulaKind == QuickAnalysisTotalFormulaKind.RunningTotal:
                InsertQuickAnalysisTotalFormulas(QuickAnalysisTotalsPlanner.BuildRunningTotalEdits, "Quick Analysis Running Total");
                break;
            case QuickAnalysisCommandKind.Table:
                TableBtn_Click(sender, e);
                break;
            case QuickAnalysisCommandKind.PivotTable:
                PivotTableBtn_Click(sender, e);
                break;
            case QuickAnalysisCommandKind.Sparkline when route.SparklineKind is { } sparklineKind:
                InsertQuickAnalysisSparkline(sparklineKind);
                break;
        }
    }

    private static string QuickAnalysisConditionalFormatDialogTitle(QuickAnalysisConditionalFormatCommand command) =>
        command switch
        {
            QuickAnalysisConditionalFormatCommand.DataBar => "Data Bar",
            QuickAnalysisConditionalFormatCommand.ColorScale => "Color Scale",
            QuickAnalysisConditionalFormatCommand.IconSet => "Icon Set",
            QuickAnalysisConditionalFormatCommand.GreaterThan => "Greater Than",
            QuickAnalysisConditionalFormatCommand.LessThan => "Less Than",
            QuickAnalysisConditionalFormatCommand.Between => "Between",
            QuickAnalysisConditionalFormatCommand.EqualTo => "Equal To",
            QuickAnalysisConditionalFormatCommand.TextContains => "Text Contains",
            QuickAnalysisConditionalFormatCommand.DateOccurring => "Date Occurring",
            QuickAnalysisConditionalFormatCommand.DuplicateValues => "Duplicate Values",
            QuickAnalysisConditionalFormatCommand.Top10Items => "Top 10 Items",
            QuickAnalysisConditionalFormatCommand.Top10Percent => "Top 10%",
            QuickAnalysisConditionalFormatCommand.Bottom10Items => "Bottom 10 Items",
            QuickAnalysisConditionalFormatCommand.Bottom10Percent => "Bottom 10%",
            QuickAnalysisConditionalFormatCommand.AboveAverage => "Above Average",
            QuickAnalysisConditionalFormatCommand.BelowAverage => "Below Average",
            _ => command.ToString()
        };

    private void InsertQuickAnalysisSparkline(SparklineKind kind)
    {
        var dialogKind = kind switch
        {
            SparklineKind.Column => "column",
            SparklineKind.WinLoss => "winloss",
            _ => "line"
        };

        InsertSparkline(dialogKind);
    }

    private void InsertQuickAnalysisTotalFormulas(
        Func<GridRange, IReadOnlyList<(CellAddress Address, Cell NewCell)>> buildEdits,
        string title)
    {
        if (SheetGrid.SelectedRange is not { } range)
            return;

        var edits = buildEdits(range);
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
        if (sender is not MenuItem { Tag: QuickAnalysisOption option } ||
            SheetGrid.SelectedRange is not { } range)
        {
            return;
        }

        var preview = QuickAnalysisPlanner.BuildHoverPreview(range, option);
        _preserveQuickAnalysisUnsupportedStatus = false;
        ApplyQuickAnalysisPreview(
            preview.Range,
            MapQuickAnalysisPreviewVisual(preview.PreviewVisual.Kind));
        StatusReadyText.Text = preview.StatusText;
    }

    private void ClearQuickAnalysisPreview(bool resetStatus = true)
    {
        ApplyQuickAnalysisPreview(null, GridQuickAnalysisPreviewVisualKind.None);
        if (resetStatus && !_preserveQuickAnalysisUnsupportedStatus)
            StatusReadyText.Text = UiText.Get("MainWindow_Text_Ready");
    }

    private void ApplyQuickAnalysisPreview(GridRange? range, GridQuickAnalysisPreviewVisualKind visual)
    {
        if (SheetGrid.QuickAnalysisPreviewRange != range)
            SheetGrid.QuickAnalysisPreviewRange = range;
        if (SheetGrid.QuickAnalysisPreviewVisual != visual)
            SheetGrid.QuickAnalysisPreviewVisual = visual;
    }

    private static GridQuickAnalysisPreviewVisualKind MapQuickAnalysisPreviewVisual(QuickAnalysisPreviewVisualKind kind) =>
        kind switch
        {
            QuickAnalysisPreviewVisualKind.DataBars => GridQuickAnalysisPreviewVisualKind.DataBars,
            QuickAnalysisPreviewVisualKind.ColorScale => GridQuickAnalysisPreviewVisualKind.ColorScale,
            QuickAnalysisPreviewVisualKind.IconSet => GridQuickAnalysisPreviewVisualKind.IconSet,
            QuickAnalysisPreviewVisualKind.Highlight => GridQuickAnalysisPreviewVisualKind.Highlight,
            QuickAnalysisPreviewVisualKind.ClearFormat => GridQuickAnalysisPreviewVisualKind.ClearFormat,
            QuickAnalysisPreviewVisualKind.TotalFormula => GridQuickAnalysisPreviewVisualKind.TotalFormula,
            QuickAnalysisPreviewVisualKind.Table => GridQuickAnalysisPreviewVisualKind.Table,
            QuickAnalysisPreviewVisualKind.LineSparkline => GridQuickAnalysisPreviewVisualKind.LineSparkline,
            QuickAnalysisPreviewVisualKind.ColumnSparkline => GridQuickAnalysisPreviewVisualKind.ColumnSparkline,
            QuickAnalysisPreviewVisualKind.WinLossSparkline => GridQuickAnalysisPreviewVisualKind.WinLossSparkline,
            QuickAnalysisPreviewVisualKind.ColumnChart => GridQuickAnalysisPreviewVisualKind.ColumnChart,
            QuickAnalysisPreviewVisualKind.LineChart => GridQuickAnalysisPreviewVisualKind.LineChart,
            QuickAnalysisPreviewVisualKind.BarChart => GridQuickAnalysisPreviewVisualKind.BarChart,
            QuickAnalysisPreviewVisualKind.StackedColumnChart => GridQuickAnalysisPreviewVisualKind.StackedColumnChart,
            QuickAnalysisPreviewVisualKind.PieChart => GridQuickAnalysisPreviewVisualKind.PieChart,
            QuickAnalysisPreviewVisualKind.AreaChart => GridQuickAnalysisPreviewVisualKind.AreaChart,
            QuickAnalysisPreviewVisualKind.ScatterChart => GridQuickAnalysisPreviewVisualKind.ScatterChart,
            _ => GridQuickAnalysisPreviewVisualKind.None
        };
}

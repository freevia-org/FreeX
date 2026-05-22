using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public partial class MainWindow
{
    private void ShowQuickAnalysisMenu()
    {
        if (SheetGrid.SelectedRange is not { } range)
            return;

        var options = QuickAnalysisPlanner.BuildOptions(range);
        if (options.Count == 0)
        {
            StatusReadyText.Text = "Select a range to use Quick Analysis.";
            return;
        }

        var menu = new ContextMenu
        {
            PlacementTarget = SheetGrid,
            Placement = PlacementMode.MousePoint
        };
        menu.Closed += (_, _) => SheetGrid.QuickAnalysisPreviewRange = null;

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
            item.Click += QuickAnalysisMenuItem_Click;
            menu.Items.Add(item);
        }

        MenuKeyTipAssigner.AssignUniqueKeyTips(menu.Items.OfType<MenuItem>().Where(item => item.IsEnabled));
        menu.IsOpen = true;
    }

    private void QuickAnalysisMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: QuickAnalysisOption option })
            return;

        var command = option.Command;
        switch (command)
        {
            case QuickAnalysisCommand.DataBar:
                ShowCfDialog("Data Bar");
                break;
            case QuickAnalysisCommand.ColorScale:
                ShowCfDialog("Color Scale");
                break;
            case QuickAnalysisCommand.IconSet:
                ShowCfDialog("Icon Set");
                break;
            case QuickAnalysisCommand.GreaterThan:
                ShowCfDialog("Greater Than");
                break;
            case QuickAnalysisCommand.LessThan:
                ShowCfDialog("Less Than");
                break;
            case QuickAnalysisCommand.Between:
                ShowCfDialog("Between");
                break;
            case QuickAnalysisCommand.EqualTo:
                ShowCfDialog("Equal To");
                break;
            case QuickAnalysisCommand.TextContains:
                ShowCfDialog("Text Contains");
                break;
            case QuickAnalysisCommand.DateOccurring:
                ShowCfDialog("Date Occurring");
                break;
            case QuickAnalysisCommand.DuplicateValues:
                ShowCfDialog("Duplicate Values");
                break;
            case QuickAnalysisCommand.Top10:
                ShowCfDialog("Top 10 Items");
                break;
            case QuickAnalysisCommand.Top10Percent:
                ShowCfDialog("Top 10%");
                break;
            case QuickAnalysisCommand.Bottom10:
                ShowCfDialog("Bottom 10 Items");
                break;
            case QuickAnalysisCommand.Bottom10Percent:
                ShowCfDialog("Bottom 10%");
                break;
            case QuickAnalysisCommand.AboveAverage:
                ShowCfDialog("Above Average");
                break;
            case QuickAnalysisCommand.BelowAverage:
                ShowCfDialog("Below Average");
                break;
            case QuickAnalysisCommand.ClearConditionalFormatting:
                CfClearRulesMenuItem_Click(sender, e);
                break;
            case QuickAnalysisCommand.ColumnChart:
                ChartColumnMenuItem_Click(sender, e);
                break;
            case QuickAnalysisCommand.StackedColumnChart:
                ChartStackedColumnMenuItem_Click(sender, e);
                break;
            case QuickAnalysisCommand.PercentStackedColumnChart:
                ChartPercentStackedColumnMenuItem_Click(sender, e);
                break;
            case QuickAnalysisCommand.LineChart:
                ChartLineMenuItem_Click(sender, e);
                break;
            case QuickAnalysisCommand.PieChart:
                ChartPieMenuItem_Click(sender, e);
                break;
            case QuickAnalysisCommand.DoughnutChart:
                ChartDoughnutMenuItem_Click(sender, e);
                break;
            case QuickAnalysisCommand.BarChart:
                ChartBarMenuItem_Click(sender, e);
                break;
            case QuickAnalysisCommand.StackedBarChart:
                ChartStackedBarMenuItem_Click(sender, e);
                break;
            case QuickAnalysisCommand.PercentStackedBarChart:
                ChartPercentStackedBarMenuItem_Click(sender, e);
                break;
            case QuickAnalysisCommand.AreaChart:
                ChartAreaMenuItem_Click(sender, e);
                break;
            case QuickAnalysisCommand.ScatterChart:
                ChartScatterMenuItem_Click(sender, e);
                break;
            case QuickAnalysisCommand.BubbleChart:
                ChartBubbleMenuItem_Click(sender, e);
                break;
            case QuickAnalysisCommand.RadarChart:
                ChartRadarMenuItem_Click(sender, e);
                break;
            case QuickAnalysisCommand.StockChart:
                ChartStockMenuItem_Click(sender, e);
                break;
            case QuickAnalysisCommand.MoreCharts:
                InsertChartPickerBtn_Click(sender, e);
                break;
            case QuickAnalysisCommand.Sum:
                InsertQuickAnalysisTotalFormulas(range => QuickAnalysisTotalsPlanner.BuildAggregateEdits(range, "SUM"), "Quick Analysis Sum");
                break;
            case QuickAnalysisCommand.Average:
                InsertQuickAnalysisTotalFormulas(range => QuickAnalysisTotalsPlanner.BuildAggregateEdits(range, "AVERAGE"), "Quick Analysis Average");
                break;
            case QuickAnalysisCommand.Count:
                InsertQuickAnalysisTotalFormulas(range => QuickAnalysisTotalsPlanner.BuildAggregateEdits(range, "COUNT"), "Quick Analysis Count");
                break;
            case QuickAnalysisCommand.PercentTotal:
                InsertQuickAnalysisTotalFormulas(QuickAnalysisTotalsPlanner.BuildPercentTotalEdits, "Quick Analysis % Total");
                break;
            case QuickAnalysisCommand.RunningTotal:
                InsertQuickAnalysisTotalFormulas(QuickAnalysisTotalsPlanner.BuildRunningTotalEdits, "Quick Analysis Running Total");
                break;
            case QuickAnalysisCommand.Max:
                InsertQuickAnalysisTotalFormulas(range => QuickAnalysisTotalsPlanner.BuildAggregateEdits(range, "MAX"), "Quick Analysis Max");
                break;
            case QuickAnalysisCommand.Min:
                InsertQuickAnalysisTotalFormulas(range => QuickAnalysisTotalsPlanner.BuildAggregateEdits(range, "MIN"), "Quick Analysis Min");
                break;
            case QuickAnalysisCommand.FormatAsTable:
                TableBtn_Click(sender, e);
                break;
            case QuickAnalysisCommand.PivotTable:
                PivotTableBtn_Click(sender, e);
                break;
            case QuickAnalysisCommand.LineSparkline:
                SparklineLineBtn_Click(sender, e);
                break;
            case QuickAnalysisCommand.ColumnSparkline:
                SparklineColumnBtn_Click(sender, e);
                break;
            case QuickAnalysisCommand.WinLossSparkline:
                SparklineWinLossBtn_Click(sender, e);
                break;
        }
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
        if (sender is not MenuItem { Tag: QuickAnalysisOption option } ||
            SheetGrid.SelectedRange is not { } range)
            return;

        var preview = QuickAnalysisPlanner.BuildHoverPreview(range, option);
        SheetGrid.QuickAnalysisPreviewRange = preview.Range;
        StatusReadyText.Text = preview.StatusText;
    }

    private void QuickAnalysisMenuItem_MouseLeave(object sender, MouseEventArgs e)
    {
        SheetGrid.QuickAnalysisPreviewRange = null;
        StatusReadyText.Text = "Ready";
    }
}

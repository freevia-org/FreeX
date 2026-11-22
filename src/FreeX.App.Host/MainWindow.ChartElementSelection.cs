using System.Windows;
using System.Windows.Input;
using FreeX.App.Presentation.Charts.Editing;
using FreeX.App.Presentation.Charts.Selection;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

/// <summary>
/// In-chart element selection for the WPF host: Delete on a selected chart title / legend / axis / axis
/// title / gridlines / data labels / series removes just that element, and a double-click on the
/// selected element opens the existing dialog that formats it ("Format Selection"). Geometry and policy
/// come from the shared <see cref="ChartElementMapPlanner"/> / <see cref="ChartElementCommandPlanner"/>
/// (the same ones the Avalonia host uses); the grid owns the click selection and highlight.
/// </summary>
public partial class MainWindow
{
    private void OnChartElementFormatRequested(Guid chartId, ChartElementRef element)
    {
        var chart = GetSelectedChartOnCurrentSheet();
        if (chart is null || chart.Id != chartId)
            return;

        // Double-click / Format Selection opens the docked Format pane (Excel); its "More Options"
        // button reaches the full dialog below.
        ShowChartFormatPane(element);
    }

    private void OpenChartElementFormatDialog(ChartModel chart, ChartElementRef element)
    {
        switch (ChartElementCommandPlanner.GetFormatTarget(chart, element))
        {
            case ChartElementFormatTarget.ChartTitles:
                ChartTitlesBtn_Click(this, new RoutedEventArgs());
                break;
            case ChartElementFormatTarget.XAxis:
                ShowChartAxisFormatDialog(useXAxis: true);
                break;
            case ChartElementFormatTarget.YAxis:
                ShowChartAxisFormatDialog(useXAxis: false);
                break;
            case ChartElementFormatTarget.DataSeries:
                ShowChartSeriesFormatDialog();
                break;
            case ChartElementFormatTarget.DataLabels:
                ChartDataLabelsBtn_Click(this, new RoutedEventArgs());
                break;
            case ChartElementFormatTarget.Trendline:
                ShowChartTrendlineDialog(element.SeriesIndex >= 0 ? element.SeriesIndex : null);
                break;
            case ChartElementFormatTarget.ErrorBars:
                ShowChartErrorBarsDialog(element.SeriesIndex >= 0 ? element.SeriesIndex : null);
                break;
            default:
                FormatChartAreaBtn_Click(this, new RoutedEventArgs());
                break;
        }
    }

    private static FreeX.App.Presentation.Charts.Selection.ChartNavigationKey ToChartNavigationKey(Key key) => key switch
    {
        Key.Up => ChartNavigationKey.Up,
        Key.Down => ChartNavigationKey.Down,
        Key.Left => ChartNavigationKey.Left,
        _ => ChartNavigationKey.Right,
    };

    /// <summary>
    /// Delete on a selected inner chart element. Returns false when no inner element is selected so the
    /// caller falls through to deleting the chart object itself.
    /// </summary>
    private bool TryDeleteSelectedChartElement()
    {
        if (SheetGrid.SelectedChartElement is not { IsChartArea: false } element ||
            GetSelectedChartOnCurrentSheet() is not { } chart)
        {
            return false;
        }

        var name = ChartElementCommandPlanner
            .Describe(element, SheetGrid.SelectedChartElementSeriesName)
            .Resolve(UiText.Get, UiText.Format);
        var plan = ChartElementCommandPlanner.PlanDelete(chart, element);

        var command = ChartElementCommandPlanner.BuildDeleteCommand(_currentSheetId, chart, plan);

        if (command is null)
        {
            _messageService.ShowInfo(UiText.Format(ChartElementCommandPlanner.CannotDeleteStatusResourceKey, name), name);
            return true;
        }

        if (!TryExecuteCommand(command, name, out _))
            return true;

        // Like Excel, deleting an element leaves the (whole) chart selected.
        SheetGrid.ClearSelectedChartElement();
        UpdateViewport();
        return true;
    }
}

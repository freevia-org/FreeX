using FreeX.App.Presentation.Charts.Editing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// The remaining Chart Format contextual-tab "quick" buttons that step a single value per click (no dialog):
/// the Text group's chart-title / axis-title / legend / data-label color and font-size buttons, and the
/// Shape Styles group's Series Dash, Series Marker (opens the full series dialog) and Marker Size, plus the
/// Chart Design Type group's Combo Chart Series toggle. Each resolves the selected chart, computes the next
/// value through the shared <see cref="ChartQuickCommandPlanner"/>, and applies it through the shared
/// <see cref="SetChartLayoutCommand"/> via <see cref="ApplyChartLayout"/>.
/// </summary>
public sealed partial class MainWindow
{
    // ---- Chart Design ▸ Type: Combo Chart Series (quick toggle) ---------------------------------------

    private void CycleChartComboSeries()
    {
        ExecuteChartQuickCommand(
            ChartQuickCommandCatalog.ComboSeries,
            UiText.Get("ChartLoc_ComboChartsNeed"));
    }

    // ---- Chart Format ▸ Text group: title / axis-title / legend / data-label color & size -------------

    private void CycleChartTitleColor()
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.ChartTitleColor);
    }

    private void CycleChartTitleSize()
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.ChartTitleFontSize);
    }

    private void CycleChartAxisTitleColor()
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.AxisTitleColor);
    }

    private void CycleChartAxisTitleSize()
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.AxisTitleFontSize);
    }

    private void CycleChartLegendFontSize()
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.LegendFontSize);
    }

    private void CycleChartDataLabelText()
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.DataLabelTextColor);
    }

    private void CycleChartDataLabelFill()
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.DataLabelFill);
    }

    private void CycleChartDataLabelBorder()
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.DataLabelBorder);
    }

    // ---- Chart Format ▸ Shape Styles group: Series Dash / Marker / Marker Size ------------------------

    private void CycleChartSeriesDash()
    {
        ExecuteChartQuickCommand(
            ChartQuickCommandCatalog.SeriesDash,
            UiText.Get("ChartLoc_NoDataSeriesToFormat"));
    }

    private void CycleChartMarkerSize()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        var command = ChartQuickCommandCatalog.SeriesMarkerSize;
        if (!TryGetSelectedChart(command.Label, out var chart))
            return;

        if (ChartTypeSupport.GetDataSeriesCount(chart) <= 0)
        {
            RefreshShell(UiText.Get("ChartLoc_NoDataSeriesToFormat"));
            return;
        }

        if (!ChartQuickCommandPlanner.CanApply(chart, command.Command))
        {
            RefreshShell(UiText.Get("ChartLoc_MarkersAvailableOn"));
            return;
        }

        ApplyChartLayout(command.Label, chart, ChartQuickCommandPlanner.Plan(chart, command.Command));
    }

    private void ExecuteChartQuickCommand(
        ChartQuickCommandDescriptor command,
        string? unsupportedMessage = null)
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        if (!TryGetSelectedChart(command.Label, out var chart))
            return;

        if (!ChartQuickCommandPlanner.CanApply(chart, command.Command))
        {
            RefreshShell(unsupportedMessage ?? UiText.Format("ChartLoc_CommandNotYetAvailable", command.Label));
            return;
        }

        ApplyChartLayout(command.Label, chart, ChartQuickCommandPlanner.Plan(chart, command.Command));
    }
}

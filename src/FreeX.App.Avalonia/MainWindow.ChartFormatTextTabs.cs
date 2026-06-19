using FreeX.App.Presentation.Charts.Editing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// The remaining Chart Format contextual-tab "quick" buttons that step a single value per click (no dialog):
/// the Text group's chart-title / axis-title / legend / data-label color and font-size buttons, and the
/// Shape Styles group's Series Dash, Series Marker (opens the full series dialog) and Marker Size, plus the
/// Chart Design Type group's Combo Chart Series toggle. Each resolves the selected chart, computes the next
/// value through the shared <see cref="ChartQuickFormatCycler"/> (which mirrors the WPF host's
/// <c>ChartOptionCycler</c> step values), and applies it through the shared <see cref="SetChartLayoutCommand"/>
/// via <see cref="ApplyChartLayout"/>. The WPF host's <c>MainWindow.ChartCommands.cs</c> button handlers are
/// the behavior reference.
/// </summary>
public sealed partial class MainWindow
{
    // ---- Chart Design ▸ Type: Combo Chart Series (quick toggle) ---------------------------------------

    private void CycleChartComboSeries()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        if (!TryGetSelectedChart("Combo Chart Series", out var chart))
            return;

        if (!ChartComboPlanner.SupportsCombo(chart))
        {
            RefreshShell("Combo charts need a column or area chart with at least two data series.");
            return;
        }

        var nextIndexes = ChartQuickFormatCycler.NextComboLineSeries(chart);
        ApplyChartLayout("Combo Chart Series", chart, new ChartLayoutOptions(
            UseComboLineForSecondarySeries: nextIndexes.Count > 0,
            ComboLineSeriesIndexes: nextIndexes));
    }

    // ---- Chart Format ▸ Text group: title / axis-title / legend / data-label color & size -------------

    private void CycleChartTitleColor()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        if (!TryGetSelectedChart("Chart Title Color", out var chart))
            return;

        ApplyChartLayout("Chart Title Color", chart, new ChartLayoutOptions(
            ChartTitleTextColor: ChartQuickFormatCycler.NextSeriesColor(chart.ChartTitleTextColor)));
    }

    private void CycleChartTitleSize()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        if (!TryGetSelectedChart("Chart Title Size", out var chart))
            return;

        ApplyChartLayout("Chart Title Size", chart, new ChartLayoutOptions(
            ChartTitleFontSize: ChartQuickFormatCycler.NextChartTitleFontSize(chart.ChartTitleFontSize)));
    }

    private void CycleChartAxisTitleColor()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        if (!TryGetSelectedChart("Axis Title Color", out var chart))
            return;

        ApplyChartLayout("Axis Title Color", chart, new ChartLayoutOptions(
            AxisTitleTextColor: ChartQuickFormatCycler.NextSeriesColor(chart.AxisTitleTextColor)));
    }

    private void CycleChartAxisTitleSize()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        if (!TryGetSelectedChart("Axis Title Size", out var chart))
            return;

        ApplyChartLayout("Axis Title Size", chart, new ChartLayoutOptions(
            AxisTitleFontSize: ChartQuickFormatCycler.NextAxisTitleFontSize(chart.AxisTitleFontSize)));
    }

    private void CycleChartLegendFontSize()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        if (!TryGetSelectedChart("Legend Font Size", out var chart))
            return;

        ApplyChartLayout("Legend Font Size", chart, new ChartLayoutOptions(
            LegendFontSize: ChartQuickFormatCycler.NextLegendFontSize(chart.LegendFontSize)));
    }

    private void CycleChartDataLabelText()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        if (!TryGetSelectedChart("Data Label Text", out var chart))
            return;

        ApplyChartLayout("Data Label Text", chart, new ChartLayoutOptions(
            ShowDataLabels: true,
            DataLabelTextColor: ChartQuickFormatCycler.NextSeriesColor(chart.DataLabelTextColor)));
    }

    private void CycleChartDataLabelFill()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        if (!TryGetSelectedChart("Data Label Fill", out var chart))
            return;

        ApplyChartLayout("Data Label Fill", chart, new ChartLayoutOptions(
            ShowDataLabels: true,
            DataLabelFillColor: ChartQuickFormatCycler.NextSeriesColor(chart.DataLabelFillColor)));
    }

    private void CycleChartDataLabelBorder()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        if (!TryGetSelectedChart("Data Label Border", out var chart))
            return;

        ApplyChartLayout("Data Label Border", chart, new ChartLayoutOptions(
            ShowDataLabels: true,
            DataLabelBorderColor: ChartQuickFormatCycler.NextSeriesColor(chart.DataLabelBorderColor),
            DataLabelBorderThickness: ChartQuickFormatCycler.NextDataLabelBorderThickness(chart.DataLabelBorderThickness)));
    }

    // ---- Chart Format ▸ Shape Styles group: Series Dash / Marker / Marker Size ------------------------

    private void CycleChartSeriesDash()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        if (!TryGetSelectedChart("Series Dash", out var chart))
            return;

        if (ChartTypeSupport.GetDataSeriesCount(chart) <= 0)
        {
            RefreshShell("This chart has no data series to format.");
            return;
        }

        var current = ChartQuickFormatCycler.ReadFirstSeriesFormat(chart);
        var updated = current with { DashStyle = ChartQuickFormatCycler.NextSeriesDash(current.DashStyle) };
        ApplyChartLayout("Series Dash", chart, new ChartLayoutOptions(
            SeriesFormats: ChartQuickFormatCycler.MergeFirstSeriesFormat(chart, updated)));
    }

    private void CycleChartMarkerSize()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        if (!TryGetSelectedChart("Marker Size", out var chart))
            return;

        if (ChartTypeSupport.GetDataSeriesCount(chart) <= 0)
        {
            RefreshShell("This chart has no data series to format.");
            return;
        }

        if (!ChartTypeSupport.SupportsSeriesMarkers(chart.Type))
        {
            RefreshShell("Markers are available on line and scatter charts.");
            return;
        }

        var current = ChartQuickFormatCycler.ReadFirstSeriesFormat(chart);
        var updated = current with { MarkerSize = ChartQuickFormatCycler.NextMarkerSize(current.MarkerSize) };
        ApplyChartLayout("Marker Size", chart, new ChartLayoutOptions(
            SeriesFormats: ChartQuickFormatCycler.MergeFirstSeriesFormat(chart, updated)));
    }
}

using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Charts.Editing;

public enum ChartQuickCommand
{
    FirstSliceAngle,
    DoughnutHoleSize,
    ExplodedSlice,
    DataLabelCategoryName,
    DataLabelSeriesName,
    DataLabelPercentage,
    DataLabelSeparator,
    DataLabelNumberFormat,
    DataLabelCallout,
    DataLabelFill,
    DataLabelTextColor,
    DataLabelBorder,
    DataLabelFontSize,
    DataLabelAngle,
    PointDataLabel,
    ChartAreaFill,
    ChartTitleColor,
    ChartTitleFontSize,
    AxisTitleColor,
    AxisTitleFontSize,
    PlotAreaFill,
    PlotAreaBorder,
    LegendTextColor,
    LegendFill,
    LegendBorder,
    LegendFontSize,
    LegendOverlay,
    TrendlineMovingAveragePeriod,
    TrendlinePolynomialOrder,
    TrendlineEquation,
    TrendlineRSquared,
    TrendlineColor,
    TrendlineDash,
    TrendlineThickness,
    SecondaryAxisSeries,
    ComboToggle,
    ComboSeries,
    SeriesWidth,
    SeriesDash,
    SeriesMarkerSize,
}

/// <summary>
/// Portable planner for chart contextual-tab quick commands that immediately emit a chart-layout delta
/// rather than opening a dialog. Shells own selection, localization, and command-bus execution; this class
/// owns the support gates and repeat-click option composition so WPF, Avalonia, and future shells walk the
/// same command policy.
/// </summary>
public static class ChartQuickCommandPlanner
{
    public static bool CanApply(ChartModel chart, ChartQuickCommand command)
    {
        ArgumentNullException.ThrowIfNull(chart);

        return command switch
        {
            ChartQuickCommand.FirstSliceAngle => ChartPieFormatPlanner.Supports(chart),
            ChartQuickCommand.DoughnutHoleSize => ChartPieFormatPlanner.SupportsHoleSize(chart),
            ChartQuickCommand.ExplodedSlice => ChartTypeSupport.SupportsExplodedSlices(chart.Type)
                && ChartTypeSupport.GetDataPointCount(chart) > 0,
            ChartQuickCommand.PointDataLabel => ChartOptionCycler.GetSeriesCount(chart) > 0
                && ChartTypeSupport.GetDataPointCount(chart) > 0,
            ChartQuickCommand.TrendlineMovingAveragePeriod
                or ChartQuickCommand.TrendlinePolynomialOrder
                or ChartQuickCommand.TrendlineEquation
                or ChartQuickCommand.TrendlineRSquared
                or ChartQuickCommand.TrendlineColor
                or ChartQuickCommand.TrendlineDash
                or ChartQuickCommand.TrendlineThickness => ChartTrendlinePlanner.SupportsTrendlines(chart.Type),
            ChartQuickCommand.SecondaryAxisSeries => ChartTypeSupport.SupportsSecondaryAxis(chart.Type)
                && ChartOptionCycler.GetSeriesCount(chart) >= 2,
            ChartQuickCommand.ComboToggle => ChartTypeSupport.SupportsComboLineOverlay(chart.Type)
                && (chart.UseComboLineForSecondarySeries || ChartComboPlanner.SupportsCombo(chart)),
            ChartQuickCommand.ComboSeries => ChartComboPlanner.SupportsCombo(chart),
            ChartQuickCommand.SeriesWidth
                or ChartQuickCommand.SeriesDash => ChartOptionCycler.GetSeriesCount(chart) > 0,
            ChartQuickCommand.SeriesMarkerSize => ChartOptionCycler.GetSeriesCount(chart) > 0
                && ChartTypeSupport.SupportsSeriesMarkers(chart.Type),
            _ => true,
        };
    }

    public static ChartLayoutOptions Plan(ChartModel chart, ChartQuickCommand command)
    {
        ArgumentNullException.ThrowIfNull(chart);
        if (!CanApply(chart, command))
            throw new InvalidOperationException($"Chart quick command '{command}' is not supported by the current chart.");

        return command switch
        {
            ChartQuickCommand.FirstSliceAngle => new ChartLayoutOptions(
                FirstSliceAngle: chart.FirstSliceAngle >= 270 ? 0 : chart.FirstSliceAngle + 90),
            ChartQuickCommand.DoughnutHoleSize => new ChartLayoutOptions(
                DoughnutHoleSize: chart.DoughnutHoleSize switch
                {
                    < 0.45 => 0.55,
                    < 0.7 => 0.75,
                    _ => 0.35
                }),
            ChartQuickCommand.ExplodedSlice => PlanExplodedSlice(chart),
            ChartQuickCommand.DataLabelCategoryName => new ChartLayoutOptions(
                ShowDataLabels: true,
                ShowDataLabelCategoryName: !chart.ShowDataLabelCategoryName),
            ChartQuickCommand.DataLabelSeriesName => new ChartLayoutOptions(
                ShowDataLabels: true,
                ShowDataLabelSeriesName: !chart.ShowDataLabelSeriesName),
            ChartQuickCommand.DataLabelPercentage => new ChartLayoutOptions(
                ShowDataLabels: true,
                ShowDataLabelPercentage: !chart.ShowDataLabelPercentage),
            ChartQuickCommand.DataLabelSeparator => new ChartLayoutOptions(
                ShowDataLabels: true,
                DataLabelSeparator: ChartOptionCycler.NextDataLabelSeparator(chart.DataLabelSeparator)),
            ChartQuickCommand.DataLabelNumberFormat => new ChartLayoutOptions(
                ShowDataLabels: true,
                DataLabelNumberFormat: ChartOptionCycler.NextDataLabelNumberFormat(chart.DataLabelNumberFormat)),
            ChartQuickCommand.DataLabelCallout => new ChartLayoutOptions(
                ShowDataLabels: true,
                ShowDataLabelCallouts: !chart.ShowDataLabelCallouts),
            ChartQuickCommand.DataLabelFill => new ChartLayoutOptions(
                ShowDataLabels: true,
                DataLabelFillColor: ChartQuickFormatCycler.NextSeriesColor(chart.DataLabelFillColor)),
            ChartQuickCommand.DataLabelTextColor => new ChartLayoutOptions(
                ShowDataLabels: true,
                DataLabelTextColor: ChartQuickFormatCycler.NextSeriesColor(chart.DataLabelTextColor)),
            ChartQuickCommand.DataLabelBorder => new ChartLayoutOptions(
                ShowDataLabels: true,
                DataLabelBorderColor: ChartQuickFormatCycler.NextSeriesColor(chart.DataLabelBorderColor),
                DataLabelBorderThickness: ChartQuickFormatCycler.NextDataLabelBorderThickness(chart.DataLabelBorderThickness)),
            ChartQuickCommand.DataLabelFontSize => new ChartLayoutOptions(
                ShowDataLabels: true,
                DataLabelFontSize: chart.DataLabelFontSize >= 16 ? 9 : chart.DataLabelFontSize + 1),
            ChartQuickCommand.DataLabelAngle => new ChartLayoutOptions(
                ShowDataLabels: true,
                DataLabelAngle: ChartOptionCycler.NextAxisLabelAngle(chart.DataLabelAngle)),
            ChartQuickCommand.PointDataLabel => PlanPointDataLabel(chart),
            ChartQuickCommand.ChartAreaFill => new ChartLayoutOptions(
                ChartAreaFillColor: ChartQuickFormatCycler.NextSeriesColor(chart.ChartAreaFillColor)),
            ChartQuickCommand.ChartTitleColor => new ChartLayoutOptions(
                ChartTitleTextColor: ChartQuickFormatCycler.NextSeriesColor(chart.ChartTitleTextColor)),
            ChartQuickCommand.ChartTitleFontSize => new ChartLayoutOptions(
                ChartTitleFontSize: ChartQuickFormatCycler.NextChartTitleFontSize(chart.ChartTitleFontSize)),
            ChartQuickCommand.AxisTitleColor => new ChartLayoutOptions(
                AxisTitleTextColor: ChartQuickFormatCycler.NextSeriesColor(chart.AxisTitleTextColor)),
            ChartQuickCommand.AxisTitleFontSize => new ChartLayoutOptions(
                AxisTitleFontSize: ChartQuickFormatCycler.NextAxisTitleFontSize(chart.AxisTitleFontSize)),
            ChartQuickCommand.PlotAreaFill => new ChartLayoutOptions(
                PlotAreaFillColor: ChartQuickFormatCycler.NextSeriesColor(chart.PlotAreaFillColor)),
            ChartQuickCommand.PlotAreaBorder => new ChartLayoutOptions(
                PlotAreaBorderColor: ChartQuickFormatCycler.NextSeriesColor(chart.PlotAreaBorderColor),
                PlotAreaBorderThickness: ChartQuickFormatCycler.NextPlotAreaBorderThickness(chart.PlotAreaBorderThickness)),
            ChartQuickCommand.LegendTextColor => new ChartLayoutOptions(
                LegendTextColor: ChartQuickFormatCycler.NextSeriesColor(chart.LegendTextColor)),
            ChartQuickCommand.LegendFill => new ChartLayoutOptions(
                LegendFillColor: ChartQuickFormatCycler.NextSeriesColor(chart.LegendFillColor)),
            ChartQuickCommand.LegendBorder => new ChartLayoutOptions(
                LegendBorderColor: ChartQuickFormatCycler.NextSeriesColor(chart.LegendBorderColor),
                LegendBorderThickness: ChartQuickFormatCycler.NextLegendBorderThickness(chart.LegendBorderThickness)),
            ChartQuickCommand.LegendFontSize => new ChartLayoutOptions(
                LegendFontSize: ChartQuickFormatCycler.NextLegendFontSize(chart.LegendFontSize)),
            ChartQuickCommand.LegendOverlay => new ChartLayoutOptions(
                ShowLegend: true,
                LegendOverlay: !chart.LegendOverlay),
            ChartQuickCommand.TrendlineMovingAveragePeriod => new ChartLayoutOptions(
                ShowLinearTrendline: true,
                TrendlineType: ChartTrendlineType.MovingAverage,
                TrendlinePeriod: chart.TrendlinePeriod >= 6 ? 2 : chart.TrendlinePeriod + 1),
            ChartQuickCommand.TrendlinePolynomialOrder => new ChartLayoutOptions(
                ShowLinearTrendline: true,
                TrendlineType: ChartTrendlineType.Polynomial,
                TrendlineOrder: chart.TrendlineOrder >= 6 ? 2 : chart.TrendlineOrder + 1),
            ChartQuickCommand.TrendlineEquation => new ChartLayoutOptions(
                ShowLinearTrendline: true,
                ShowTrendlineEquation: !chart.ShowTrendlineEquation),
            ChartQuickCommand.TrendlineRSquared => new ChartLayoutOptions(
                ShowLinearTrendline: true,
                ShowTrendlineRSquared: !chart.ShowTrendlineRSquared),
            ChartQuickCommand.TrendlineColor => new ChartLayoutOptions(
                ShowLinearTrendline: true,
                TrendlineColor: ChartOptionCycler.NextTrendlineColor(chart.TrendlineColor)),
            ChartQuickCommand.TrendlineDash => new ChartLayoutOptions(
                ShowLinearTrendline: true,
                TrendlineDashStyle: chart.TrendlineDashStyle switch
                {
                    ChartLineDashStyle.Dash => ChartLineDashStyle.Dot,
                    ChartLineDashStyle.Dot => ChartLineDashStyle.Solid,
                    _ => ChartLineDashStyle.Dash
                }),
            ChartQuickCommand.TrendlineThickness => new ChartLayoutOptions(
                ShowLinearTrendline: true,
                TrendlineThickness: ChartQuickFormatCycler.NextTrendlineThickness(chart.TrendlineThickness)),
            ChartQuickCommand.SecondaryAxisSeries => PlanSecondaryAxisSeries(chart),
            ChartQuickCommand.ComboToggle => new ChartLayoutOptions(
                UseComboLineForSecondarySeries: !chart.UseComboLineForSecondarySeries,
                ComboLineSeriesIndexes: !chart.UseComboLineForSecondarySeries ? chart.ComboLineSeriesIndexes : []),
            ChartQuickCommand.ComboSeries => PlanComboSeries(chart),
            ChartQuickCommand.SeriesWidth => PlanFirstSeriesFormat(
                chart,
                format => format with
                {
                    StrokeThickness = format.StrokeThickness is null or >= 4 ? 1.5 : format.StrokeThickness.Value + 0.75
                }),
            ChartQuickCommand.SeriesDash => PlanFirstSeriesFormat(
                chart,
                format => format with
                {
                    DashStyle = ChartQuickFormatCycler.NextSeriesDash(format.DashStyle)
                }),
            ChartQuickCommand.SeriesMarkerSize => PlanFirstSeriesFormat(
                chart,
                format => format with
                {
                    MarkerSize = ChartQuickFormatCycler.NextMarkerSize(format.MarkerSize)
                }),
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, null),
        };
    }

    private static ChartLayoutOptions PlanExplodedSlice(ChartModel chart)
    {
        var sliceCount = ChartTypeSupport.GetDataPointCount(chart);
        var nextIndex = chart.ExplodedSliceIndex < 0
            ? 0
            : chart.ExplodedSliceIndex + 1 >= sliceCount ? -1 : chart.ExplodedSliceIndex + 1;
        var nextDistance = nextIndex < 0
            ? 0.1
            : chart.ExplodedSliceDistance >= 0.22 ? 0.1 : chart.ExplodedSliceDistance + 0.06;

        return new ChartLayoutOptions(
            ExplodedSliceIndex: nextIndex,
            ExplodedSliceDistance: nextDistance);
    }

    private static ChartLayoutOptions PlanPointDataLabel(ChartModel chart)
    {
        var formats = new List<ChartPointDataLabelFormat>(chart.PointDataLabelFormats);
        var existingIndex = IndexOfPointDataLabelFormat(formats, 0, 0);
        var current = existingIndex >= 0 ? formats[existingIndex] : new ChartPointDataLabelFormat(0, 0);
        var updated = current with
        {
            FillColor = ChartQuickFormatCycler.NextSeriesColor(current.FillColor),
            BorderColor = ChartQuickFormatCycler.NextSeriesColor(current.BorderColor ?? current.FillColor),
            BorderThickness = ChartQuickFormatCycler.NextPointDataLabelBorderThickness(current.BorderThickness),
            TextColor = ChartQuickFormatCycler.NextSeriesColor(current.TextColor),
            FontSize = current.FontSize is null or >= 16 ? 9 : current.FontSize.Value + 1
        };

        if (existingIndex >= 0)
            formats[existingIndex] = updated;
        else
            formats.Add(updated);

        return new ChartLayoutOptions(
            ShowDataLabels: true,
            PointDataLabelFormats: formats);
    }

    private static ChartLayoutOptions PlanSecondaryAxisSeries(ChartModel chart)
    {
        var next = ChartOptionCycler.GetNextSecondaryAxisSeries(chart, ChartOptionCycler.GetSeriesCount(chart));
        return new ChartLayoutOptions(
            ShowSecondaryAxis: next.ShowSecondaryAxis,
            SecondaryAxisSeriesIndexes: next.SeriesIndexes);
    }

    private static ChartLayoutOptions PlanComboSeries(ChartModel chart)
    {
        var nextIndexes = ChartQuickFormatCycler.NextComboLineSeries(chart);
        return new ChartLayoutOptions(
            UseComboLineForSecondarySeries: nextIndexes.Count > 0,
            ComboLineSeriesIndexes: nextIndexes);
    }

    private static ChartLayoutOptions PlanFirstSeriesFormat(
        ChartModel chart,
        Func<ChartSeriesFormat, ChartSeriesFormat> update)
    {
        var current = ChartQuickFormatCycler.ReadFirstSeriesFormat(chart);
        var updated = update(current);
        return new ChartLayoutOptions(SeriesFormats: ChartQuickFormatCycler.MergeFirstSeriesFormat(chart, updated));
    }

    private static int IndexOfPointDataLabelFormat(
        IReadOnlyList<ChartPointDataLabelFormat> formats,
        int seriesIndex,
        int pointIndex)
    {
        for (var index = 0; index < formats.Count; index++)
        {
            var format = formats[index];
            if (format.SeriesIndex == seriesIndex && format.PointIndex == pointIndex)
                return index;
        }

        return -1;
    }
}

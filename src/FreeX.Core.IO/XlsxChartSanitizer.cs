using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxChartSanitizer
{
    public static void SanitizeLoadedChart(ChartModel chart)
    {
        var seriesCount = ChartTypeSupport.GetDataSeriesCount(chart);
        ChartSeriesIndexSanitizer.SanitizeSecondaryAxisAndComboLineIndexes(chart, seriesCount);

        ChartTrendlineSupportPolicy.NormalizeUnsupported(chart);
        if (!ChartTypeSupport.SupportsSeriesLines(chart.Type))
        {
            chart.ShowSeriesLines = false;
            chart.SeriesLineColor = null;
            chart.SeriesLineThemeColor = null;
            chart.SeriesLineThickness = 1;
            chart.SeriesLineDashStyle = ChartLineDashStyle.Solid;
        }

        var dataPointCount = ChartTypeSupport.GetDataPointCount(chart);
        if (chart.ExplodedSliceIndex < 0 || chart.ExplodedSliceIndex >= dataPointCount)
        {
            chart.ExplodedSliceIndex = -1;
            chart.ExplodedSliceDistance = 0.1;
        }
        chart.SeriesFormats = chart.SeriesFormats
            .Where(format => format.SeriesIndex >= 0)
            .GroupBy(format => format.SeriesIndex)
            .Select(group => group.Last())
            .OrderBy(format => format.SeriesIndex)
            .ToList();
        chart.PointDataLabelFormats = chart.PointDataLabelFormats
            .Where(format => format.SeriesIndex >= 0 && format.PointIndex >= 0 && format.PointIndex < dataPointCount)
            .GroupBy(format => (format.SeriesIndex, format.PointIndex))
            .Select(group => group.Last())
            .OrderBy(format => format.SeriesIndex)
            .ThenBy(format => format.PointIndex)
            .ToList();
        if (!ChartTypeSupport.SupportsAxes(chart.Type))
        {
            chart.XAxisTitle = null;
            chart.YAxisTitle = null;
            chart.AxisTitleTextColor = null;
            chart.AxisTitleFontSize = 12;
            ClearXAxisBounds(chart);
            ClearYAxisBounds(chart);
            return;
        }

        if (!ChartTypeSupport.SupportsXAxisBounds(chart.Type))
            ClearXAxisValueBounds(chart, keepDateAxisUnits: chart.XAxisIsDateAxis);
        if (!ChartTypeSupport.SupportsYAxisBounds(chart.Type))
            ClearYAxisValueBounds(chart);
    }

    private static void ClearXAxisValueBounds(ChartModel chart, bool keepDateAxisUnits = false)
    {
        // R71-io-chart-axis-4-1: on a date (category) X axis, an explicit min/max is a pinned DATE
        // RANGE (e.g. Jan 2020..Dec 2022 as date serials) captured by
        // XlsxChartAxisReader.ApplyCategoryAxisProperties -- not a value-axis bound -- so it must
        // survive here exactly like the date-unit majorUnit/minorUnit multiplier below, even though
        // the chart type has no genuine value axis on X. Only strip bounds when the X axis is a plain
        // (non-date) category axis or a genuine value axis.
        if (!keepDateAxisUnits)
        {
            chart.XAxisMinimum = null;
            chart.XAxisMaximum = null;
        }
        // On a date (category) X axis the numeric major/minor unit is the date-unit multiplier
        // ("every 2 months"), not a value-axis bound — so it must survive even though the chart type
        // has no value axis on X. Only strip them when the X axis is genuinely a value axis.
        if (!keepDateAxisUnits)
        {
            chart.XAxisMajorUnit = null;
            chart.XAxisMinorUnit = null;
        }
        chart.XAxisLogScale = false;
        chart.XAxisNumberFormat = ChartDataLabelNumberFormat.General;
    }

    private static void ClearYAxisValueBounds(ChartModel chart)
    {
        chart.YAxisMinimum = null;
        chart.YAxisMaximum = null;
        chart.YAxisMajorUnit = null;
        chart.YAxisMinorUnit = null;
        chart.YAxisLogScale = false;
        chart.YAxisNumberFormat = ChartDataLabelNumberFormat.General;
    }

    private static void ClearXAxisBounds(ChartModel chart)
    {
        ClearXAxisValueBounds(chart);
        chart.ShowXAxisMajorGridlines = false;
        chart.ShowXAxisMinorGridlines = false;
        chart.XAxisMajorGridlineColor = null;
        chart.XAxisMinorGridlineColor = null;
        chart.XAxisGridlineThickness = 1;
        chart.XAxisMajorTickStyle = ChartAxisTickStyle.Outside;
        chart.XAxisMinorTickStyle = ChartAxisTickStyle.None;
        chart.ShowXAxisLabels = true;
        chart.XAxisLabelTextColor = null;
        chart.XAxisLabelFontSize = 11;
        chart.XAxisLabelAngle = 0;
        chart.XAxisLineColor = null;
        chart.XAxisLineThickness = 1;
    }

    private static void ClearYAxisBounds(ChartModel chart)
    {
        ClearYAxisValueBounds(chart);
        chart.ShowYAxisMajorGridlines = false;
        chart.ShowYAxisMinorGridlines = false;
        chart.YAxisMajorGridlineColor = null;
        chart.YAxisMinorGridlineColor = null;
        chart.YAxisGridlineThickness = 1;
        chart.YAxisMajorTickStyle = ChartAxisTickStyle.Outside;
        chart.YAxisMinorTickStyle = ChartAxisTickStyle.None;
        chart.ShowYAxisLabels = true;
        chart.YAxisLabelTextColor = null;
        chart.YAxisLabelFontSize = 11;
        chart.YAxisLabelAngle = 0;
        chart.YAxisLineColor = null;
        chart.YAxisLineThickness = 1;
    }
}

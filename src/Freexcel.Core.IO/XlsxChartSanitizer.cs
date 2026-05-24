using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class XlsxChartSanitizer
{
    public static void SanitizeLoadedChart(ChartModel chart)
    {
        var seriesCount = ChartTypeSupport.GetDataSeriesCount(chart);
        chart.SecondaryAxisSeriesIndexes = chart.SecondaryAxisSeriesIndexes
            .Where(index => index > 0 && index < seriesCount)
            .Distinct()
            .Order()
            .ToList();

        if (!ChartTypeSupport.SupportsSecondaryAxis(chart.Type)
            || (chart.ShowSecondaryAxis && chart.SecondaryAxisSeriesIndexes.Count == 0))
        {
            chart.ShowSecondaryAxis = false;
            chart.SecondaryAxisSeriesIndexes = [];
        }

        chart.ComboLineSeriesIndexes = chart.ComboLineSeriesIndexes
            .Where(index => index > 0 && index < seriesCount)
            .Distinct()
            .Order()
            .ToList();

        if (!ChartTypeSupport.SupportsComboLineOverlay(chart)
            || (chart.UseComboLineForSecondarySeries && chart.ComboLineSeriesIndexes.Count == 0))
        {
            chart.UseComboLineForSecondarySeries = false;
            chart.ComboLineSeriesIndexes = [];
        }

        if (!ChartTypeSupport.SupportsTrendlines(chart.Type))
        {
            chart.ShowLinearTrendline = false;
            chart.TrendlineType = ChartTrendlineType.Linear;
            chart.TrendlinePeriod = 2;
            chart.TrendlineOrder = 2;
            chart.ShowTrendlineEquation = false;
            chart.ShowTrendlineRSquared = false;
            chart.TrendlineColor = null;
            chart.TrendlineThemeColor = null;
            chart.TrendlineThickness = 1.5;
            chart.TrendlineDashStyle = ChartLineDashStyle.Dash;
        }
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
            ClearXAxisValueBounds(chart);
        if (!ChartTypeSupport.SupportsYAxisBounds(chart.Type))
            ClearYAxisValueBounds(chart);
    }

    private static void ClearXAxisValueBounds(ChartModel chart)
    {
        chart.XAxisMinimum = null;
        chart.XAxisMaximum = null;
        chart.XAxisMajorUnit = null;
        chart.XAxisMinorUnit = null;
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

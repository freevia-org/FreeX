using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public sealed partial class SetChartLayoutCommand
{
    private static void ApplyOptions(ChartModel chart, ChartLayoutOptions options)
    {
        if (options.Title is not null)
            chart.Title = options.Title;
        if (options.XAxisTitle is not null)
            chart.XAxisTitle = options.XAxisTitle;
        if (options.YAxisTitle is not null)
            chart.YAxisTitle = options.YAxisTitle;
        if (options.ChartTitleTextColor is not null)
            chart.ChartTitleTextColor = options.ChartTitleTextColor;
        if (options.ChartTitleFontSize is not null)
            chart.ChartTitleFontSize = ClampFinite(options.ChartTitleFontSize.Value, 6, 72);
        if (options.AxisTitleTextColor is not null)
            chart.AxisTitleTextColor = options.AxisTitleTextColor;
        if (options.AxisTitleFontSize is not null)
            chart.AxisTitleFontSize = ClampFinite(options.AxisTitleFontSize.Value, 6, 72);
        if (options.ChartAreaFillColor is not null)
        {
            chart.ChartAreaFillColor = options.ChartAreaFillColor;
            chart.ChartAreaFillThemeColor = null;
        }
        if (options.PlotAreaFillColor is not null)
        {
            chart.PlotAreaFillColor = options.PlotAreaFillColor;
            chart.PlotAreaFillThemeColor = null;
        }
        if (options.PlotAreaBorderColor is not null)
        {
            chart.PlotAreaBorderColor = options.PlotAreaBorderColor;
            chart.PlotAreaBorderThemeColor = null;
        }
        if (options.PlotAreaBorderThickness is not null)
            chart.PlotAreaBorderThickness = ClampFinite(options.PlotAreaBorderThickness.Value, 0, 10);
        if (options.LegendTextColor is not null)
        {
            chart.LegendTextColor = options.LegendTextColor;
            chart.LegendTextThemeColor = null;
        }
        if (options.LegendFillColor is not null)
        {
            chart.LegendFillColor = options.LegendFillColor;
            chart.LegendFillThemeColor = null;
        }
        if (options.LegendBorderColor is not null)
        {
            chart.LegendBorderColor = options.LegendBorderColor;
            chart.LegendBorderThemeColor = null;
        }
        if (options.LegendBorderThickness is not null)
            chart.LegendBorderThickness = ClampFinite(options.LegendBorderThickness.Value, 0, 10);
        if (options.LegendFontSize is not null)
            chart.LegendFontSize = ClampFinite(options.LegendFontSize.Value, 6, 72);
        if (options.DoughnutHoleSize is not null)
            chart.DoughnutHoleSize = ClampFinite(options.DoughnutHoleSize.Value, 0.1, 0.9);
        if (options.FirstSliceAngle is not null)
            chart.FirstSliceAngle = NormalizeAngle(options.FirstSliceAngle.Value);
        if (options.ExplodedSliceIndex is not null)
        {
            var requestedIndex = options.ExplodedSliceIndex.Value;
            var dataPointCount = ChartTypeSupport.GetDataPointCount(chart);
            chart.ExplodedSliceIndex = requestedIndex >= 0 && requestedIndex < dataPointCount
                ? requestedIndex
                : -1;
        }
        if (options.ExplodedSliceDistance is not null)
            chart.ExplodedSliceDistance = ClampFinite(options.ExplodedSliceDistance.Value, 0, 0.5);
        if (options.ClearXAxisBounds)
            ClearXAxisBounds(chart);
        if (options.ClearYAxisBounds)
            ClearYAxisBounds(chart);
        if (options.XAxisMinimum is not null)
            chart.XAxisMinimum = FiniteOrNull(options.XAxisMinimum.Value);
        if (options.XAxisMaximum is not null)
            chart.XAxisMaximum = FiniteOrNull(options.XAxisMaximum.Value);
        if (options.XAxisMajorUnit is not null)
            chart.XAxisMajorUnit = PositiveFiniteOrMin(options.XAxisMajorUnit.Value, double.Epsilon);
        if (options.XAxisMinorUnit is not null)
            chart.XAxisMinorUnit = PositiveFiniteOrMin(options.XAxisMinorUnit.Value, double.Epsilon);
        if (options.XAxisLogScale is not null)
            chart.XAxisLogScale = options.XAxisLogScale.Value;
        if (options.XAxisNumberFormat is not null)
            chart.XAxisNumberFormat = ValidEnumOrDefault(options.XAxisNumberFormat.Value, ChartDataLabelNumberFormat.General);
        if (options.ShowXAxisMajorGridlines is not null)
            chart.ShowXAxisMajorGridlines = options.ShowXAxisMajorGridlines.Value;
        if (options.ShowXAxisMinorGridlines is not null)
            chart.ShowXAxisMinorGridlines = options.ShowXAxisMinorGridlines.Value;
        if (options.XAxisMajorGridlineColor is not null)
            chart.XAxisMajorGridlineColor = options.XAxisMajorGridlineColor;
        if (options.XAxisMinorGridlineColor is not null)
            chart.XAxisMinorGridlineColor = options.XAxisMinorGridlineColor;
        if (options.XAxisGridlineThickness is not null)
            chart.XAxisGridlineThickness = ClampFinite(options.XAxisGridlineThickness.Value, 0.25, 10);
        if (options.XAxisMajorTickStyle is not null)
            chart.XAxisMajorTickStyle = ValidEnumOrDefault(options.XAxisMajorTickStyle.Value, ChartAxisTickStyle.Outside);
        if (options.XAxisMinorTickStyle is not null)
            chart.XAxisMinorTickStyle = ValidEnumOrDefault(options.XAxisMinorTickStyle.Value, ChartAxisTickStyle.None);
        if (options.ShowXAxisLabels is not null)
            chart.ShowXAxisLabels = options.ShowXAxisLabels.Value;
        if (options.XAxisLabelTextColor is not null)
            chart.XAxisLabelTextColor = options.XAxisLabelTextColor;
        if (options.XAxisLabelFontSize is not null)
            chart.XAxisLabelFontSize = ClampFinite(options.XAxisLabelFontSize.Value, 6, 72);
        if (options.XAxisLabelAngle is not null)
            chart.XAxisLabelAngle = ClampFinite(options.XAxisLabelAngle.Value, -90, 90);
        if (options.XAxisLineColor is not null)
            chart.XAxisLineColor = options.XAxisLineColor;
        if (options.XAxisLineThickness is not null)
            chart.XAxisLineThickness = ClampFinite(options.XAxisLineThickness.Value, 0.5, 10);
        if (options.YAxisMinimum is not null)
            chart.YAxisMinimum = FiniteOrNull(options.YAxisMinimum.Value);
        if (options.YAxisMaximum is not null)
            chart.YAxisMaximum = FiniteOrNull(options.YAxisMaximum.Value);
        if (options.YAxisMajorUnit is not null)
            chart.YAxisMajorUnit = PositiveFiniteOrMin(options.YAxisMajorUnit.Value, double.Epsilon);
        if (options.YAxisMinorUnit is not null)
            chart.YAxisMinorUnit = PositiveFiniteOrMin(options.YAxisMinorUnit.Value, double.Epsilon);
        if (options.YAxisLogScale is not null)
            chart.YAxisLogScale = options.YAxisLogScale.Value;
        if (options.YAxisNumberFormat is not null)
            chart.YAxisNumberFormat = ValidEnumOrDefault(options.YAxisNumberFormat.Value, ChartDataLabelNumberFormat.General);
        if (options.ShowYAxisMajorGridlines is not null)
            chart.ShowYAxisMajorGridlines = options.ShowYAxisMajorGridlines.Value;
        if (options.ShowYAxisMinorGridlines is not null)
            chart.ShowYAxisMinorGridlines = options.ShowYAxisMinorGridlines.Value;
        if (options.YAxisMajorGridlineColor is not null)
            chart.YAxisMajorGridlineColor = options.YAxisMajorGridlineColor;
        if (options.YAxisMinorGridlineColor is not null)
            chart.YAxisMinorGridlineColor = options.YAxisMinorGridlineColor;
        if (options.YAxisGridlineThickness is not null)
            chart.YAxisGridlineThickness = ClampFinite(options.YAxisGridlineThickness.Value, 0.25, 10);
        if (options.YAxisMajorTickStyle is not null)
            chart.YAxisMajorTickStyle = ValidEnumOrDefault(options.YAxisMajorTickStyle.Value, ChartAxisTickStyle.Outside);
        if (options.YAxisMinorTickStyle is not null)
            chart.YAxisMinorTickStyle = ValidEnumOrDefault(options.YAxisMinorTickStyle.Value, ChartAxisTickStyle.None);
        if (options.ShowYAxisLabels is not null)
            chart.ShowYAxisLabels = options.ShowYAxisLabels.Value;
        if (options.YAxisLabelTextColor is not null)
            chart.YAxisLabelTextColor = options.YAxisLabelTextColor;
        if (options.YAxisLabelFontSize is not null)
            chart.YAxisLabelFontSize = ClampFinite(options.YAxisLabelFontSize.Value, 6, 72);
        if (options.YAxisLabelAngle is not null)
            chart.YAxisLabelAngle = ClampFinite(options.YAxisLabelAngle.Value, -90, 90);
        if (options.YAxisLineColor is not null)
            chart.YAxisLineColor = options.YAxisLineColor;
        if (options.YAxisLineThickness is not null)
            chart.YAxisLineThickness = ClampFinite(options.YAxisLineThickness.Value, 0.5, 10);
        if (options.LegendPosition is not null)
            chart.LegendPosition = ValidEnumOrDefault(options.LegendPosition.Value, ChartLegendPosition.Right);
        if (options.LegendOverlay is not null)
            chart.LegendOverlay = options.LegendOverlay.Value;
        if (options.ShowLegend is not null)
            chart.ShowLegend = options.ShowLegend.Value;
        if (options.ShowDataLabels is not null)
            chart.ShowDataLabels = options.ShowDataLabels.Value;
        if (options.DataLabelPosition is not null)
            chart.DataLabelPosition = ValidEnumOrDefault(options.DataLabelPosition.Value, ChartDataLabelPosition.BestFit);
        if (options.ShowDataLabelCategoryName is not null)
            chart.ShowDataLabelCategoryName = options.ShowDataLabelCategoryName.Value;
        if (options.ShowDataLabelSeriesName is not null)
            chart.ShowDataLabelSeriesName = options.ShowDataLabelSeriesName.Value;
        if (options.ShowDataLabelPercentage is not null)
            chart.ShowDataLabelPercentage = options.ShowDataLabelPercentage.Value;
        if (options.DataLabelSeparator is not null)
            chart.DataLabelSeparator = ValidEnumOrDefault(options.DataLabelSeparator.Value, ChartDataLabelSeparator.Comma);
        if (options.DataLabelNumberFormat is not null)
            chart.DataLabelNumberFormat = ValidEnumOrDefault(options.DataLabelNumberFormat.Value, ChartDataLabelNumberFormat.General);
        if (options.ShowDataLabelCallouts is not null)
            chart.ShowDataLabelCallouts = options.ShowDataLabelCallouts.Value;
        if (options.DataLabelFillColor is not null)
        {
            chart.DataLabelFillColor = options.DataLabelFillColor;
            chart.DataLabelFillThemeColor = null;
        }
        if (options.DataLabelBorderColor is not null)
        {
            chart.DataLabelBorderColor = options.DataLabelBorderColor;
            chart.DataLabelBorderThemeColor = null;
        }
        if (options.DataLabelTextColor is not null)
        {
            chart.DataLabelTextColor = options.DataLabelTextColor;
            chart.DataLabelTextThemeColor = null;
        }
        if (options.DataLabelBorderThickness is not null)
            chart.DataLabelBorderThickness = ClampFinite(options.DataLabelBorderThickness.Value, 0, 10);
        if (options.DataLabelFontSize is not null)
            chart.DataLabelFontSize = ClampFinite(options.DataLabelFontSize.Value, 6, 72);
        if (options.DataLabelAngle is not null)
            chart.DataLabelAngle = ClampFinite(options.DataLabelAngle.Value, -90, 90);
        if (options.ShowLinearTrendline is not null)
            chart.ShowLinearTrendline = options.ShowLinearTrendline.Value;
        if (options.TrendlineType is not null)
            chart.TrendlineType = ValidEnumOrDefault(options.TrendlineType.Value, ChartTrendlineType.Linear);
        if (options.TrendlinePeriod is not null)
            chart.TrendlinePeriod = Math.Max(2, options.TrendlinePeriod.Value);
        if (options.TrendlineOrder is not null)
            chart.TrendlineOrder = Math.Clamp(options.TrendlineOrder.Value, 2, 6);
        if (options.ShowTrendlineEquation is not null)
            chart.ShowTrendlineEquation = options.ShowTrendlineEquation.Value;
        if (options.ShowTrendlineRSquared is not null)
            chart.ShowTrendlineRSquared = options.ShowTrendlineRSquared.Value;
        if (options.TrendlineColor is not null)
        {
            chart.TrendlineColor = options.TrendlineColor;
            chart.TrendlineThemeColor = null;
        }
        if (options.TrendlineThickness is not null)
            chart.TrendlineThickness = ClampFinite(options.TrendlineThickness.Value, 0.5, 10);
        if (options.TrendlineDashStyle is not null)
            chart.TrendlineDashStyle = ValidEnumOrDefault(options.TrendlineDashStyle.Value, ChartLineDashStyle.Dash);
        if (options.ShowErrorBars is not null)
            chart.ShowErrorBars = options.ShowErrorBars.Value;
        if (options.ErrorBarKind is not null)
            chart.ErrorBarKind = ValidEnumOrDefault(options.ErrorBarKind.Value, ChartErrorBarKind.StandardError);
        if (options.ErrorBarDirection is not null)
            chart.ErrorBarDirection = ValidEnumOrDefault(options.ErrorBarDirection.Value, ChartErrorBarDirection.Both);
        if (options.ErrorBarValue is not null)
            chart.ErrorBarValue = ClampFinite(options.ErrorBarValue.Value, 0, 1000);
        if (options.ErrorBarEndCaps is not null)
            chart.ErrorBarEndCaps = options.ErrorBarEndCaps.Value;
        if (options.ShowSecondaryAxis is not null)
            chart.ShowSecondaryAxis = options.ShowSecondaryAxis.Value;
        if (options.SecondaryAxisSeriesIndexes is not null)
        {
            var seriesCount = ChartTypeSupport.GetDataSeriesCount(chart);
            chart.SecondaryAxisSeriesIndexes = options.SecondaryAxisSeriesIndexes.Where(index => index > 0 && index < seriesCount).Distinct().Order().ToList();
        }
        if (options.ComboLineSeriesIndexes is not null)
        {
            var seriesCount = ChartTypeSupport.GetDataSeriesCount(chart);
            chart.ComboLineSeriesIndexes = options.ComboLineSeriesIndexes.Where(index => index > 0 && index < seriesCount).Distinct().Order().ToList();
        }
        if (options.SeriesFormats is not null)
        {
            var seriesCount = ChartTypeSupport.GetDataSeriesCount(chart);
            chart.SeriesFormats = options.SeriesFormats
                .Where(format => format.SeriesIndex >= 0 && format.SeriesIndex < seriesCount)
                .GroupBy(format => format.SeriesIndex)
                .Select(group => ClampSeriesFormat(chart.Type, group.Last()))
                .Where(HasSeriesFormatting)
                .OrderBy(format => format.SeriesIndex)
                .ToList();
        }
        if (options.PointDataLabelFormats is not null)
        {
            var seriesCount = ChartTypeSupport.GetDataSeriesCount(chart);
            var pointCount = ChartTypeSupport.GetDataPointCount(chart);
            chart.PointDataLabelFormats = options.PointDataLabelFormats
                .Where(format => format.SeriesIndex >= 0
                    && format.SeriesIndex < seriesCount
                    && format.PointIndex >= 0
                    && format.PointIndex < pointCount)
                .GroupBy(format => (format.SeriesIndex, format.PointIndex))
                .Select(group => ClampPointDataLabelFormat(group.Last()))
                .Where(HasPointDataLabelFormatting)
                .OrderBy(format => format.SeriesIndex)
                .ThenBy(format => format.PointIndex)
                .ToList();
        }
        if (options.UseComboLineForSecondarySeries is not null)
            chart.UseComboLineForSecondarySeries = options.UseComboLineForSecondarySeries.Value;
        EnforceAxisTitleSupport(chart);
        EnforceAxisBoundsSupport(chart);
        EnforcePieAndDoughnutSupport(chart);
        EnforcePercentageDataLabelSupport(chart);
        EnforceTrendlineSupport(chart);
        EnforceSecondaryAxisSupport(chart);
        EnforceComboLineOverlaySupport(chart);
    }

}

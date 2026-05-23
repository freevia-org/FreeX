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

    private static void EnforceAxisTitleSupport(ChartModel chart)
    {
        if (ChartTypeSupport.SupportsAxes(chart.Type))
            return;

        chart.XAxisTitle = null;
        chart.YAxisTitle = null;
        chart.AxisTitleTextColor = null;
        chart.AxisTitleFontSize = 12;
    }

    private static void EnforceAxisBoundsSupport(ChartModel chart)
    {
        if (!ChartTypeSupport.SupportsXAxisBounds(chart.Type))
            ClearXAxisBounds(chart);
        if (!ChartTypeSupport.SupportsYAxisBounds(chart.Type))
            ClearYAxisBounds(chart);
    }

    private static void ClearXAxisBounds(ChartModel chart)
    {
        chart.XAxisMinimum = null;
        chart.XAxisMaximum = null;
        chart.XAxisMajorUnit = null;
        chart.XAxisMinorUnit = null;
        chart.XAxisLogScale = false;
        chart.XAxisNumberFormat = ChartDataLabelNumberFormat.General;
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
        chart.YAxisMinimum = null;
        chart.YAxisMaximum = null;
        chart.YAxisMajorUnit = null;
        chart.YAxisMinorUnit = null;
        chart.YAxisLogScale = false;
        chart.YAxisNumberFormat = ChartDataLabelNumberFormat.General;
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

    private static void EnforcePieAndDoughnutSupport(ChartModel chart)
    {
        if (!ChartTypeSupport.SupportsDoughnutHoleSize(chart.Type))
            chart.DoughnutHoleSize = 0.55;
        if (!ChartTypeSupport.SupportsFirstSliceAngle(chart.Type))
            chart.FirstSliceAngle = 0;
        if (!ChartTypeSupport.SupportsExplodedSlices(chart.Type))
        {
            chart.ExplodedSliceIndex = -1;
            chart.ExplodedSliceDistance = 0.1;
        }
    }

    private static void EnforcePercentageDataLabelSupport(ChartModel chart)
    {
        if (ChartTypeSupport.SupportsPercentageDataLabels(chart.Type))
            return;

        chart.ShowDataLabelPercentage = false;
    }

    private static void EnforceTrendlineSupport(ChartModel chart)
    {
        if (ChartTypeSupport.SupportsTrendlines(chart.Type))
            return;

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

    private static void EnforceSecondaryAxisSupport(ChartModel chart)
    {
        if (ChartTypeSupport.SupportsSecondaryAxis(chart.Type)
            && (!chart.ShowSecondaryAxis || chart.SecondaryAxisSeriesIndexes.Count > 0))
            return;

        chart.ShowSecondaryAxis = false;
        chart.SecondaryAxisSeriesIndexes = [];
    }

    private static void EnforceComboLineOverlaySupport(ChartModel chart)
    {
        if (ChartTypeSupport.SupportsComboLineOverlay(chart)
            && (!chart.UseComboLineForSecondarySeries || chart.ComboLineSeriesIndexes.Count > 0))
            return;

        chart.UseComboLineForSecondarySeries = false;
        chart.ComboLineSeriesIndexes = [];
    }

    private static ChartPointDataLabelFormat ClampPointDataLabelFormat(ChartPointDataLabelFormat format) =>
        format with
        {
            BorderThickness = format.BorderThickness is { } borderThickness
                ? ClampFinite(borderThickness, 0, 10)
                : null,
            FontSize = format.FontSize is { } fontSize
                ? ClampFinite(fontSize, 6, 72)
                : null
        };

    private static bool HasPointDataLabelFormatting(ChartPointDataLabelFormat format) =>
        format.FillColor is not null
        || format.BorderColor is not null
        || format.BorderThickness is not null
        || format.TextColor is not null
        || format.FontSize is not null
        || format.FillThemeColor is not null
        || format.BorderThemeColor is not null
        || format.TextThemeColor is not null;

    private static ChartSeriesFormat ClampSeriesFormat(ChartType chartType, ChartSeriesFormat format)
    {
        var supportsMarkers = ChartTypeSupport.SupportsSeriesMarkers(chartType);
        return format with
        {
            StrokeThickness = format.StrokeThickness is { } strokeThickness
                ? ClampFinite(strokeThickness, 0.5, 10)
                : null,
            MarkerSize = supportsMarkers && format.MarkerSize is { } markerSize
                ? ClampFinite(markerSize, 1, 30)
                : null,
            DashStyle = ValidNullableEnumOrNull(format.DashStyle),
            MarkerStyle = supportsMarkers ? ValidNullableEnumOrNull(format.MarkerStyle) : null
        };
    }

    private static bool HasSeriesFormatting(ChartSeriesFormat format) =>
        format.FillColor is not null
        || format.StrokeColor is not null
        || format.StrokeThickness is not null
        || format.DashStyle is not null
        || format.MarkerStyle is not null
        || format.MarkerSize is not null
        || format.FillThemeColor is not null
        || format.StrokeThemeColor is not null;

    private static TEnum ValidEnumOrDefault<TEnum>(TEnum value, TEnum defaultValue)
        where TEnum : struct, Enum =>
        Enum.IsDefined(value) ? value : defaultValue;

    private static TEnum? ValidNullableEnumOrNull<TEnum>(TEnum? value)
        where TEnum : struct, Enum =>
        value is { } enumValue && Enum.IsDefined(enumValue) ? enumValue : null;

    private static double? FiniteOrNull(double value) =>
        double.IsFinite(value) ? value : null;

    private static double PositiveFiniteOrMin(double value, double min) =>
        double.IsFinite(value) && value > min ? value : min;

    private static double ClampFinite(double value, double min, double max) =>
        double.IsNaN(value) ? min : Math.Clamp(value, min, max);

    private static void RestoreLayout(ChartModel chart, ChartLayoutOptions snapshot)
    {
        chart.Title = snapshot.Title;
        chart.XAxisTitle = snapshot.XAxisTitle;
        chart.YAxisTitle = snapshot.YAxisTitle;
        chart.ChartTitleTextColor = snapshot.ChartTitleTextColor;
        chart.ChartTitleFontSize = snapshot.ChartTitleFontSize ?? 16;
        chart.AxisTitleTextColor = snapshot.AxisTitleTextColor;
        chart.AxisTitleFontSize = snapshot.AxisTitleFontSize ?? 12;
        chart.ChartAreaFillColor = snapshot.ChartAreaFillColor;
        chart.ChartAreaFillThemeColor = snapshot.ChartAreaFillThemeColor;
        chart.PlotAreaFillColor = snapshot.PlotAreaFillColor;
        chart.PlotAreaFillThemeColor = snapshot.PlotAreaFillThemeColor;
        chart.PlotAreaBorderColor = snapshot.PlotAreaBorderColor;
        chart.PlotAreaBorderThemeColor = snapshot.PlotAreaBorderThemeColor;
        chart.PlotAreaBorderThickness = snapshot.PlotAreaBorderThickness ?? 1;
        chart.LegendTextColor = snapshot.LegendTextColor;
        chart.LegendTextThemeColor = snapshot.LegendTextThemeColor;
        chart.LegendFillColor = snapshot.LegendFillColor;
        chart.LegendFillThemeColor = snapshot.LegendFillThemeColor;
        chart.LegendBorderColor = snapshot.LegendBorderColor;
        chart.LegendBorderThemeColor = snapshot.LegendBorderThemeColor;
        chart.LegendBorderThickness = snapshot.LegendBorderThickness ?? 0;
        chart.LegendFontSize = snapshot.LegendFontSize ?? 12;
        chart.DoughnutHoleSize = snapshot.DoughnutHoleSize ?? 0.55;
        chart.FirstSliceAngle = snapshot.FirstSliceAngle ?? 0;
        chart.ExplodedSliceIndex = snapshot.ExplodedSliceIndex ?? -1;
        chart.ExplodedSliceDistance = snapshot.ExplodedSliceDistance ?? 0.1;
        chart.XAxisMinimum = snapshot.XAxisMinimum;
        chart.XAxisMaximum = snapshot.XAxisMaximum;
        chart.XAxisMajorUnit = snapshot.XAxisMajorUnit;
        chart.XAxisMinorUnit = snapshot.XAxisMinorUnit;
        chart.XAxisLogScale = snapshot.XAxisLogScale ?? false;
        chart.XAxisNumberFormat = snapshot.XAxisNumberFormat ?? ChartDataLabelNumberFormat.General;
        chart.ShowXAxisMajorGridlines = snapshot.ShowXAxisMajorGridlines ?? false;
        chart.ShowXAxisMinorGridlines = snapshot.ShowXAxisMinorGridlines ?? false;
        chart.XAxisMajorGridlineColor = snapshot.XAxisMajorGridlineColor;
        chart.XAxisMinorGridlineColor = snapshot.XAxisMinorGridlineColor;
        chart.XAxisGridlineThickness = snapshot.XAxisGridlineThickness ?? 1;
        chart.XAxisMajorTickStyle = snapshot.XAxisMajorTickStyle ?? ChartAxisTickStyle.Outside;
        chart.XAxisMinorTickStyle = snapshot.XAxisMinorTickStyle ?? ChartAxisTickStyle.None;
        chart.ShowXAxisLabels = snapshot.ShowXAxisLabels ?? true;
        chart.XAxisLabelTextColor = snapshot.XAxisLabelTextColor;
        chart.XAxisLabelFontSize = snapshot.XAxisLabelFontSize ?? 11;
        chart.XAxisLabelAngle = snapshot.XAxisLabelAngle ?? 0;
        chart.XAxisLineColor = snapshot.XAxisLineColor;
        chart.XAxisLineThickness = snapshot.XAxisLineThickness ?? 1;
        chart.YAxisMinimum = snapshot.YAxisMinimum;
        chart.YAxisMaximum = snapshot.YAxisMaximum;
        chart.YAxisMajorUnit = snapshot.YAxisMajorUnit;
        chart.YAxisMinorUnit = snapshot.YAxisMinorUnit;
        chart.YAxisLogScale = snapshot.YAxisLogScale ?? false;
        chart.YAxisNumberFormat = snapshot.YAxisNumberFormat ?? ChartDataLabelNumberFormat.General;
        chart.ShowYAxisMajorGridlines = snapshot.ShowYAxisMajorGridlines ?? false;
        chart.ShowYAxisMinorGridlines = snapshot.ShowYAxisMinorGridlines ?? false;
        chart.YAxisMajorGridlineColor = snapshot.YAxisMajorGridlineColor;
        chart.YAxisMinorGridlineColor = snapshot.YAxisMinorGridlineColor;
        chart.YAxisGridlineThickness = snapshot.YAxisGridlineThickness ?? 1;
        chart.YAxisMajorTickStyle = snapshot.YAxisMajorTickStyle ?? ChartAxisTickStyle.Outside;
        chart.YAxisMinorTickStyle = snapshot.YAxisMinorTickStyle ?? ChartAxisTickStyle.None;
        chart.ShowYAxisLabels = snapshot.ShowYAxisLabels ?? true;
        chart.YAxisLabelTextColor = snapshot.YAxisLabelTextColor;
        chart.YAxisLabelFontSize = snapshot.YAxisLabelFontSize ?? 11;
        chart.YAxisLabelAngle = snapshot.YAxisLabelAngle ?? 0;
        chart.YAxisLineColor = snapshot.YAxisLineColor;
        chart.YAxisLineThickness = snapshot.YAxisLineThickness ?? 1;
        chart.LegendPosition = snapshot.LegendPosition ?? ChartLegendPosition.Right;
        chart.LegendOverlay = snapshot.LegendOverlay ?? false;
        chart.ShowLegend = snapshot.ShowLegend ?? true;
        chart.ShowDataLabels = snapshot.ShowDataLabels ?? false;
        chart.DataLabelPosition = snapshot.DataLabelPosition ?? ChartDataLabelPosition.BestFit;
        chart.ShowDataLabelCategoryName = snapshot.ShowDataLabelCategoryName ?? false;
        chart.ShowDataLabelSeriesName = snapshot.ShowDataLabelSeriesName ?? false;
        chart.ShowDataLabelPercentage = snapshot.ShowDataLabelPercentage ?? false;
        chart.DataLabelSeparator = snapshot.DataLabelSeparator ?? ChartDataLabelSeparator.Comma;
        chart.DataLabelNumberFormat = snapshot.DataLabelNumberFormat ?? ChartDataLabelNumberFormat.General;
        chart.ShowDataLabelCallouts = snapshot.ShowDataLabelCallouts ?? false;
        chart.DataLabelFillColor = snapshot.DataLabelFillColor;
        chart.DataLabelFillThemeColor = snapshot.DataLabelFillThemeColor;
        chart.DataLabelBorderColor = snapshot.DataLabelBorderColor;
        chart.DataLabelBorderThemeColor = snapshot.DataLabelBorderThemeColor;
        chart.DataLabelTextColor = snapshot.DataLabelTextColor;
        chart.DataLabelTextThemeColor = snapshot.DataLabelTextThemeColor;
        chart.DataLabelBorderThickness = snapshot.DataLabelBorderThickness ?? 0;
        chart.DataLabelFontSize = snapshot.DataLabelFontSize ?? 11;
        chart.DataLabelAngle = snapshot.DataLabelAngle ?? 0;
        chart.ShowLinearTrendline = snapshot.ShowLinearTrendline ?? false;
        chart.TrendlineType = snapshot.TrendlineType ?? ChartTrendlineType.Linear;
        chart.TrendlinePeriod = snapshot.TrendlinePeriod ?? 2;
        chart.TrendlineOrder = snapshot.TrendlineOrder ?? 2;
        chart.ShowTrendlineEquation = snapshot.ShowTrendlineEquation ?? false;
        chart.ShowTrendlineRSquared = snapshot.ShowTrendlineRSquared ?? false;
        chart.TrendlineColor = snapshot.TrendlineColor;
        chart.TrendlineThemeColor = snapshot.TrendlineThemeColor;
        chart.TrendlineThickness = snapshot.TrendlineThickness ?? 1.5;
        chart.TrendlineDashStyle = snapshot.TrendlineDashStyle ?? ChartLineDashStyle.Dash;
        chart.ShowErrorBars = snapshot.ShowErrorBars ?? false;
        chart.ErrorBarKind = snapshot.ErrorBarKind ?? ChartErrorBarKind.StandardError;
        chart.ErrorBarDirection = snapshot.ErrorBarDirection ?? ChartErrorBarDirection.Both;
        chart.ErrorBarValue = snapshot.ErrorBarValue ?? 5;
        chart.ErrorBarEndCaps = snapshot.ErrorBarEndCaps ?? true;
        chart.ShowSecondaryAxis = snapshot.ShowSecondaryAxis ?? false;
        chart.SecondaryAxisSeriesIndexes = snapshot.SecondaryAxisSeriesIndexes?.ToList() ?? [];
        chart.ComboLineSeriesIndexes = snapshot.ComboLineSeriesIndexes?.ToList() ?? [];
        chart.SeriesFormats = snapshot.SeriesFormats?.ToList() ?? [];
        chart.PointDataLabelFormats = snapshot.PointDataLabelFormats?.ToList() ?? [];
        chart.UseComboLineForSecondarySeries = snapshot.UseComboLineForSecondarySeries ?? false;
    }

    private static double NormalizeAngle(double value)
    {
        var normalized = value % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }
}

using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public sealed partial class SetChartLayoutCommand
{
    private static void EnforceAxisTitleSupport(ChartModel chart)
    {
        if (ChartTypeSupport.SupportsAxes(chart.Type))
            return;

        chart.XAxisTitle = null;
        chart.YAxisTitle = null;
        chart.AxisTitleTextColor = null;
        chart.AxisTitleTextThemeColor = null;
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
        chart.XAxisIsDateAxis = false;
        chart.XAxisMajorGridlineColor = null;
        chart.XAxisMinorGridlineColor = null;
        chart.XAxisGridlineThickness = 1;
        chart.XAxisMajorTickStyle = ChartAxisTickStyle.Outside;
        chart.XAxisMinorTickStyle = ChartAxisTickStyle.None;
        chart.ShowXAxisLabels = true;
        chart.XAxisLabelTextColor = null;
        chart.XAxisLabelTextThemeColor = null;
        chart.XAxisLabelFontSize = 11;
        chart.XAxisLabelAngle = 0;
        chart.XAxisLabelSkip = 0;
        chart.XAxisTickMarkSkip = 0;
        chart.XAxisLabelOffset = 0;
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
        chart.YAxisLabelTextThemeColor = null;
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
        chart.TrendlineLabelNumberFormatCode = null;
        chart.TrendlineLabelNumberFormatSourceLinked = null;
        chart.TrendlineLabelLayout = null;
        chart.TrendlineLabelFillColor = null;
        chart.TrendlineLabelFillThemeColor = null;
        chart.TrendlineLabelBorderColor = null;
        chart.TrendlineLabelBorderThemeColor = null;
        chart.TrendlineLabelBorderThickness = null;
        chart.TrendlineLabelTextColor = null;
        chart.TrendlineLabelTextThemeColor = null;
        chart.TrendlineLabelFontSize = null;
        chart.TrendlineLabelAngle = null;
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
                : null,
            Position = ValidNullableEnumOrNull(format.Position)
        };

    private static bool HasPointDataLabelFormatting(ChartPointDataLabelFormat format) =>
        format.FillColor is not null
        || format.BorderColor is not null
        || format.BorderThickness is not null
        || format.TextColor is not null
        || format.FontSize is not null
        || format.FillThemeColor is not null
        || format.BorderThemeColor is not null
        || format.TextThemeColor is not null
        || format.IsDeleted is not null
        || format.Position is not null
        || format.ShowValue is not null
        || format.ShowCategoryName is not null
        || format.ShowSeriesName is not null
        || format.ShowLegendKey is not null
        || format.ShowPercentage is not null
        || format.ShowBubbleSize is not null
        || !string.IsNullOrEmpty(format.NumberFormatCode)
        || format.NumberFormatSourceLinked is not null
        || format.SeparatorText is not null;

    private static ChartSeriesFormat ClampSeriesFormat(ChartType chartType, ChartSeriesFormat format)
    {
        var supportsMarkers = ChartTypeSupport.SupportsSeriesMarkers(chartType);
        var supportsSmooth = chartType is ChartType.Line or ChartType.ThreeDLine or ChartType.Scatter;
        var supportsInvertIfNegative = ChartTypeSupport.SupportsInvertIfNegative(chartType);
        return format with
        {
            StrokeThickness = format.StrokeThickness is { } strokeThickness
                ? ClampFinite(strokeThickness, 0.5, 10)
                : null,
            MarkerSize = supportsMarkers && format.MarkerSize is { } markerSize
                ? ClampFinite(markerSize, 1, 30)
                : null,
            DashStyle = ValidNullableEnumOrNull(format.DashStyle),
            MarkerStyle = supportsMarkers ? ValidNullableEnumOrNull(format.MarkerStyle) : null,
            Smooth = supportsSmooth ? format.Smooth : null,
            MarkerBorderColor = supportsMarkers ? format.MarkerBorderColor : null,
            MarkerBorderThemeColor = supportsMarkers ? format.MarkerBorderThemeColor : null,
            MarkerBorderThickness = supportsMarkers && format.MarkerBorderThickness is { } markerBorderThickness
                ? ClampFinite(markerBorderThickness, 0, 10)
                : null,
            InvertIfNegative = supportsInvertIfNegative ? format.InvertIfNegative : null
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
        || format.StrokeThemeColor is not null
        || format.Smooth is not null
        || format.MarkerBorderColor is not null
        || format.MarkerBorderThemeColor is not null
        || format.MarkerBorderThickness is not null
        || format.InvertIfNegative is not null;

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
        chart.ChartTitleTextThemeColor = snapshot.ChartTitleTextThemeColor;
        chart.ChartTitleFontSize = snapshot.ChartTitleFontSize ?? 16;
        chart.AxisTitleTextColor = snapshot.AxisTitleTextColor;
        chart.AxisTitleTextThemeColor = snapshot.AxisTitleTextThemeColor;
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
        chart.XAxisIsDateAxis = snapshot.XAxisIsDateAxis ?? false;
        chart.XAxisMajorGridlineColor = snapshot.XAxisMajorGridlineColor;
        chart.XAxisMinorGridlineColor = snapshot.XAxisMinorGridlineColor;
        chart.XAxisGridlineThickness = snapshot.XAxisGridlineThickness ?? 1;
        chart.XAxisMajorTickStyle = snapshot.XAxisMajorTickStyle ?? ChartAxisTickStyle.Outside;
        chart.XAxisMinorTickStyle = snapshot.XAxisMinorTickStyle ?? ChartAxisTickStyle.None;
        chart.ShowXAxisLabels = snapshot.ShowXAxisLabels ?? true;
        chart.XAxisLabelTextColor = snapshot.XAxisLabelTextColor;
        chart.XAxisLabelTextThemeColor = snapshot.XAxisLabelTextThemeColor;
        chart.XAxisLabelFontSize = snapshot.XAxisLabelFontSize ?? 11;
        chart.XAxisLabelAngle = snapshot.XAxisLabelAngle ?? 0;
        chart.XAxisLabelSkip = snapshot.XAxisLabelSkip ?? 0;
        chart.XAxisTickMarkSkip = snapshot.XAxisTickMarkSkip ?? 0;
        chart.XAxisLabelOffset = snapshot.XAxisLabelOffset ?? 0;
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
        chart.YAxisLabelTextThemeColor = snapshot.YAxisLabelTextThemeColor;
        chart.YAxisLabelFontSize = snapshot.YAxisLabelFontSize ?? 11;
        chart.YAxisLabelAngle = snapshot.YAxisLabelAngle ?? 0;
        chart.YAxisLineColor = snapshot.YAxisLineColor;
        chart.YAxisLineThickness = snapshot.YAxisLineThickness ?? 1;
        chart.LegendPosition = snapshot.LegendPosition ?? ChartLegendPosition.Right;
        chart.LegendOverlay = snapshot.LegendOverlay ?? false;
        chart.ShowLegend = snapshot.ShowLegend ?? true;
        chart.ShowDataLabels = snapshot.ShowDataLabels ?? false;
        chart.DataLabelPosition = snapshot.DataLabelPosition ?? ChartDataLabelPosition.BestFit;
        chart.ShowDataLabelValue = snapshot.ShowDataLabelValue ?? true;
        chart.ShowDataLabelLegendKey = snapshot.ShowDataLabelLegendKey ?? false;
        chart.ShowDataLabelBubbleSize = snapshot.ShowDataLabelBubbleSize ?? false;
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
        chart.ErrorBarColor = snapshot.ErrorBarColor;
        chart.ErrorBarThemeColor = snapshot.ErrorBarThemeColor;
        chart.ErrorBarThickness = snapshot.ErrorBarThickness ?? 1;
        chart.ErrorBarDashStyle = snapshot.ErrorBarDashStyle ?? ChartLineDashStyle.Solid;
        chart.DropLineColor = snapshot.DropLineColor;
        chart.DropLineThemeColor = snapshot.DropLineThemeColor;
        chart.DropLineThickness = snapshot.DropLineThickness ?? 1;
        chart.DropLineDashStyle = snapshot.DropLineDashStyle ?? ChartLineDashStyle.Solid;
        chart.HighLowLineColor = snapshot.HighLowLineColor;
        chart.HighLowLineThemeColor = snapshot.HighLowLineThemeColor;
        chart.HighLowLineThickness = snapshot.HighLowLineThickness ?? 1;
        chart.HighLowLineDashStyle = snapshot.HighLowLineDashStyle ?? ChartLineDashStyle.Solid;
        chart.UpDownBarGapWidth = snapshot.UpDownBarGapWidth;
        chart.UpBarFillColor = snapshot.UpBarFillColor;
        chart.UpBarFillThemeColor = snapshot.UpBarFillThemeColor;
        chart.UpBarBorderColor = snapshot.UpBarBorderColor;
        chart.UpBarBorderThemeColor = snapshot.UpBarBorderThemeColor;
        chart.UpBarBorderThickness = snapshot.UpBarBorderThickness;
        chart.DownBarFillColor = snapshot.DownBarFillColor;
        chart.DownBarFillThemeColor = snapshot.DownBarFillThemeColor;
        chart.DownBarBorderColor = snapshot.DownBarBorderColor;
        chart.DownBarBorderThemeColor = snapshot.DownBarBorderThemeColor;
        chart.DownBarBorderThickness = snapshot.DownBarBorderThickness;
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

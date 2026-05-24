using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public sealed partial class NativeJsonAdapter
{
    private static void SanitizeLoadedChart(ChartModel chart)
    {
        chart.Type = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.Type, ChartType.Column);
        chart.ChartTitleFontSize = Math.Clamp(chart.ChartTitleFontSize, 6, 72);
        chart.AxisTitleFontSize = Math.Clamp(chart.AxisTitleFontSize, 6, 72);
        if (!ChartTypeSupport.SupportsAxes(chart.Type))
        {
            chart.XAxisTitle = null;
            chart.YAxisTitle = null;
            chart.AxisTitleTextColor = null;
            chart.AxisTitleTextThemeColor = null;
            chart.AxisTitleFontSize = 12;
        }
        chart.PlotAreaBorderThickness = Math.Clamp(chart.PlotAreaBorderThickness, 0, 10);
        chart.LegendBorderThickness = Math.Clamp(chart.LegendBorderThickness, 0, 10);
        chart.ChartAreaBorderThickness = ClampNullableDouble(chart.ChartAreaBorderThickness, 0, 10);
        chart.LegendFontSize = Math.Clamp(chart.LegendFontSize, 6, 72);
        chart.DoughnutHoleSize = Math.Clamp(chart.DoughnutHoleSize, 0.1, 0.9);
        chart.FirstSliceAngle = NormalizeChartAngle(chart.FirstSliceAngle);
        chart.ExplodedSliceDistance = Math.Clamp(chart.ExplodedSliceDistance, 0, 0.5);
        if (!ChartTypeSupport.SupportsDoughnutHoleSize(chart.Type))
            chart.DoughnutHoleSize = 0.55;
        if (!ChartTypeSupport.SupportsFirstSliceAngle(chart.Type))
            chart.FirstSliceAngle = 0;
        if (!ChartTypeSupport.SupportsExplodedSlices(chart.Type))
        {
            chart.ExplodedSliceIndex = -1;
            chart.ExplodedSliceDistance = 0.1;
        }
        chart.XAxisMajorUnit = ClampPositiveAxisUnit(chart.XAxisMajorUnit);
        chart.XAxisMinorUnit = ClampPositiveAxisUnit(chart.XAxisMinorUnit);
        chart.XAxisNumberFormat = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.XAxisNumberFormat, ChartDataLabelNumberFormat.General);
        chart.XAxisNumberFormatCode = string.IsNullOrWhiteSpace(chart.XAxisNumberFormatCode) ? null : chart.XAxisNumberFormatCode;
        chart.XAxisGridlineThickness = Math.Clamp(chart.XAxisGridlineThickness, 0.25, 10);
        chart.XAxisMajorTickStyle = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.XAxisMajorTickStyle, ChartAxisTickStyle.Outside);
        chart.XAxisMinorTickStyle = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.XAxisMinorTickStyle, ChartAxisTickStyle.None);
        chart.XAxisLabelFontSize = Math.Clamp(chart.XAxisLabelFontSize, 6, 72);
        chart.XAxisLabelAngle = Math.Clamp(chart.XAxisLabelAngle, -90, 90);
        chart.XAxisLabelSkip = Math.Max(0, chart.XAxisLabelSkip);
        chart.XAxisTickMarkSkip = Math.Max(0, chart.XAxisTickMarkSkip);
        chart.XAxisLabelOffset = Math.Max(0, chart.XAxisLabelOffset);
        chart.XAxisLineThickness = Math.Clamp(chart.XAxisLineThickness, 0.5, 10);
        chart.YAxisMajorUnit = ClampPositiveAxisUnit(chart.YAxisMajorUnit);
        chart.YAxisMinorUnit = ClampPositiveAxisUnit(chart.YAxisMinorUnit);
        chart.YAxisNumberFormat = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.YAxisNumberFormat, ChartDataLabelNumberFormat.General);
        chart.YAxisNumberFormatCode = string.IsNullOrWhiteSpace(chart.YAxisNumberFormatCode) ? null : chart.YAxisNumberFormatCode;
        chart.YAxisGridlineThickness = Math.Clamp(chart.YAxisGridlineThickness, 0.25, 10);
        chart.YAxisMajorTickStyle = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.YAxisMajorTickStyle, ChartAxisTickStyle.Outside);
        chart.YAxisMinorTickStyle = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.YAxisMinorTickStyle, ChartAxisTickStyle.None);
        chart.YAxisLabelFontSize = Math.Clamp(chart.YAxisLabelFontSize, 6, 72);
        chart.YAxisLabelAngle = Math.Clamp(chart.YAxisLabelAngle, -90, 90);
        chart.YAxisLineThickness = Math.Clamp(chart.YAxisLineThickness, 0.5, 10);
        if (!ChartTypeSupport.SupportsXAxisBounds(chart.Type))
            ClearXAxisBounds(chart);
        if (!ChartTypeSupport.SupportsYAxisBounds(chart.Type))
            ClearYAxisBounds(chart);
        chart.LegendPosition = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.LegendPosition, ChartLegendPosition.Right);
        chart.DataLabelPosition = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.DataLabelPosition, ChartDataLabelPosition.BestFit);
        chart.DataLabelSeparator = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.DataLabelSeparator, ChartDataLabelSeparator.Comma);
        chart.DataLabelNumberFormat = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.DataLabelNumberFormat, ChartDataLabelNumberFormat.General);
        chart.DataLabelNumberFormatCode = string.IsNullOrWhiteSpace(chart.DataLabelNumberFormatCode) ? null : chart.DataLabelNumberFormatCode;
        chart.DrawingAnchorKind = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.DrawingAnchorKind, ChartDrawingAnchorKind.Absolute);
        chart.StockSubtype = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.StockSubtype, StockChartSubtype.HighLowClose);
        if (chart.Type != ChartType.Stock)
            chart.StockSubtype = StockChartSubtype.HighLowClose;
        chart.PivotSourceFormatId = ClampNullableInt(chart.PivotSourceFormatId, 0, int.MaxValue);
        chart.BubbleScale = Math.Clamp(chart.BubbleScale, 0, 300);
        chart.BubbleSizeRepresents = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.BubbleSizeRepresents, ChartBubbleSizeRepresents.Area);
        if (chart.Type != ChartType.Bubble)
        {
            chart.BubbleScale = 100;
            chart.ShowNegativeBubbles = false;
            chart.BubbleSizeRepresents = ChartBubbleSizeRepresents.Area;
        }
        if (chart.ThreeDView is { } threeDView)
        {
            threeDView.RotationX = ClampNullableInt(threeDView.RotationX, -90, 90);
            threeDView.HeightPercent = ClampNullableInt(threeDView.HeightPercent, 5, 500);
            threeDView.RotationY = ClampNullableInt(threeDView.RotationY, 0, 360);
            threeDView.DepthPercent = ClampNullableInt(threeDView.DepthPercent, 20, 2000);
            threeDView.Perspective = ClampNullableInt(threeDView.Perspective, 0, 240);
            if (threeDView.RotationX is null
                && threeDView.HeightPercent is null
                && threeDView.RotationY is null
                && threeDView.DepthPercent is null
                && threeDView.RightAngleAxes is null
                && threeDView.Perspective is null)
            {
                chart.ThreeDView = null;
            }
        }
        chart.DataLabelBorderThickness = Math.Clamp(chart.DataLabelBorderThickness, 0, 10);
        chart.DataLabelFontSize = Math.Clamp(chart.DataLabelFontSize, 6, 72);
        chart.DataLabelAngle = Math.Clamp(chart.DataLabelAngle, -90, 90);
        if (!ChartTypeSupport.SupportsPercentageDataLabels(chart.Type))
            chart.ShowDataLabelPercentage = false;
        chart.TrendlineType = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.TrendlineType, ChartTrendlineType.Linear);
        chart.TrendlinePeriod = Math.Max(2, chart.TrendlinePeriod);
        chart.TrendlineOrder = Math.Clamp(chart.TrendlineOrder, 2, 6);
        chart.TrendlineName = string.IsNullOrWhiteSpace(chart.TrendlineName) ? null : chart.TrendlineName;
        chart.TrendlineForward = ClampNullableDouble(chart.TrendlineForward, 0, 1000);
        chart.TrendlineBackward = ClampNullableDouble(chart.TrendlineBackward, 0, 1000);
        chart.TrendlineIntercept = chart.TrendlineIntercept is { } intercept && double.IsFinite(intercept) ? intercept : null;
        chart.TrendlineThickness = Math.Clamp(chart.TrendlineThickness, 0.5, 10);
        chart.TrendlineDashStyle = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.TrendlineDashStyle, ChartLineDashStyle.Dash);
        chart.ErrorBarKind = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.ErrorBarKind, ChartErrorBarKind.StandardError);
        chart.ErrorBarAxisDirection = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.ErrorBarAxisDirection, ChartErrorBarAxisDirection.Y);
        chart.ErrorBarDirection = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.ErrorBarDirection, ChartErrorBarDirection.Both);
        chart.ErrorBarValue = Math.Clamp(chart.ErrorBarValue, 0, 1000);
        chart.ErrorBarPlusRangeFormula = string.IsNullOrWhiteSpace(chart.ErrorBarPlusRangeFormula) ? null : chart.ErrorBarPlusRangeFormula;
        chart.ErrorBarMinusRangeFormula = string.IsNullOrWhiteSpace(chart.ErrorBarMinusRangeFormula) ? null : chart.ErrorBarMinusRangeFormula;
        chart.ErrorBarThickness = Math.Clamp(chart.ErrorBarThickness, 0.5, 10);
        chart.ErrorBarDashStyle = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.ErrorBarDashStyle, ChartLineDashStyle.Solid);
        chart.DropLineThickness = Math.Clamp(chart.DropLineThickness, 0.5, 10);
        chart.DropLineDashStyle = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.DropLineDashStyle, ChartLineDashStyle.Solid);
        chart.HighLowLineThickness = Math.Clamp(chart.HighLowLineThickness, 0.5, 10);
        chart.HighLowLineDashStyle = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.HighLowLineDashStyle, ChartLineDashStyle.Solid);
        chart.SeriesLineThickness = Math.Clamp(chart.SeriesLineThickness, 0.5, 10);
        chart.SeriesLineDashStyle = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.SeriesLineDashStyle, ChartLineDashStyle.Solid);
        if (!ChartTypeSupport.SupportsSeriesLines(chart.Type))
        {
            chart.ShowSeriesLines = false;
            chart.SeriesLineColor = null;
            chart.SeriesLineThemeColor = null;
            chart.SeriesLineThickness = 1;
            chart.SeriesLineDashStyle = ChartLineDashStyle.Solid;
        }
        chart.UpDownBarGapWidth = ClampNullableInt(chart.UpDownBarGapWidth, 0, 500);
        chart.UpBarBorderThickness = ClampNullableDouble(chart.UpBarBorderThickness, 0, 10);
        chart.DownBarBorderThickness = ClampNullableDouble(chart.DownBarBorderThickness, 0, 10);
        SanitizeChartSurfaceFormat(chart.FloorFormat);
        SanitizeChartSurfaceFormat(chart.SideWallFormat);
        SanitizeChartSurfaceFormat(chart.BackWallFormat);
        if (!ChartTypeSupport.SupportsTrendlines(chart.Type))
        {
            chart.ShowLinearTrendline = false;
            chart.TrendlineName = null;
            chart.TrendlineType = ChartTrendlineType.Linear;
            chart.TrendlinePeriod = 2;
            chart.TrendlineOrder = 2;
            chart.TrendlineForward = null;
            chart.TrendlineBackward = null;
            chart.TrendlineIntercept = null;
            chart.ShowTrendlineEquation = false;
            chart.ShowTrendlineRSquared = false;
            chart.TrendlineColor = null;
            chart.TrendlineThemeColor = null;
            chart.TrendlineThickness = 1.5;
            chart.TrendlineDashStyle = ChartLineDashStyle.Dash;
        }

        var dataPointCount = ChartTypeSupport.GetDataPointCount(chart);
        if (chart.ExplodedSliceIndex < 0 || chart.ExplodedSliceIndex >= dataPointCount)
            chart.ExplodedSliceIndex = -1;

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
        chart.SeriesFormats = chart.SeriesFormats
            .Where(format => format.SeriesIndex >= 0 && format.SeriesIndex < seriesCount)
            .GroupBy(format => format.SeriesIndex)
            .Select(group => ClampSeriesFormat(chart.Type, group.Last()))
            .Where(HasSeriesFormatting)
            .OrderBy(format => format.SeriesIndex)
            .ToList();
        chart.PointDataLabelFormats = chart.PointDataLabelFormats
            .Where(format => format.SeriesIndex >= 0
                && format.SeriesIndex < seriesCount
                && format.PointIndex >= 0
                && format.PointIndex < dataPointCount)
            .GroupBy(format => (format.SeriesIndex, format.PointIndex))
            .Select(group => ClampPointDataLabelFormat(group.Last()))
            .Where(HasPointDataLabelFormatting)
            .OrderBy(format => format.SeriesIndex)
            .ThenBy(format => format.PointIndex)
            .ToList();
    }

    private static double? ClampPositiveAxisUnit(double? value) =>
        value is null ? null : Math.Max(double.Epsilon, value.Value);

    private static int? ClampNullableInt(int? value, int min, int max) =>
        value is { } intValue ? Math.Clamp(intValue, min, max) : null;

    private static double? ClampNullableDouble(double? value, double min, double max) =>
        value is { } doubleValue && double.IsFinite(doubleValue)
            ? Math.Clamp(doubleValue, min, max)
            : null;

    private static void SanitizeChartSurfaceFormat(ChartSurfaceFormatModel? format)
    {
        if (format is null)
            return;

        format.BorderThickness = ClampNullableDouble(format.BorderThickness, 0, 10);
    }

    private static double NormalizeChartAngle(double value)
    {
        var normalized = value % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }

    private static void ClearXAxisBounds(ChartModel chart)
    {
        chart.XAxisMinimum = null;
        chart.XAxisMaximum = null;
        chart.XAxisMajorUnit = null;
        chart.XAxisMinorUnit = null;
        chart.XAxisLogScale = false;
        chart.XAxisNumberFormat = ChartDataLabelNumberFormat.General;
        chart.XAxisNumberFormatCode = null;
        chart.XAxisNumberFormatSourceLinked = null;
        chart.ShowXAxisMajorGridlines = false;
        chart.ShowXAxisMinorGridlines = false;
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
        chart.YAxisNumberFormatCode = null;
        chart.YAxisNumberFormatSourceLinked = null;
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

    private static ChartSeriesFormat ClampSeriesFormat(ChartType chartType, ChartSeriesFormat format)
    {
        var supportsMarkers = ChartTypeSupport.SupportsSeriesMarkers(chartType);
        var supportsSmooth = chartType is ChartType.Line or ChartType.ThreeDLine or ChartType.Scatter;
        var supportsInvertIfNegative = ChartTypeSupport.SupportsInvertIfNegative(chartType);
        return format with
        {
            StrokeThickness = format.StrokeThickness is { } strokeThickness
                ? Math.Clamp(strokeThickness, 0.5, 10)
                : null,
            MarkerSize = supportsMarkers && format.MarkerSize is { } markerSize
                ? Math.Clamp(markerSize, 1, 30)
                : null,
            DashStyle = NativeJsonValueSanitizer.ValidNullableEnumOrNull(format.DashStyle),
            MarkerStyle = supportsMarkers ? NativeJsonValueSanitizer.ValidNullableEnumOrNull(format.MarkerStyle) : null,
            Smooth = supportsSmooth ? format.Smooth : null,
            MarkerBorderColor = supportsMarkers ? format.MarkerBorderColor : null,
            MarkerBorderThemeColor = supportsMarkers ? format.MarkerBorderThemeColor : null,
            MarkerBorderThickness = supportsMarkers && format.MarkerBorderThickness is { } markerBorderThickness
                ? Math.Clamp(markerBorderThickness, 0, 10)
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

    private static ChartPointDataLabelFormat ClampPointDataLabelFormat(ChartPointDataLabelFormat format) =>
        format with
        {
            BorderThickness = format.BorderThickness is { } borderThickness
                ? Math.Clamp(borderThickness, 0, 10)
                : null,
            FontSize = format.FontSize is { } fontSize
                ? Math.Clamp(fontSize, 6, 72)
                : null,
            Position = NativeJsonValueSanitizer.ValidNullableEnumOrNull(format.Position)
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
}

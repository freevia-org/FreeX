using OxyPlot;
using OxyPlot.Axes;

using Freexcel.Core.Model;

namespace Freexcel.App.UI;

public static partial class ChartRenderer
{
    private static void ApplyAxisBounds(PlotModel model, ChartModel chart)
    {
        for (var index = 0; index < model.Axes.Count; index++)
        {
            var axis = model.Axes[index];
            if (axis is not LinearAxis linearAxis)
                continue;

            if (ShouldUseLogAxis(chart, linearAxis))
            {
                var logAxis = new LogarithmicAxis
                {
                    Position = linearAxis.Position,
                    Title = linearAxis.Title,
                    Key = linearAxis.Key,
                    Minimum = GetPositiveAxisValue(linearAxis.Minimum),
                    Maximum = GetPositiveAxisValue(linearAxis.Maximum),
                    MajorStep = GetPositiveAxisValue(linearAxis.MajorStep),
                    MinorStep = GetPositiveAxisValue(linearAxis.MinorStep),
                    LabelFormatter = linearAxis.LabelFormatter
                };
                model.Axes[index] = logAxis;
                axis = logAxis;
            }

            if (axis.Position is AxisPosition.Bottom or AxisPosition.Top)
            {
                ApplyAxisTitleStyle(axis, chart);
                if (ChartTypeSupport.SupportsXAxisBounds(chart.Type))
                {
                    if (chart.XAxisMinimum is { } minimum)
                        axis.Minimum = ShouldUseLogAxis(chart, axis) ? Math.Max(double.Epsilon, minimum) : minimum;
                    if (chart.XAxisMaximum is { } maximum)
                        axis.Maximum = ShouldUseLogAxis(chart, axis) ? Math.Max(double.Epsilon, maximum) : maximum;
                    if (chart.XAxisMajorUnit is { } majorUnit)
                        axis.MajorStep = majorUnit;
                    if (chart.XAxisMinorUnit is { } minorUnit)
                        axis.MinorStep = minorUnit;
                }
                if (ChartTypeSupport.SupportsXAxisBounds(chart.Type) &&
                    chart.XAxisNumberFormat != ChartDataLabelNumberFormat.General &&
                    axis.LabelFormatter is null)
                    axis.LabelFormatter = value => FormatAxisValue(chart.XAxisNumberFormat, value);
                ApplyGridlineStyle(
                    axis,
                    chart.ShowXAxisMajorGridlines,
                    chart.ShowXAxisMinorGridlines,
                    chart.XAxisMajorGridlineColor,
                    chart.XAxisMinorGridlineColor,
                    chart.XAxisGridlineThickness);
                ApplyTickAndLabelStyle(axis, chart.XAxisMajorTickStyle, chart.XAxisMinorTickStyle, chart.ShowXAxisLabels);
                ApplyAxisLabelStyle(axis, chart.XAxisLabelTextColor, chart.XAxisLabelFontSize, chart.XAxisLabelAngle);
                ApplyAxisLineStyle(axis, chart.XAxisLineColor, chart.XAxisLineThickness);
            }
            else if (axis.Position is AxisPosition.Left or AxisPosition.Right)
            {
                ApplyAxisTitleStyle(axis, chart);
                if (ChartTypeSupport.SupportsYAxisBounds(chart.Type))
                {
                    if (chart.YAxisMinimum is { } minimum)
                        axis.Minimum = ShouldUseLogAxis(chart, axis) ? Math.Max(double.Epsilon, minimum) : minimum;
                    if (chart.YAxisMaximum is { } maximum)
                        axis.Maximum = ShouldUseLogAxis(chart, axis) ? Math.Max(double.Epsilon, maximum) : maximum;
                    if (chart.YAxisMajorUnit is { } majorUnit)
                        axis.MajorStep = majorUnit;
                    if (chart.YAxisMinorUnit is { } minorUnit)
                        axis.MinorStep = minorUnit;
                }
                if (ChartTypeSupport.SupportsYAxisBounds(chart.Type) &&
                    chart.YAxisNumberFormat != ChartDataLabelNumberFormat.General &&
                    axis.LabelFormatter is null)
                    axis.LabelFormatter = value => FormatAxisValue(chart.YAxisNumberFormat, value);
                ApplyGridlineStyle(
                    axis,
                    chart.ShowYAxisMajorGridlines,
                    chart.ShowYAxisMinorGridlines,
                    chart.YAxisMajorGridlineColor,
                    chart.YAxisMinorGridlineColor,
                    chart.YAxisGridlineThickness);
                ApplyTickAndLabelStyle(axis, chart.YAxisMajorTickStyle, chart.YAxisMinorTickStyle, chart.ShowYAxisLabels);
                ApplyAxisLabelStyle(axis, chart.YAxisLabelTextColor, chart.YAxisLabelFontSize, chart.YAxisLabelAngle);
                ApplyAxisLineStyle(axis, chart.YAxisLineColor, chart.YAxisLineThickness);
            }
        }
    }

    private static void ApplyAreaStyle(PlotModel model, ChartModel chart, WorkbookTheme theme)
    {
        if (chart.ResolveChartAreaFillColor(theme) is { } chartFill)
            model.Background = OxyColor.FromRgb(chartFill.R, chartFill.G, chartFill.B);
        if (chart.ResolvePlotAreaFillColor(theme) is { } plotFill)
            model.PlotAreaBackground = OxyColor.FromRgb(plotFill.R, plotFill.G, plotFill.B);
        if (chart.ResolvePlotAreaBorderColor(theme) is { } plotBorder)
            model.PlotAreaBorderColor = OxyColor.FromRgb(plotBorder.R, plotBorder.G, plotBorder.B);
        model.PlotAreaBorderThickness = new OxyThickness(chart.PlotAreaBorderThickness);
    }

    private static void ApplyTitleStyle(PlotModel model, ChartModel chart)
    {
        model.TitleFontSize = chart.ChartTitleFontSize;
        if (chart.ChartTitleTextColor is { } titleColor)
            model.TitleColor = OxyColor.FromRgb(titleColor.R, titleColor.G, titleColor.B);
    }

    private static void ApplyAxisTitleStyle(Axis axis, ChartModel chart)
    {
        axis.TitleFontSize = chart.AxisTitleFontSize;
        if (chart.AxisTitleTextColor is { } titleColor)
            axis.TitleColor = OxyColor.FromRgb(titleColor.R, titleColor.G, titleColor.B);
    }

    private static void ApplyGridlineStyle(
        Axis axis,
        bool showMajor,
        bool showMinor,
        CellColor? majorColor,
        CellColor? minorColor,
        double thickness)
    {
        axis.MajorGridlineStyle = showMajor ? LineStyle.Solid : LineStyle.None;
        axis.MajorGridlineColor = ToOxyColor(majorColor) ?? OxyColor.FromRgb(220, 220, 220);
        axis.MajorGridlineThickness = thickness;
        axis.MinorGridlineStyle = showMinor ? LineStyle.Dot : LineStyle.None;
        axis.MinorGridlineColor = ToOxyColor(minorColor) ?? OxyColor.FromRgb(235, 235, 235);
        axis.MinorGridlineThickness = Math.Max(0.25, thickness * 0.75);
    }

    private static void ApplyTickAndLabelStyle(
        Axis axis,
        ChartAxisTickStyle majorTickStyle,
        ChartAxisTickStyle minorTickStyle,
        bool showLabels)
    {
        axis.TickStyle = ToOxyTickStyle(majorTickStyle);
        axis.MajorTickSize = GetTickSize(majorTickStyle);
        axis.MinorTickSize = GetTickSize(minorTickStyle);
        if (!showLabels)
            axis.TextColor = OxyColors.Transparent;
    }

    private static void ApplyAxisLabelStyle(Axis axis, CellColor? textColor, double fontSize, double angle)
    {
        axis.FontSize = fontSize;
        axis.Angle = angle;
        if (textColor is { } color && axis.TextColor != OxyColors.Transparent)
            axis.TextColor = OxyColor.FromRgb(color.R, color.G, color.B);
    }

    private static void ApplyAxisLineStyle(Axis axis, CellColor? color, double thickness)
    {
        axis.AxislineStyle = LineStyle.Solid;
        axis.AxislineThickness = thickness;
        if (color is { } lineColor)
            axis.AxislineColor = OxyColor.FromRgb(lineColor.R, lineColor.G, lineColor.B);
    }

    private static double GetTickSize(ChartAxisTickStyle tickStyle) =>
        tickStyle switch
        {
            ChartAxisTickStyle.None => 0,
            ChartAxisTickStyle.Inside => 4,
            ChartAxisTickStyle.Cross => 8,
            _ => 6
        };

    private static TickStyle ToOxyTickStyle(ChartAxisTickStyle tickStyle) =>
        tickStyle switch
        {
            ChartAxisTickStyle.None => TickStyle.None,
            ChartAxisTickStyle.Inside => TickStyle.Inside,
            ChartAxisTickStyle.Cross => TickStyle.Crossing,
            _ => TickStyle.Outside
        };

    private static string FormatAxisValue(ChartDataLabelNumberFormat format, double value) =>
        format switch
        {
            ChartDataLabelNumberFormat.Number => value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            ChartDataLabelNumberFormat.Currency => value.ToString("$#,##0.00", System.Globalization.CultureInfo.InvariantCulture),
            ChartDataLabelNumberFormat.Percent => value.ToString("0%", System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
        };

    private static bool ShouldUseLogAxis(ChartModel chart, Axis axis) =>
        axis.Position is AxisPosition.Bottom or AxisPosition.Top
            ? chart.XAxisLogScale && ChartTypeSupport.SupportsXAxisLogScale(chart.Type)
            : chart.YAxisLogScale && ChartTypeSupport.SupportsYAxisLogScale(chart.Type);

    private static double GetPositiveAxisValue(double value) =>
        double.IsNaN(value) || value <= 0 ? double.NaN : value;
}

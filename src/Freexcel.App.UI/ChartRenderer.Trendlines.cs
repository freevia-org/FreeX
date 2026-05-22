using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Series;

using Freexcel.Core.Model;

namespace Freexcel.App.UI;

public static partial class ChartRenderer
{
    private static void AddTrendlineIfRequested(
        PlotModel model,
        ChartModel chart,
        WorkbookTheme theme,
        IReadOnlyList<DataPoint>? points,
        bool swapTrendlineAxes = false)
    {
        if (!chart.ShowLinearTrendline || !ChartTypeSupport.SupportsTrendlines(chart.Type) || points is null || points.Count < 2)
            return;

        var trendPoints = ChartTrendlineCalculator.Calculate(
            chart.TrendlineType,
            points,
            chart.TrendlinePeriod,
            chart.TrendlineOrder);
        if (trendPoints.Count < 2)
            return;

        var trendline = new LineSeries
        {
            Title = GetTrendlineTitle(chart.TrendlineType),
            LineStyle = ToOxyLineStyle(chart.TrendlineDashStyle),
            StrokeThickness = chart.TrendlineThickness,
            Color = chart.ResolveTrendlineColor(theme) is { } color
                ? OxyColor.FromRgb(color.R, color.G, color.B)
                : OxyColors.Gray
        };
        var displaySourcePoints = swapTrendlineAxes
            ? points.Select(point => new DataPoint(point.Y, point.X)).ToArray()
            : points;
        foreach (var point in trendPoints)
            trendline.Points.Add(swapTrendlineAxes ? new DataPoint(point.Y, point.X) : point);
        model.Series.Add(trendline);
        AddTrendlineInfoIfRequested(model, chart, points, trendPoints, displaySourcePoints);
    }

    private static LineStyle ToOxyLineStyle(ChartLineDashStyle dashStyle) =>
        dashStyle switch
        {
            ChartLineDashStyle.Solid => LineStyle.Solid,
            ChartLineDashStyle.Dot => LineStyle.Dot,
            _ => LineStyle.Dash
        };

    private static MarkerType ToOxyMarkerType(ChartMarkerStyle markerStyle) =>
        markerStyle switch
        {
            ChartMarkerStyle.None => MarkerType.None,
            ChartMarkerStyle.Square => MarkerType.Square,
            ChartMarkerStyle.Diamond => MarkerType.Diamond,
            ChartMarkerStyle.Triangle => MarkerType.Triangle,
            _ => MarkerType.Circle
        };

    private static void AddTrendlineInfoIfRequested(
        PlotModel model,
        ChartModel chart,
        IReadOnlyList<DataPoint> sourcePoints,
        IReadOnlyList<DataPoint> trendPoints,
        IReadOnlyList<DataPoint> displaySourcePoints)
    {
        if (!chart.ShowTrendlineEquation && !chart.ShowTrendlineRSquared)
            return;

        var lines = new List<string>();
        if (chart.ShowTrendlineEquation)
            lines.Add(GetTrendlineEquationText(chart, trendPoints));
        if (chart.ShowTrendlineRSquared && ChartTrendlineCalculator.TryCalculateRSquared(sourcePoints, trendPoints, out var rSquared))
            lines.Add($"R² = {rSquared:0.0000}");
        if (lines.Count == 0)
            return;

        model.Annotations.Add(new TextAnnotation
        {
            Text = string.Join(Environment.NewLine, lines),
            TextPosition = new DataPoint(
                displaySourcePoints.Min(point => point.X),
                displaySourcePoints.Max(point => point.Y)),
            TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Left,
            TextVerticalAlignment = OxyPlot.VerticalAlignment.Top,
            Background = OxyColor.FromAColor(220, OxyColors.White),
            Stroke = OxyColors.LightGray,
            StrokeThickness = 1,
            Padding = new OxyThickness(4)
        });
    }

    private static string GetTrendlineEquationText(ChartModel chart, IReadOnlyList<DataPoint> trendPoints)
    {
        if (chart.TrendlineType == ChartTrendlineType.MovingAverage)
            return $"Moving average ({Math.Max(2, chart.TrendlinePeriod)})";
        if (chart.TrendlineType == ChartTrendlineType.Polynomial)
            return $"Polynomial (order {Math.Clamp(chart.TrendlineOrder, 2, 6)})";
        if (trendPoints.Count < 2)
            return GetTrendlineTitle(chart.TrendlineType);

        var first = trendPoints[0];
        var last = trendPoints[^1];
        var dx = last.X - first.X;
        if (Math.Abs(dx) < double.Epsilon)
            return GetTrendlineTitle(chart.TrendlineType);

        return chart.TrendlineType switch
        {
            ChartTrendlineType.Exponential when first.Y > 0 && last.Y > 0 =>
                FormatExponentialEquation(first, last, dx),
            ChartTrendlineType.Logarithmic when first.X > 0 && last.X > 0 =>
                FormatLogarithmicEquation(first, last),
            ChartTrendlineType.Power when first.X > 0 && last.X > 0 && first.Y > 0 && last.Y > 0 =>
                FormatPowerEquation(first, last),
            _ => FormatLinearEquation(first, last, dx)
        };
    }

    private static string FormatLinearEquation(DataPoint first, DataPoint last, double dx)
    {
        var slope = (last.Y - first.Y) / dx;
        var intercept = first.Y - (slope * first.X);
        return $"y = {slope:0.###}x {FormatSigned(intercept)}";
    }

    private static string FormatExponentialEquation(DataPoint first, DataPoint last, double dx)
    {
        var b = Math.Log(last.Y / first.Y) / dx;
        var a = first.Y / Math.Exp(b * first.X);
        return $"y = {a:0.###}e^({b:0.###}x)";
    }

    private static string FormatLogarithmicEquation(DataPoint first, DataPoint last)
    {
        var dLogX = Math.Log(last.X) - Math.Log(first.X);
        if (Math.Abs(dLogX) < double.Epsilon)
            return "Logarithmic Trendline";

        var b = (last.Y - first.Y) / dLogX;
        var a = first.Y - (b * Math.Log(first.X));
        return $"y = {b:0.###}ln(x) {FormatSigned(a)}";
    }

    private static string FormatPowerEquation(DataPoint first, DataPoint last)
    {
        var dLogX = Math.Log(last.X) - Math.Log(first.X);
        if (Math.Abs(dLogX) < double.Epsilon)
            return "Power Trendline";

        var b = Math.Log(last.Y / first.Y) / dLogX;
        var a = first.Y / Math.Pow(first.X, b);
        return $"y = {a:0.###}x^{b:0.###}";
    }

    private static string FormatSigned(double value) =>
        value < 0 ? $"- {Math.Abs(value):0.###}" : $"+ {value:0.###}";

    private static string GetTrendlineTitle(ChartTrendlineType type) =>
        type switch
        {
            ChartTrendlineType.Exponential => "Exponential Trendline",
            ChartTrendlineType.Logarithmic => "Logarithmic Trendline",
            ChartTrendlineType.Power => "Power Trendline",
            ChartTrendlineType.MovingAverage => "Moving Average",
            ChartTrendlineType.Polynomial => "Polynomial Trendline",
            _ => "Linear Trendline"
        };
}

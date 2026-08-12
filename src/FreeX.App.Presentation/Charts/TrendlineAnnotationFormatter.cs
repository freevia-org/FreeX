using FreeX.Core.Model;

namespace FreeX.App.Presentation.Charts;

/// <summary>
/// Portable, UI-framework-free formatter for the trendline equation / R-squared annotation text.
/// Mirrors the source (WPF) renderer's <c>ChartRenderer.Trendlines.cs</c> formatting exactly
/// (same equation shapes, same R² precision) so both desktop hosts show identical annotation text
/// when <see cref="ChartModel.ShowTrendlineEquation"/> and/or <see cref="ChartModel.ShowTrendlineRSquared"/>
/// are set.
/// </summary>
public static class TrendlineAnnotationFormatter
{
    /// <summary>
    /// Builds the annotation text lines (equation and/or R²) for the given chart + trendline fit,
    /// or an empty list when neither flag is set or no line can be produced (e.g. degenerate fit).
    /// </summary>
    public static IReadOnlyList<string> BuildAnnotationLines(
        ChartModel chart,
        IReadOnlyList<TrendPoint> sourcePoints,
        IReadOnlyList<TrendPoint> trendPoints)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(sourcePoints);
        ArgumentNullException.ThrowIfNull(trendPoints);

        var lines = new List<string>();
        if (!chart.ShowTrendlineEquation && !chart.ShowTrendlineRSquared)
            return lines;

        if (chart.ShowTrendlineEquation)
            lines.Add(GetEquationText(chart, trendPoints));

        var logTransformY = chart.TrendlineType is ChartTrendlineType.Exponential or ChartTrendlineType.Power;
        if (chart.ShowTrendlineRSquared
            && TrendlineCalculator.TryCalculateRSquared(sourcePoints, trendPoints, out var rSquared, logTransformY))
            lines.Add($"R² = {rSquared:0.0000}");

        return lines;
    }

    private static string GetEquationText(ChartModel chart, IReadOnlyList<TrendPoint> trendPoints)
    {
        if (chart.TrendlineType == ChartTrendlineType.MovingAverage)
            return $"Moving average ({Math.Max(2, chart.TrendlinePeriod)})";
        if (chart.TrendlineType == ChartTrendlineType.Polynomial)
            return $"Polynomial (order {Math.Clamp(chart.TrendlineOrder, 2, 6)})";
        if (trendPoints.Count < 2)
            return GetTitle(chart.TrendlineType);

        var first = trendPoints[0];
        var last = trendPoints[^1];
        var dx = last.X - first.X;
        if (Math.Abs(dx) < double.Epsilon)
            return GetTitle(chart.TrendlineType);

        return chart.TrendlineType switch
        {
            ChartTrendlineType.Exponential when first.Y > 0 && last.Y > 0 =>
                FormatExponentialEquation(first, last, dx),
            ChartTrendlineType.Logarithmic when first.X > 0 && last.X > 0 =>
                FormatLogarithmicEquation(first, last),
            ChartTrendlineType.Power when first.X > 0 && last.X > 0 && first.Y > 0 && last.Y > 0 =>
                FormatPowerEquation(first, last),
            _ => FormatLinearEquation(first, last, dx),
        };
    }

    private static string FormatLinearEquation(TrendPoint first, TrendPoint last, double dx)
    {
        var slope = (last.Y - first.Y) / dx;
        var intercept = first.Y - (slope * first.X);
        return $"y = {slope:0.###}x {FormatSigned(intercept)}";
    }

    private static string FormatExponentialEquation(TrendPoint first, TrendPoint last, double dx)
    {
        var b = Math.Log(last.Y / first.Y) / dx;
        var a = first.Y / Math.Exp(b * first.X);
        return $"y = {a:0.###}e^({b:0.###}x)";
    }

    private static string FormatLogarithmicEquation(TrendPoint first, TrendPoint last)
    {
        var dLogX = Math.Log(last.X) - Math.Log(first.X);
        if (Math.Abs(dLogX) < double.Epsilon)
            return "Logarithmic Trendline";

        var b = (last.Y - first.Y) / dLogX;
        var a = first.Y - (b * Math.Log(first.X));
        return $"y = {b:0.###}ln(x) {FormatSigned(a)}";
    }

    private static string FormatPowerEquation(TrendPoint first, TrendPoint last)
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

    public static string GetTitle(ChartTrendlineType type) =>
        type switch
        {
            ChartTrendlineType.Exponential => "Exponential Trendline",
            ChartTrendlineType.Logarithmic => "Logarithmic Trendline",
            ChartTrendlineType.Power => "Power Trendline",
            ChartTrendlineType.MovingAverage => "Moving Average",
            ChartTrendlineType.Polynomial => "Polynomial Trendline",
            _ => "Linear Trendline",
        };
}

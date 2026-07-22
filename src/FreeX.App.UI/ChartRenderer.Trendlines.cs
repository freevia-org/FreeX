using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Series;

using FreeX.Core.Model;

namespace FreeX.App.UI;

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

        // Excel only allows a fixed intercept on the Linear trendline; recompute the fit with
        // the intercept pinned (least squares over the residual y - intercept) rather than the
        // free-intercept fit ChartTrendlineCalculator returned.
        if (chart.TrendlineType == ChartTrendlineType.Linear && chart.TrendlineIntercept is { } fixedIntercept)
            trendPoints = CalculateLinearWithFixedIntercept(points, fixedIntercept) ?? trendPoints;

        trendPoints = ApplyTrendlineForecast(chart, trendPoints);
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

    /// <summary>
    /// Refits a linear trendline with the intercept pinned to <paramref name="intercept"/> (Excel's
    /// "Set Intercept" option), returning the two fitted endpoints across the source X range. Uses
    /// ordinary least squares on the residual (y - intercept) so slope = Σx·(y-intercept) / Σx².
    /// Returns null when the fit is undefined (fewer than 2 points or a degenerate X range).
    /// </summary>
    private static IReadOnlyList<DataPoint>? CalculateLinearWithFixedIntercept(
        IReadOnlyList<DataPoint> points,
        double intercept)
    {
        var sumXX = 0.0;
        var sumXResidual = 0.0;
        var minX = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var count = 0;
        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            sumXX += point.X * point.X;
            sumXResidual += point.X * (point.Y - intercept);
            minX = Math.Min(minX, point.X);
            maxX = Math.Max(maxX, point.X);
            count++;
        }

        if (count < 2 || Math.Abs(sumXX) < double.Epsilon)
            return null;

        var slope = sumXResidual / sumXX;
        return [new DataPoint(minX, intercept + slope * minX), new DataPoint(maxX, intercept + slope * maxX)];
    }

    /// <summary>
    /// Extends the fitted trendline by Excel's Forward/Backward forecast periods (measured in
    /// category-axis units, i.e. the same X units as the source points). Extrapolates using the
    /// trendline's own boundary segment (linear/exponential/logarithmic/power all sample a smooth
    /// curve whose two nearest boundary points define the local slope) so the extension continues the
    /// fitted shape rather than requiring a shared-file change to the trendline calculator. Moving
    /// Average has no Excel forecast option and is returned unchanged.
    /// </summary>
    private static IReadOnlyList<DataPoint> ApplyTrendlineForecast(
        ChartModel chart,
        IReadOnlyList<DataPoint> trendPoints)
    {
        var forward = chart.TrendlineForward is { } f && f > 0 ? f : 0;
        var backward = chart.TrendlineBackward is { } b && b > 0 ? b : 0;
        if ((forward <= 0 && backward <= 0) || chart.TrendlineType == ChartTrendlineType.MovingAverage || trendPoints.Count < 2)
            return trendPoints;

        var result = new List<DataPoint>(trendPoints.Count + 2);
        if (backward > 0)
        {
            var first = trendPoints[0];
            var second = trendPoints[1];
            var extendedX = first.X - backward;
            result.Add(new DataPoint(extendedX, ExtrapolateY(chart.TrendlineType, first, second, extendedX)));
        }

        result.AddRange(trendPoints);

        if (forward > 0)
        {
            var last = trendPoints[^1];
            var secondToLast = trendPoints[^2];
            var extendedX = last.X + forward;
            result.Add(new DataPoint(extendedX, ExtrapolateY(chart.TrendlineType, secondToLast, last, extendedX)));
        }

        return result;
    }

    /// <summary>
    /// Extrapolates a Y value at <paramref name="targetX"/> beyond the boundary segment
    /// (<paramref name="a"/>, <paramref name="b"/>) of a fitted trendline, using the closed-form shape
    /// appropriate to <paramref name="type"/> (log-linear for exponential/power in the relevant axis,
    /// straight-line extension otherwise). Falls back to a linear extension of the segment when the
    /// closed form is undefined for the given points (e.g. non-positive X/Y for log/power).
    /// </summary>
    private static double ExtrapolateY(ChartTrendlineType type, DataPoint a, DataPoint b, double targetX)
    {
        var dx = b.X - a.X;
        if (Math.Abs(dx) < double.Epsilon)
            return b.Y;

        switch (type)
        {
            case ChartTrendlineType.Exponential when a.Y > 0 && b.Y > 0:
            {
                var slope = Math.Log(b.Y / a.Y) / dx;
                return a.Y * Math.Exp(slope * (targetX - a.X));
            }
            case ChartTrendlineType.Power when a.X > 0 && b.X > 0 && a.Y > 0 && b.Y > 0 && targetX > 0:
            {
                var dLogX = Math.Log(b.X) - Math.Log(a.X);
                if (Math.Abs(dLogX) < double.Epsilon)
                    break;
                var slope = Math.Log(b.Y / a.Y) / dLogX;
                return a.Y * Math.Pow(targetX / a.X, slope);
            }
            case ChartTrendlineType.Logarithmic when a.X > 0 && b.X > 0 && targetX > 0:
            {
                var dLogX = Math.Log(b.X) - Math.Log(a.X);
                if (Math.Abs(dLogX) < double.Epsilon)
                    break;
                var slope = (b.Y - a.Y) / dLogX;
                return b.Y + slope * (Math.Log(targetX) - Math.Log(b.X));
            }
        }

        // Linear (and any degenerate curve case above) extends the straight segment.
        var linearSlope = (b.Y - a.Y) / dx;
        return b.Y + linearSlope * (targetX - b.X);
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
            ChartMarkerStyle.X => MarkerType.Cross,
            ChartMarkerStyle.Star => MarkerType.Star,
            ChartMarkerStyle.Plus => MarkerType.Plus,
            ChartMarkerStyle.Dot => MarkerType.Circle,
            ChartMarkerStyle.Dash => MarkerType.Square,
            ChartMarkerStyle.Auto => MarkerType.Circle,
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
        var logTransformY = chart.TrendlineType is ChartTrendlineType.Exponential or ChartTrendlineType.Power;
        if (chart.ShowTrendlineRSquared && ChartTrendlineCalculator.TryCalculateRSquared(sourcePoints, trendPoints, out var rSquared, logTransformY))
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

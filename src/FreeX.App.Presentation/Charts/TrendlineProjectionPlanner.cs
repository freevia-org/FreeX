using FreeX.Core.Model;

namespace FreeX.App.Presentation.Charts;

/// <summary>
/// Portable trendline projection, including fit options, display-axis projection, and annotations.
/// Renderers only map the resulting data-space points to pixels and draw them.
/// </summary>
public sealed record TrendlineProjectionPlan(
    ChartTrendlineType Type,
    string Title,
    IReadOnlyList<TrendPoint> Points,
    IReadOnlyList<string> AnnotationLines,
    TrendPoint? AnnotationAnchor);

public static class TrendlineProjectionPlanner
{
    public static TrendlineProjectionPlan? Plan(
        ChartModel chart,
        IReadOnlyList<TrendPoint> sourcePoints,
        bool swapAxes = false)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(sourcePoints);

        if (!chart.ShowLinearTrendline
            || !ChartTypeSupport.SupportsTrendlines(chart.Type)
            || sourcePoints.Count < 2)
        {
            return null;
        }

        var trendPoints = TrendlineCalculator.Calculate(
            chart.TrendlineType,
            sourcePoints,
            chart.TrendlinePeriod,
            chart.TrendlineOrder);
        if (trendPoints.Count < 2)
            return null;

        if (chart.TrendlineType == ChartTrendlineType.Linear
            && chart.TrendlineIntercept is { } fixedIntercept)
        {
            trendPoints = CalculateLinearWithFixedIntercept(sourcePoints, fixedIntercept) ?? trendPoints;
        }

        trendPoints = ApplyForecast(chart, trendPoints);
        if (trendPoints.Count < 2)
            return null;

        var annotationLines = TrendlineAnnotationFormatter.BuildAnnotationLines(chart, sourcePoints, trendPoints);
        var projectedPoints = ProjectPoints(trendPoints, swapAxes);
        TrendPoint? annotationAnchor = annotationLines.Count == 0
            ? null
            : CalculateAnnotationAnchor(sourcePoints, swapAxes);

        return new TrendlineProjectionPlan(
            chart.TrendlineType,
            TrendlineAnnotationFormatter.GetTitle(chart.TrendlineType),
            projectedPoints,
            annotationLines,
            annotationAnchor);
    }

    private static IReadOnlyList<TrendPoint>? CalculateLinearWithFixedIntercept(
        IReadOnlyList<TrendPoint> points,
        double intercept)
    {
        var sumXX = 0.0;
        var sumXResidual = 0.0;
        var minX = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            sumXX += point.X * point.X;
            sumXResidual += point.X * (point.Y - intercept);
            minX = Math.Min(minX, point.X);
            maxX = Math.Max(maxX, point.X);
        }

        if (points.Count < 2 || Math.Abs(sumXX) < double.Epsilon)
            return null;

        var slope = sumXResidual / sumXX;
        return
        [
            new TrendPoint(minX, intercept + (slope * minX)),
            new TrendPoint(maxX, intercept + (slope * maxX)),
        ];
    }

    private static IReadOnlyList<TrendPoint> ApplyForecast(
        ChartModel chart,
        IReadOnlyList<TrendPoint> trendPoints)
    {
        var forward = chart.TrendlineForward is { } forwardValue && forwardValue > 0 ? forwardValue : 0;
        var backward = chart.TrendlineBackward is { } backwardValue && backwardValue > 0 ? backwardValue : 0;
        if ((forward <= 0 && backward <= 0)
            || chart.TrendlineType == ChartTrendlineType.MovingAverage
            || trendPoints.Count < 2)
        {
            return trendPoints;
        }

        var result = new List<TrendPoint>(trendPoints.Count + 2);
        if (backward > 0)
        {
            var first = trendPoints[0];
            var second = trendPoints[1];
            var extendedX = first.X - backward;
            result.Add(new TrendPoint(extendedX, ExtrapolateY(chart.TrendlineType, first, second, extendedX)));
        }

        result.AddRange(trendPoints);

        if (forward > 0)
        {
            var last = trendPoints[^1];
            var secondToLast = trendPoints[^2];
            var extendedX = last.X + forward;
            result.Add(new TrendPoint(extendedX, ExtrapolateY(chart.TrendlineType, secondToLast, last, extendedX)));
        }

        return result;
    }

    private static double ExtrapolateY(ChartTrendlineType type, TrendPoint a, TrendPoint b, double targetX)
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
                return b.Y + (slope * (Math.Log(targetX) - Math.Log(b.X)));
            }
        }

        var linearSlope = (b.Y - a.Y) / dx;
        return b.Y + (linearSlope * (targetX - b.X));
    }

    private static IReadOnlyList<TrendPoint> ProjectPoints(
        IReadOnlyList<TrendPoint> points,
        bool swapAxes)
    {
        if (!swapAxes)
            return points;

        var projected = new TrendPoint[points.Count];
        for (var i = 0; i < points.Count; i++)
            projected[i] = new TrendPoint(points[i].Y, points[i].X);

        return projected;
    }

    private static TrendPoint CalculateAnnotationAnchor(
        IReadOnlyList<TrendPoint> sourcePoints,
        bool swapAxes)
    {
        var minX = swapAxes ? sourcePoints[0].Y : sourcePoints[0].X;
        var maxY = swapAxes ? sourcePoints[0].X : sourcePoints[0].Y;
        for (var i = 1; i < sourcePoints.Count; i++)
        {
            var point = sourcePoints[i];
            minX = Math.Min(minX, swapAxes ? point.Y : point.X);
            maxY = Math.Max(maxY, swapAxes ? point.X : point.Y);
        }

        return new TrendPoint(minX, maxY);
    }
}

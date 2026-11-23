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
    /// <summary>
    /// Plans one trendline. <paramref name="categoryOrigin"/> is the number Excel gives the first category
    /// when it fits a trendline over a plain (non-date) category axis (1: categories are numbered 1, 2, 3...).
    /// <paramref name="sourcePoints"/> carry the zero-based category index as X; the fit, equation and
    /// forecast are computed in Excel's numbering, and the returned plan is shifted back to the zero-based
    /// index space the axes use. Scatter and date axes pass 0.
    /// </summary>
    public static TrendlineProjectionPlan? Plan(
        ChartModel chart,
        IReadOnlyList<TrendPoint> sourcePoints,
        bool swapAxes = false,
        string? seriesName = null,
        double categoryOrigin = 0)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(sourcePoints);

        if (categoryOrigin != 0)
        {
            var shifted = new TrendPoint[sourcePoints.Count];
            for (var i = 0; i < shifted.Length; i++)
                shifted[i] = sourcePoints[i] with { X = sourcePoints[i].X + categoryOrigin };

            var plan = Plan(chart, shifted, swapAxes, seriesName);
            if (plan is null)
                return null;

            // A swapped plan is (value, category): the category coordinate is Y there.
            TrendPoint Shift(TrendPoint point) =>
                swapAxes ? point with { Y = point.Y - categoryOrigin } : point with { X = point.X - categoryOrigin };
            return plan with
            {
                Points = plan.Points.Select(Shift).ToArray(),
                AnnotationAnchor = plan.AnnotationAnchor is { } anchor ? Shift(anchor) : null,
            };
        }

        if (!chart.ShowLinearTrendline
            || !ChartTypeSupport.SupportsTrendlines(chart.Type)
            || sourcePoints.Count < 2)
        {
            return null;
        }

        // The fit is evaluated over the forecast range itself (Forward/Backward periods), so curved
        // forecasts follow the regression and the portable axes/renderers share one set of points.
        var forward = chart.TrendlineForward is { } forwardValue && forwardValue > 0 ? forwardValue : 0;
        var backward = chart.TrendlineBackward is { } backwardValue && backwardValue > 0 ? backwardValue : 0;
        var trendPoints = TrendlineCalculator.Calculate(
            chart.TrendlineType,
            sourcePoints,
            chart.TrendlinePeriod,
            chart.TrendlineOrder,
            backward,
            forward);
        if (trendPoints.Count < 2)
            return null;

        if (chart.TrendlineType == ChartTrendlineType.Linear
            && chart.TrendlineIntercept is { } fixedIntercept)
        {
            trendPoints = CalculateLinearWithFixedIntercept(sourcePoints, fixedIntercept, backward, forward) ?? trendPoints;
        }

        var annotationLines = TrendlineAnnotationFormatter.BuildAnnotationLines(chart, sourcePoints, trendPoints);
        var projectedPoints = ProjectPoints(trendPoints, swapAxes);
        TrendPoint? annotationAnchor = annotationLines.Count == 0
            ? null
            : CalculateAnnotationAnchor(sourcePoints, swapAxes);

        return new TrendlineProjectionPlan(
            chart.TrendlineType,
            seriesName is null
                ? TrendlineAnnotationFormatter.GetTitle(chart.TrendlineType)
                : TrendlineAnnotationFormatter.GetLegendName(chart, seriesName),
            projectedPoints,
            annotationLines,
            annotationAnchor);
    }

    private static IReadOnlyList<TrendPoint>? CalculateLinearWithFixedIntercept(
        IReadOnlyList<TrendPoint> points,
        double intercept,
        double backward,
        double forward)
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
        minX -= backward;
        maxX += forward;
        return
        [
            new TrendPoint(minX, intercept + (slope * minX)),
            new TrendPoint(maxX, intercept + (slope * maxX)),
        ];
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

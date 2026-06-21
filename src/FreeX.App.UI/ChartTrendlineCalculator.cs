using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;
using OxyPlot;

namespace FreeX.App.UI;

public static class ChartTrendlineCalculator
{
    public static IReadOnlyList<DataPoint> Calculate(
        ChartTrendlineType type,
        IReadOnlyList<DataPoint> points,
        int period,
        int order)
    {
        var trend = TrendlineCalculator.Calculate(type, ToTrendPoints(points), period, order);
        return ToDataPoints(trend);
    }

    public static bool TryCalculateRSquared(
        IReadOnlyList<DataPoint> sourcePoints,
        IReadOnlyList<DataPoint> trendPoints,
        out double rSquared,
        bool logTransformY = false) =>
        TrendlineCalculator.TryCalculateRSquared(
            ToTrendPoints(sourcePoints),
            ToTrendPoints(trendPoints),
            out rSquared,
            logTransformY);

    private static TrendPoint[] ToTrendPoints(IReadOnlyList<DataPoint> points)
    {
        var result = new TrendPoint[points.Count];
        for (var i = 0; i < points.Count; i++)
            result[i] = new TrendPoint(points[i].X, points[i].Y);

        return result;
    }

    private static IReadOnlyList<DataPoint> ToDataPoints(IReadOnlyList<TrendPoint> points)
    {
        var result = new DataPoint[points.Count];
        for (var i = 0; i < points.Count; i++)
            result[i] = new DataPoint(points[i].X, points[i].Y);

        return result;
    }
}

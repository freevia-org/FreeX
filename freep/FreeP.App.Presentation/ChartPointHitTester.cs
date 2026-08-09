using FreeP.Core.Model;
using Free.Shared.Drawing;

namespace FreeP.App.Compositor;

/// <summary>Identifies a chart data point in the same coordinate space as the slide canvas.</summary>
public readonly record struct ChartPointHit(uint ShapeId, int SeriesIndex, int PointIndex);

/// <summary>
/// Resolves pointer locations to chart data points using the renderer-neutral chart scene.
/// Both canvas hosts use this path so a point is identified from the same planned geometry
/// that they paint, including rotated chart shapes.
/// </summary>
public static class ChartPointHitTester
{
    private const double PointToleranceDip = 8.0;

    public static bool TryHitTest(
        Slide slide,
        Presentation presentation,
        double slidePtX,
        double slidePtY,
        out ChartPointHit hit)
    {
        ArgumentNullException.ThrowIfNull(slide);
        ArgumentNullException.ThrowIfNull(presentation);

        var shapeId = ShapeHitTester.HitTest(slide, presentation, slidePtX, slidePtY);
        var shape = shapeId is { } id ? ShapeHitTester.FindShape(slide, id) : null;
        if (shape?.Kind != SlideShapeKind.Chart || shape.Chart is null)
        {
            hit = default;
            return false;
        }

        var bounds = ShapeHitTester.GetShapeBoundsDip(shape, slide, presentation);
        var local = SlideTransformCore.UnRotatePoint(
            slidePtX,
            slidePtY,
            bounds.Left + bounds.Width / 2.0,
            bounds.Top + bounds.Height / 2.0,
            shape.RotationDeg);
        var chartPoint = new ChartPlanPoint(local.X - bounds.Left, local.Y - bounds.Top);
        var scene = ChartRenderPlanner.BuildScenePlan(
            shape.Chart,
            new ChartPlanRect(0, 0, bounds.Width, bounds.Height));

        if (!TryHitScene(scene, chartPoint, out var point))
        {
            hit = default;
            return false;
        }

        hit = new ChartPointHit(shape.Id, point.SeriesIndex, point.PointIndex);
        return true;
    }

    private static bool TryHitScene(
        ChartScenePlan scene,
        ChartPlanPoint point,
        out (int SeriesIndex, int PointIndex) hit)
    {
        // Rectangular series use their actual painted footprint. This includes columns,
        // bars, waterfall points, and the secondary bars of an OfPie chart.
        for (var index = scene.Rectangles.Count - 1; index >= 0; index--)
        {
            var rectangle = scene.Rectangles[index];
            if (Contains(rectangle.Bounds, point))
            {
                hit = (rectangle.SeriesIndex, rectangle.CategoryIndex);
                return true;
            }
        }

        foreach (var circle in EnumerateCircles(scene))
        {
            if (Distance(circle.Center, point) <= Math.Max(PointToleranceDip, circle.Radius + 2))
            {
                hit = (circle.SeriesIndex, circle.PointIndex);
                return true;
            }
        }

        foreach (var candidate in EnumeratePoints(scene))
        {
            if (Distance(candidate.Point, point) <= PointToleranceDip)
            {
                hit = (candidate.SeriesIndex, candidate.PointIndex);
                return true;
            }
        }

        foreach (var slice in scene.PieSlices.Concat(scene.DoughnutSlices).Concat(scene.OfPieSecondarySlices))
        {
            if (IsInsideSlice(slice, point))
            {
                hit = (slice.SeriesIndex, slice.PointIndex);
                return true;
            }
        }

        foreach (var segment in scene.FunnelSegments)
        {
            if (segment.Path.Points.Count > 0 &&
                Contains(ChartPlanRectFromPoints(segment.Path.Points), point))
            {
                hit = (segment.SeriesIndex, segment.CategoryIndex);
                return true;
            }
        }

        hit = default;
        return false;
    }

    private static IEnumerable<ChartCirclePrimitive> EnumerateCircles(ChartScenePlan scene)
    {
        foreach (var line in scene.LineSeries)
            foreach (var marker in line.Markers)
                yield return marker;
        foreach (var line in scene.ComboLineSeries)
            foreach (var marker in line.Markers)
                yield return marker;
        if (scene.Scatter is { } scatter)
            foreach (var series in scatter.Series)
                foreach (var marker in series.Markers)
                    yield return marker;
        if (scene.Bubble is { } bubble)
            foreach (var item in bubble.Bubbles)
                yield return new ChartCirclePrimitive(
                    item.SeriesIndex,
                    item.PointIndex,
                    item.Center,
                    item.Radius,
                    ChartMarkerPrimitiveSymbol.Circle,
                    item.Fill,
                    item.Stroke);
        if (scene.Radar is { } radar)
            foreach (var series in radar.Series)
                foreach (var marker in series.Markers)
                    yield return marker;
    }

    private static IEnumerable<(int SeriesIndex, int PointIndex, ChartPlanPoint Point)> EnumeratePoints(
        ChartScenePlan scene)
    {
        foreach (var line in scene.LineSeries.Concat(scene.ComboLineSeries))
        {
            for (var index = 0; index < line.Points.Count; index++)
                if (line.Points[index] is { } point)
                    yield return (line.SeriesIndex, index, point);
        }

        foreach (var area in scene.AreaSeries)
        {
            for (var index = 0; index < area.Points.Count; index++)
                yield return (area.SeriesIndex, index, area.Points[index]);
        }

        if (scene.Surface is { } surface)
            foreach (var point in surface.Points)
                yield return (point.SeriesIndex, point.CategoryIndex, point.Point);

        if (scene.Scatter is { } scatter)
            foreach (var series in scatter.Series)
                for (var index = 0; index < series.Points.Count; index++)
                    if (series.Points[index] is { } point)
                        yield return (series.SeriesIndex, index, point);

        if (scene.Radar is { } radar)
            foreach (var series in radar.Series)
                for (var index = 0; index < series.Points.Count; index++)
                    if (series.Points[index] is { } point)
                        yield return (series.SeriesIndex, index, point);
    }

    private static bool IsInsideSlice(ChartPieSlicePrimitive slice, ChartPlanPoint point)
    {
        var dx = point.X - slice.Center.X;
        var dy = (point.Y - slice.Center.Y) / slice.EffectiveVerticalScale;
        var radius = Math.Sqrt(dx * dx + dy * dy);
        if (radius < slice.InnerRadius - PointToleranceDip || radius > slice.OuterRadius + PointToleranceDip)
            return false;

        var angle = Math.Atan2(dy, dx);
        var start = NormalizeAngle(slice.StartAngle);
        var sweep = slice.SweepAngle;
        var delta = NormalizeAngle(angle - start);
        return sweep >= 0
            ? delta <= sweep + 0.05
            : delta >= 2 * Math.PI + sweep - 0.05;
    }

    private static bool Contains(ChartPlanRect rect, ChartPlanPoint point) =>
        point.X >= rect.X - PointToleranceDip &&
        point.X <= rect.Right + PointToleranceDip &&
        point.Y >= rect.Y - PointToleranceDip &&
        point.Y <= rect.Bottom + PointToleranceDip;

    private static ChartPlanRect ChartPlanRectFromPoints(IReadOnlyList<ChartPlanPoint> points)
    {
        var left = points.Min(point => point.X);
        var top = points.Min(point => point.Y);
        var right = points.Max(point => point.X);
        var bottom = points.Max(point => point.Y);
        return new ChartPlanRect(left, top, right - left, bottom - top);
    }

    private static double Distance(ChartPlanPoint first, ChartPlanPoint second) =>
        Math.Sqrt(Math.Pow(first.X - second.X, 2) + Math.Pow(first.Y - second.Y, 2));

    private static double NormalizeAngle(double angle)
    {
        var normalized = angle % (2 * Math.PI);
        return normalized < 0 ? normalized + 2 * Math.PI : normalized;
    }
}

using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum ChartSubtargetKind
{
    ChartArea,
    PlotArea,
    Point,
    Series,
    Title,
    Legend,
    CategoryAxis,
    ValueAxis,
    AxisTitle,
}

public readonly record struct ChartSubtargetHit(
    uint ShapeId,
    ChartSubtargetKind Kind,
    int SeriesIndex = -1,
    int PointIndex = -1);

/// <summary>Resolves chart context-menu targets from the shared planned scene.</summary>
public static class ChartSubtargetHitTester
{
    private const double HitToleranceDip = 8.0;

    public static bool TryHitTest(
        Slide slide,
        Presentation presentation,
        double slidePtX,
        double slidePtY,
        out ChartSubtargetHit hit)
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

        var bounds = ShapeHitTester.GetShapeBoundsDip(shape, presentation);
        var local = SlideTransformCore.UnRotatePoint(
            slidePtX,
            slidePtY,
            bounds.Left + bounds.Width / 2.0,
            bounds.Top + bounds.Height / 2.0,
            shape.RotationDeg);
        var point = new ChartPlanPoint(local.X - bounds.Left, local.Y - bounds.Top);
        var scene = ChartRenderPlanner.BuildScenePlan(
            shape.Chart,
            new ChartPlanRect(0, 0, bounds.Width, bounds.Height));

        if (ChartPointHitTester.TryHitTest(slide, presentation, slidePtX, slidePtY, out var pointHit))
        {
            hit = new ChartSubtargetHit(shape.Id, ChartSubtargetKind.Point, pointHit.SeriesIndex, pointHit.PointIndex);
            return true;
        }

        if (scene.Title is { } title && Contains(title.Bounds, point))
        {
            hit = new ChartSubtargetHit(shape.Id, ChartSubtargetKind.Title);
            return true;
        }

        foreach (var axisTitle in scene.AxisTitles)
        {
            if (Contains(axisTitle.Label.Bounds, point))
            {
                hit = new ChartSubtargetHit(shape.Id, ChartSubtargetKind.AxisTitle);
                return true;
            }
        }

        foreach (var legend in scene.LegendItems)
        {
            if (Contains(legend.SwatchBounds, point) || Contains(legend.Label.Bounds, point))
            {
                hit = new ChartSubtargetHit(shape.Id, ChartSubtargetKind.Legend);
                return true;
            }
        }

        if (scene.CategoryAxisLabels.Any(label => Contains(label.Bounds, point)))
        {
            hit = new ChartSubtargetHit(shape.Id, ChartSubtargetKind.CategoryAxis);
            return true;
        }

        if (scene.ValueAxisLabels.Any(label => Contains(label.Bounds, point)) ||
            scene.SecondaryAxis.Labels.Any(label => Contains(label.Bounds, point)))
        {
            hit = new ChartSubtargetHit(shape.Id, ChartSubtargetKind.ValueAxis);
            return true;
        }

        if (TryFindSeries(scene, point, out var seriesIndex))
        {
            hit = new ChartSubtargetHit(shape.Id, ChartSubtargetKind.Series, seriesIndex);
            return true;
        }

        if (scene.Frame.HasPlot && Contains(scene.Frame.Plot, point))
        {
            hit = new ChartSubtargetHit(shape.Id, ChartSubtargetKind.PlotArea);
            return true;
        }

        if (Contains(scene.Frame.Bounds, point))
        {
            hit = new ChartSubtargetHit(shape.Id, ChartSubtargetKind.ChartArea);
            return true;
        }

        hit = default;
        return false;
    }

    private static bool TryFindSeries(ChartScenePlan scene, ChartPlanPoint point, out int seriesIndex)
    {
        foreach (var rectangle in scene.Rectangles.Reverse())
        {
            if (Contains(rectangle.Bounds, point))
            {
                seriesIndex = rectangle.SeriesIndex;
                return true;
            }
        }

        foreach (var line in scene.LineSeries.Concat(scene.ComboLineSeries))
        {
            if (line.LineSegments.Any(segment => DistanceToSegment(segment.Start, segment.End, point) <= HitToleranceDip))
            {
                seriesIndex = line.SeriesIndex;
                return true;
            }
        }

        foreach (var area in scene.AreaSeries)
        {
            if (Contains(ChartPlanRectFromPoints(area.Points), point))
            {
                seriesIndex = area.SeriesIndex;
                return true;
            }
        }

        foreach (var slice in scene.PieSlices.Concat(scene.DoughnutSlices).Concat(scene.OfPieSecondarySlices))
        {
            if (Contains(ChartPlanRectFromPoints(new[] { slice.OuterStart, slice.OuterEnd, slice.InnerStart, slice.InnerEnd }), point))
            {
                seriesIndex = slice.SeriesIndex;
                return true;
            }
        }

        foreach (var segment in scene.FunnelSegments)
        {
            if (segment.Path.Points.Count > 0 && Contains(ChartPlanRectFromPoints(segment.Path.Points), point))
            {
                seriesIndex = segment.SeriesIndex;
                return true;
            }
        }

        seriesIndex = -1;
        return false;
    }

    private static bool Contains(ChartPlanRect rect, ChartPlanPoint point) =>
        point.X >= rect.X - HitToleranceDip && point.X <= rect.Right + HitToleranceDip &&
        point.Y >= rect.Y - HitToleranceDip && point.Y <= rect.Bottom + HitToleranceDip;

    private static ChartPlanRect ChartPlanRectFromPoints(IReadOnlyList<ChartPlanPoint> points)
    {
        var left = points.Min(point => point.X);
        var top = points.Min(point => point.Y);
        var right = points.Max(point => point.X);
        var bottom = points.Max(point => point.Y);
        return new ChartPlanRect(left, top, right - left, bottom - top);
    }

    private static double DistanceToSegment(ChartPlanPoint start, ChartPlanPoint end, ChartPlanPoint point)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        if (dx == 0 && dy == 0)
            return Math.Sqrt(Math.Pow(point.X - start.X, 2) + Math.Pow(point.Y - start.Y, 2));

        var t = Math.Clamp(((point.X - start.X) * dx + (point.Y - start.Y) * dy) / (dx * dx + dy * dy), 0, 1);
        var closest = new ChartPlanPoint(start.X + t * dx, start.Y + t * dy);
        return Math.Sqrt(Math.Pow(point.X - closest.X, 2) + Math.Pow(point.Y - closest.Y, 2));
    }
}

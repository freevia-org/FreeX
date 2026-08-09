using Avalonia;
using Avalonia.Media;
using FreeP.App.Compositor;

namespace FreeP.App.Rendering.Avalonia;

public sealed partial class SlideCanvas
{
    private static void RenderChart(DrawingContext dc, DrawOp.Chart chartOperation)
    {
        var plan = ChartRenderCommandPlanner.Build(
            chartOperation,
            ChartRenderExecutionProfile.Avalonia);
        if (!plan.Transform.IsIdentity)
        {
            using var transformScope = dc.PushTransform(ToAvaloniaMatrix(plan.Transform));
            foreach (var command in plan.Commands)
                RenderChartCommand(dc, command);
            return;
        }

        foreach (var command in plan.Commands)
            RenderChartCommand(dc, command);
    }

    private static void RenderChartCommand(DrawingContext dc, ChartRenderCommand command)
    {
        switch (command)
        {
            case ChartRenderCommand.Frame frame:
            {
                var bounds = ToRect(frame.Bounds);
                var fill = ToBrush(frame.Fill);
                var stroke = frame.Stroke is { } frameStroke ? ToPen(frameStroke) : null;
                if (frame.RoundedCorners)
                {
                    double radius = Math.Min(
                        ChartRenderPlanner.RoundedChartCornerRadius,
                        Math.Min(bounds.Width, bounds.Height) / 2.0);
                    dc.DrawRectangle(fill, stroke, bounds, radius, radius);
                }
                else
                {
                    dc.FillRectangle(fill, bounds);
                    if (stroke is not null)
                        dc.DrawRectangle(stroke, bounds);
                }
                break;
            }
            case ChartRenderCommand.Rectangle rectangle:
                DrawChartRectangle(dc, rectangle);
                break;
            case ChartRenderCommand.Line line:
                dc.DrawLine(
                    ToPen(line.Primitive.Stroke),
                    ToPoint(line.Primitive.Start),
                    ToPoint(line.Primitive.End));
                break;
            case ChartRenderCommand.Path path:
                dc.DrawGeometry(
                    path.Primitive.Fill is { } pathFill ? ToBrush(pathFill) : null,
                    path.Stroke is { } pathStroke ? ToPen(pathStroke) : null,
                    ToGeometry(path.Primitive));
                break;
            case ChartRenderCommand.LinePath path:
                dc.DrawGeometry(
                    null,
                    ToPen(path.Primitive.Stroke),
                    ToGeometry(path.Primitive, path.Depth));
                break;
            case ChartRenderCommand.Marker marker:
                DrawChartMarker(dc, marker.Primitive);
                break;
            case ChartRenderCommand.PieSlice pieSlice:
                DrawChartPieSlice(dc, pieSlice);
                break;
            case ChartRenderCommand.DoughnutSlice doughnutSlice:
                DrawChartDoughnutSlice(dc, doughnutSlice);
                break;
            case ChartRenderCommand.SurfaceFacet facet:
                dc.DrawGeometry(
                    ToBrush(facet.Primitive.Fill),
                    ToPen(facet.Primitive.Stroke),
                    ToSurfaceFacetGeometry(facet.Primitive));
                break;
            case ChartRenderCommand.Bubble bubble:
                dc.DrawEllipse(
                    ToBrush(bubble.Primitive.Fill),
                    ToPen(bubble.Primitive.Stroke),
                    ToPoint(bubble.Primitive.Center),
                    bubble.Primitive.Radius,
                    bubble.Primitive.Radius);
                break;
            case ChartRenderCommand.Text text:
                DrawChartText(dc, text.Plan);
                break;
        }
    }

    private static void DrawChartRectangle(
        DrawingContext dc,
        ChartRenderCommand.Rectangle rectangle)
    {
        if (rectangle.ClipBounds is { } clipBounds)
        {
            using var clipScope = dc.PushClip(ToRect(clipBounds));
            DrawChartRectangleCore(dc, rectangle);
            return;
        }

        DrawChartRectangleCore(dc, rectangle);
    }

    private static void DrawChartRectangleCore(
        DrawingContext dc,
        ChartRenderCommand.Rectangle rectangle)
    {
        var bounds = ToRect(rectangle.Bounds);
        if (rectangle.Fill is { } fill)
            dc.FillRectangle(ToBrush(fill), bounds);
        if (rectangle.Stroke is { } stroke)
            dc.DrawRectangle(ToPen(stroke), bounds);
    }

    private static void DrawChartText(DrawingContext dc, ChartTextRenderPlan plan)
    {
        if (plan.ClipBounds is { } clipBounds)
        {
            using var clipScope = dc.PushClip(ToRect(clipBounds));
            DrawChartTextCore(dc, plan);
            return;
        }

        DrawChartTextCore(dc, plan);
    }

    private static void DrawChartTextCore(DrawingContext dc, ChartTextRenderPlan plan)
    {
        if (plan.RotationCenter is { } center && Math.Abs(plan.RotationDegrees) > 0.0001)
        {
            double angle = plan.RotationDegrees * Math.PI / 180.0;
            using var rotationScope = dc.PushTransform(
                Matrix.CreateTranslation(-center.X, -center.Y)
                * Matrix.CreateRotation(angle)
                * Matrix.CreateTranslation(center.X, center.Y));
            DrawChartTextCoreUntransformed(dc, plan);
            return;
        }

        DrawChartTextCoreUntransformed(dc, plan);
    }

    private static void DrawChartTextCoreUntransformed(
        DrawingContext dc,
        ChartTextRenderPlan plan)
    {
        var label = plan.Label;
        DrawChartLabel(
            dc,
            label.Text,
            ToRect(label.Bounds),
            label.IsBold,
            label.FontSize,
            ToTextAlignment(label.Alignment),
            label.IsItalic,
            label.TextColor,
            label.FontFamily,
            plan.MaxLineCount,
            label.HorizontalScale);
    }

    private static void DrawChartPieSlice(
        DrawingContext dc,
        ChartRenderCommand.PieSlice command)
    {
        Geometry geometry = command.Pass switch
        {
            ChartPieSliceRenderPass.Depth =>
                ToPieSliceGeometry(command.Primitive, command.Primitive.DepthOffsetY),
            ChartPieSliceRenderPass.DepthSidewall =>
                ToPieSliceDepthGeometry(command.DepthSidewallGeometry
                    ?? throw new InvalidOperationException("Pie sidewall geometry is required.")),
            _ => ToPieSliceGeometry(command.Primitive),
        };
        var border = command.Pass == ChartPieSliceRenderPass.Body
            ? new Pen(Brushes.White, 0.8)
            : null;
        dc.DrawGeometry(ToBrush(command.Fill), border, geometry);
    }

    private static void DrawChartDoughnutSlice(
        DrawingContext dc,
        ChartRenderCommand.DoughnutSlice command)
    {
        var primitive = command.Primitive;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(ToPoint(primitive.OuterStart), isFilled: true);
            context.ArcTo(
                ToPoint(primitive.OuterEnd),
                new Size(primitive.OuterRadius, primitive.OuterRadiusY),
                0,
                primitive.IsLargeArc,
                SweepDirection.Clockwise);
            context.LineTo(ToPoint(primitive.InnerEnd));
            context.ArcTo(
                ToPoint(primitive.InnerStart),
                new Size(primitive.InnerRadius, primitive.InnerRadiusY),
                0,
                primitive.IsLargeArc,
                SweepDirection.CounterClockwise);
            context.EndFigure(isClosed: true);
        }

        dc.DrawGeometry(ToBrush(command.Fill), new Pen(Brushes.White, 0.8), geometry);
    }

    private static StreamGeometry ToPieSliceGeometry(
        ChartPieSlicePrimitive primitive,
        double offsetY = 0)
    {
        var center = new ChartPlanPoint(primitive.Center.X, primitive.Center.Y + offsetY);
        var start = new ChartPlanPoint(primitive.OuterStart.X, primitive.OuterStart.Y + offsetY);
        var end = new ChartPlanPoint(primitive.OuterEnd.X, primitive.OuterEnd.Y + offsetY);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(ToPoint(center), isFilled: true);
            context.LineTo(ToPoint(start));
            context.ArcTo(
                ToPoint(end),
                new Size(primitive.OuterRadius, primitive.OuterRadiusY),
                0,
                primitive.IsLargeArc,
                SweepDirection.Clockwise);
            context.EndFigure(isClosed: true);
        }
        return geometry;
    }

    private static StreamGeometry ToPieSliceDepthGeometry(
        ChartPieDepthSidewallGeometryPlan plan)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(ToPoint(plan.TopStart), isFilled: true);
            context.ArcTo(
                ToPoint(plan.TopEnd),
                new Size(plan.RadiusX, plan.RadiusY),
                0,
                isLargeArc: false,
                SweepDirection.Clockwise);
            context.LineTo(ToPoint(plan.BottomEnd));
            context.ArcTo(
                ToPoint(plan.BottomStart),
                new Size(plan.RadiusX, plan.RadiusY),
                0,
                isLargeArc: false,
                SweepDirection.CounterClockwise);
            context.EndFigure(isClosed: true);
        }
        return geometry;
    }
}

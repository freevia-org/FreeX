using System.Windows;
using System.Windows.Media;
using FreeP.App.Compositor;

namespace FreeP.App.Rendering.Wpf;

public sealed partial class SlideCanvas
{
    private static void RenderChart(DrawingContext dc, DrawOp.Chart chartOperation)
    {
        var plan = ChartRenderCommandPlanner.Build(
            chartOperation,
            ChartRenderExecutionProfile.Wpf);
        if (!plan.Transform.IsIdentity)
            dc.PushTransform(ToWpfTransform(plan.Transform));

        ChartRenderCommandDispatcher.Dispatch(plan.Commands, new ChartRenderCommandSink(dc));

        if (!plan.Transform.IsIdentity)
            dc.Pop();
    }

    private sealed class ChartRenderCommandSink(DrawingContext dc) : IChartRenderCommandSink
    {
        public void Render(ChartRenderCommand.Frame frame)
        {
            var bounds = ToRect(frame.Bounds);
            var fill = ToBrush(frame.Fill);
            var stroke = frame.Stroke is { } frameStroke ? ToPen(frameStroke) : null;
            if (frame.CornerRadius > 0)
            {
                dc.DrawRoundedRectangle(
                    fill,
                    stroke,
                    bounds,
                    frame.CornerRadius,
                    frame.CornerRadius);
            }
            else
            {
                dc.DrawRectangle(fill, stroke, bounds);
            }
        }

        public void Render(ChartRenderCommand.Rectangle command) => DrawChartRectangle(dc, command);

        public void Render(ChartRenderCommand.Line line)
        {
            var pen = ToPen(line.Primitive.Stroke);
            if (line.PixelSnapHorizontal)
            {
                double left = Math.Min(line.Primitive.Start.X, line.Primitive.End.X);
                double right = Math.Max(line.Primitive.Start.X, line.Primitive.End.X);
                double top = Math.Round(
                    line.Primitive.Start.Y - 0.5,
                    MidpointRounding.AwayFromZero);
                dc.DrawRectangle(pen.Brush, null, new Rect(left, top, right - left, 1.0));
                return;
            }

            dc.DrawLine(pen, ToPoint(line.Primitive.Start), ToPoint(line.Primitive.End));
        }

        public void Render(ChartRenderCommand.Path path) =>
            dc.DrawGeometry(
                path.Primitive.Fill is { } fill ? ToBrush(fill) : null,
                path.Stroke is { } stroke ? ToPen(stroke) : null,
                path.UseAreaGeometry ? ToAreaGeometry(path.Primitive) : ToGeometry(path.Primitive));

        public void Render(ChartRenderCommand.LinePath path) =>
            dc.DrawGeometry(null, ToPen(path.Primitive.Stroke), ToGeometry(path.Primitive));

        public void Render(ChartRenderCommand.Marker command) => DrawChartMarker(dc, command.Primitive);

        public void Render(ChartRenderCommand.PieSlice command) => DrawChartPieSlice(dc, command);

        public void Render(ChartRenderCommand.DoughnutSlice command) => DrawChartDoughnutSlice(dc, command);

        public void Render(ChartRenderCommand.SurfaceFacet facet) =>
            dc.DrawGeometry(
                ToBrush(facet.Primitive.Fill),
                ToPen(facet.Primitive.Stroke),
                ToSurfaceFacetGeometry(facet.Primitive));

        public void Render(ChartRenderCommand.Bubble bubble) =>
            dc.DrawEllipse(
                ToBrush(bubble.Primitive.Fill),
                ToPen(bubble.Primitive.Stroke),
                ToPoint(bubble.Primitive.Center),
                bubble.Primitive.Radius,
                bubble.Primitive.Radius);

        public void Render(ChartRenderCommand.Text command) => DrawChartText(dc, command.Plan);
    }

    private static void DrawChartRectangle(
        DrawingContext dc,
        ChartRenderCommand.Rectangle rectangle)
    {
        if (rectangle.ClipBounds is { } clipBounds)
            dc.PushClip(new RectangleGeometry(ToRect(clipBounds)));

        dc.DrawRectangle(
            rectangle.Fill is { } fill ? ToBrush(fill) : null,
            rectangle.Stroke is { } stroke ? ToPen(stroke) : null,
            ToRect(rectangle.Bounds));

        if (rectangle.ClipBounds.HasValue)
            dc.Pop();
    }

    private static void DrawChartText(DrawingContext dc, ChartTextRenderPlan plan)
    {
        if (plan.ClipBounds is { } clipBounds)
            dc.PushClip(new RectangleGeometry(ToRect(clipBounds)));
        if (plan.RotationCenter is { } center && Math.Abs(plan.RotationDegrees) > 0.0001)
            dc.PushTransform(new RotateTransform(plan.RotationDegrees, center.X, center.Y));

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

        if (plan.RotationCenter.HasValue && Math.Abs(plan.RotationDegrees) > 0.0001)
            dc.Pop();
        if (plan.ClipBounds.HasValue)
            dc.Pop();
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
        dc.DrawGeometry(
            ToBrush(command.Fill),
            command.Stroke is { } stroke ? ToPen(stroke) : null,
            geometry);
    }

    private static void DrawChartDoughnutSlice(
        DrawingContext dc,
        ChartRenderCommand.DoughnutSlice command)
    {
        var primitive = command.Primitive;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(ToPoint(primitive.OuterStart), isFilled: true, isClosed: true);
            context.ArcTo(
                ToPoint(primitive.OuterEnd),
                new Size(primitive.OuterRadius, primitive.OuterRadiusY),
                0,
                primitive.IsLargeArc,
                SweepDirection.Clockwise,
                isStroked: false,
                isSmoothJoin: false);
            context.LineTo(ToPoint(primitive.InnerEnd), isStroked: false, isSmoothJoin: false);
            context.ArcTo(
                ToPoint(primitive.InnerStart),
                new Size(primitive.InnerRadius, primitive.InnerRadiusY),
                0,
                primitive.IsLargeArc,
                SweepDirection.Counterclockwise,
                isStroked: false,
                isSmoothJoin: false);
        }
        if (geometry.CanFreeze)
            geometry.Freeze();

        var border = new Pen(FreezeBrush(new SolidColorBrush(Colors.White)), 0.8);
        if (border.CanFreeze)
            border.Freeze();
        dc.DrawGeometry(ToBrush(command.Fill), border, geometry);
    }

    private static StreamGeometry ToPieSliceGeometry(
        ChartPieSlicePrimitive primitive,
        double offsetY = 0)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            var center = new ChartPlanPoint(primitive.Center.X, primitive.Center.Y + offsetY);
            var start = new ChartPlanPoint(primitive.OuterStart.X, primitive.OuterStart.Y + offsetY);
            var end = new ChartPlanPoint(primitive.OuterEnd.X, primitive.OuterEnd.Y + offsetY);
            context.BeginFigure(ToPoint(center), isFilled: true, isClosed: true);
            context.LineTo(ToPoint(start), isStroked: false, isSmoothJoin: false);
            context.ArcTo(
                ToPoint(end),
                new Size(primitive.OuterRadius, primitive.OuterRadiusY),
                0,
                primitive.IsLargeArc,
                SweepDirection.Clockwise,
                isStroked: false,
                isSmoothJoin: false);
        }
        if (geometry.CanFreeze)
            geometry.Freeze();
        return geometry;
    }

    private static StreamGeometry ToPieSliceDepthGeometry(
        ChartPieDepthSidewallGeometryPlan plan)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(ToPoint(plan.TopStart), isFilled: true, isClosed: true);
            context.ArcTo(
                ToPoint(plan.TopEnd),
                new Size(plan.RadiusX, plan.RadiusY),
                0,
                isLargeArc: false,
                SweepDirection.Clockwise,
                isStroked: false,
                isSmoothJoin: false);
            context.LineTo(ToPoint(plan.BottomEnd), isStroked: false, isSmoothJoin: false);
            context.ArcTo(
                ToPoint(plan.BottomStart),
                new Size(plan.RadiusX, plan.RadiusY),
                0,
                isLargeArc: false,
                SweepDirection.Counterclockwise,
                isStroked: false,
                isSmoothJoin: false);
        }
        if (geometry.CanFreeze)
            geometry.Freeze();
        return geometry;
    }
}

using Free.Shared.Drawing;

namespace FreeP.App.Compositor;

/// <summary>
/// Creates transient draw-operation copies for a multi-selection transform.
/// The source operations remain ordered by the compositor; hosts replace a source
/// operation with the copy at that same position so the preview keeps slide z-order.
/// </summary>
public static class CanvasTransformPreviewComposer
{
    public static IReadOnlyDictionary<uint, DrawOp> Compose(
        IReadOnlyList<DrawOp> sourceOps,
        CanvasMultiTransformPlan plan)
    {
        ArgumentNullException.ThrowIfNull(sourceOps);

        if (plan.Shapes.Count == 0)
            return new Dictionary<uint, DrawOp>();

        var transforms = plan.Shapes.ToDictionary(shape => shape.ShapeId);
        var previews = new Dictionary<uint, DrawOp>();
        foreach (var source in sourceOps)
        {
            if (!TryGetShapeId(source, out var shapeId)
                || !transforms.TryGetValue(shapeId, out var transform)
                || TryCompose(source, transform) is not { } preview)
            {
                continue;
            }

            previews[shapeId] = preview;
        }

        return previews;
    }

    public static bool TryGetShapeId(DrawOp op, out uint shapeId)
    {
        ArgumentNullException.ThrowIfNull(op);

        shapeId = op switch
        {
            DrawOp.Shape shape => shape.ShapeId,
            DrawOp.Picture picture => picture.ShapeId,
            DrawOp.Table table => table.ShapeId,
            DrawOp.Chart chart => chart.ShapeId,
            _ => 0
        };
        return shapeId != 0;
    }

    private static DrawOp? TryCompose(DrawOp source, CanvasShapeTransform transform) =>
        source switch
        {
            DrawOp.Shape shape => ComposeShape(shape, transform),
            DrawOp.Picture picture => ComposePicture(picture, transform),
            DrawOp.Table table => ComposeTable(table, transform),
            DrawOp.Chart chart => ComposeChart(chart, transform),
            _ => null
        };

    private static DrawOp.Shape ComposeShape(DrawOp.Shape source, CanvasShapeTransform transform)
    {
        var bounds = ToBounds(transform);
        return new DrawOp.Shape
        {
            ShapeId = source.ShapeId,
            Geometry = TransformGeometry(source.Geometry, source.BoundsDip, bounds),
            Fill = source.Fill,
            Outline = source.Outline,
            RotationDeg = transform.RotationDeg,
            FlipH = source.FlipH,
            FlipV = source.FlipV,
            BoundsDip = bounds,
            Text = source.Text,
            Effects = source.Effects,
            ElbowRouteDip = source.ElbowRouteDip is null
                ? null
                : source.ElbowRouteDip.Select(point => TransformPoint(point, source.BoundsDip, bounds)).ToArray(),
        };
    }

    private static DrawOp.Picture ComposePicture(DrawOp.Picture source, CanvasShapeTransform transform) =>
        new()
        {
            ShapeId = source.ShapeId,
            Bytes = source.Bytes,
            ContentType = source.ContentType,
            DestDip = ToBounds(transform),
            RotationDeg = transform.RotationDeg,
            Outline = source.Outline,
            IsMedia = source.IsMedia,
            CropLeft = source.CropLeft,
            CropTop = source.CropTop,
            CropRight = source.CropRight,
            CropBottom = source.CropBottom,
            Grayscale = source.Grayscale,
            BiLevelThreshold = source.BiLevelThreshold,
            Brightness = source.Brightness,
            Contrast = source.Contrast,
            AlphaModPct = source.AlphaModPct,
            Effects = source.Effects,
            PictureFrameGeometry = source.PictureFrameGeometry,
        };

    private static DrawOp.Table ComposeTable(DrawOp.Table source, CanvasShapeTransform transform)
    {
        var bounds = ToBounds(transform);
        return new DrawOp.Table
        {
            ShapeId = source.ShapeId,
            BoundsDip = bounds,
            RotationDeg = transform.RotationDeg,
            FlipH = source.FlipH,
            FlipV = source.FlipV,
            Cells = source.Cells
                .Select(cell => new TableCellOp
                {
                    BoundsDip = TransformRect(cell.BoundsDip, source.BoundsDip, bounds),
                    Fill = cell.Fill,
                    BorderLeft = cell.BorderLeft,
                    BorderRight = cell.BorderRight,
                    BorderTop = cell.BorderTop,
                    BorderBottom = cell.BorderBottom,
                    Text = cell.Text,
                    Anchor = cell.Anchor,
                })
                .ToArray(),
        };
    }

    private static DrawOp.Chart ComposeChart(DrawOp.Chart source, CanvasShapeTransform transform) =>
        new()
        {
            ShapeId = source.ShapeId,
            BoundsDip = ToBounds(transform),
            RotationDeg = transform.RotationDeg,
            ChartShape = source.ChartShape,
            SeriesColors = source.SeriesColors,
            FillPlans = source.FillPlans,
            ChartAreaFill = source.ChartAreaFill,
            ChartAreaOutline = source.ChartAreaOutline,
            PlotAreaFill = source.PlotAreaFill,
            PlotAreaOutline = source.PlotAreaOutline,
        };

    private static LayoutRect ToBounds(CanvasShapeTransform transform) =>
        new(
            SlideTransformCore.EmuToDip(transform.XEmu),
            SlideTransformCore.EmuToDip(transform.YEmu),
            SlideTransformCore.EmuToDip(transform.CxEmu),
            SlideTransformCore.EmuToDip(transform.CyEmu));

    private static ShapeGeometry TransformGeometry(
        ShapeGeometry geometry,
        LayoutRect sourceBounds,
        LayoutRect targetBounds)
    {
        return new ShapeGeometry(geometry.Contours
            .Select(contour => new ShapeContour(
                TransformPoint(contour.Start, sourceBounds, targetBounds),
                contour.Segments.Select(segment => new ShapeSegment(
                    segment.Kind,
                    TransformPoint(segment.End, sourceBounds, targetBounds),
                    TransformPoint(segment.Control1, sourceBounds, targetBounds),
                    TransformPoint(segment.Control2, sourceBounds, targetBounds),
                    Math.Abs(segment.RadiusX * ScaleX(sourceBounds, targetBounds)),
                    Math.Abs(segment.RadiusY * ScaleY(sourceBounds, targetBounds)),
                    segment.LargeArc,
                    segment.SweepClockwise)).ToArray(),
                contour.Closed,
                contour.Filled))
            .ToArray());
    }

    private static LayoutRect TransformRect(LayoutRect rect, LayoutRect sourceBounds, LayoutRect targetBounds) =>
        new(
            targetBounds.X + (rect.X - sourceBounds.X) * ScaleX(sourceBounds, targetBounds),
            targetBounds.Y + (rect.Y - sourceBounds.Y) * ScaleY(sourceBounds, targetBounds),
            rect.Width * ScaleX(sourceBounds, targetBounds),
            rect.Height * ScaleY(sourceBounds, targetBounds));

    private static LayoutPoint TransformPoint(LayoutPoint point, LayoutRect sourceBounds, LayoutRect targetBounds) =>
        new(
            targetBounds.X + (point.X - sourceBounds.X) * ScaleX(sourceBounds, targetBounds),
            targetBounds.Y + (point.Y - sourceBounds.Y) * ScaleY(sourceBounds, targetBounds));

    private static double ScaleX(LayoutRect sourceBounds, LayoutRect targetBounds) =>
        sourceBounds.Width == 0 ? 1 : targetBounds.Width / sourceBounds.Width;

    private static double ScaleY(LayoutRect sourceBounds, LayoutRect targetBounds) =>
        sourceBounds.Height == 0 ? 1 : targetBounds.Height / sourceBounds.Height;
}

using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public readonly record struct SlideScreenRect(
    double Left,
    double Top,
    double Width,
    double Height)
{
    public double Right => Left + Width;

    public double Bottom => Top + Height;
}

public readonly record struct InCanvasEditorPlacement(
    double Left,
    double Top,
    double Width,
    double Height)
{
    /// <summary>Shape rotation in screen/editor space, matching the rendered shape.</summary>
    public double RotationDegrees { get; init; }

    /// <summary>Whether the rendered shape is horizontally flipped.</summary>
    public bool FlipHorizontal { get; init; }

    /// <summary>Whether the rendered shape is vertically flipped.</summary>
    public bool FlipVertical { get; init; }

    /// <summary>
    /// The unexpanded shape center in editor-local coordinates.  This remains distinct from
    /// the editor center when a tiny shape is clamped to the minimum editing size.
    /// </summary>
    public double TransformOriginX { get; init; }

    /// <summary>See <see cref="TransformOriginX"/> for the Y coordinate.</summary>
    public double TransformOriginY { get; init; }

    public bool HasTransform =>
        Math.Abs(RotationDegrees) > 0.0001 || FlipHorizontal || FlipVertical;

    public double EffectiveTransformOriginX =>
        TransformOriginX > 0 ? TransformOriginX : Width / 2;

    public double EffectiveTransformOriginY =>
        TransformOriginY > 0 ? TransformOriginY : Height / 2;
}

public static class SlideCanvasGeometryPlanner
{
    public static SlideScreenRect DipBoundsToScreen(
        double left,
        double top,
        double width,
        double height,
        SlideTransformCore transform)
    {
        ArgumentNullException.ThrowIfNull(transform);

        var origin = transform.SlideToScreen(left, top);
        return new SlideScreenRect(
            origin.X,
            origin.Y,
            Math.Max(0, transform.ScaleDipToScreen(width)),
            Math.Max(0, transform.ScaleDipToScreen(height)));
    }

    public static SlideScreenRect DipBoundsToScreen(ShapeBoundsDip bounds, SlideTransformCore transform) =>
        DipBoundsToScreen(bounds.Left, bounds.Top, bounds.Width, bounds.Height, transform);

    public static SlideScreenRect DipBoundsToScreen(CellRectDip bounds, SlideTransformCore transform) =>
        DipBoundsToScreen(bounds.X, bounds.Y, bounds.Width, bounds.Height, transform);

    public static SlideScreenRect DipBoundsToScreen(LayoutRect bounds, SlideTransformCore transform) =>
        DipBoundsToScreen(bounds.X, bounds.Y, bounds.Width, bounds.Height, transform);

    public static SlideScreenRect EmuBoundsToScreen(
        long offsetXEmu,
        long offsetYEmu,
        long extentCxEmu,
        long extentCyEmu,
        SlideTransformCore transform) =>
        DipBoundsToScreen(
            SlideTransformCore.EmuToDip(offsetXEmu),
            SlideTransformCore.EmuToDip(offsetYEmu),
            SlideTransformCore.EmuToDip(extentCxEmu),
            SlideTransformCore.EmuToDip(extentCyEmu),
            transform);

    /// <summary>
    /// Returns the axis-aligned screen envelope of a rotated, unexpanded slide-DIP frame.
    /// This is used by selection chrome; the compositor still owns the member's oriented
    /// paint transform.
    /// </summary>
    public static SlideScreenRect OrientedBoundsToScreen(
        double left,
        double top,
        double width,
        double height,
        double rotationDeg,
        SlideTransformCore transform)
    {
        ArgumentNullException.ThrowIfNull(transform);

        double centerX = left + width / 2.0;
        double centerY = top + height / 2.0;
        double radians = rotationDeg * Math.PI / 180.0;
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);
        var corners = new[]
        {
            RotateCorner(left, top, centerX, centerY, cos, sin),
            RotateCorner(left + width, top, centerX, centerY, cos, sin),
            RotateCorner(left + width, top + height, centerX, centerY, cos, sin),
            RotateCorner(left, top + height, centerX, centerY, cos, sin),
        };

        double minX = corners.Min(point => point.X);
        double minY = corners.Min(point => point.Y);
        double maxX = corners.Max(point => point.X);
        double maxY = corners.Max(point => point.Y);
        return DipBoundsToScreen(minX, minY, maxX - minX, maxY - minY, transform);
    }

    public static SlideScreenRect ShapeVisualBoundsToScreen(
        SlideShape shape,
        Slide slide,
        Presentation presentation,
        SlideTransformCore transform)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(slide);
        ArgumentNullException.ThrowIfNull(presentation);

        var bounds = ShapeHitTester.GetShapeBoundsDip(shape, slide, presentation);
        return OrientedBoundsToScreen(
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            shape.RotationDeg,
            transform);
    }

    public static SlideScreenRect? ShapeBoundsToScreen(
        Slide slide,
        Presentation presentation,
        uint shapeId,
        SlideTransformCore transform)
    {
        ArgumentNullException.ThrowIfNull(slide);
        ArgumentNullException.ThrowIfNull(presentation);

        var shape = ShapeHitTester.FindShape(slide, shapeId);
        return shape is null
            ? null
            : ShapeBoundsToScreen(shape, slide, presentation, transform);
    }

    public static SlideScreenRect ShapeBoundsToScreen(
        SlideShape shape,
        Slide slide,
        Presentation presentation,
        SlideTransformCore transform)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(slide);
        ArgumentNullException.ThrowIfNull(presentation);

        return DipBoundsToScreen(ShapeHitTester.GetShapeBoundsDip(shape, slide, presentation), transform);
    }

    public static SlideScreenRect ScreenRectBetween(
        CanvasGesturePoint start,
        CanvasGesturePoint current)
    {
        double left = Math.Min(start.X, current.X);
        double top = Math.Min(start.Y, current.Y);
        double right = Math.Max(start.X, current.X);
        double bottom = Math.Max(start.Y, current.Y);
        return new SlideScreenRect(left, top, right - left, bottom - top);
    }

    public static InCanvasEditorPlacement PlanEditorPlacement(
        SlideScreenRect screenRect,
        double minimumWidth,
        double minimumHeight,
        double rotationDegrees = 0,
        bool flipHorizontal = false,
        bool flipVertical = false)
    {
        var placement = new InCanvasEditorPlacement(
            screenRect.Left,
            screenRect.Top,
            Math.Max(minimumWidth, screenRect.Width),
            Math.Max(minimumHeight, screenRect.Height))
        {
            RotationDegrees = rotationDegrees,
            FlipHorizontal = flipHorizontal,
            FlipVertical = flipVertical,
        };
        if (placement.HasTransform)
        {
            placement = placement with
            {
                TransformOriginX = screenRect.Width / 2,
                TransformOriginY = screenRect.Height / 2,
            };
        }
        return placement;
    }

    /// <summary>
    /// Plans a cell editor in the transformed table frame. The editor keeps the cell's
    /// local width and height, while its center is moved by the table-frame transform;
    /// both hosts then apply the same local rotation/flip to the control.
    /// </summary>
    public static InCanvasEditorPlacement PlanTableCellEditorPlacement(
        CellRectDip cellRect,
        ShapeBoundsDip tableBounds,
        SlideTransformCore transform,
        double minimumWidth,
        double minimumHeight,
        double rotationDegrees,
        bool flipHorizontal,
        bool flipVertical)
    {
        ArgumentNullException.ThrowIfNull(transform);
        ArgumentOutOfRangeException.ThrowIfNegative(minimumWidth);
        ArgumentOutOfRangeException.ThrowIfNegative(minimumHeight);

        var cellScreen = DipBoundsToScreen(cellRect, transform);
        var tableScreen = DipBoundsToScreen(tableBounds, transform);
        var cellCenter = (X: cellScreen.Left + cellScreen.Width / 2.0,
            Y: cellScreen.Top + cellScreen.Height / 2.0);
        var tableCenter = (X: tableScreen.Left + tableScreen.Width / 2.0,
            Y: tableScreen.Top + tableScreen.Height / 2.0);
        var transformedCenter = ShapeTransformPlanner.TransformPoint(
            tableCenter.X,
            tableCenter.Y,
            cellCenter.X,
            cellCenter.Y,
            rotationDegrees,
            flipHorizontal,
            flipVertical);

        double width = Math.Max(minimumWidth, cellScreen.Width);
        double height = Math.Max(minimumHeight, cellScreen.Height);
        var placement = new InCanvasEditorPlacement(
            transformedCenter.X - width / 2.0,
            transformedCenter.Y - height / 2.0,
            width,
            height)
        {
            RotationDegrees = rotationDegrees,
            FlipHorizontal = flipHorizontal,
            FlipVertical = flipVertical,
        };
        return placement.HasTransform
            ? placement with
            {
                TransformOriginX = width / 2.0,
                TransformOriginY = height / 2.0,
            }
            : placement;
    }

    public static SlideScreenRect? Union(IEnumerable<SlideScreenRect> rects)
    {
        ArgumentNullException.ThrowIfNull(rects);

        using var enumerator = rects.GetEnumerator();
        if (!enumerator.MoveNext())
            return null;

        double left = enumerator.Current.Left;
        double top = enumerator.Current.Top;
        double right = enumerator.Current.Right;
        double bottom = enumerator.Current.Bottom;

        while (enumerator.MoveNext())
        {
            var rect = enumerator.Current;
            left = Math.Min(left, rect.Left);
            top = Math.Min(top, rect.Top);
            right = Math.Max(right, rect.Right);
            bottom = Math.Max(bottom, rect.Bottom);
        }

        return new SlideScreenRect(left, top, right - left, bottom - top);
    }

    public static double SnapGuideToScreenPosition(SnapGuideLine guide, SlideTransformCore transform)
    {
        ArgumentNullException.ThrowIfNull(transform);

        var point = guide.IsHorizontal
            ? transform.SlideToScreen(0, guide.Position)
            : transform.SlideToScreen(guide.Position, 0);
        return guide.IsHorizontal ? point.Y : point.X;
    }

    private static (double X, double Y) RotateCorner(
        double x,
        double y,
        double centerX,
        double centerY,
        double cos,
        double sin)
    {
        double dx = x - centerX;
        double dy = y - centerY;
        return (centerX + dx * cos - dy * sin, centerY + dx * sin + dy * cos);
    }
}

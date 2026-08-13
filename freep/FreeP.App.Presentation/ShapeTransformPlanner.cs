using Free.Shared.Drawing;

namespace FreeP.App.Compositor;

public readonly record struct ShapeAffineTransform(
    double M11,
    double M12,
    double M21,
    double M22,
    double OffsetX,
    double OffsetY)
{
    public static ShapeAffineTransform Identity { get; } = new(1, 0, 0, 1, 0, 0);

    public bool IsIdentity =>
        M11 == 1 && M12 == 0 &&
        M21 == 0 && M22 == 1 &&
        OffsetX == 0 && OffsetY == 0;

    public ShapeAffineTransform Append(ShapeAffineTransform next) => new(
        M11 * next.M11 + M12 * next.M21,
        M11 * next.M12 + M12 * next.M22,
        M21 * next.M11 + M22 * next.M21,
        M21 * next.M12 + M22 * next.M22,
        OffsetX * next.M11 + OffsetY * next.M21 + next.OffsetX,
        OffsetX * next.M12 + OffsetY * next.M22 + next.OffsetY);
}

public static class ShapeTransformPlanner
{
    public static (double X, double Y) TransformPoint(
        double centerX,
        double centerY,
        double pointX,
        double pointY,
        double rotationDeg,
        bool flipH,
        bool flipV)
    {
        double x = pointX - centerX;
        double y = pointY - centerY;
        if (flipH) x = -x;
        if (flipV) y = -y;

        double radians = rotationDeg * Math.PI / 180.0;
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);
        return (
            centerX + cos * x - sin * y,
            centerY + sin * x + cos * y);
    }

    public static (double X, double Y) InverseTransformPoint(
        double centerX,
        double centerY,
        double pointX,
        double pointY,
        double rotationDeg,
        bool flipH,
        bool flipV)
    {
        double x = pointX - centerX;
        double y = pointY - centerY;
        double radians = -rotationDeg * Math.PI / 180.0;
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);
        double unrotatedX = cos * x - sin * y;
        double unrotatedY = sin * x + cos * y;
        if (flipH) unrotatedX = -unrotatedX;
        if (flipV) unrotatedY = -unrotatedY;
        return (centerX + unrotatedX, centerY + unrotatedY);
    }

    public static ShapeAffineTransform PlanShapeTransform(DrawOp.Shape shape) =>
        PlanShapeTransform(shape.BoundsDip, shape.RotationDeg, shape.FlipH, shape.FlipV);

    public static ShapeAffineTransform PlanPictureTransform(DrawOp.Picture picture) =>
        PlanShapeTransform(picture.DestDip, picture.RotationDeg, picture.FlipH, picture.FlipV);

    /// <summary>
    /// Produces the single transform used to render a shape, including its ordinary
    /// DrawingML transform and any supported 3-D scene-camera projection.
    /// </summary>
    public static ShapeAffineTransform PlanShapeRenderTransform(DrawOp.Shape shape) =>
        PlanShapeTransform(shape).Append(
            Scene3dProjectionPlanner.Plan(shape.BoundsDip, shape.Effects?.Scene3dCameraPreset));

    public static ShapeAffineTransform PlanShapeTransform(
        LayoutRect bounds,
        double rotationDeg,
        bool flipH,
        bool flipV)
    {
        double cx = bounds.X + bounds.Width / 2;
        double cy = bounds.Y + bounds.Height / 2;
        var transform = ShapeAffineTransform.Identity;

        if (flipH)
            transform = transform.Append(new ShapeAffineTransform(-1, 0, 0, 1, cx * 2, 0));

        if (flipV)
            transform = transform.Append(new ShapeAffineTransform(1, 0, 0, -1, 0, cy * 2));

        if (rotationDeg != 0)
            transform = transform.Append(CreateRotationAt(rotationDeg, cx, cy));

        return transform;
    }

    private static ShapeAffineTransform CreateRotationAt(double angleDeg, double cx, double cy)
    {
        double rad = angleDeg * Math.PI / 180.0;
        double cos = Math.Cos(rad);
        double sin = Math.Sin(rad);

        return new ShapeAffineTransform(
            cos,
            sin,
            -sin,
            cos,
            cx - cx * cos + cy * sin,
            cy - cx * sin - cy * cos);
    }
}

/// <summary>
/// Conservative 2-D projections for DrawingML scene camera presets. This keeps
/// camera interpretation shared by the WPF and Avalonia renderers while leaving
/// unsupported presets as their existing front-on rendering.
/// </summary>
public static class Scene3dProjectionPlanner
{
    public static ShapeAffineTransform Plan(LayoutRect bounds, string? cameraPreset)
    {
        if (!string.Equals(cameraPreset, "isometricTopUp", StringComparison.OrdinalIgnoreCase))
            return ShapeAffineTransform.Identity;

        // PowerPoint's isometricTopUp camera projects the local X axis down-right
        // and the local Y axis down-left at a 120-degree angle. This oblique basis
        // is deliberately not expressible as a simple rotation plus scale.
        const double xAxisX = 0.505;
        const double xAxisY = 0.2925;
        const double yAxisX = -1.015;
        const double yAxisY = 0.588;

        double cx = bounds.X + bounds.Width / 2.0;
        double cy = bounds.Y + bounds.Height / 2.0;
        var toOrigin = new ShapeAffineTransform(1, 0, 0, 1, -cx, -cy);
        var projection = new ShapeAffineTransform(xAxisX, xAxisY, yAxisX, yAxisY, 0, 0);
        var fromOrigin = new ShapeAffineTransform(1, 0, 0, 1, cx, cy);

        return toOrigin.Append(projection).Append(fromOrigin);
    }
}

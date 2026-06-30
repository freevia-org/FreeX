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
    public static ShapeAffineTransform PlanShapeTransform(DrawOp.Shape shape) =>
        PlanShapeTransform(shape.BoundsDip, shape.RotationDeg, shape.FlipH, shape.FlipV);

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

using FreeP.Core.Model;
using FreeP.App.Compositor;

namespace FreeP.App.Rendering.Wpf;

/// <summary>
/// Hit-tests a slide-space point against the shapes on a slide, honouring z-order
/// (topmost shape wins).
///
/// This type is framework-free so it can be unit-tested without STA or a live window.
/// It works in slide DIP coordinates — callers convert screen→slide with <see cref="SlideTransform"/>
/// before calling.
/// </summary>
public static class ShapeHitTester
{
    // 1 EMU = 1/9525 DIP
    private const double EmuPerDip = 9525.0;

    /// <summary>
    /// Returns the id of the topmost shape whose axis-aligned bounding box contains
    /// <paramref name="slidePtX"/>,<paramref name="slidePtY"/> (slide DIP coords), or null if none.
    /// Z-order: the last shape in the list is topmost (painter order = back-to-front).
    /// </summary>
    public static uint? HitTest(
        Slide slide,
        Presentation presentation,
        double slidePtX,
        double slidePtY)
    {
        // Iterate in reverse z-order (topmost first).
        for (int i = slide.Shapes.Count - 1; i >= 0; i--)
        {
            var shape = slide.Shapes[i];
            if (HitTestShape(shape, presentation, slidePtX, slidePtY))
                return shape.Id;
        }
        return null;
    }

    /// <summary>
    /// Returns all shape ids whose bounding boxes intersect the given marquee rectangle (slide DIP coords).
    /// Result is in z-order (back-to-front).
    /// </summary>
    public static IReadOnlyList<uint> MarqueeHitTest(
        Slide slide,
        Presentation presentation,
        double left, double top, double right, double bottom)
    {
        // Normalise
        double l = Math.Min(left, right);
        double r = Math.Max(left, right);
        double t = Math.Min(top, bottom);
        double b = Math.Max(top, bottom);

        var result = new List<uint>();
        foreach (var shape in slide.Shapes)
        {
            var bounds = GetShapeBoundsDip(shape, presentation);
            // Intersects when both axes overlap
            if (bounds.Right > l && bounds.Left < r &&
                bounds.Bottom > t && bounds.Top < b)
                result.Add(shape.Id);
        }
        return result;
    }

    // ── Bounds helpers ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the axis-aligned bounding box of a shape in slide DIP coords,
    /// respecting placeholder inheritance (uses OffsetX/Y/ExtentCx/Cy; groups use child union).
    /// Does NOT apply rotation (uses AABB for simplicity — good enough for hit-testing).
    /// </summary>
    public static ShapeBoundsDip GetShapeBoundsDip(SlideShape shape, Presentation presentation)
    {
        // Placeholder resolution: defer to PlaceholderResolver.
        var anchor = PlaceholderResolver.ResolveAnchor(shape, presentation);
        double x  = anchor.OffsetXEmu / EmuPerDip;
        double y  = anchor.OffsetYEmu / EmuPerDip;
        double cx = anchor.ExtentCxEmu / EmuPerDip;
        double cy = anchor.ExtentCyEmu / EmuPerDip;
        return new ShapeBoundsDip(x, y, cx, cy);
    }

    // ── Internal ────────────────────────────────────────────────────────────────────────────────

    private static bool HitTestShape(SlideShape shape, Presentation presentation,
                                      double px, double py)
    {
        var b = GetShapeBoundsDip(shape, presentation);

        // AD4: Un-rotate the test point into the shape's local (axis-aligned) frame before
        // comparing against the AABB.  For a 0° shape this is a no-op.
        if (shape.RotationDeg != 0)
        {
            double cx = b.Left + b.Width  / 2.0;
            double cy = b.Top  + b.Height / 2.0;
            (px, py)  = SlideTransformCore.UnRotatePoint(px, py, cx, cy, shape.RotationDeg);
        }

        return px >= b.Left && px <= b.Right && py >= b.Top && py <= b.Bottom;
    }
}

/// <summary>
/// Axis-aligned bounding box of a shape in slide DIP coordinates.
/// </summary>
public readonly struct ShapeBoundsDip
{
    public double Left   { get; }
    public double Top    { get; }
    public double Width  { get; }
    public double Height { get; }

    public double Right  => Left + Width;
    public double Bottom => Top  + Height;

    public ShapeBoundsDip(double left, double top, double width, double height)
    {
        Left   = left;
        Top    = top;
        Width  = width;
        Height = height;
    }
}

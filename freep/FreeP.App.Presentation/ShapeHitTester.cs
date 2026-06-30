using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Hit-tests a slide-space point against the shapes on a slide, honouring z-order
/// (topmost shape wins).
///
/// This type is framework-free so it can be unit-tested without STA or a live window.
/// It works in slide DIP coordinates; callers convert screen to slide with
/// <see cref="SlideTransformCore"/> before calling.
///
/// This shared implementation is used by both the WPF and Avalonia renderers.
/// </summary>
public static class ShapeHitTester
{
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
        var point = new LayoutPoint(slidePtX, slidePtY);
        for (var i = slide.Shapes.Count - 1; i >= 0; i--)
        {
            var shape = slide.Shapes[i];
            var bounds = GetShapeBoundsDip(shape, presentation).ToLayoutRect();
            if (DrawingBoundsHitTester.Contains(bounds, point, shape.RotationDeg))
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
        double left,
        double top,
        double right,
        double bottom)
    {
        var marquee = DrawingObjectInteractionPlanner.NormalizeRect(left, top, right, bottom);
        var result = new List<uint>();
        foreach (var shape in slide.Shapes)
        {
            if (DrawingObjectInteractionPlanner.Intersects(
                GetShapeBoundsDip(shape, presentation).ToLayoutRect(),
                marquee))
            {
                result.Add(shape.Id);
            }
        }

        return result;
    }

    /// <summary>
    /// Returns the axis-aligned bounding box of a shape in slide DIP coords,
    /// respecting placeholder inheritance (uses OffsetX/Y/ExtentCx/Cy; groups use child union).
    /// Does NOT apply rotation (uses AABB for simplicity - good enough for hit-testing).
    /// </summary>
    public static ShapeBoundsDip GetShapeBoundsDip(SlideShape shape, Presentation presentation)
    {
        var anchor = PlaceholderResolver.ResolveAnchor(shape, presentation);
        return new ShapeBoundsDip(
            DrawingMlCoordinateUnits.EmuToPixels(anchor.OffsetXEmu),
            DrawingMlCoordinateUnits.EmuToPixels(anchor.OffsetYEmu),
            DrawingMlCoordinateUnits.EmuToPixels(anchor.ExtentCxEmu),
            DrawingMlCoordinateUnits.EmuToPixels(anchor.ExtentCyEmu));
    }

}

/// <summary>
/// Axis-aligned bounding box of a shape in slide DIP coordinates.
/// </summary>
public readonly struct ShapeBoundsDip
{
    public double Left { get; }
    public double Top { get; }
    public double Width { get; }
    public double Height { get; }

    public double Right => Left + Width;
    public double Bottom => Top + Height;

    public ShapeBoundsDip(double left, double top, double width, double height)
    {
        Left = left;
        Top = top;
        Width = width;
        Height = height;
    }

    public LayoutRect ToLayoutRect() => new(Left, Top, Width, Height);
}

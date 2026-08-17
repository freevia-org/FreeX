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
    /// Returns the child-index path for a shape id. The first index addresses the slide's
    /// top-level shape list; subsequent indexes address group children.
    /// </summary>
    public static IReadOnlyList<int>? FindShapePath(Slide slide, uint shapeId)
    {
        ArgumentNullException.ThrowIfNull(slide);
        var path = new List<int>();
        return TryFindShapePath(slide.Shapes, shapeId, path) ? path.ToArray() : null;
    }

    /// <summary>Resolves a previously captured slide/group child path.</summary>
    public static SlideShape? ResolveShapePath(Slide slide, IReadOnlyList<int> path)
    {
        ArgumentNullException.ThrowIfNull(slide);
        ArgumentNullException.ThrowIfNull(path);
        if (path.Count == 0)
            return null;

        IReadOnlyList<SlideShape> shapes = slide.Shapes;
        SlideShape? current = null;
        foreach (var index in path)
        {
            if (index < 0 || index >= shapes.Count)
                return null;

            current = shapes[index];
            shapes = current.Children;
        }

        return current;
    }

    /// <summary>Finds a shape and its shared slide/group child path in one traversal.</summary>
    public static bool TryFindShape(
        Slide slide,
        uint shapeId,
        out SlideShape? shape,
        out IReadOnlyList<int>? path)
    {
        ArgumentNullException.ThrowIfNull(slide);
        var indexes = new List<int>();
        if (TryFindShape(slide.Shapes, shapeId, indexes, out shape))
        {
            path = indexes.ToArray();
            return true;
        }

        path = null;
        shape = null;
        return false;
    }

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
            // A hidden shape (Selection Pane eye-icon toggle) is never drawn - see
            // SlideCompositor.ComposeShape's `if (shape.IsHidden) return;` guard, which also
            // skips composing the shape's children. Hit-testing must skip it (and its subtree)
            // the same way, or an invisible shape silently steals clicks meant for whatever is
            // actually visible underneath it.
            if (shape.IsHidden)
                continue;

            var childHit = HitTestChildren(shape.Children, slide, presentation, point);
            if (childHit.HasValue)
                return childHit;

            var bounds = GetShapeBoundsDip(shape, slide, presentation).ToLayoutRect();
            if (DrawingBoundsHitTester.Contains(bounds, point, shape.RotationDeg))
                return shape.Id;
        }

        return null;
    }

    /// <summary>
    /// Finds a shape by id, including descendants of grouped shapes.
    /// Group children use the same absolute slide coordinate space as their parent.
    /// </summary>
    public static SlideShape? FindShape(Slide slide, uint shapeId)
    {
        ArgumentNullException.ThrowIfNull(slide);
        return TryFindShape(slide, shapeId, out var shape, out _) ? shape : null;
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
            // Same rationale as HitTest: a hidden shape is never rendered, so a drag-select
            // marquee must not pick it up either.
            if (shape.IsHidden)
                continue;

            if (DrawingObjectInteractionPlanner.Intersects(
                GetShapeBoundsDip(shape, slide, presentation).ToLayoutRect(),
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
    public static ShapeBoundsDip GetShapeBoundsDip(SlideShape shape, Slide slide, Presentation presentation)
    {
        var anchor = PlaceholderResolver.ResolveAnchor(shape, slide, presentation);
        return new ShapeBoundsDip(
            DrawingMlCoordinateUnits.EmuToPixels(anchor.OffsetXEmu),
            DrawingMlCoordinateUnits.EmuToPixels(anchor.OffsetYEmu),
            DrawingMlCoordinateUnits.EmuToPixels(anchor.ExtentCxEmu),
            DrawingMlCoordinateUnits.EmuToPixels(anchor.ExtentCyEmu));
    }

    /// <summary>Returns bounds for a top-level or grouped-child shape id.</summary>
    public static ShapeBoundsDip? GetShapeBoundsDip(
        Slide slide,
        Presentation presentation,
        uint shapeId)
    {
        var shape = FindShape(slide, shapeId);
        return shape is null ? null : GetShapeBoundsDip(shape, slide, presentation);
    }

    private static uint? HitTestChildren(
        IReadOnlyList<SlideShape> children,
        Slide slide,
        Presentation presentation,
        LayoutPoint point)
    {
        for (var i = children.Count - 1; i >= 0; i--)
        {
            var child = children[i];
            if (child.IsHidden)
                continue;

            var descendantHit = HitTestChildren(child.Children, slide, presentation, point);
            if (descendantHit.HasValue)
                return descendantHit;

            if (DrawingBoundsHitTester.Contains(
                    GetShapeBoundsDip(child, slide, presentation).ToLayoutRect(),
                    point,
                    child.RotationDeg))
            {
                return child.Id;
            }
        }

        return null;
    }

    private static SlideShape? FindShape(IEnumerable<SlideShape> shapes, uint shapeId)
    {
        foreach (var shape in shapes)
        {
            if (shape.Id == shapeId)
                return shape;

            if (shape.Children.Count > 0 && FindShape(shape.Children, shapeId) is { } child)
                return child;
        }

        return null;
    }

    private static bool TryFindShapePath(
        IReadOnlyList<SlideShape> shapes,
        uint shapeId,
        List<int> path)
    {
        for (var index = 0; index < shapes.Count; index++)
        {
            path.Add(index);
            if (shapes[index].Id == shapeId || TryFindShapePath(shapes[index].Children, shapeId, path))
                return true;
            path.RemoveAt(path.Count - 1);
        }

        return false;
    }

    private static bool TryFindShape(
        IReadOnlyList<SlideShape> shapes,
        uint shapeId,
        List<int> path,
        out SlideShape? shape)
    {
        for (var index = 0; index < shapes.Count; index++)
        {
            var candidate = shapes[index];
            path.Add(index);
            if (candidate.Id == shapeId)
            {
                shape = candidate;
                return true;
            }

            if (TryFindShape(candidate.Children, shapeId, path, out shape))
                return true;

            path.RemoveAt(path.Count - 1);
        }

        shape = null;
        return false;
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

namespace FreeP.Core.Model;

/// <summary>Traverses the complete shape tree of a slide in depth-first order.</summary>
public static class SlideShapeTraversal
{
    public static SlideShape? FindById(Slide slide, uint shapeId) =>
        FindById(slide.Shapes, shapeId);

    public static SlideShape? FindById(IEnumerable<SlideShape> shapes, uint shapeId)
    {
        foreach (var shape in shapes)
        {
            if (shape.Id == shapeId)
                return shape;

            if (shape.Children.Count > 0 && FindById(shape.Children, shapeId) is { } child)
                return child;
        }

        return null;
    }

    public static IEnumerable<SlideShape> EnumerateDepthFirst(Slide slide) =>
        EnumerateDepthFirst(slide.Shapes);

    public static IEnumerable<SlideShape> EnumerateDepthFirst(SlideShape shape)
    {
        yield return shape;
        foreach (var descendant in EnumerateDepthFirst(shape.Children))
            yield return descendant;
    }

    public static IEnumerable<SlideShape> EnumerateDepthFirst(IEnumerable<SlideShape> shapes)
    {
        foreach (var shape in shapes)
        {
            foreach (var item in EnumerateDepthFirst(shape))
                yield return item;
        }
    }

    /// <summary>
    /// All descendants of <paramref name="shape"/> at every depth (its children, their children,
    /// and so on) -- not including <paramref name="shape"/> itself.
    /// </summary>
    public static IEnumerable<SlideShape> EnumerateDescendants(SlideShape shape) =>
        EnumerateDepthFirst(shape.Children);

    /// <summary>
    /// Shifts <paramref name="shape"/> and every descendant by the same EMU delta. Group children
    /// store their offset in absolute (slide-space) coordinates, not relative to the group (see
    /// GroupShapesCommand.Apply), so any code that repositions a shape -- moving it, applying a
    /// paste offset, etc. -- must translate every descendant by the same amount or the group's
    /// stored bounds and its members' rendered positions fall out of sync. Shared by
    /// <c>MoveShapeCommand</c> and <c>EditingSession</c>'s paste path so a future call site cannot
    /// reintroduce a top-level-only offset that leaves group children behind.
    /// </summary>
    public static void TranslateWithDescendants(SlideShape shape, long dxEmu, long dyEmu)
    {
        shape.OffsetXEmu += dxEmu;
        shape.OffsetYEmu += dyEmu;
        foreach (var descendant in EnumerateDescendants(shape))
        {
            descendant.OffsetXEmu += dxEmu;
            descendant.OffsetYEmu += dyEmu;
        }
    }

}

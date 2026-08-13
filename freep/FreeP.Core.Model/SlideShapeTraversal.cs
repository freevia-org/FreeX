namespace FreeP.Core.Model;

/// <summary>Traverses the complete shape tree of a slide in depth-first order.</summary>
public static class SlideShapeTraversal
{
    public static SlideShape? FindById(Slide slide, uint shapeId) =>
        FindById(slide.Shapes, shapeId);

    public static IEnumerable<SlideShape> EnumerateDepthFirst(Slide slide) =>
        EnumerateDepthFirst(slide.Shapes);

    private static SlideShape? FindById(IEnumerable<SlideShape> shapes, uint shapeId)
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

    private static IEnumerable<SlideShape> EnumerateDepthFirst(IEnumerable<SlideShape> shapes)
    {
        foreach (var shape in shapes)
        {
            yield return shape;
            foreach (var child in EnumerateDepthFirst(shape.Children))
                yield return child;
        }
    }
}

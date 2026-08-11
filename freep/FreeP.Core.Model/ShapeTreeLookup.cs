namespace FreeP.Core.Model;

/// <summary>Resolves interactions against the complete slide shape tree.</summary>
public static class ShapeTreeLookup
{
    public static SlideShape? Find(Slide slide, uint shapeId) =>
        Find(slide.Shapes, shapeId);

    public static IEnumerable<SlideShape> Enumerate(Slide slide) =>
        Enumerate(slide.Shapes);

    private static SlideShape? Find(IEnumerable<SlideShape> shapes, uint shapeId)
    {
        foreach (var shape in shapes)
        {
            if (shape.Id == shapeId)
                return shape;

            if (shape.Children.Count > 0 && Find(shape.Children, shapeId) is { } child)
                return child;
        }

        return null;
    }

    private static IEnumerable<SlideShape> Enumerate(IEnumerable<SlideShape> shapes)
    {
        foreach (var shape in shapes)
        {
            yield return shape;
            foreach (var child in Enumerate(shape.Children))
                yield return child;
        }
    }
}

using Free.Shared.Drawing;

namespace FreeP.App.Compositor;

/// <summary>Common Change Shape command ids and the bounded preset menu shared by both hosts.</summary>
public static class ShapeChangePlanner
{
    public const string MenuCommandId = "freep.arrange.change-shape";
    public const string RectangleCommandId = "freep.arrange.change-shape.rectangle";
    public const string EllipseCommandId = "freep.arrange.change-shape.ellipse";
    public const string TriangleCommandId = "freep.arrange.change-shape.triangle";
    public const string DiamondCommandId = "freep.arrange.change-shape.diamond";
    public const string RightArrowCommandId = "freep.arrange.change-shape.right-arrow";
    public const string HexagonCommandId = "freep.arrange.change-shape.hexagon";
    public const string Star5CommandId = "freep.arrange.change-shape.star5";

    public static IReadOnlyList<(string CommandId, DrawingShapeKind Kind)> Presets =>
    [
        (RectangleCommandId, DrawingShapeKind.Rectangle),
        (EllipseCommandId, DrawingShapeKind.Ellipse),
        (TriangleCommandId, DrawingShapeKind.Triangle),
        (DiamondCommandId, DrawingShapeKind.Diamond),
        (RightArrowCommandId, DrawingShapeKind.RightArrow),
        (HexagonCommandId, DrawingShapeKind.Hexagon),
        (Star5CommandId, DrawingShapeKind.Star5),
    ];
}

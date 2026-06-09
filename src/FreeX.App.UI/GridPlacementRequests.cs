using FreeX.Core.Model;

namespace FreeX.App.UI;

public readonly record struct ShapePlacementRequest(
    DrawingShapeKind Kind,
    CellAddress Anchor,
    double Width,
    double Height);

public readonly record struct TextBoxPlacementRequest(
    CellAddress Anchor,
    double Width,
    double Height);

using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// UI-free factory that turns a chosen shape kind + anchor into the Core <see cref="AddDrawingShapeCommand"/>
/// the shell runs to place a drawing shape, plus the small gallery of common shapes the Insert menu offers.
/// Kept portable (no Avalonia types) so placement defaults and the catalog are unit testable; the Avalonia
/// drawing overlay already renders <see cref="DrawingShapeModel"/>s and supports move/resize/rotate.
/// </summary>
internal static class InsertShapeCommandFactory
{
    public const double DefaultWidth = 120d;
    public const double DefaultHeight = 70d;

    /// <summary>The shape the bare "Shapes" ribbon button inserts.</summary>
    public const DrawingShapeKind DefaultShape = DrawingShapeKind.Rectangle;

    /// <summary>A common shape offered in the Insert ▸ Shape menu.</summary>
    internal sealed record ShapeCatalogItem(DrawingShapeKind Kind, string Label);

    /// <summary>The common shapes the Insert ▸ Shape menu lists, in display order.</summary>
    public static IReadOnlyList<ShapeCatalogItem> Catalog { get; } =
    [
        new(DrawingShapeKind.Rectangle, "Rectangle"),
        new(DrawingShapeKind.RoundedRectangle, "Rounded Rectangle"),
        new(DrawingShapeKind.Ellipse, "Oval"),
        new(DrawingShapeKind.Line, "Line"),
        new(DrawingShapeKind.Triangle, "Triangle"),
        new(DrawingShapeKind.RightTriangle, "Right Triangle"),
        new(DrawingShapeKind.Diamond, "Diamond"),
        new(DrawingShapeKind.Pentagon, "Pentagon"),
        new(DrawingShapeKind.Hexagon, "Hexagon"),
        new(DrawingShapeKind.RightArrow, "Right Arrow"),
        new(DrawingShapeKind.LeftRightArrow, "Left-Right Arrow"),
        new(DrawingShapeKind.Star5, "5-Point Star"),
    ];

    /// <summary>
    /// Builds the <see cref="AddDrawingShapeCommand"/> placing a <paramref name="kind"/> shape at
    /// <paramref name="anchor"/> with the default size. Line-like kinds drop their fill (handled by Core).
    /// </summary>
    public static AddDrawingShapeCommand Build(SheetId sheetId, CellAddress anchor, DrawingShapeKind kind) =>
        new(sheetId, anchor, kind, DefaultWidth, DefaultHeight);
}

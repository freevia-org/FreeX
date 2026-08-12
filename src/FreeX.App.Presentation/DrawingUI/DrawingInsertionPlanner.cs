using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.DrawingUI;

public sealed record DrawingShapeGalleryItem(DrawingShapeKind Kind, string Label, string KeyTip);

public sealed record DrawingShapeGalleryMenuItem(DrawingShapeKind Kind, string Label, string KeyTip);

public sealed record DrawingShapeGalleryGroup(string Label, string KeyTip, IReadOnlyList<DrawingShapeGalleryItem> Items)
{
    public IReadOnlyList<DrawingShapeGalleryMenuItem> MenuItems { get; } =
        Items.Select(item => new DrawingShapeGalleryMenuItem(item.Kind, item.Label, KeyTip + item.KeyTip)).ToArray();
}

/// <summary>
/// Portable insertion catalog and command factory for drawing shapes and text boxes. Renderers own menus,
/// selection, focus and post-insert editing; this planner owns the shared gallery order and default commands.
/// </summary>
public static class DrawingInsertionPlanner
{
    public const double DefaultShapeWidth = 120d;
    public const double DefaultShapeHeight = 70d;
    public const double DefaultTextBoxWidth = TextBoxModel.DefaultWidth;
    public const double DefaultTextBoxHeight = TextBoxModel.DefaultHeight;
    public const string TextBoxPlaceholder = "Text Box";

    public const DrawingShapeKind DefaultShape = DrawingShapeKind.Rectangle;

    public static IReadOnlyList<DrawingShapeGalleryGroup> ShapeGroups { get; } =
    [
        new(
            "Lines",
            "1",
            [
                new(DrawingShapeKind.Line, "Line", "L"),
                new(DrawingShapeKind.ElbowConnector, "Elbow Connector", "E"),
                new(DrawingShapeKind.CurvedConnector, "Curved Connector", "C")
            ]),
        new(
            "Rectangles",
            "2",
            [
                new(DrawingShapeKind.Rectangle, "Rectangle", "R"),
                new(DrawingShapeKind.RoundedRectangle, "Rounded Rectangle", "O")
            ]),
        new(
            "Basic Shapes",
            "3",
            [
                new(DrawingShapeKind.Ellipse, "Oval", "O"),
                new(DrawingShapeKind.Triangle, "Triangle", "T"),
                new(DrawingShapeKind.RightTriangle, "Right Triangle", "R"),
                new(DrawingShapeKind.Diamond, "Diamond", "D"),
                new(DrawingShapeKind.Parallelogram, "Parallelogram", "P"),
                new(DrawingShapeKind.Trapezoid, "Trapezoid", "Z"),
                new(DrawingShapeKind.Pentagon, "Pentagon", "N"),
                new(DrawingShapeKind.Hexagon, "Hexagon", "H"),
                new(DrawingShapeKind.Octagon, "Octagon", "G"),
                new(DrawingShapeKind.Cross, "Cross", "X")
            ]),
        new(
            "Block Arrows",
            "4",
            [
                new(DrawingShapeKind.RightArrow, "Right Arrow", "R"),
                new(DrawingShapeKind.LeftArrow, "Left Arrow", "L"),
                new(DrawingShapeKind.UpArrow, "Up Arrow", "U"),
                new(DrawingShapeKind.DownArrow, "Down Arrow", "D"),
                new(DrawingShapeKind.LeftRightArrow, "Left-Right Arrow", "H"),
                new(DrawingShapeKind.UpDownArrow, "Up-Down Arrow", "V")
            ]),
        new(
            "Equation Shapes",
            "5",
            [
                new(DrawingShapeKind.PlusSign, "Plus", "P"),
                new(DrawingShapeKind.MinusSign, "Minus", "M"),
                new(DrawingShapeKind.MultiplySign, "Multiply", "X"),
                new(DrawingShapeKind.DivideSign, "Divide", "D"),
                new(DrawingShapeKind.EqualSign, "Equal", "E"),
                new(DrawingShapeKind.NotEqualSign, "Not Equal", "N")
            ]),
        new(
            "Flowchart",
            "6",
            [
                new(DrawingShapeKind.FlowchartProcess, "Process", "P"),
                new(DrawingShapeKind.FlowchartDecision, "Decision", "D"),
                new(DrawingShapeKind.FlowchartData, "Data", "A"),
                new(DrawingShapeKind.FlowchartPredefinedProcess, "Predefined Process", "R"),
                new(DrawingShapeKind.FlowchartDocument, "Document", "O"),
                new(DrawingShapeKind.FlowchartTerminator, "Terminator", "T")
            ]),
        new(
            "Stars and Banners",
            "7",
            [
                new(DrawingShapeKind.Star5, "5-Point Star", "5"),
                new(DrawingShapeKind.Star8, "8-Point Star", "8"),
                new(DrawingShapeKind.Explosion, "Explosion", "E"),
                new(DrawingShapeKind.Ribbon, "Ribbon", "R"),
                new(DrawingShapeKind.Wave, "Wave", "W")
            ]),
        new(
            "Callouts",
            "8",
            [
                new(DrawingShapeKind.RectangularCallout, "Rectangular Callout", "R"),
                new(DrawingShapeKind.RoundedRectangularCallout, "Rounded Rectangular Callout", "O"),
                new(DrawingShapeKind.OvalCallout, "Oval Callout", "V"),
                new(DrawingShapeKind.LineCallout, "Line Callout", "L")
            ])
    ];

    public static IEnumerable<DrawingShapeGalleryItem> ShapeItems =>
        ShapeGroups.SelectMany(group => group.Items);

    public static string GetRibbonCommandId(DrawingShapeKind kind) => $"insert.shape.{kind}";

    public static AddDrawingShapeCommand BuildShapeCommand(
        SheetId sheetId,
        CellAddress anchor,
        DrawingShapeKind kind,
        double width = DefaultShapeWidth,
        double height = DefaultShapeHeight,
        CellColor? fillColor = null,
        CellColor? outlineColor = null,
        bool hasFill = true) =>
        new(sheetId, anchor, kind, width, height, fillColor, outlineColor, hasFill);

    public static AddTextBoxCommand BuildTextBoxCommand(
        SheetId sheetId,
        CellAddress anchor,
        string? text = null,
        double width = DefaultTextBoxWidth,
        double height = DefaultTextBoxHeight) =>
        new(sheetId, anchor, NormalizeTextBoxText(text), width, height);

    public static AddTextBoxCommand BuildInlineEditTextBoxCommand(
        SheetId sheetId,
        CellAddress anchor,
        double width = DefaultTextBoxWidth,
        double height = DefaultTextBoxHeight) =>
        new(sheetId, anchor, string.Empty, width, height);

    private static string NormalizeTextBoxText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return TextBoxPlaceholder;

        return text.Trim();
    }
}

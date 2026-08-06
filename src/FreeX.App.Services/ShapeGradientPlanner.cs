using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services;

/// <summary>
/// Portable, UI-free planning for the shape "Gradient Fill" dialog: seed colors, the ordered set of gradient
/// directions, direction normalization, validated result creation, and the normalized preview vector shared by
/// renderer-specific dialogs.
/// </summary>
public static class ShapeGradientPlanner
{
    /// <summary>Logical width used by parity capture for the compact shape gradient dialog.</summary>
    public const int DialogWidth = 500;

    /// <summary>Logical height used by parity capture for the compact shape gradient dialog.</summary>
    public const int DialogHeight = 300;

    /// <summary>Seed start color when the shape has no fill to reuse.</summary>
    public static CellColor DefaultStartColor { get; } = DrawingShapeModel.DefaultFillColor;

    /// <summary>Seed end color (a light tint) when the shape has no current gradient end.</summary>
    public static CellColor DefaultEndColor { get; } = new(0xFF, 0xFF, 0xFF);

    /// <summary>One selectable gradient direction and the resource key naming it.</summary>
    public sealed record GradientDirectionOption(DrawingShapeGradientDirection Direction, string LabelKey);

    /// <summary>The fields the dialog seeds from the selected shape.</summary>
    public sealed record GradientValues(
        CellColor StartColor,
        CellColor EndColor,
        DrawingShapeGradientDirection Direction);

    /// <summary>The validated result handed to <c>SetDrawingShapeGradientCommand</c>.</summary>
    public sealed record GradientResult(
        CellColor StartColor,
        CellColor EndColor,
        DrawingShapeGradientDirection Direction);

    /// <summary>
    /// Captures a shape's current gradient, or a sensible seed when it has none. The start color reuses the
    /// existing fill when present, and the end color reuses the stored gradient end when present.
    /// </summary>
    public static GradientValues Capture(DrawingShapeModel shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        var start = shape.FillColor ?? DefaultStartColor;
        var end = shape.GradientFillEndColor ?? DefaultEndColor;
        return new GradientValues(start, end, NormalizeDirection(shape.GradientFillDirection));
    }

    /// <summary>The ordered diagonal-down / horizontal / vertical / diagonal-up directions the dialog offers.</summary>
    public static IReadOnlyList<GradientDirectionOption> CreateDirectionOptions() =>
    [
        new(DrawingShapeGradientDirection.DiagonalDown, "ShapeGradient_DirectionDiagonalDown"),
        new(DrawingShapeGradientDirection.Horizontal, "ShapeGradient_DirectionHorizontal"),
        new(DrawingShapeGradientDirection.Vertical, "ShapeGradient_DirectionVertical"),
        new(DrawingShapeGradientDirection.DiagonalUp, "ShapeGradient_DirectionDiagonalUp"),
    ];

    /// <summary>Maps an undefined stored direction back to <see cref="DrawingShapeGradientDirection.DiagonalDown"/>.</summary>
    public static DrawingShapeGradientDirection NormalizeDirection(DrawingShapeGradientDirection direction) =>
        Enum.IsDefined(direction) ? direction : DrawingShapeGradientDirection.DiagonalDown;

    /// <summary>Index of <paramref name="direction"/> within <see cref="CreateDirectionOptions"/> (0 when not found).</summary>
    public static int FindDirectionIndex(
        IReadOnlyList<GradientDirectionOption> options,
        DrawingShapeGradientDirection direction)
    {
        ArgumentNullException.ThrowIfNull(options);
        var normalized = NormalizeDirection(direction);
        for (var i = 0; i < options.Count; i++)
        {
            if (options[i].Direction == normalized)
                return i;
        }

        return 0;
    }

    /// <summary>Resolves a direction option by index, clamping out-of-range selections to diagonal-down.</summary>
    public static DrawingShapeGradientDirection DirectionAt(
        IReadOnlyList<GradientDirectionOption> options,
        int index)
    {
        ArgumentNullException.ThrowIfNull(options);
        return index >= 0 && index < options.Count
            ? options[index].Direction
            : DrawingShapeGradientDirection.DiagonalDown;
    }

    /// <summary>Builds the validated result. Direction is normalized; colors come straight from the pickers.</summary>
    public static GradientResult CreateResult(
        CellColor startColor,
        CellColor endColor,
        DrawingShapeGradientDirection direction) =>
        new(startColor, endColor, NormalizeDirection(direction));

    public static SetDrawingShapeGradientCommand BuildCommand(
        SheetId sheetId,
        Guid shapeId,
        GradientResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return BuildCommand(sheetId, shapeId, result.StartColor, result.EndColor, result.Direction);
    }

    public static SetDrawingShapeGradientCommand BuildCommand(
        SheetId sheetId,
        Guid shapeId,
        CellColor startColor,
        CellColor endColor,
        DrawingShapeGradientDirection direction) =>
        new(sheetId, shapeId, startColor, endColor, NormalizeDirection(direction));

    /// <summary>
    /// Normalized (0-1) gradient preview vector for a given direction and aspect, matching the host preview so
    /// the swatch reads the same on every shell. The diagonal cases inset the vector to the shape's aspect so
    /// the gradient runs corner-to-corner of the visible box.
    /// </summary>
    public static (double StartX, double StartY, double EndX, double EndY) PreviewVector(
        DrawingShapeGradientDirection direction,
        double width,
        double height)
    {
        direction = NormalizeDirection(direction);
        if (direction == DrawingShapeGradientDirection.Horizontal)
            return (0, 0.5, 1, 0.5);
        if (direction == DrawingShapeGradientDirection.Vertical)
            return (0.5, 0, 0.5, 1);

        if (width <= 0 || height <= 0)
        {
            return direction == DrawingShapeGradientDirection.DiagonalUp
                ? (0, 1, 1, 0)
                : (0, 0, 1, 1);
        }

        var xSpan = 1.0;
        var ySpan = 1.0;
        if (width > height)
            xSpan = height / width;
        else if (height > width)
            ySpan = width / height;

        var startX = 0.5 - xSpan / 2;
        var endX = 0.5 + xSpan / 2;
        var startY = 0.5 - ySpan / 2;
        var endY = 0.5 + ySpan / 2;
        return direction == DrawingShapeGradientDirection.DiagonalUp
            ? (startX, endY, endX, startY)
            : (startX, startY, endX, endY);
    }
}

using FreeX.Core.Model;

namespace FreeX.App.Services;

/// <summary>
/// Shared deterministic state for the WPF/Avalonia Shape Gradient parity capture.
/// The values intentionally come from the planner defaults so the evidence follows the
/// product's no-existing-gradient seed behavior on both shells.
/// </summary>
public static class ShapeGradientParityFixture
{
    public static CellColor StartColor => ShapeGradientPlanner.DefaultStartColor;

    public static CellColor EndColor => ShapeGradientPlanner.DefaultEndColor;

    public const DrawingShapeGradientDirection Direction = DrawingShapeGradientDirection.DiagonalDown;

    public static void Apply(DrawingShapeModel shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        shape.FillColor = StartColor;
        shape.GradientFillEndColor = EndColor;
        shape.GradientFillDirection = Direction;
    }
}

namespace FreeP.Core.Model;

/// <summary>Undoable chart-area or plot-area fill and outline formatting.</summary>
public sealed record ChartAreaOptions(
    ChartAreaFormattingTarget Target,
    ShapeFill? Fill,
    ShapeOutline? Outline);

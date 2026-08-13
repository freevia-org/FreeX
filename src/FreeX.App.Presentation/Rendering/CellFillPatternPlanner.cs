using FreeX.Core.Model;

namespace FreeX.App.Presentation.Rendering;

public enum CellFillPatternPlanKind
{
    None,
    Opacity,
    Hatch,
}

public enum CellFillPatternLinePrimitive
{
    Horizontal,
    Vertical,
    DescendingDiagonal,
    AscendingDiagonal,
}

public sealed record CellFillPatternPlan(
    CellFillPatternPlanKind Kind,
    double Opacity,
    double TileSize,
    double StrokeThickness,
    IReadOnlyList<CellFillPatternLinePrimitive> Lines);

/// <summary>
/// Portable opacity and hatch-line policy for OOXML cell fill patterns.
/// </summary>
public static class CellFillPatternPlanner
{
    private const double LineTileSize = 6.0;
    private const double DiagonalTileSize = 8.0;
    private const double StrokeThickness = 0.75;

    private static readonly IReadOnlyList<CellFillPatternLinePrimitive> NoLines =
        Array.AsReadOnly(Array.Empty<CellFillPatternLinePrimitive>());
    private static readonly IReadOnlyList<CellFillPatternLinePrimitive> Horizontal =
        Array.AsReadOnly([CellFillPatternLinePrimitive.Horizontal]);
    private static readonly IReadOnlyList<CellFillPatternLinePrimitive> Vertical =
        Array.AsReadOnly([CellFillPatternLinePrimitive.Vertical]);
    private static readonly IReadOnlyList<CellFillPatternLinePrimitive> Grid =
        Array.AsReadOnly([CellFillPatternLinePrimitive.Horizontal, CellFillPatternLinePrimitive.Vertical]);
    private static readonly IReadOnlyList<CellFillPatternLinePrimitive> Descending =
        Array.AsReadOnly([CellFillPatternLinePrimitive.DescendingDiagonal]);
    private static readonly IReadOnlyList<CellFillPatternLinePrimitive> Ascending =
        Array.AsReadOnly([CellFillPatternLinePrimitive.AscendingDiagonal]);
    private static readonly IReadOnlyList<CellFillPatternLinePrimitive> Trellis =
        Array.AsReadOnly([
            CellFillPatternLinePrimitive.DescendingDiagonal,
            CellFillPatternLinePrimitive.AscendingDiagonal,
        ]);

    private static readonly CellFillPatternPlan None =
        new(CellFillPatternPlanKind.None, 0, 0, 0, NoLines);

    public static CellFillPatternPlan Plan(CellFillPatternStyle style) => style switch
    {
        CellFillPatternStyle.Gray0625 => Opacity(0.12),
        CellFillPatternStyle.Gray125 => Opacity(0.18),
        CellFillPatternStyle.LightGray => Opacity(0.28),
        CellFillPatternStyle.MediumGray => Opacity(0.45),
        CellFillPatternStyle.DarkGray => Opacity(0.62),
        CellFillPatternStyle.LightHorizontal or CellFillPatternStyle.DarkHorizontal =>
            Hatch(LineTileSize, Horizontal),
        CellFillPatternStyle.LightVertical or CellFillPatternStyle.DarkVertical =>
            Hatch(LineTileSize, Vertical),
        CellFillPatternStyle.LightGrid or CellFillPatternStyle.DarkGrid =>
            Hatch(LineTileSize, Grid),
        CellFillPatternStyle.LightDown or CellFillPatternStyle.DarkDown =>
            Hatch(DiagonalTileSize, Descending),
        CellFillPatternStyle.LightUp or CellFillPatternStyle.DarkUp =>
            Hatch(DiagonalTileSize, Ascending),
        CellFillPatternStyle.LightTrellis or CellFillPatternStyle.DarkTrellis =>
            Hatch(DiagonalTileSize, Trellis),
        _ => None,
    };

    private static CellFillPatternPlan Opacity(double opacity) =>
        new(CellFillPatternPlanKind.Opacity, opacity, 0, 0, NoLines);

    private static CellFillPatternPlan Hatch(
        double tileSize,
        IReadOnlyList<CellFillPatternLinePrimitive> lines) =>
        new(CellFillPatternPlanKind.Hatch, 0, tileSize, StrokeThickness, lines);
}

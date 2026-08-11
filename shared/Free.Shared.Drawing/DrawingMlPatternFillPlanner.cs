namespace Free.Shared.Drawing;

/// <summary>Visual families used to approximate DrawingML preset pattern fills.</summary>
public enum DrawingMlPatternFillFamily
{
    Horizontal,
    Vertical,
    DownDiagonal,
    UpDiagonal,
    Cross,
    Dot,
    Brick,
    DiagonalCross,
}

/// <summary>Identifies which of the two DrawingML pattern colors paints a primitive.</summary>
public enum DrawingMlPatternFillColorRole
{
    Foreground,
    Background,
}

public readonly record struct DrawingMlPatternFillPoint(double X, double Y);

public abstract record DrawingMlPatternFillPrimitive(DrawingMlPatternFillColorRole ColorRole);

public sealed record DrawingMlPatternFillRectangle(
    double X,
    double Y,
    double Width,
    double Height,
    DrawingMlPatternFillColorRole ColorRole)
    : DrawingMlPatternFillPrimitive(ColorRole);

public sealed record DrawingMlPatternFillLine(
    DrawingMlPatternFillPoint Start,
    DrawingMlPatternFillPoint End,
    double StrokeWidth,
    DrawingMlPatternFillColorRole ColorRole)
    : DrawingMlPatternFillPrimitive(ColorRole);

public sealed record DrawingMlPatternFillEllipse(
    double CenterX,
    double CenterY,
    double RadiusX,
    double RadiusY,
    DrawingMlPatternFillColorRole ColorRole)
    : DrawingMlPatternFillPrimitive(ColorRole);

/// <summary>A framework-neutral tile recipe in screen coordinates (origin at top-left).</summary>
public sealed record DrawingMlPatternFillRecipe(
    DrawingMlPatternFillFamily Family,
    double TileWidth,
    double TileHeight,
    IReadOnlyList<DrawingMlPatternFillPrimitive> Primitives);

/// <summary>
/// Owns DrawingML preset-family bucketing and the shared tile geometry consumed by live and PDF renderers.
/// Unknown presets deliberately use the long-standing diagonal-cross fallback.
/// </summary>
public static class DrawingMlPatternFillPlanner
{
    private static readonly IReadOnlyDictionary<DrawingMlPatternFillFamily, DrawingMlPatternFillRecipe> s_recipes =
        new Dictionary<DrawingMlPatternFillFamily, DrawingMlPatternFillRecipe>
        {
            [DrawingMlPatternFillFamily.Horizontal] = Recipe(
                DrawingMlPatternFillFamily.Horizontal,
                Line(0, 4, 8, 4)),
            [DrawingMlPatternFillFamily.Vertical] = Recipe(
                DrawingMlPatternFillFamily.Vertical,
                Line(4, 0, 4, 8)),
            [DrawingMlPatternFillFamily.DownDiagonal] = Recipe(
                DrawingMlPatternFillFamily.DownDiagonal,
                Line(0, 0, 8, 8)),
            [DrawingMlPatternFillFamily.UpDiagonal] = Recipe(
                DrawingMlPatternFillFamily.UpDiagonal,
                Line(0, 8, 8, 0)),
            [DrawingMlPatternFillFamily.Cross] = Recipe(
                DrawingMlPatternFillFamily.Cross,
                Line(0, 4, 8, 4),
                Line(4, 0, 4, 8)),
            [DrawingMlPatternFillFamily.Dot] = Recipe(
                DrawingMlPatternFillFamily.Dot,
                new DrawingMlPatternFillEllipse(4, 4, 1, 1, DrawingMlPatternFillColorRole.Foreground)),
            [DrawingMlPatternFillFamily.Brick] = Recipe(
                DrawingMlPatternFillFamily.Brick,
                12,
                8,
                Line(0, 0, 12, 0, 0.5),
                Line(6, 4, 12, 4, 0.5),
                Line(0, 4, 3, 4, 0.5),
                Line(6, 0, 6, 4, 0.5),
                Line(0, 4, 0, 8, 0.5),
                Line(12, 4, 12, 8, 0.5)),
            [DrawingMlPatternFillFamily.DiagonalCross] = Recipe(
                DrawingMlPatternFillFamily.DiagonalCross,
                Line(0, 0, 8, 8),
                Line(8, 0, 0, 8)),
        };

    public static DrawingMlPatternFillRecipe Plan(string? preset) => RecipeFor(Classify(preset));

    public static DrawingMlPatternFillRecipe RecipeFor(DrawingMlPatternFillFamily family) => s_recipes[family];

    public static DrawingMlPatternFillFamily Classify(string? preset) => preset switch
    {
        "horz" or "ltHorz" or "medGray" or "dkHorz" or "pct5" or "pct10" or "pct20"
            => DrawingMlPatternFillFamily.Horizontal,
        "vert" or "ltVert" or "dkVert" or "pct25" or "pct30"
            => DrawingMlPatternFillFamily.Vertical,
        "diagStripe" or "ltDnDiag" or "dkDnDiag" or "dnDiag" or "pct50"
            => DrawingMlPatternFillFamily.DownDiagonal,
        "ltUpDiag" or "dkUpDiag" or "upDiag" or "pct60" or "pct70"
            => DrawingMlPatternFillFamily.UpDiagonal,
        "cross" or "ltGrid" or "dkGrid" or "pct75" or "pct80"
            => DrawingMlPatternFillFamily.Cross,
        "dotGrid" or "dotDmnd" or "smGrid" or "pct90"
            => DrawingMlPatternFillFamily.Dot,
        "horzBrick" or "divot" or "weave"
            => DrawingMlPatternFillFamily.Brick,
        _ => DrawingMlPatternFillFamily.DiagonalCross,
    };

    private static DrawingMlPatternFillRecipe Recipe(
        DrawingMlPatternFillFamily family,
        params DrawingMlPatternFillPrimitive[] foregroundPrimitives) =>
        Recipe(family, 8, 8, foregroundPrimitives);

    private static DrawingMlPatternFillRecipe Recipe(
        DrawingMlPatternFillFamily family,
        double width,
        double height,
        params DrawingMlPatternFillPrimitive[] foregroundPrimitives) =>
        new(
            family,
            width,
            height,
            [
                new DrawingMlPatternFillRectangle(
                    0,
                    0,
                    width,
                    height,
                    DrawingMlPatternFillColorRole.Background),
                .. foregroundPrimitives,
            ]);

    private static DrawingMlPatternFillLine Line(
        double startX,
        double startY,
        double endX,
        double endY,
        double strokeWidth = 1) =>
        new(
            new DrawingMlPatternFillPoint(startX, startY),
            new DrawingMlPatternFillPoint(endX, endY),
            strokeWidth,
            DrawingMlPatternFillColorRole.Foreground);
}

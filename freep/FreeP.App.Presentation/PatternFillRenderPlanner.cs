namespace FreeP.App.Compositor;

public enum PatternFillRendererProfile
{
    WpfVector,
    AvaloniaPixel
}

public enum PatternFillColorRole
{
    Background,
    Foreground
}

public readonly record struct PatternFillLineSegment(
    double StartX,
    double StartY,
    double EndX,
    double EndY);

public abstract record PatternFillVectorPrimitive(PatternFillColorRole Color)
{
    public sealed record Rectangle(
        double X,
        double Y,
        double Width,
        double Height,
        PatternFillColorRole Fill) : PatternFillVectorPrimitive(Fill);

    public sealed record Ellipse(
        double CenterX,
        double CenterY,
        double RadiusX,
        double RadiusY,
        PatternFillColorRole Fill) : PatternFillVectorPrimitive(Fill);

    public sealed record LinePath(
        IReadOnlyList<PatternFillLineSegment> Segments,
        double StrokeWidth,
        PatternFillColorRole Stroke) : PatternFillVectorPrimitive(Stroke);
}

public abstract record PatternFillRenderPlan
{
    public sealed record Solid(PatternFillColorRole Color) : PatternFillRenderPlan;

    public sealed record VectorTile(
        double Width,
        double Height,
        IReadOnlyList<PatternFillVectorPrimitive> Primitives) : PatternFillRenderPlan;

    public sealed record PixelTile(
        int Width,
        int Height,
        IReadOnlyList<PatternFillColorRole> Pixels) : PatternFillRenderPlan;
}

/// <summary>
/// Classifies OOXML pattern presets into the exact vector or pixel recipe used by each host.
/// </summary>
public static class PatternFillRenderPlanner
{
    public static PatternFillRenderPlan Plan(
        string? preset,
        PatternFillRendererProfile profile) =>
        profile switch
        {
            PatternFillRendererProfile.WpfVector => PlanWpfVector(preset),
            PatternFillRendererProfile.AvaloniaPixel => PlanAvaloniaPixel(preset),
            _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, null)
        };

    private static PatternFillRenderPlan PlanWpfVector(string? preset) => preset switch
    {
        "pct0" => new PatternFillRenderPlan.Solid(PatternFillColorRole.Background),
        "pct5" => DotTile(PatternFillColorRole.Background, PatternFillColorRole.Foreground, 1, 0.25),
        "pct10" => DotTile(PatternFillColorRole.Background, PatternFillColorRole.Foreground, 1, 0.5),
        "pct20" => DotTile(PatternFillColorRole.Background, PatternFillColorRole.Foreground, 2, 0.75),
        "pct25" => DotTile(PatternFillColorRole.Background, PatternFillColorRole.Foreground, 2, 1.0),
        "pct30" => DotTile(PatternFillColorRole.Background, PatternFillColorRole.Foreground, 2, 1.25),
        "pct40" => CheckerTile(),
        "pct50" => HalfTile(horizontal: false),
        "pct60" => DotTile(PatternFillColorRole.Foreground, PatternFillColorRole.Background, 3, 1.5),
        "pct75" => DotTile(PatternFillColorRole.Foreground, PatternFillColorRole.Background, 2, 1.0),
        "pct90" => DotTile(PatternFillColorRole.Foreground, PatternFillColorRole.Background, 1, 0.25),
        "pct100" => new PatternFillRenderPlan.Solid(PatternFillColorRole.Foreground),
        "horzStripe" or "ltHorz" or "dashHorz" => StripeTile(horizontal: true),
        "vertStripe" or "ltVert" or "dashVert" => StripeTile(horizontal: false),
        "diagStripe" or "ltDnDiag" or "dnDiag" => DiagonalTile(down: true),
        "upDiag" or "ltUpDiag" => DiagonalTile(down: false),
        "cross" => CrossTile(tileSize: 8, strokeWidth: 1),
        "smGrid" => CrossTile(tileSize: 6, strokeWidth: 2),
        "diagCross" or "smConfetti" or "wave" or "trellis" => DiagonalCrossTile(),
        _ => new PatternFillRenderPlan.Solid(PatternFillColorRole.Foreground)
    };

    private static PatternFillRenderPlan.VectorTile DotTile(
        PatternFillColorRole background,
        PatternFillColorRole dots,
        int dotCount,
        double dotSize)
    {
        const double tileWidth = 4;
        const double tileHeight = 4;
        var primitives = new List<PatternFillVectorPrimitive>
        {
            new PatternFillVectorPrimitive.Rectangle(0, 0, tileWidth, tileHeight, background)
        };
        double spacing = tileWidth / dotCount;
        for (int index = 0; index < dotCount; index++)
        {
            primitives.Add(new PatternFillVectorPrimitive.Ellipse(
                spacing * index + spacing / 2,
                tileHeight / 2,
                dotSize / 2,
                dotSize / 2,
                dots));
        }

        return new PatternFillRenderPlan.VectorTile(tileWidth, tileHeight, primitives);
    }

    private static PatternFillRenderPlan.VectorTile HalfTile(bool horizontal)
    {
        PatternFillVectorPrimitive[] primitives = horizontal
            ?
            [
                new PatternFillVectorPrimitive.Rectangle(0, 0, 4, 2, PatternFillColorRole.Background),
                new PatternFillVectorPrimitive.Rectangle(0, 2, 4, 2, PatternFillColorRole.Foreground)
            ]
            :
            [
                new PatternFillVectorPrimitive.Rectangle(0, 0, 2, 4, PatternFillColorRole.Background),
                new PatternFillVectorPrimitive.Rectangle(2, 0, 2, 4, PatternFillColorRole.Foreground)
            ];
        return new PatternFillRenderPlan.VectorTile(4, 4, primitives);
    }

    private static PatternFillRenderPlan.VectorTile CheckerTile()
    {
        var primitives = new List<PatternFillVectorPrimitive>
        {
            new PatternFillVectorPrimitive.Rectangle(0, 0, 4, 4, PatternFillColorRole.Background)
        };
        for (int y = 0; y < 4; y++)
        for (int x = 0; x < 4; x++)
        {
            if ((x + y) % 2 == 0)
            {
                primitives.Add(new PatternFillVectorPrimitive.Rectangle(
                    x,
                    y,
                    1,
                    1,
                    PatternFillColorRole.Foreground));
            }
        }

        return new PatternFillRenderPlan.VectorTile(4, 4, primitives);
    }

    private static PatternFillRenderPlan.VectorTile StripeTile(bool horizontal) =>
        new(
            6,
            6,
            horizontal
                ?
                [
                    new PatternFillVectorPrimitive.Rectangle(0, 0, 6, 6, PatternFillColorRole.Background),
                    new PatternFillVectorPrimitive.Rectangle(0, 2, 6, 2, PatternFillColorRole.Foreground)
                ]
                :
                [
                    new PatternFillVectorPrimitive.Rectangle(0, 0, 6, 6, PatternFillColorRole.Background),
                    new PatternFillVectorPrimitive.Rectangle(2, 0, 2, 6, PatternFillColorRole.Foreground)
                ]);

    private static PatternFillRenderPlan.VectorTile DiagonalTile(bool down) =>
        new(
            6,
            6,
            [
                new PatternFillVectorPrimitive.Rectangle(0, 0, 6, 6, PatternFillColorRole.Background),
                new PatternFillVectorPrimitive.LinePath(
                    down
                        ? [new PatternFillLineSegment(0, 0, 6, 6)]
                        : [new PatternFillLineSegment(0, 6, 6, 0)],
                    1.5,
                    PatternFillColorRole.Foreground)
            ]);

    private static PatternFillRenderPlan.VectorTile CrossTile(double tileSize, double strokeWidth) =>
        new(
            tileSize,
            tileSize,
            [
                new PatternFillVectorPrimitive.Rectangle(
                    0, 0, tileSize, tileSize, PatternFillColorRole.Background),
                new PatternFillVectorPrimitive.Rectangle(
                    0, 0, strokeWidth, tileSize, PatternFillColorRole.Foreground),
                new PatternFillVectorPrimitive.Rectangle(
                    0, 0, tileSize, strokeWidth, PatternFillColorRole.Foreground)
            ]);

    private static PatternFillRenderPlan.VectorTile DiagonalCrossTile() =>
        new(
            6,
            6,
            [
                new PatternFillVectorPrimitive.Rectangle(0, 0, 6, 6, PatternFillColorRole.Background),
                new PatternFillVectorPrimitive.LinePath(
                    [
                        new PatternFillLineSegment(0, 0, 6, 6),
                        new PatternFillLineSegment(6, 0, 0, 6)
                    ],
                    1.5,
                    PatternFillColorRole.Foreground)
            ]);

    private static PatternFillRenderPlan.PixelTile PlanAvaloniaPixel(string? preset)
    {
        int tileSize = preset == "cross" ? 8 : 6;
        var pixels = Enumerable.Repeat(
            PatternFillColorRole.Background,
            tileSize * tileSize).ToArray();

        void Fill(PatternFillColorRole role) => Array.Fill(pixels, role);
        void Set(int x, int y, PatternFillColorRole role)
        {
            if (x >= 0 && x < tileSize && y >= 0 && y < tileSize)
                pixels[y * tileSize + x] = role;
        }

        switch (preset)
        {
            case "horzStripe" or "ltHorz" or "dashHorz":
                for (int x = 0; x < tileSize; x++)
                {
                    Set(x, 2, PatternFillColorRole.Foreground);
                    Set(x, 3, PatternFillColorRole.Foreground);
                }
                break;
            case "vertStripe" or "ltVert" or "dashVert":
                for (int y = 0; y < tileSize; y++)
                {
                    Set(2, y, PatternFillColorRole.Foreground);
                    Set(3, y, PatternFillColorRole.Foreground);
                }
                break;
            case "pct50" or "pct40":
                for (int x = 0; x < tileSize; x++)
                for (int y = 0; y < tileSize; y++)
                    if ((x + y) % 2 == 0)
                        Set(x, y, PatternFillColorRole.Foreground);
                break;
            case "pct0":
                break;
            case "pct100":
                Fill(PatternFillColorRole.Foreground);
                break;
            case "pct25" or "pct30" or "pct5" or "pct10" or "pct20":
                for (int x = 0; x < tileSize; x++)
                for (int y = 0; y < tileSize; y++)
                    if ((x * 2 + y * 3) % 4 == 0)
                        Set(x, y, PatternFillColorRole.Foreground);
                break;
            case "pct75" or "pct60" or "pct90":
                Fill(PatternFillColorRole.Foreground);
                for (int x = 0; x < tileSize; x++)
                for (int y = 0; y < tileSize; y++)
                    if ((x + y) % 3 == 0)
                        Set(x, y, PatternFillColorRole.Background);
                break;
            case "diagStripe" or "ltDnDiag" or "dnDiag":
                for (int index = 0; index < tileSize; index++)
                    Set(index, index, PatternFillColorRole.Foreground);
                break;
            case "upDiag" or "ltUpDiag":
                for (int index = 0; index < tileSize; index++)
                    Set(index, tileSize - 1 - index, PatternFillColorRole.Foreground);
                break;
            case "cross":
                for (int x = 0; x < tileSize; x++)
                    Set(x, 0, PatternFillColorRole.Foreground);
                for (int y = 0; y < tileSize; y++)
                    Set(0, y, PatternFillColorRole.Foreground);
                break;
            case "smGrid":
                for (int x = 0; x < tileSize; x++)
                    Set(x, 2, PatternFillColorRole.Foreground);
                for (int y = 0; y < tileSize; y++)
                    Set(2, y, PatternFillColorRole.Foreground);
                break;
            case "diagCross" or "smConfetti" or "wave" or "trellis":
                for (int index = 0; index < tileSize; index++)
                {
                    Set(index, index, PatternFillColorRole.Foreground);
                    Set(index, tileSize - 1 - index, PatternFillColorRole.Foreground);
                }
                break;
            default:
                Fill(PatternFillColorRole.Foreground);
                break;
        }

        return new PatternFillRenderPlan.PixelTile(tileSize, tileSize, pixels);
    }
}

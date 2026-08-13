namespace FreeW.App.Presentation.DocumentView;

public sealed record PageBorderAppleMotif(
    double Xdip,
    double Ydip,
    double SizeDip);

public sealed record PageBorderShadowedSquareMotif(
    double Xdip,
    double Ydip,
    double SizeDip);

public sealed record PageBorderShorebirdTrackMotif(
    double CenterXDip,
    double CenterYDip,
    double SizeDip,
    int QuarterTurns);

public sealed record PageBorderBatMotif(
    double Xdip,
    double Ydip,
    double SizeDip);

public sealed record PageBorderArtPoint(
    double XDip,
    double YDip);

public sealed record PageBorderArtLineSegment(
    double X1Dip,
    double Y1Dip,
    double X2Dip,
    double Y2Dip);

public sealed record PageBorderArtFillRectangle(
    double Xdip,
    double Ydip,
    double WidthDip,
    double HeightDip,
    byte Red,
    byte Green,
    byte Blue,
    bool Antialias = false);

public sealed record PageBorderArtCubicStroke(
    double StartXDip,
    double StartYDip,
    double Control1XDip,
    double Control1YDip,
    double Control2XDip,
    double Control2YDip,
    double EndXDip,
    double EndYDip,
    double WidthDip,
    byte Red,
    byte Green,
    byte Blue);

public sealed record PageBorderArtPolygon(
    IReadOnlyList<PageBorderArtPoint> Points,
    byte Red,
    byte Green,
    byte Blue);

public sealed record PageBorderArtColor(
    byte Red,
    byte Green,
    byte Blue);

public sealed record PageBorderArtStrokeLine(
    PageBorderArtLineSegment Segment,
    double WidthDip,
    PageBorderArtColor Color,
    bool RoundCaps = false);

public sealed record PageBorderArtCubicSegment(
    PageBorderArtPoint Control1,
    PageBorderArtPoint Control2,
    PageBorderArtPoint End);

public sealed record PageBorderArtCubicFigure(
    PageBorderArtPoint Start,
    IReadOnlyList<PageBorderArtCubicSegment> Segments,
    bool IsClosed,
    PageBorderArtColor? Fill,
    PageBorderArtColor? Stroke,
    double StrokeWidthDip = 0,
    bool RoundCaps = false);

public sealed record PageBorderArtFramePlan(
    IReadOnlyList<PageBorderArtFillRectangle> Fills,
    IReadOnlyList<PageBorderArtPolygon> Polygons,
    IReadOnlyList<PageBorderArtStrokeLine> Lines,
    IReadOnlyList<PageBorderArtCubicFigure> CubicFigures);

public sealed record PageBorderDecorativeArchPlan(
    IReadOnlyList<PageBorderArtFillRectangle> Fills,
    IReadOnlyList<PageBorderArtCubicStroke> Strokes);

public sealed record PageBorderArtFilledShapePlan(
    IReadOnlyList<PageBorderArtFillRectangle> Fills,
    IReadOnlyList<PageBorderArtPolygon> Polygons);

public static class PageBorderArtVisualPlanner
{
    private static readonly IReadOnlyList<(byte Red, byte Green, byte Blue)?> MapleMuffinsPalette =
    [
        null,
        (0xFE, 0x7F, 0x00),
        (0xBE, 0x41, 0x00),
        (0x14, 0x0A, 0x04),
        (0x6B, 0x29, 0x01),
        (0xDB, 0x64, 0x00),
        (0x49, 0x42, 0x3C),
        (0x96, 0x39, 0x00),
        (0xEF, 0xEF, 0xEF),
        (0x3D, 0x1B, 0x06),
        (0x8C, 0x8A, 0x89),
        (0xD4, 0xD4, 0xD4),
    ];

    private static readonly IReadOnlyList<(byte Red, byte Green, byte Blue)?> IceCreamConesPalette =
    [
        null,
        (0xFE, 0xFE, 0x7F),
        (0xFC, 0x7F, 0xFC),
        (0x57, 0x3F, 0x27),
        (0xEF, 0xEF, 0xEF),
        (0x1E, 0x18, 0x16),
        (0xBB, 0xBB, 0x5E),
        (0x79, 0x6D, 0x73),
        (0xDF, 0xDF, 0xDF),
        (0xCF, 0xCF, 0xCF),
        (0xAE, 0x57, 0xAE),
        (0xB0, 0xB0, 0xB0),
    ];

    private static readonly IReadOnlyList<(byte Red, byte Green, byte Blue)?> PeoplePalette =
    [
        null,
        (0x00, 0x00, 0x00),
        (0x10, 0x10, 0x10),
        (0x20, 0x20, 0x20),
        (0x30, 0x30, 0x30),
        (0x40, 0x40, 0x40),
        (0x50, 0x50, 0x50),
        (0x60, 0x60, 0x60),
        (0x70, 0x70, 0x70),
        (0x80, 0x80, 0x80),
        (0x90, 0x90, 0x90),
        (0xA0, 0xA0, 0xA0),
        (0xB0, 0xB0, 0xB0),
        (0xC0, 0xC0, 0xC0),
        (0xD0, 0xD0, 0xD0),
        (0xE0, 0xE0, 0xE0),
        (0xEF, 0xEF, 0xEF),
        (0xFF, 0xFF, 0xFF),
    ];

    private static readonly IReadOnlyList<(byte Red, byte Green, byte Blue)?> FlowersRosesPalette =
    [
        null,
        (0xE7, 0x69, 0xD1),
        (0x1A, 0xB3, 0x00),
        (0xA8, 0x4D, 0x98),
        (0x8A, 0x89, 0x8A),
        (0x13, 0x85, 0x00),
        (0x38, 0x46, 0x35),
        (0xB3, 0xB2, 0xB3),
        (0x12, 0x5C, 0x05),
        (0xDD, 0xDD, 0xDD),
        (0x6C, 0x53, 0x68),
        (0x18, 0xA3, 0x00),
    ];

    // Word's Vine border is a fixed 48x32 monochrome sprite repeated along each rail.
    private static readonly string[] VineRailMask =
    [
        "................................................",
        "................................................",
        "................................................",
        "................................................",
        "..........................##########............",
        ".........................###########............",
        "..........................############..........",
        "...............#######....##############........",
        "..........################.##################...",
        ".........#################..#######.#########...",
        "........#########......###.#.......#########....",
        ".......#########..........##################....",
        "....########............#################.......",
        "....########............################........",
        "..#########.............###############.........",
        "############.............############...........",
        "##############....###.......#.................##",
        "##############..########...................#####",
        "####....#######.########........################",
        "##........#####.#########.......################",
        ".............#######.######........############.",
        "............#########.#####.....................",
        "...........###########.#####....................",
        "...........#################....................",
        "...........##################...................",
        "............###################.................",
        "............###################.................",
        "..............##############....................",
        "................................................",
        "................................................",
        "................................................",
        "................................................",
    ];

    private static readonly string[] VineBottomRailMask =
    [
        "................................................",
        "................................................",
        "................................................",
        "................................................",
        "...................################.............",
        ".................###################............",
        "..................##################............",
        "....................######..#########...........",
        "....................##########.######...........",
        ".....................#########..####............",
        "....######...........##########.####.#..........",
        "###############.......###########...##..........",
        "################.......##########.######.....###",
        "########................########...#############",
        "####....................########...#############",
        "##..............######..............############",
        "..........##############..............##########",
        ".........###############..............########..",
        "........################.............#######....",
        ".......#################............########....",
        "....##############..##..#.......#########.......",
        "....################....################........",
        "...##################...################........",
        "....##################..##############..........",
        "..........############..........................",
        "............###########.........................",
        "............###########.........................",
        ".............#########..........................",
        "................................................",
        "................................................",
        "................................................",
        "................................................",
    ];

    private static readonly string[] VineLeftRailMask =
    [
        "................................................",
        "................................................",
        "................................................",
        "................................................",
        "..............#########.........................",
        ".............##########.........................",
        "............############........................",
        "...........############.........................",
        ".......###############.##############...........",
        "....##################.################.........",
        "....################...#################........",
        ".....#############..###.........#########.......",
        "........################............#######.....",
        "........################............########....",
        "..........##############..............########..",
        "...........#############.............##########.",
        "##................####.............#############",
        "####.....................#######..##############",
        "########................#########.##############",
        "################........#########.######......##",
        ".#############........###########...#...........",
        "....###...............##########.###............",
        ".....................#########..#####...........",
        ".....................#######...#######..........",
        "....................#################...........",
        "..................###################...........",
        "..................##################............",
        "....................###############.............",
        "................................................",
        "................................................",
        "................................................",
        "................................................",
    ];

    private static readonly string[][] VineCornerMasks =
    [
        [
            "................................", "................................", "................................", "................................",
            "....................#...........", "................#####...........", "..............########..........", ".............####.####..........",
            ".....#######.####.#####.........", "....########..###.#####.........", "....#########.##.######.........", "...##########.##.######.........",
            "....#####.###...####............", "....######.#.....#..#...........", "....########.......#####........", "....########......######........",
            "......###.........#######...###.", ".........###.....##..####...###.", ".........###....#####..##...###.", "........####.##########.##...##.",
            "........########.##########...#.", "........###.####.##########.....", "........##..####.########.......", "........########..######........",
            ".........######.................", ".........###.....####...........", ".........###....######..........", "..........#.....#####...........",
            "................####............", "................####............", "................####............", "................................",
        ],
        [
            "................................", "................................", "................................", "................................",
            ".........###....................", ".........#######................", ".........########...............", "........##########..............",
            "........####..####..#####.......", "........#####.###.########......", "........######.#..#########.....", "........######.#.######.####....",
            "......#.....##....##.######.....", "....###...###......########.....", "..####.#####.......########.....", "#####.######........#######.....",
            "####..######..........##........", "####..###...#......##...........", "####.###..####....####..........", "##...###.#####.##..####.........",
            "....#########.#####.###.........", "....#########.#####.###.........", "......#######..####.###.........", ".......######..########.........",
            ".............#..######..........", "............####...##...........", "............#####...#...........", "............####....#...........",
            "...........#####................", "...........#####................", "...........#####................", "................................",
        ],
        [
            "................#####...........", "................#####...........", "................#####...........", "................#####...........",
            "..........##....#####...........", ".........###....#####...........", ".........#####..####............", "........##.#####.###............",
            "........##.#####.#######........", "........###.####.########.......", "........########.##########.....", "........####.###.#####.###......",
            "........#####...####..###....##.", ".........###.....###.####...###.", "..................#######.#####.", ".....######.......#############.",
            "....########......######.....#..", "....#######......#.####.........", "....######..#....##.............", "....##....####.#.#####..........",
            "...##########.###.#####.........", "....#########.###..####.........", ".....#######.#####.####.........", "......####..######.####.........",
            ".............#########..........", "...............######...........", "...................##...........", "....................#...........",
            "................................", "................................", "................................", "................................",
        ],
        [
            "............####................", "............####................", "............####................", "............####................",
            "............####....###.........", "...........#####.#######........", "...........#....########........", "...........#...#####.####.......",
            "........######..####.####.......", "......#########.####.####.......", "....###########.########........", ".....##############.####........",
            "####..#####.####..####..........", "####..######.#..................", "#####.#######...................", "######.######.......######......",
            ".############.......########....", "....########........########....", "......##.....##....##..#####....", ".........########.#####.####....",
            ".........#####.##.##########....", "........#####.####.#########....", ".........###.#####..#######.....", ".........###.######...####......",
            ".........#########..............", "..........######................", "..........##....................", "..........#.....................",
            "................................", "................................", "................................", "................................",
        ],
    ];

    public const int ApplesArtId = 1;
    public const int MapleMuffinsArtId = 2;
    public const int CakeSliceArtId = 3;
    public const int CandyCornArtId = 4;
    public const int IceCreamConesArtId = 5;
    public const int BirdsFlightArtId = 35;
    public const int FlowersRosesArtId = 38;
    public const int PaintedEggsArtId = 66;
    public const int PeopleArtId = 84;
    public const int ShadowedSquaresArtId = 57;
    public const int ShorebirdTracksArtId = 83;
    public const int DecorativeArchArtId = 89;
    public const int BatsArtId = 37;
    public const int PapyrusArtId = 92;
    public const int VineArtId = 47;
    public const int WeavingRibbonArtId = 95;
    public const int Handmade2ArtId = 160;
    public const byte AppleFillRed = 0xB5;
    public const byte AppleStemRed = 0x66;
    public const byte AppleHighlightRed = 0xD8;
    public const byte AppleHighlightGreen = 0x59;
    public const byte AppleHighlightBlue = 0x59;
    public const byte ShadowedSquareBlue = 0x80;
    public const double ShadowedSquareFaceInsetDip = 6.0;
    public const double ShadowedSquareOutlineInsetDip = 5.0;
    public const double ShorebirdTrackStrokeWidthDip = 0.5;

    private const double DipPerPoint = 96.0 / 72.0;
    private const double ArtSizeUnitsPerModelPoint = 8.0;
    private const double MinimumMotifSizeDip = 8.0;
    private const double MaximumMotifSizeDip = 64.0;

    /// <summary>
    /// Builds a complete renderer-neutral scene for a supported page-border art style.
    /// Coordinates are local to the supplied frame; native hosts only translate the primitives.
    /// </summary>
    public static bool TryBuildFramePlan(
        int artId,
        double modelWidthPt,
        double frameWidthDip,
        double frameHeightDip,
        double edgeInsetDip,
        out PageBorderArtFramePlan plan)
    {
        plan = EmptyFramePlan();
        switch (artId)
        {
            case ApplesArtId:
                TryBuildApplesFrame(
                    artId, modelWidthPt, frameWidthDip, frameHeightDip, edgeInsetDip, out var apples);
                plan = BuildAppleFramePlan(apples);
                return true;
            case ShadowedSquaresArtId:
                TryBuildShadowedSquaresFrame(
                    artId, modelWidthPt, frameWidthDip, frameHeightDip, edgeInsetDip, out var squares);
                plan = BuildShadowedSquaresFramePlan(squares);
                return true;
            case ShorebirdTracksArtId:
                TryBuildShorebirdTracksFrame(
                    artId, modelWidthPt, frameWidthDip, frameHeightDip, edgeInsetDip, out var tracks);
                plan = BuildShorebirdTracksFramePlan(tracks);
                return true;
            case BatsArtId:
                TryBuildBatsFrame(
                    artId, modelWidthPt, frameWidthDip, frameHeightDip, edgeInsetDip, out var bats);
                plan = new PageBorderArtFramePlan(
                    [],
                    bats.Select(motif => new PageBorderArtPolygon(BuildBatPolygon(motif), 0, 0, 0)).ToArray(),
                    [],
                    []);
                return true;
            case MapleMuffinsArtId:
                TryBuildMapleMuffinsFrame(
                    artId, modelWidthPt, frameWidthDip, frameHeightDip, edgeInsetDip, out var muffins);
                plan = FromFilledShapePlan(muffins);
                return true;
            case CakeSliceArtId:
                TryBuildCakeSliceFrame(
                    artId, modelWidthPt, frameWidthDip, frameHeightDip, edgeInsetDip, out var cake);
                plan = FromFilledShapePlan(cake);
                return true;
            case BirdsFlightArtId:
                TryBuildBirdsFlightFrame(
                    artId, modelWidthPt, frameWidthDip, frameHeightDip, edgeInsetDip, out var birds);
                plan = FromFilledShapePlan(birds);
                return true;
            case PaintedEggsArtId:
                TryBuildPaintedEggsFrame(
                    artId, modelWidthPt, frameWidthDip, frameHeightDip, edgeInsetDip, out var eggs);
                plan = FromFilledShapePlan(eggs);
                return true;
            case CandyCornArtId:
                TryBuildCandyCornFrame(
                    artId, modelWidthPt, frameWidthDip, frameHeightDip, edgeInsetDip, out var candy);
                plan = FromFilledShapePlan(candy);
                return true;
            case IceCreamConesArtId:
                TryBuildIceCreamConesFrame(
                    artId, modelWidthPt, frameWidthDip, frameHeightDip, edgeInsetDip, out var cones);
                plan = FromFilledShapePlan(cones);
                return true;
            case PeopleArtId:
                TryBuildPeopleFrame(
                    artId, modelWidthPt, frameWidthDip, frameHeightDip, edgeInsetDip, out var people);
                plan = FromFilledShapePlan(people);
                return true;
            case FlowersRosesArtId:
                TryBuildFlowersRosesFrame(
                    artId, modelWidthPt, frameWidthDip, frameHeightDip, edgeInsetDip, out var roses);
                plan = FromFilledShapePlan(roses);
                return true;
            case VineArtId:
                TryBuildVineFrame(
                    artId, modelWidthPt, frameWidthDip, frameHeightDip, edgeInsetDip, out var vine);
                plan = FromFilledShapePlan(vine);
                return true;
            case PapyrusArtId:
                TryBuildPapyrusFrame(
                    artId, modelWidthPt, frameWidthDip, frameHeightDip, edgeInsetDip, out var papyrus);
                plan = FromFilledShapePlan(papyrus);
                return true;
            case WeavingRibbonArtId:
                TryBuildWeavingRibbonFrame(
                    artId, modelWidthPt, frameWidthDip, frameHeightDip, edgeInsetDip, out var ribbon);
                plan = FromFilledShapePlan(ribbon);
                return true;
            case DecorativeArchArtId:
                TryBuildDecorativeArchFrame(
                    artId, modelWidthPt, frameWidthDip, frameHeightDip, edgeInsetDip, out var arch);
                plan = FromDecorativePlan(arch);
                return true;
            case Handmade2ArtId:
                TryBuildHandmade2Frame(
                    artId, modelWidthPt, frameWidthDip, frameHeightDip, edgeInsetDip, out var handmade);
                plan = FromDecorativePlan(handmade);
                return true;
            default:
                return false;
        }
    }

    private static PageBorderArtFramePlan BuildAppleFramePlan(
        IReadOnlyList<PageBorderAppleMotif> motifs)
    {
        var figures = new List<PageBorderArtCubicFigure>(motifs.Count * 3);
        foreach (var motif in motifs)
        {
            PageBorderArtPoint Point(double x, double y) =>
                new(motif.Xdip + motif.SizeDip * x, motif.Ydip + motif.SizeDip * y);

            figures.Add(new PageBorderArtCubicFigure(
                Point(0.50, 0.22),
                [
                    new(Point(0.35, 0.04), Point(0.04, 0.10), Point(0.03, 0.51)),
                    new(Point(0.02, 0.82), Point(0.24, 1.00), Point(0.50, 0.91)),
                    new(Point(0.76, 1.00), Point(0.98, 0.82), Point(0.97, 0.51)),
                    new(Point(0.96, 0.10), Point(0.65, 0.04), Point(0.50, 0.22)),
                ],
                true,
                new PageBorderArtColor(AppleFillRed, 0, 0),
                null));
            figures.Add(new PageBorderArtCubicFigure(
                Point(0.50, 0.30),
                [new(Point(0.56, 0.24), Point(0.61, 0.10), Point(0.62, 0.03))],
                false,
                null,
                new PageBorderArtColor(AppleStemRed, 0, 0),
                1.35 * motif.SizeDip / 32.0,
                true));
            figures.Add(new PageBorderArtCubicFigure(
                Point(0.25, 0.34),
                [new(Point(0.15, 0.47), Point(0.15, 0.70), Point(0.22, 0.78))],
                false,
                null,
                new PageBorderArtColor(AppleHighlightRed, AppleHighlightGreen, AppleHighlightBlue),
                2.0 * motif.SizeDip / 32.0,
                true));
        }

        return new PageBorderArtFramePlan([], [], [], figures);
    }

    private static PageBorderArtFramePlan BuildShadowedSquaresFramePlan(
        IReadOnlyList<PageBorderShadowedSquareMotif> motifs)
    {
        var fills = new List<PageBorderArtFillRectangle>(motifs.Count * 6);
        foreach (var motif in motifs)
        {
            var shadowSize = Math.Max(0, motif.SizeDip - 4.0);
            fills.Add(new PageBorderArtFillRectangle(
                motif.Xdip, motif.Ydip, shadowSize, shadowSize, 0, 0, ShadowedSquareBlue, Antialias: true));

            var faceSize = Math.Max(0, motif.SizeDip - 6.0);
            var faceX = motif.Xdip + ShadowedSquareFaceInsetDip;
            var faceY = motif.Ydip + ShadowedSquareFaceInsetDip;
            fills.Add(new PageBorderArtFillRectangle(
                faceX, faceY, faceSize, faceSize, 0xFF, 0xFF, 0xFF, Antialias: true));

            var outlineSize = Math.Max(0, motif.SizeDip - 4.0);
            var outlineX = motif.Xdip + ShadowedSquareOutlineInsetDip;
            var outlineY = motif.Ydip + ShadowedSquareOutlineInsetDip;
            fills.Add(new PageBorderArtFillRectangle(
                outlineX, outlineY, outlineSize, 1, 0, 0, ShadowedSquareBlue, Antialias: true));
            fills.Add(new PageBorderArtFillRectangle(
                outlineX, outlineY + outlineSize - 1, outlineSize, 1, 0, 0, ShadowedSquareBlue, Antialias: true));
            fills.Add(new PageBorderArtFillRectangle(
                outlineX, outlineY, 1, outlineSize, 0, 0, ShadowedSquareBlue, Antialias: true));
            fills.Add(new PageBorderArtFillRectangle(
                outlineX + outlineSize - 1, outlineY, 1, outlineSize, 0, 0, ShadowedSquareBlue, Antialias: true));
        }

        return new PageBorderArtFramePlan(fills, [], [], []);
    }

    private static PageBorderArtFramePlan BuildShorebirdTracksFramePlan(
        IReadOnlyList<PageBorderShorebirdTrackMotif> motifs)
    {
        var black = new PageBorderArtColor(0, 0, 0);
        var lines = motifs
            .SelectMany(BuildShorebirdTrackSegments)
            .Select(segment => new PageBorderArtStrokeLine(
                segment,
                ShorebirdTrackStrokeWidthDip,
                black))
            .ToArray();
        return new PageBorderArtFramePlan([], [], lines, []);
    }

    private static PageBorderArtFramePlan FromFilledShapePlan(PageBorderArtFilledShapePlan plan) =>
        new(plan.Fills, plan.Polygons, [], []);

    private static PageBorderArtFramePlan FromDecorativePlan(PageBorderDecorativeArchPlan plan)
    {
        var figures = plan.Strokes
            .Select(stroke => new PageBorderArtCubicFigure(
                new PageBorderArtPoint(stroke.StartXDip, stroke.StartYDip),
                [new PageBorderArtCubicSegment(
                    new PageBorderArtPoint(stroke.Control1XDip, stroke.Control1YDip),
                    new PageBorderArtPoint(stroke.Control2XDip, stroke.Control2YDip),
                    new PageBorderArtPoint(stroke.EndXDip, stroke.EndYDip))],
                false,
                null,
                new PageBorderArtColor(stroke.Red, stroke.Green, stroke.Blue),
                stroke.WidthDip))
            .ToArray();
        return new PageBorderArtFramePlan(plan.Fills, [], [], figures);
    }

    private static PageBorderArtFramePlan EmptyFramePlan() => new([], [], [], []);

    public static bool TryBuildApplesFrame(
        int artId,
        double modelWidthPt,
        double frameWidthDip,
        double frameHeightDip,
        double edgeInsetDip,
        out IReadOnlyList<PageBorderAppleMotif> motifs)
    {
        if (artId != ApplesArtId)
        {
            motifs = [];
            return false;
        }

        motifs = BuildFrame(frameWidthDip, frameHeightDip, edgeInsetDip, modelWidthPt)
            .Select(placement => new PageBorderAppleMotif(placement.Xdip, placement.Ydip, placement.SizeDip))
            .ToList();
        return true;
    }

    public static bool TryBuildShadowedSquaresFrame(
        int artId,
        double modelWidthPt,
        double frameWidthDip,
        double frameHeightDip,
        double edgeInsetDip,
        out IReadOnlyList<PageBorderShadowedSquareMotif> motifs)
    {
        if (artId != ShadowedSquaresArtId)
        {
            motifs = [];
            return false;
        }

        motifs = BuildFrame(frameWidthDip, frameHeightDip, edgeInsetDip, modelWidthPt)
            .Select(placement => new PageBorderShadowedSquareMotif(placement.Xdip, placement.Ydip, placement.SizeDip))
            .ToList();
        return true;
    }

    public static bool TryBuildMapleMuffinsFrame(
        int artId,
        double modelWidthPt,
        double frameWidthDip,
        double frameHeightDip,
        double edgeInsetDip,
        out PageBorderArtFilledShapePlan plan)
    {
        if (artId != MapleMuffinsArtId)
        {
            plan = new PageBorderArtFilledShapePlan([], []);
            return false;
        }

        var fills = new List<PageBorderArtFillRectangle>();
        foreach (var placement in BuildFrame(frameWidthDip, frameHeightDip, edgeInsetDip, modelWidthPt))
            AddIndexedPaletteMask(
                fills,
                PageBorderArtSpriteMasks.MapleMuffinsMask,
                MapleMuffinsPalette,
                placement.Xdip,
                placement.Ydip,
                placement.SizeDip,
                horizontal: true,
                transparentMaterial: 0);
        plan = new PageBorderArtFilledShapePlan(fills, []);
        return true;
    }

    public static bool TryBuildCakeSliceFrame(
        int artId,
        double modelWidthPt,
        double frameWidthDip,
        double frameHeightDip,
        double edgeInsetDip,
        out PageBorderArtFilledShapePlan plan)
    {
        if (artId != CakeSliceArtId)
        {
            plan = new PageBorderArtFilledShapePlan([], []);
            return false;
        }

        var fills = new List<PageBorderArtFillRectangle>();
        foreach (var placement in BuildFrame(frameWidthDip, frameHeightDip, edgeInsetDip, modelWidthPt))
            AddMaterialMask(
                fills,
                PageBorderArtSpriteMasks.CakeSliceMask,
                placement.Xdip,
                placement.Ydip,
                placement.SizeDip,
                horizontal: true,
                transparentMaterial: 3,
                material1: (0xFF, 0xEE, 0xCA),
                material2: (0xFF, 0x99, 0xC2));
        plan = new PageBorderArtFilledShapePlan(fills, []);
        return true;
    }

    public static bool TryBuildBirdsFlightFrame(
        int artId,
        double modelWidthPt,
        double frameWidthDip,
        double frameHeightDip,
        double edgeInsetDip,
        out PageBorderArtFilledShapePlan plan)
    {
        if (artId != BirdsFlightArtId)
        {
            plan = new PageBorderArtFilledShapePlan([], []);
            return false;
        }

        var fills = new List<PageBorderArtFillRectangle>();
        foreach (var placement in BuildFrame(frameWidthDip, frameHeightDip, edgeInsetDip, modelWidthPt))
            AddMaterialMask(
                fills,
                PageBorderArtSpriteMasks.BirdsFlightMask,
                placement.Xdip,
                placement.Ydip,
                placement.SizeDip,
                horizontal: true,
                transparentMaterial: 3,
                material0: (0x04, 0x07, 0x50),
                material1: (0x62, 0x64, 0x92),
                material2: (0xAE, 0xAF, 0xC6));
        plan = new PageBorderArtFilledShapePlan(fills, []);
        return true;
    }

    public static bool TryBuildPaintedEggsFrame(
        int artId,
        double modelWidthPt,
        double frameWidthDip,
        double frameHeightDip,
        double edgeInsetDip,
        out PageBorderArtFilledShapePlan plan)
    {
        if (artId != PaintedEggsArtId)
        {
            plan = new PageBorderArtFilledShapePlan([], []);
            return false;
        }

        var fills = new List<PageBorderArtFillRectangle>();
        foreach (var placement in BuildFrame(frameWidthDip, frameHeightDip, edgeInsetDip, modelWidthPt))
            AddMaterialMask(
                fills,
                PageBorderArtSpriteMasks.PaintedEggMask,
                placement.Xdip,
                placement.Ydip,
                placement.SizeDip,
                horizontal: true,
                transparentMaterial: 3,
                material1: (0x55, 0x55, 0x55),
                material2: (0xAA, 0xAA, 0xAA));
        plan = new PageBorderArtFilledShapePlan(fills, []);
        return true;
    }

    public static bool TryBuildCandyCornFrame(
        int artId,
        double modelWidthPt,
        double frameWidthDip,
        double frameHeightDip,
        double edgeInsetDip,
        out PageBorderArtFilledShapePlan plan)
    {
        if (artId != CandyCornArtId)
        {
            plan = new PageBorderArtFilledShapePlan([], []);
            return false;
        }

        var frameWidth = Math.Max(0, frameWidthDip);
        var frameHeight = Math.Max(0, frameHeightDip);
        var inset = Math.Max(0, edgeInsetDip);
        var scale = ResolveMotifSize(modelWidthPt) / 32.0;
        var candySize = 14 * scale;
        var tileSize = 32 * scale;
        var polygons = new List<PageBorderArtPolygon>();

        for (var x = inset + 16 * scale; x <= frameWidth - inset - tileSize; x += tileSize)
        {
            AddCandyCorn(polygons, x + scale, inset + scale, candySize, 0);
            AddCandyCorn(polygons, x + 17 * scale, inset + scale, candySize, 2);
            AddCandyCorn(polygons, x + 9 * scale, inset + 17 * scale, candySize, 1);

            var bottom = frameHeight - inset - tileSize;
            AddCandyCorn(polygons, x + scale, bottom + 17 * scale, candySize, 2);
            AddCandyCorn(polygons, x + 17 * scale, bottom + 17 * scale, candySize, 0);
            AddCandyCorn(polygons, x + 9 * scale, bottom + scale, candySize, 3);
        }

        for (var y = inset + 16 * scale; y <= frameHeight - inset - tileSize; y += tileSize)
        {
            AddCandyCorn(polygons, inset + scale, y + 17 * scale, candySize, 3);
            AddCandyCorn(polygons, inset + scale, y + scale, candySize, 1);
            AddCandyCorn(polygons, inset + 17 * scale, y + 9 * scale, candySize, 0);

            var right = frameWidth - inset - tileSize;
            AddCandyCorn(polygons, right + 17 * scale, y + scale, candySize, 1);
            AddCandyCorn(polygons, right + 17 * scale, y + 17 * scale, candySize, 3);
            AddCandyCorn(polygons, right + scale, y + 9 * scale, candySize, 2);
        }

        plan = new PageBorderArtFilledShapePlan([], polygons);
        return true;
    }

    public static bool TryBuildIceCreamConesFrame(
        int artId,
        double modelWidthPt,
        double frameWidthDip,
        double frameHeightDip,
        double edgeInsetDip,
        out PageBorderArtFilledShapePlan plan)
    {
        if (artId != IceCreamConesArtId)
        {
            plan = new PageBorderArtFilledShapePlan([], []);
            return false;
        }

        var fills = new List<PageBorderArtFillRectangle>();
        foreach (var placement in BuildFrame(frameWidthDip, frameHeightDip, edgeInsetDip, modelWidthPt))
            AddIndexedPaletteMask(
                fills,
                PageBorderArtSpriteMasks.IceCreamConesMask,
                IceCreamConesPalette,
                placement.Xdip,
                placement.Ydip,
                placement.SizeDip,
                horizontal: true,
                transparentMaterial: 0);
        plan = new PageBorderArtFilledShapePlan(fills, []);
        return true;
    }

    public static bool TryBuildPeopleFrame(
        int artId,
        double modelWidthPt,
        double frameWidthDip,
        double frameHeightDip,
        double edgeInsetDip,
        out PageBorderArtFilledShapePlan plan)
    {
        if (artId != PeopleArtId)
        {
            plan = new PageBorderArtFilledShapePlan([], []);
            return false;
        }

        var fills = new List<PageBorderArtFillRectangle>();
        foreach (var placement in BuildFrame(frameWidthDip, frameHeightDip, edgeInsetDip, modelWidthPt))
            AddIndexedPaletteMask(
                fills,
                PageBorderArtSpriteMasks.PeopleMask,
                PeoplePalette,
                placement.Xdip,
                placement.Ydip,
                placement.SizeDip,
                horizontal: true,
                transparentMaterial: 0);
        plan = new PageBorderArtFilledShapePlan(fills, []);
        return true;
    }

    public static bool TryBuildFlowersRosesFrame(
        int artId,
        double modelWidthPt,
        double frameWidthDip,
        double frameHeightDip,
        double edgeInsetDip,
        out PageBorderArtFilledShapePlan plan)
    {
        if (artId != FlowersRosesArtId)
        {
            plan = new PageBorderArtFilledShapePlan([], []);
            return false;
        }

        var fills = new List<PageBorderArtFillRectangle>();
        foreach (var placement in BuildFrame(frameWidthDip, frameHeightDip, edgeInsetDip, modelWidthPt))
            AddIndexedPaletteMask(
                fills,
                PageBorderArtSpriteMasks.FlowersRosesMask,
                FlowersRosesPalette,
                placement.Xdip,
                placement.Ydip,
                placement.SizeDip,
                horizontal: true,
                transparentMaterial: 0);
        plan = new PageBorderArtFilledShapePlan(fills, []);
        return true;
    }

    public static bool TryBuildShorebirdTracksFrame(
        int artId,
        double modelWidthPt,
        double frameWidthDip,
        double frameHeightDip,
        double edgeInsetDip,
        out IReadOnlyList<PageBorderShorebirdTrackMotif> motifs)
    {
        if (artId != ShorebirdTracksArtId)
        {
            motifs = [];
            return false;
        }

        var frameWidth = Math.Max(0, frameWidthDip);
        var frameHeight = Math.Max(0, frameHeightDip);
        var inset = Math.Max(0, edgeInsetDip);
        var size = ResolveMotifSize(modelWidthPt);
        var horizontalStart = inset + size * 1.75;
        var horizontalEnd = frameWidth - inset - size * 1.65;
        var verticalStart = inset + size * 1.6875;
        var verticalEnd = frameHeight - inset - size * 1.875;
        if (horizontalEnd < horizontalStart || verticalEnd < verticalStart)
        {
            motifs = [];
            return true;
        }

        var result = new List<PageBorderShorebirdTrackMotif>();
        var horizontalCount = Math.Max(1,
            (int)Math.Round((horizontalEnd - horizontalStart) / (size * 4.0 / 3.0)) + 1);
        var verticalCount = Math.Max(1,
            (int)Math.Round((verticalEnd - verticalStart) / (size * 1.45)) + 1);
        var horizontalCenter = inset + size / 2.0;
        var verticalCenter = inset + size / 2.0;
        AddShorebirdEdge(result, horizontalStart, horizontalEnd, horizontalCenter, horizontalCount, size, 0);
        AddShorebirdEdge(result, horizontalStart, horizontalEnd, frameHeight - verticalCenter, horizontalCount, size, 2);
        AddShorebirdEdge(result, verticalStart, verticalEnd, horizontalCenter, verticalCount, size, 3);
        AddShorebirdEdge(result, verticalStart, verticalEnd, frameWidth - horizontalCenter, verticalCount, size, 1);
        motifs = result;
        return true;
    }

    public static IReadOnlyList<PageBorderArtLineSegment> BuildShorebirdTrackSegments(
        PageBorderShorebirdTrackMotif motif)
    {
        var scale = motif.SizeDip / 32.0;
        var local = new (double X1, double Y1, double X2, double Y2)[]
        {
            (-16, 0, -7, 0),
            (0, 0, 16, 0),
            (0, 0, 11, -8),
            (0, 0, 11, 8),
        };
        return local.Select(segment =>
        {
            var start = Rotate(segment.X1 * scale, segment.Y1 * scale, motif.QuarterTurns);
            var end = Rotate(segment.X2 * scale, segment.Y2 * scale, motif.QuarterTurns);
            return new PageBorderArtLineSegment(
                motif.CenterXDip + start.X,
                motif.CenterYDip + start.Y,
                motif.CenterXDip + end.X,
                motif.CenterYDip + end.Y);
        }).ToList();
    }

    public static bool TryBuildBatsFrame(
        int artId,
        double modelWidthPt,
        double frameWidthDip,
        double frameHeightDip,
        double edgeInsetDip,
        out IReadOnlyList<PageBorderBatMotif> motifs)
    {
        if (artId != BatsArtId)
        {
            motifs = [];
            return false;
        }

        motifs = BuildFrame(frameWidthDip, frameHeightDip, edgeInsetDip, modelWidthPt)
            .Select(placement => new PageBorderBatMotif(placement.Xdip, placement.Ydip, placement.SizeDip))
            .ToList();
        return true;
    }

    public static IReadOnlyList<PageBorderArtPoint> BuildBatPolygon(PageBorderBatMotif motif)
    {
        var scale = motif.SizeDip / 32.0;
        var local = new (double X, double Y)[]
        {
            (4, 7),
            (3, 12),
            (4, 15),
            (6, 18),
            (9, 22),
            (12, 24),
            (15, 24),
            (16, 22),
            (18, 24),
            (21, 22),
            (24, 21),
            (26, 18),
            (28, 14),
            (25, 14),
            (22, 16),
            (20, 18),
            (18, 17),
            (16, 18),
            (14, 17),
            (12, 18),
            (10, 17),
            (8, 15),
            (7, 12),
        };
        return local
            .Select(point => new PageBorderArtPoint(
                motif.Xdip + point.X * scale,
                motif.Ydip + point.Y * scale))
            .ToList();
    }

    public static bool TryBuildWeavingRibbonFrame(
        int artId,
        double modelWidthPt,
        double frameWidthDip,
        double frameHeightDip,
        double edgeInsetDip,
        out PageBorderArtFilledShapePlan plan)
    {
        if (artId != WeavingRibbonArtId)
        {
            plan = new PageBorderArtFilledShapePlan([], []);
            return false;
        }

        var frameWidth = Math.Max(0, frameWidthDip);
        var frameHeight = Math.Max(0, frameHeightDip);
        var inset = Math.Max(0, edgeInsetDip);
        var size = ResolveMotifSize(modelWidthPt);
        var railWidth = frameWidth - 2 * inset;
        var railHeight = frameHeight - 2 * inset;
        if (railWidth < size || railHeight < size)
        {
            plan = new PageBorderArtFilledShapePlan([], []);
            return true;
        }

        var fills = new List<PageBorderArtFillRectangle>
        {
            new(inset, inset, railWidth, size, 0, 0, 0),
            new(inset, frameHeight - inset - size, railWidth, size, 0, 0, 0),
            new(inset, inset, size, railHeight, 0, 0, 0),
            new(frameWidth - inset - size, inset, size, railHeight, 0, 0, 0),
        };
        var polygons = new List<PageBorderArtPolygon>();
        var horizontalStart = inset + size;
        var horizontalLength = frameWidth - 2 * horizontalStart;
        var verticalStart = inset + size;
        var verticalLength = frameHeight - 2 * verticalStart;
        AddWeavingRibbonRail(fills, PageBorderArtSpriteMasks.WeavingRibbonMasks[PageBorderArtSpriteMasks.WeavingRibbonTopRail], horizontalStart, horizontalLength, inset, size, horizontal: true);
        AddWeavingRibbonRail(fills, PageBorderArtSpriteMasks.WeavingRibbonMasks[PageBorderArtSpriteMasks.WeavingRibbonBottomRail], horizontalStart, horizontalLength, frameHeight - inset - size, size, horizontal: true);
        AddWeavingRibbonRail(fills, PageBorderArtSpriteMasks.WeavingRibbonMasks[PageBorderArtSpriteMasks.WeavingRibbonLeftRail], verticalStart, verticalLength, inset, size, horizontal: false);
        AddWeavingRibbonRail(fills, PageBorderArtSpriteMasks.WeavingRibbonMasks[PageBorderArtSpriteMasks.WeavingRibbonRightRail], verticalStart, verticalLength, frameWidth - inset - size, size, horizontal: false);
        AddMaterialMask(fills, PageBorderArtSpriteMasks.WeavingRibbonMasks[PageBorderArtSpriteMasks.WeavingRibbonTopLeftCorner], inset, inset, size, horizontal: true);
        AddMaterialMask(fills, PageBorderArtSpriteMasks.WeavingRibbonMasks[PageBorderArtSpriteMasks.WeavingRibbonTopRightCorner], frameWidth - inset - size, inset, size, horizontal: true);
        AddMaterialMask(fills, PageBorderArtSpriteMasks.WeavingRibbonMasks[PageBorderArtSpriteMasks.WeavingRibbonBottomLeftCorner], inset, frameHeight - inset - size, size, horizontal: true);
        AddMaterialMask(fills, PageBorderArtSpriteMasks.WeavingRibbonMasks[PageBorderArtSpriteMasks.WeavingRibbonBottomRightCorner], frameWidth - inset - size, frameHeight - inset - size, size, horizontal: true);
        plan = new PageBorderArtFilledShapePlan(fills, polygons);
        return true;
    }

    public static bool TryBuildPapyrusFrame(
        int artId,
        double modelWidthPt,
        double frameWidthDip,
        double frameHeightDip,
        double edgeInsetDip,
        out PageBorderArtFilledShapePlan plan)
    {
        if (artId != PapyrusArtId)
        {
            plan = new PageBorderArtFilledShapePlan([], []);
            return false;
        }

        var frameWidth = Math.Max(0, frameWidthDip);
        var frameHeight = Math.Max(0, frameHeightDip);
        var inset = Math.Max(0, edgeInsetDip);
        var size = ResolveMotifSize(modelWidthPt);
        if (frameWidth < 2 * (inset + size) || frameHeight < 2 * (inset + size))
        {
            plan = new PageBorderArtFilledShapePlan([], []);
            return true;
        }

        var scale = size / 32.0;
        var railOffset = 7 * scale;
        var railThickness = 17 * scale;
        var innerOffset = 4 * scale;
        var innerThickness = 9 * scale;
        var horizontalLength = frameWidth - 2 * inset;
        var verticalLength = frameHeight - 2 * inset;
        var left = inset + railOffset;
        var right = frameWidth - inset - railOffset - railThickness;
        var top = inset + railOffset;
        var bottom = frameHeight - inset - railOffset - railThickness;
        var innerStart = inset + size;
        var innerHorizontalLength = frameWidth - 2 * innerStart;
        var innerVerticalLength = frameHeight - 2 * innerStart;

        var fills = new List<PageBorderArtFillRectangle>
        {
            new(inset, top, horizontalLength, railThickness, 0, 0, 0),
            new(inset, bottom, horizontalLength, railThickness, 0, 0, 0),
            new(left, inset, railThickness, verticalLength, 0, 0, 0),
            new(right, inset, railThickness, verticalLength, 0, 0, 0),
            new(innerStart, top + innerOffset, innerHorizontalLength, innerThickness, 0xFF, 0xFF, 0xFF),
            new(innerStart, bottom + innerOffset, innerHorizontalLength, innerThickness, 0xFF, 0xFF, 0xFF),
            new(left + innerOffset, innerStart, innerThickness, innerVerticalLength, 0xFF, 0xFF, 0xFF),
            new(right + innerOffset, innerStart, innerThickness, innerVerticalLength, 0xFF, 0xFF, 0xFF),
        };
        var polygons = new List<PageBorderArtPolygon>();
        AddPapyrusRailTiles(polygons, innerStart, innerHorizontalLength, top + innerOffset, innerThickness, size, horizontal: true);
        AddPapyrusRailTiles(polygons, innerStart, innerHorizontalLength, bottom + innerOffset, innerThickness, size, horizontal: true);
        AddPapyrusRailTiles(polygons, innerStart, innerVerticalLength, left + innerOffset, innerThickness, size, horizontal: false);
        AddPapyrusRailTiles(polygons, innerStart, innerVerticalLength, right + innerOffset, innerThickness, size, horizontal: false);

        AddPapyrusCorner(polygons, inset, inset, size);
        AddPapyrusCorner(polygons, frameWidth - inset - size, inset, size);
        AddPapyrusCorner(polygons, inset, frameHeight - inset - size, size);
        AddPapyrusCorner(polygons, frameWidth - inset - size, frameHeight - inset - size, size);

        plan = new PageBorderArtFilledShapePlan(fills, polygons);
        return true;
    }

    public static bool TryBuildVineFrame(
        int artId,
        double modelWidthPt,
        double frameWidthDip,
        double frameHeightDip,
        double edgeInsetDip,
        out PageBorderArtFilledShapePlan plan)
    {
        if (artId != VineArtId)
        {
            plan = new PageBorderArtFilledShapePlan([], []);
            return false;
        }

        var frameWidth = Math.Max(0, frameWidthDip);
        var frameHeight = Math.Max(0, frameHeightDip);
        var inset = Math.Max(0, edgeInsetDip);
        var size = ResolveMotifSize(modelWidthPt);
        if (frameWidth < 2 * (inset + size) || frameHeight < 2 * (inset + size))
        {
            plan = new PageBorderArtFilledShapePlan([], []);
            return true;
        }

        var fills = new List<PageBorderArtFillRectangle>
        {
            new(inset, inset, frameWidth - 2 * inset, size, 0, 0, 0),
            new(inset, frameHeight - inset - size, frameWidth - 2 * inset, size, 0, 0, 0),
            new(inset, inset, size, frameHeight - 2 * inset, 0, 0, 0),
            new(frameWidth - inset - size, inset, size, frameHeight - 2 * inset, 0, 0, 0),
        };
        var polygons = new List<PageBorderArtPolygon>();
        var horizontalStart = inset + size;
        var horizontalLength = frameWidth - 2 * horizontalStart;
        var verticalStart = inset + size;
        var verticalLength = frameHeight - 2 * verticalStart;
        AddVineRail(fills, VineRailMask, horizontalStart, horizontalLength, inset, size, horizontal: true, reverseAcross: false);
        AddVineRail(fills, VineBottomRailMask, horizontalStart, horizontalLength, frameHeight - inset - size, size, horizontal: true, reverseAcross: false);
        AddVineRail(fills, VineLeftRailMask, verticalStart, verticalLength, inset, size, horizontal: false, reverseAcross: false);
        AddVineRail(fills, VineRailMask, verticalStart, verticalLength, frameWidth - inset - size, size, horizontal: false, reverseAcross: true);
        AddVineCorner(fills, inset, inset, size, VineCornerMasks[0]);
        AddVineCorner(fills, frameWidth - inset - size, inset, size, VineCornerMasks[1]);
        AddVineCorner(fills, inset, frameHeight - inset - size, size, VineCornerMasks[2]);
        AddVineCorner(fills, frameWidth - inset - size, frameHeight - inset - size, size, VineCornerMasks[3]);

        plan = new PageBorderArtFilledShapePlan(fills, polygons);
        return true;
    }

    public static bool TryBuildDecorativeArchFrame(
        int artId,
        double modelWidthPt,
        double frameWidthDip,
        double frameHeightDip,
        double edgeInsetDip,
        out PageBorderDecorativeArchPlan plan)
    {
        if (artId != DecorativeArchArtId)
        {
            plan = new PageBorderDecorativeArchPlan([], []);
            return false;
        }

        var frameWidth = Math.Max(0, frameWidthDip);
        var frameHeight = Math.Max(0, frameHeightDip);
        var inset = Math.Max(0, edgeInsetDip);
        var size = ResolveMotifSize(modelWidthPt);
        if (frameWidth < 2 * (inset + size) || frameHeight < 2 * (inset + size))
        {
            plan = new PageBorderDecorativeArchPlan([], []);
            return true;
        }

        var fills = new List<PageBorderArtFillRectangle>();
        var strokes = new List<PageBorderArtCubicStroke>();
        var left = inset + size * 0.15625;
        var right = frameWidth - inset - size + size * 0.15625;
        var top = inset + size * 0.25;
        var bottom = frameHeight - inset - size + size * 0.25;
        var horizontalStart = inset + size / 2.0;
        var horizontalWidth = frameWidth - 2 * horizontalStart;
        var verticalStart = inset + size / 2.0;
        var verticalHeight = frameHeight - 2 * verticalStart;

        AddHorizontalRail(fills, horizontalStart, top, horizontalWidth, bottom: false);
        AddHorizontalRail(fills, horizontalStart, bottom, horizontalWidth, bottom: true);
        AddVerticalRail(fills, left, verticalStart, verticalHeight);
        AddVerticalRail(fills, right, verticalStart, verticalHeight);
        AddDecorativeArchCorner(fills, strokes, inset, inset, size, flipX: false, flipY: false);
        AddDecorativeArchCorner(fills, strokes, frameWidth - inset - size, inset, size, flipX: true, flipY: false);
        AddDecorativeArchCorner(fills, strokes, inset, frameHeight - inset - size, size, flipX: false, flipY: true);
        AddDecorativeArchCorner(fills, strokes, frameWidth - inset - size, frameHeight - inset - size, size, flipX: true, flipY: true);
        plan = new PageBorderDecorativeArchPlan(fills, strokes);
        return true;
    }

    public static bool TryBuildHandmade2Frame(
        int artId,
        double modelWidthPt,
        double frameWidthDip,
        double frameHeightDip,
        double edgeInsetDip,
        out PageBorderDecorativeArchPlan plan)
    {
        if (artId != Handmade2ArtId)
        {
            plan = new PageBorderDecorativeArchPlan([], []);
            return false;
        }

        var width = Math.Max(0, frameWidthDip);
        var height = Math.Max(0, frameHeightDip);
        var inset = Math.Max(0, edgeInsetDip);
        var scale = ResolveMotifSize(modelWidthPt) / 32.0;
        var outerLeft = inset + 4 * scale;
        var outerTop = inset + 5 * scale;
        var outerRight = width - inset - 5 * scale;
        var outerBottom = height - inset - 5 * scale;
        var innerLeft = inset + 12 * scale;
        var innerTop = inset + 13 * scale;
        var innerRight = width - inset - 12 * scale;
        var innerBottom = height - inset - 13 * scale;
        if (outerRight <= outerLeft || outerBottom <= outerTop
            || innerRight <= innerLeft || innerBottom <= innerTop)
        {
            plan = new PageBorderDecorativeArchPlan([], []);
            return true;
        }

        var strokes = new List<PageBorderArtCubicStroke>();
        AddHandmadeFrame(strokes, outerLeft, outerTop, outerRight, outerBottom, 3 * scale, 2.5 * scale);
        AddHandmadeFrame(strokes, innerLeft, innerTop, innerRight, innerBottom, 2 * scale, 1.5 * scale);
        plan = new PageBorderDecorativeArchPlan([], strokes);
        return true;
    }

    private static void AddHandmadeFrame(
        List<PageBorderArtCubicStroke> strokes,
        double left,
        double top,
        double right,
        double bottom,
        double strokeWidth,
        double wobble)
    {
        var horizontal = right - left;
        var vertical = bottom - top;
        strokes.Add(new PageBorderArtCubicStroke(
            left, top,
            left + horizontal * 0.33, top - wobble,
            left + horizontal * 0.67, top + wobble * 0.4,
            right, top,
            strokeWidth, 0, 0, 0));
        strokes.Add(new PageBorderArtCubicStroke(
            right, top,
            right - wobble * 0.6, top + vertical * 0.33,
            right + wobble, top + vertical * 0.67,
            right, bottom,
            strokeWidth, 0, 0, 0));
        strokes.Add(new PageBorderArtCubicStroke(
            right, bottom,
            right - horizontal * 0.33, bottom + wobble,
            left + horizontal * 0.33, bottom - wobble * 0.4,
            left, bottom,
            strokeWidth, 0, 0, 0));
        strokes.Add(new PageBorderArtCubicStroke(
            left, bottom,
            left + wobble * 0.6, bottom - vertical * 0.33,
            left - wobble, top + vertical * 0.33,
            left, top,
            strokeWidth, 0, 0, 0));
    }

    private static IReadOnlyList<PageBorderArtPlacement> BuildFrame(
        double frameWidthDip,
        double frameHeightDip,
        double edgeInsetDip,
        double modelWidthPt)
    {
        var frameWidth = Math.Max(0, frameWidthDip);
        var frameHeight = Math.Max(0, frameHeightDip);
        var inset = Math.Max(0, edgeInsetDip);
        var motifSize = ResolveMotifSize(modelWidthPt);
        var horizontalLength = frameWidth - 2 * inset;
        var verticalLength = frameHeight - 2 * inset;
        if (horizontalLength < motifSize || verticalLength < motifSize)
            return [];

        var result = new List<PageBorderArtPlacement>();
        AddEdge(result, inset, inset, horizontalLength, motifSize, horizontal: true);
        AddEdge(result, inset, frameHeight - inset - motifSize, horizontalLength, motifSize, horizontal: true);
        AddEdge(result, inset, inset, verticalLength, motifSize, horizontal: false, skipEnds: true);
        AddEdge(result, frameWidth - inset - motifSize, inset, verticalLength, motifSize, horizontal: false, skipEnds: true);
        return result;
    }

    private static double ResolveMotifSize(double modelWidthPt) =>
        Math.Clamp(
            Math.Max(0, modelWidthPt) * ArtSizeUnitsPerModelPoint * DipPerPoint,
            MinimumMotifSizeDip,
            MaximumMotifSizeDip);

    private static void AddShorebirdEdge(
        List<PageBorderShorebirdTrackMotif> motifs,
        double start,
        double end,
        double fixedCenter,
        int count,
        double size,
        int quarterTurns)
    {
        var step = count > 1 ? (end - start) / (count - 1) : 0;
        var lateral = size * 0.203125;
        for (var index = 0; index < count; index++)
        {
            var along = start + index * step;
            var localLateral = index % 2 == 0 ? lateral : -lateral;
            var offset = Rotate(0, localLateral, quarterTurns);
            motifs.Add(quarterTurns % 2 == 0
                ? new PageBorderShorebirdTrackMotif(along + offset.X, fixedCenter + offset.Y, size, quarterTurns)
                : new PageBorderShorebirdTrackMotif(fixedCenter + offset.X, along + offset.Y, size, quarterTurns));
        }
    }

    private static void AddHorizontalRail(
        List<PageBorderArtFillRectangle> fills,
        double x,
        double y,
        double width,
        bool bottom)
    {
        if (!bottom)
        {
            AddFill(fills, x, y, width, 1, 0x33);
            AddFill(fills, x, y + 1, width, 6, 0xCC);
            AddFill(fills, x, y + 7, width, 8, 0x00);
            AddFill(fills, x, y + 15, width, 5, 0xCC);
            AddFill(fills, x, y + 20, width, 1, 0x60);
            return;
        }

        AddFill(fills, x, y, width, 1, 0x20);
        AddFill(fills, x, y + 1, width, 1, 0xB2);
        AddFill(fills, x, y + 2, width, 5, 0xCC);
        AddFill(fills, x, y + 7, width, 8, 0x00);
        AddFill(fills, x, y + 15, width, 5, 0xCC);
        AddFill(fills, x, y + 20, width, 1, 0x00);
    }

    private static void AddPapyrusRailTiles(
        List<PageBorderArtPolygon> polygons,
        double alongStart,
        double alongLength,
        double acrossStart,
        double innerThickness,
        double size,
        bool horizontal)
    {
        var scale = size / 32.0;
        var ovalWidth = 24 * scale;
        var endInset = 4 * scale;
        var count = Math.Max(1, (int)Math.Floor((alongLength - 0.01) / size));
        var first = alongStart + endInset;
        var last = alongStart + alongLength - endInset - ovalWidth;
        var step = count > 1 ? (last - first) / (count - 1) : 0;
        for (var index = 0; index < count; index++)
        {
            var ovalStart = first + index * step;
            AddPapyrusOval(polygons, ovalStart, acrossStart, ovalWidth, innerThickness, horizontal);
            AddPapyrusHourglass(polygons, ovalStart - endInset, acrossStart, innerThickness, scale, horizontal);
        }

        AddPapyrusHourglass(polygons, last + ovalWidth + endInset, acrossStart, innerThickness, scale, horizontal);
    }

    private static void AddPapyrusOval(
        List<PageBorderArtPolygon> polygons,
        double alongStart,
        double acrossStart,
        double alongLength,
        double acrossLength,
        bool horizontal)
    {
        PageBorderArtPoint Point(double along, double across) => horizontal
            ? new PageBorderArtPoint(alongStart + along, acrossStart + across)
            : new PageBorderArtPoint(acrossStart + across, alongStart + along);

        polygons.Add(new PageBorderArtPolygon(
            [
                Point(0, acrossLength * 0.5),
                Point(alongLength * 0.08, acrossLength * 0.22),
                Point(alongLength * 0.29, acrossLength * 0.05),
                Point(alongLength * 0.71, acrossLength * 0.05),
                Point(alongLength * 0.92, acrossLength * 0.22),
                Point(alongLength, acrossLength * 0.5),
                Point(alongLength * 0.92, acrossLength * 0.78),
                Point(alongLength * 0.71, acrossLength * 0.95),
                Point(alongLength * 0.29, acrossLength * 0.95),
                Point(alongLength * 0.08, acrossLength * 0.78),
            ],
            0x7F, 0x7F, 0x7F));
    }

    private static void AddPapyrusHourglass(
        List<PageBorderArtPolygon> polygons,
        double center,
        double acrossStart,
        double acrossLength,
        double scale,
        bool horizontal)
    {
        var outer = 7 * scale;
        var inner = 4 * scale;

        PageBorderArtPoint Point(double along, double across) => horizontal
            ? new PageBorderArtPoint(center + along, acrossStart + across)
            : new PageBorderArtPoint(acrossStart + across, center + along);

        polygons.Add(new PageBorderArtPolygon(
            [
                Point(-outer, 0),
                Point(outer, 0),
                Point(inner, acrossLength * 0.5),
                Point(outer, acrossLength),
                Point(-outer, acrossLength),
                Point(-inner, acrossLength * 0.5),
            ],
            0, 0, 0));
    }

    private static void AddPapyrusCorner(
        List<PageBorderArtPolygon> polygons,
        double x,
        double y,
        double size)
    {
        var scale = size / 32.0;
        var centerX = x + 16 * scale;
        var centerY = y + 16 * scale;
        var points = new (double X, double Y)[]
        {
            (16, 2), (19, 11), (25, 5), (22, 13),
            (30, 12), (23, 16), (30, 20), (22, 19),
            (25, 27), (19, 21), (16, 30), (13, 21),
            (7, 27), (10, 19), (2, 20), (9, 16),
            (2, 12), (10, 13), (7, 5), (13, 11),
        };
        polygons.Add(new PageBorderArtPolygon(
            points.Select(point => new PageBorderArtPoint(x + point.X * scale, y + point.Y * scale)).ToList(),
            0xFF, 0xFF, 0xFF));
        polygons.Add(new PageBorderArtPolygon(
            [
                new PageBorderArtPoint(centerX, y + 9 * scale),
                new PageBorderArtPoint(x + 23 * scale, centerY),
                new PageBorderArtPoint(centerX, y + 23 * scale),
                new PageBorderArtPoint(x + 9 * scale, centerY),
            ],
            0x7F, 0x7F, 0x7F));
    }

    private static void AddVineRail(
        List<PageBorderArtFillRectangle> fills,
        IReadOnlyList<string> maskRows,
        double alongStart,
        double alongLength,
        double acrossStart,
        double size,
        bool horizontal,
        bool reverseAcross)
    {
        var motifLength = size * 1.5;
        var count = Math.Max(1, (int)Math.Floor((alongLength - 0.01) / motifLength));
        var step = count > 1 ? (alongLength - motifLength) / (count - 1) : 0;
        for (var index = 0; index < count; index++)
        {
            var motifStart = alongStart + index * step;
            var scale = size / 32.0;
            for (var row = 0; row < maskRows.Count; row++)
            {
                var mask = maskRows[row];
                var runStart = -1;
                for (var column = 0; column <= mask.Length; column++)
                {
                    var isFilled = column < mask.Length && mask[column] == '#';
                    if (isFilled && runStart < 0)
                        runStart = column;
                    if (isFilled || runStart < 0)
                        continue;

                    var runLength = column - runStart;
                    var across = reverseAcross ? size - (row + 1) * scale : row * scale;
                    fills.Add(horizontal
                        ? new PageBorderArtFillRectangle(
                            motifStart + runStart * scale,
                            acrossStart + across,
                            runLength * scale,
                            scale,
                            0xFF, 0xFF, 0xFF)
                        : new PageBorderArtFillRectangle(
                            acrossStart + across,
                            motifStart + runStart * scale,
                            scale,
                            runLength * scale,
                            0xFF, 0xFF, 0xFF));
                    runStart = -1;
                }
            }
        }
    }

    private static void AddCandyCorn(
        List<PageBorderArtPolygon> polygons,
        double x,
        double y,
        double size,
        int quarterTurns)
    {
        var scale = size / 16.0;
        PageBorderArtPoint Point(double px, double py)
        {
            var rotated = Rotate((px - 8) * scale, (py - 8) * scale, quarterTurns);
            return new PageBorderArtPoint(x + 8 * scale + rotated.X, y + 8 * scale + rotated.Y);
        }

        void Add(byte red, byte green, byte blue, params (double X, double Y)[] points) =>
            polygons.Add(new PageBorderArtPolygon(
                points.Select(point => Point(point.X, point.Y)).ToList(), red, green, blue));

        Add(0, 0, 0,
            (8, 0), (11, 2), (14, 6), (16, 11), (14, 14), (11, 16),
            (5, 16), (2, 14), (0, 11), (2, 6), (5, 2));
        Add(0xF5, 0xC6, 0x0A,
            (2, 10), (14, 10), (14, 13), (11, 15), (5, 15), (2, 13));
        Add(0xFE, 0x45, 0x01,
            (3, 4), (13, 4), (15, 10), (1, 10));
        Add(0xFF, 0xFF, 0xFF,
            (8, 1), (11, 3), (13, 5), (3, 5), (5, 3));
    }

    private static void AddVineCorner(
        List<PageBorderArtFillRectangle> fills,
        double x,
        double y,
        double size,
        IReadOnlyList<string> mask)
    {
        var scale = size / 32.0;
        for (var row = 0; row < mask.Count; row++)
        {
            var runStart = -1;
            for (var column = 0; column <= mask[row].Length; column++)
            {
                var isFilled = column < mask[row].Length && mask[row][column] == '#';
                if (isFilled && runStart < 0)
                    runStart = column;
                if (isFilled || runStart < 0)
                    continue;

                fills.Add(new PageBorderArtFillRectangle(
                    x + runStart * scale,
                    y + row * scale,
                    (column - runStart) * scale,
                    scale,
                    0xFF, 0xFF, 0xFF));
                runStart = -1;
            }
        }
    }

    private static void AddWeavingRibbonRail(
        List<PageBorderArtFillRectangle> fills,
        IReadOnlyList<byte> mask,
        double alongStart,
        double alongLength,
        double acrossStart,
        double size,
        bool horizontal)
    {
        var count = Math.Max(1, (int)Math.Floor((alongLength - 0.01) / size));
        var step = count > 1 ? (alongLength - size) / (count - 1) : 0;
        for (var index = 0; index < count; index++)
        {
            var along = alongStart + index * step;
            AddMaterialMask(
                fills,
                mask,
                horizontal ? along : acrossStart,
                horizontal ? acrossStart : along,
                size,
                horizontal);
        }
    }

    private static void AddMaterialMask(
        List<PageBorderArtFillRectangle> fills,
        IReadOnlyList<byte> mask,
        double x,
        double y,
        double size,
        bool horizontal,
        byte transparentMaterial = 0,
        (byte Red, byte Green, byte Blue)? material0 = null,
        (byte Red, byte Green, byte Blue)? material1 = null,
        (byte Red, byte Green, byte Blue)? material2 = null)
    {
        var color0 = material0 ?? (0x00, 0x00, 0x00);
        var color1 = material1 ?? (0xC0, 0xC0, 0xC0);
        var color2 = material2 ?? (0xFF, 0xFF, 0xFF);
        var scale = size / PageBorderArtSpriteMasks.MaskSize;
        for (var row = 0; row < PageBorderArtSpriteMasks.MaskSize; row++)
        {
            var runStart = -1;
            var material = transparentMaterial;
            for (var column = 0; column <= PageBorderArtSpriteMasks.MaskSize; column++)
            {
                var next = column < PageBorderArtSpriteMasks.MaskSize
                    ? mask[row * PageBorderArtSpriteMasks.MaskSize + column]
                    : transparentMaterial;
                if (next != material && runStart >= 0)
                {
                    var runLength = column - runStart;
                    var color = material switch
                    {
                        0 => color0,
                        1 => color1,
                        2 => color2,
                        _ => ((byte)0xFF, (byte)0xFF, (byte)0xFF),
                    };
                    fills.Add(horizontal
                        ? new PageBorderArtFillRectangle(
                            x + runStart * scale,
                            y + row * scale,
                            runLength * scale,
                            scale,
                            color.Item1, color.Item2, color.Item3)
                        : new PageBorderArtFillRectangle(
                            x + row * scale,
                            y + runStart * scale,
                            scale,
                            runLength * scale,
                            color.Item1, color.Item2, color.Item3));
                    runStart = -1;
                }
                if (next != material)
                {
                    material = next;
                    if (material != transparentMaterial)
                        runStart = column;
                }
            }
        }
    }

    private static void AddIndexedPaletteMask(
        List<PageBorderArtFillRectangle> fills,
        IReadOnlyList<byte> mask,
        IReadOnlyList<(byte Red, byte Green, byte Blue)?> palette,
        double x,
        double y,
        double size,
        bool horizontal,
        byte transparentMaterial)
    {
        var scale = size / PageBorderArtSpriteMasks.MaskSize;
        for (var row = 0; row < PageBorderArtSpriteMasks.MaskSize; row++)
        {
            var runStart = -1;
            var material = transparentMaterial;
            for (var column = 0; column <= PageBorderArtSpriteMasks.MaskSize; column++)
            {
                var next = column < PageBorderArtSpriteMasks.MaskSize
                    ? mask[row * PageBorderArtSpriteMasks.MaskSize + column]
                    : transparentMaterial;
                if (next != material && runStart >= 0)
                {
                    var color = palette[material]!.Value;
                    var runLength = column - runStart;
                    fills.Add(horizontal
                        ? new PageBorderArtFillRectangle(
                            x + runStart * scale,
                            y + row * scale,
                            runLength * scale,
                            scale,
                            color.Red, color.Green, color.Blue)
                        : new PageBorderArtFillRectangle(
                            x + row * scale,
                            y + runStart * scale,
                            scale,
                            runLength * scale,
                            color.Red, color.Green, color.Blue));
                    runStart = -1;
                }
                if (next != material)
                {
                    material = next;
                    if (material >= palette.Count)
                        throw new InvalidOperationException("Page-border art mask references an unknown material.");
                    if (palette[material].HasValue)
                        runStart = column;
                }
            }
        }
    }

    private static void AddVerticalRail(
        List<PageBorderArtFillRectangle> fills,
        double x,
        double y,
        double height)
    {
        AddFill(fills, x, y, 1, height, 0x00);
        AddFill(fills, x + 1, y, 6, height, 0xB2);
        AddFill(fills, x + 7, y, 8, height, 0x00);
        AddFill(fills, x + 15, y, 5, height, 0xB2);
        AddFill(fills, x + 20, y, 1, height, 0x00);
    }

    private static void AddDecorativeArchCorner(
        List<PageBorderArtFillRectangle> fills,
        List<PageBorderArtCubicStroke> strokes,
        double x,
        double y,
        double size,
        bool flipX,
        bool flipY)
    {
        var scale = size / 32.0;
        var tileX = x + 5 * scale;
        var tileY = y;
        AddFill(fills, tileX, tileY, 21 * scale, 32 * scale, 0x00);

        (double X, double Y) Point(double localX, double localY)
        {
            var px = flipX ? 32 - localX : localX;
            var py = flipY ? 32 - localY : localY;
            return (x + px * scale, y + py * scale);
        }

        var start = Point(6, 30);
        var control1 = Point(6, 8);
        var control2 = Point(26, 8);
        var end = Point(26, 30);
        AddStroke(strokes, start, control1, control2, end, 10 * scale, 0x00);
        AddStroke(strokes, start, control1, control2, end, 8 * scale, 0xB2);
        AddStroke(strokes, start, control1, control2, end, 4 * scale, 0xFF);
        AddStroke(strokes, start, control1, control2, end, 1 * scale, 0x00);
    }

    private static void AddFill(
        List<PageBorderArtFillRectangle> fills,
        double x,
        double y,
        double width,
        double height,
        byte gray) =>
        fills.Add(new PageBorderArtFillRectangle(x, y, width, height, gray, gray, gray));

    private static void AddStroke(
        List<PageBorderArtCubicStroke> strokes,
        (double X, double Y) start,
        (double X, double Y) control1,
        (double X, double Y) control2,
        (double X, double Y) end,
        double width,
        byte gray) =>
        strokes.Add(new PageBorderArtCubicStroke(
            start.X, start.Y,
            control1.X, control1.Y,
            control2.X, control2.Y,
            end.X, end.Y,
            width,
            gray, gray, gray));

    private static (double X, double Y) Rotate(double x, double y, int quarterTurns) =>
        (((quarterTurns % 4) + 4) % 4) switch
        {
            1 => (-y, x),
            2 => (-x, -y),
            3 => (y, -x),
            _ => (x, y),
        };

    private static void AddEdge(
        List<PageBorderArtPlacement> motifs,
        double x,
        double y,
        double availableLength,
        double motifSize,
        bool horizontal,
        bool skipEnds = false)
    {
        // Word drops the final repeat when the available span is an exact multiple of the art size,
        // then distributes the remaining motifs evenly across the edge. The tiny epsilon preserves
        // that boundary behavior without changing ordinary non-integral spans.
        var count = Math.Max(1, (int)Math.Floor((availableLength - 0.01) / motifSize));
        var step = count > 1 ? (availableLength - motifSize) / (count - 1) : 0;
        var first = skipEnds ? 1 : 0;
        var end = skipEnds ? count - 1 : count;
        for (var index = first; index < end; index++)
        {
            motifs.Add(new PageBorderArtPlacement(
                horizontal ? x + index * step : x,
                horizontal ? y : y + index * step,
                motifSize));
        }
    }

    private sealed record PageBorderArtPlacement(double Xdip, double Ydip, double SizeDip);
}

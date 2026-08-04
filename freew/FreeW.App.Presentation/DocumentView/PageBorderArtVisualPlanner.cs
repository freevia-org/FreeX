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
    byte Blue);

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

public sealed record PageBorderDecorativeArchPlan(
    IReadOnlyList<PageBorderArtFillRectangle> Fills,
    IReadOnlyList<PageBorderArtCubicStroke> Strokes);

public sealed record PageBorderArtFilledShapePlan(
    IReadOnlyList<PageBorderArtFillRectangle> Fills,
    IReadOnlyList<PageBorderArtPolygon> Polygons);

public static class PageBorderArtVisualPlanner
{
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

        var polygons = new List<PageBorderArtPolygon>();
        foreach (var placement in BuildFrame(frameWidthDip, frameHeightDip, edgeInsetDip, modelWidthPt))
            AddMapleMuffin(polygons, placement.Xdip, placement.Ydip, placement.SizeDip);
        plan = new PageBorderArtFilledShapePlan([], polygons);
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

        var polygons = new List<PageBorderArtPolygon>();
        foreach (var placement in BuildFrame(frameWidthDip, frameHeightDip, edgeInsetDip, modelWidthPt))
            AddCakeSlice(polygons, placement.Xdip, placement.Ydip, placement.SizeDip);
        plan = new PageBorderArtFilledShapePlan([], polygons);
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

        var polygons = new List<PageBorderArtPolygon>();
        foreach (var placement in BuildFrame(frameWidthDip, frameHeightDip, edgeInsetDip, modelWidthPt))
            AddBirdInFlight(polygons, placement.Xdip, placement.Ydip, placement.SizeDip);
        plan = new PageBorderArtFilledShapePlan([], polygons);
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

        var polygons = new List<PageBorderArtPolygon>();
        foreach (var placement in BuildFrame(frameWidthDip, frameHeightDip, edgeInsetDip, modelWidthPt))
            AddPaintedEgg(polygons, placement.Xdip, placement.Ydip, placement.SizeDip);
        plan = new PageBorderArtFilledShapePlan([], polygons);
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

        var polygons = new List<PageBorderArtPolygon>();
        foreach (var placement in BuildFrame(frameWidthDip, frameHeightDip, edgeInsetDip, modelWidthPt))
            AddIceCreamCone(polygons, placement.Xdip, placement.Ydip, placement.SizeDip);
        plan = new PageBorderArtFilledShapePlan([], polygons);
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

        var polygons = new List<PageBorderArtPolygon>();
        foreach (var placement in BuildFrame(frameWidthDip, frameHeightDip, edgeInsetDip, modelWidthPt))
            AddPerson(polygons, placement.Xdip, placement.Ydip, placement.SizeDip);
        plan = new PageBorderArtFilledShapePlan([], polygons);
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

        var polygons = new List<PageBorderArtPolygon>();
        foreach (var placement in BuildFrame(frameWidthDip, frameHeightDip, edgeInsetDip, modelWidthPt))
            AddRose(polygons, placement.Xdip, placement.Ydip, placement.SizeDip);
        plan = new PageBorderArtFilledShapePlan([], polygons);
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
        AddWeavingRibbonMask(fills, PageBorderArtSpriteMasks.WeavingRibbonMasks[PageBorderArtSpriteMasks.WeavingRibbonTopLeftCorner], inset, inset, size, horizontal: true);
        AddWeavingRibbonMask(fills, PageBorderArtSpriteMasks.WeavingRibbonMasks[PageBorderArtSpriteMasks.WeavingRibbonTopRightCorner], frameWidth - inset - size, inset, size, horizontal: true);
        AddWeavingRibbonMask(fills, PageBorderArtSpriteMasks.WeavingRibbonMasks[PageBorderArtSpriteMasks.WeavingRibbonBottomLeftCorner], inset, frameHeight - inset - size, size, horizontal: true);
        AddWeavingRibbonMask(fills, PageBorderArtSpriteMasks.WeavingRibbonMasks[PageBorderArtSpriteMasks.WeavingRibbonBottomRightCorner], frameWidth - inset - size, frameHeight - inset - size, size, horizontal: true);
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

    private static void AddMapleMuffin(
        List<PageBorderArtPolygon> polygons,
        double x,
        double y,
        double size)
    {
        var scale = size / 32.0;
        PageBorderArtPoint Point(double px, double py) => new(x + px * scale, y + py * scale);
        void Add(byte red, byte green, byte blue, params (double X, double Y)[] points) =>
            polygons.Add(new PageBorderArtPolygon(
                points.Select(point => Point(point.X, point.Y)).ToList(),
                red,
                green,
                blue));

        Add(0, 0, 0,
            (5, 13), (3, 12), (2, 9), (3, 6), (6, 4), (11, 4), (12, 2), (20, 2),
            (21, 4), (26, 4), (29, 6), (30, 9), (29, 12), (27, 13), (26, 17), (6, 17));
        Add(0xFF, 0x80, 0,
            (5, 12), (4, 10), (4, 7), (7, 5), (12, 5), (13, 4), (19, 4), (20, 5),
            (25, 5), (28, 7), (28, 10), (26, 11), (22, 10), (19, 11), (16, 10),
            (13, 11), (9, 10), (6, 11));
        Add(0xBF, 0x40, 0,
            (6, 12), (10, 11), (13, 12), (16, 11), (19, 12), (22, 11), (26, 12),
            (25, 15), (7, 15));
        Add(0, 0, 0, (8, 14), (24, 14), (22, 31), (10, 31));
        Add(0xFF, 0x80, 0, (10, 15), (22, 15), (20, 29), (12, 29));
        Add(0xBF, 0x40, 0, (11, 16), (13, 16), (14, 28), (12, 28));
        Add(0xBF, 0x40, 0, (15, 16), (17, 16), (18, 28), (15, 28));
        Add(0xBF, 0x40, 0, (19, 16), (21, 16), (20, 28), (18, 28));
    }

    private static void AddCakeSlice(
        List<PageBorderArtPolygon> polygons,
        double x,
        double y,
        double size)
    {
        var scale = size / 32.0;
        PageBorderArtPoint Point(double px, double py) => new(x + px * scale, y + py * scale);
        void Add(byte red, byte green, byte blue, params (double X, double Y)[] points) =>
            polygons.Add(new PageBorderArtPolygon(
                points.Select(point => Point(point.X, point.Y)).ToList(),
                red,
                green,
                blue));

        Add(0, 0, 0,
            (7, 4), (12, 2), (17, 4), (22, 0), (26, 2), (27, 5), (31, 9), (31, 15),
            (29, 18), (29, 24), (26, 26), (25, 32), (20, 31), (17, 29), (11, 28),
            (6, 25), (2, 24), (1, 20), (3, 16), (3, 12), (5, 8));
        Add(0xFF, 0xEE, 0xCA,
            (5, 10), (9, 7), (17, 9), (22, 10), (27, 15), (26, 18), (23, 20),
            (17, 18), (11, 16), (6, 14));
        Add(0xFF, 0x99, 0xC2,
            (8, 6), (13, 5), (19, 7), (22, 4), (25, 5), (26, 8), (29, 10), (29, 14),
            (27, 16), (24, 13), (20, 10), (14, 9), (9, 9));
        Add(0, 0, 0,
            (9, 14), (15, 15), (21, 18), (25, 20), (25, 22), (21, 21), (15, 18), (9, 16));
        Add(0xFF, 0xEE, 0xCA,
            (4, 19), (8, 17), (14, 20), (22, 23), (26, 25), (25, 28), (21, 29),
            (16, 27), (10, 26), (4, 23));
    }

    private static void AddBirdInFlight(
        List<PageBorderArtPolygon> polygons,
        double x,
        double y,
        double size)
    {
        var scale = size / 32.0;
        PageBorderArtPoint Point(double px, double py) => new(x + px * scale, y + py * scale);
        polygons.Add(new PageBorderArtPolygon(
            [
                Point(2, 3), Point(7, 5), Point(14, 16), Point(17, 12), Point(23, 6), Point(25, 0),
                Point(25, 6), Point(22, 13), Point(30, 7), Point(31, 8), Point(24, 15), Point(22, 19),
                Point(31, 23), Point(32, 26), Point(26, 26), Point(20, 24), Point(20, 29), Point(27, 32),
                Point(20, 31), Point(16, 26), Point(11, 31), Point(8, 32), Point(8, 27), Point(11, 22),
                Point(7, 18), Point(6, 13), Point(6, 7),
            ],
            0x04,
            0x07,
            0x50));
    }

    private static void AddPaintedEgg(
        List<PageBorderArtPolygon> polygons,
        double x,
        double y,
        double size)
    {
        var scale = size / 32.0;
        PageBorderArtPoint Point(double px, double py) => new(x + px * scale, y + py * scale);
        void Add(byte red, byte green, byte blue, params (double X, double Y)[] points) =>
            polygons.Add(new PageBorderArtPolygon(
                points.Select(point => Point(point.X, point.Y)).ToList(), red, green, blue));
        void AddPatch(params (double X, double Y)[] points)
        {
            const double scaleFromCenter = 0.70;
            var centerX = points.Average(point => point.X);
            var centerY = points.Average(point => point.Y);
            Add(0, 0, 0, points
                .Select(point => (
                    centerX + (point.X - centerX) * scaleFromCenter,
                    centerY + (point.Y - centerY) * scaleFromCenter))
                .ToArray());
        }

        Add(0, 0, 0,
            (6, 24), (14, 26), (22, 25), (28, 23), (32, 26), (29, 30), (22, 30),
            (13, 30), (7, 29));
        Add(0, 0, 0,
            (11, 0), (18, 0), (24, 2), (28, 8), (29, 15), (26, 22), (21, 27),
            (14, 29), (7, 26), (1, 21), (0, 14), (3, 8), (7, 3));
        Add(0xFF, 0xFF, 0xFF,
            (12, 2), (18, 0), (24, 4), (28, 9), (28, 15), (25, 21), (19, 25),
            (13, 27), (7, 24), (3, 19), (2, 14), (5, 8), (8, 4));
        AddPatch((10, 4), (15, 1), (19, 4), (18, 9), (13, 8));
        AddPatch((18, 1), (23, 3), (26, 8), (23, 10), (19, 7));
        AddPatch((5, 12), (9, 9), (12, 10), (10, 15), (6, 16));
        AddPatch((14, 12), (18, 10), (21, 13), (18, 17), (14, 16));
        AddPatch((21, 17), (25, 16), (26, 21), (23, 24), (20, 22));
        AddPatch((3, 18), (8, 18), (12, 22), (10, 26), (6, 24));
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

    private static void AddIceCreamCone(
        List<PageBorderArtPolygon> polygons,
        double x,
        double y,
        double size)
    {
        var scale = size / 32.0;
        PageBorderArtPoint Point(double px, double py) => new(x + px * scale, y + py * scale);
        void Add(byte red, byte green, byte blue, params (double X, double Y)[] points) =>
            polygons.Add(new PageBorderArtPolygon(
                points.Select(point => Point(point.X, point.Y)).ToList(), red, green, blue));

        Add(0, 0, 0, (9, 11), (23, 11), (16, 31));
        Add(0x60, 0x40, 0x20, (11, 13), (21, 13), (16, 28));
        Add(0, 0, 0,
            (5, 7), (7, 4), (11, 1), (21, 1), (25, 4), (27, 7),
            (25, 11), (22, 14), (10, 14), (7, 11));
        Add(0xFF, 0x80, 0xFF,
            (6, 8), (26, 8), (24, 11), (22, 13), (10, 13), (8, 11));
        Add(0xFF, 0xFF, 0x80,
            (7, 6), (9, 3), (13, 1), (20, 1), (24, 3), (26, 6), (24, 9), (8, 9));
    }

    private static void AddPerson(
        List<PageBorderArtPolygon> polygons,
        double x,
        double y,
        double size)
    {
        var scale = size / 32.0;
        PageBorderArtPoint Point(double px, double py) => new(x + px * scale, y + py * scale);
        void Add(byte gray, params (double X, double Y)[] points) =>
            polygons.Add(new PageBorderArtPolygon(
                points.Select(point => Point(point.X, point.Y)).ToList(), gray, gray, gray));

        Add(0,
            (16, 1), (19, 2), (20, 5), (19, 8), (16, 9), (13, 8), (12, 5), (13, 2));
        Add(0xFF,
            (16, 2), (18, 3), (19, 5), (18, 7), (16, 8), (14, 7), (13, 5), (14, 3));
        Add(0,
            (14, 9), (18, 9), (19, 11), (24, 12), (28, 16), (26, 18),
            (21, 14), (21, 20), (24, 27), (21, 29), (17, 23), (17, 31),
            (14, 31), (14, 23), (10, 29), (7, 27), (11, 20), (11, 14),
            (6, 18), (4, 16), (8, 12), (13, 11));
        Add(0xFF,
            (15, 10), (17, 10), (18, 12), (22, 13), (26, 16), (25, 16.5),
            (20, 13), (19.5, 20), (22.5, 27), (21, 28), (16, 22), (16, 30),
            (15, 30), (15, 22), (10, 28), (8.5, 27), (12.5, 20), (12, 13),
            (7, 16.5), (6, 16), (10, 13), (14, 12));
    }

    private static void AddRose(
        List<PageBorderArtPolygon> polygons,
        double x,
        double y,
        double size)
    {
        var scale = size / 32.0;
        PageBorderArtPoint Point(double px, double py) => new(x + px * scale, y + py * scale);
        void Add(byte red, byte green, byte blue, params (double X, double Y)[] points) =>
            polygons.Add(new PageBorderArtPolygon(
                points.Select(point => Point(point.X, point.Y)).ToList(), red, green, blue));

        Add(0x40, 0x40, 0x40,
            (13, 14), (16, 14), (19, 22), (28, 28), (27, 31), (18, 26), (14, 18));
        Add(0x0F, 0x64, 0x00,
            (15, 16), (16, 17), (20, 23), (27, 28), (26, 29), (19, 25));
        Add(0x40, 0x40, 0x40,
            (18, 19), (19, 14), (23, 8), (27, 7), (30, 10), (32, 16),
            (31, 22), (28, 25), (24, 25), (21, 22));
        Add(0x1A, 0xB3, 0x00,
            (20, 18), (21, 14), (24, 9), (27, 8), (29, 11), (31, 16),
            (30, 21), (27, 24), (24, 23), (22, 21));
        Add(0x0F, 0x64, 0x00, (25, 9), (27, 9), (27, 23), (25, 24));

        Add(0x40, 0x40, 0x40,
            (1, 5), (4, 1), (8, 2), (11, 0), (15, 2), (20, 1), (23, 4),
            (20, 8), (19, 11), (23, 14), (22, 18), (18, 21), (14, 20),
            (11, 23), (6, 22), (4, 18), (0, 16), (1, 12), (0, 9));
        Add(0xE9, 0x6A, 0xD3,
            (2, 6), (5, 3), (8, 4), (11, 2), (14, 4), (19, 3), (21, 5),
            (18, 8), (17, 11), (21, 14), (20, 17), (17, 19), (13, 18),
            (10, 21), (7, 20), (6, 17), (2, 15), (3, 12), (2, 9));
        Add(0xA0, 0x49, 0x91, (2, 6), (7, 7), (9, 11), (3, 10));
        Add(0xA0, 0x49, 0x91, (6, 13), (18, 12), (19, 15), (8, 16));
        Add(0xA0, 0x49, 0x91, (9, 3), (12, 4), (11, 9), (15, 12), (13, 14), (8, 10));

        Add(0x40, 0x40, 0x40,
            (0, 21), (4, 19), (8, 20), (11, 23), (10, 26), (7, 27),
            (7, 32), (4, 32), (4, 27), (1, 25));
        Add(0xE9, 0x6A, 0xD3,
            (2, 21), (5, 21), (9, 23), (8, 25), (6, 25), (6, 27),
            (5, 30), (5, 26), (2, 24));
        Add(0x0F, 0x64, 0x00, (6, 26), (9, 24), (8, 29), (6, 31));
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

    private static void AddWhitePolygon(
        List<PageBorderArtPolygon> polygons,
        Func<double, double, PageBorderArtPoint> point,
        params (double X, double Y)[] coordinates) =>
        polygons.Add(new PageBorderArtPolygon(
            coordinates.Select(coordinate => point(coordinate.X, coordinate.Y)).ToList(),
            0xFF, 0xFF, 0xFF));

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
            AddWeavingRibbonMask(
                fills,
                mask,
                horizontal ? along : acrossStart,
                horizontal ? acrossStart : along,
                size,
                horizontal);
        }
    }

    private static void AddWeavingRibbonMask(
        List<PageBorderArtFillRectangle> fills,
        IReadOnlyList<byte> mask,
        double x,
        double y,
        double size,
        bool horizontal)
    {
        var scale = size / PageBorderArtSpriteMasks.MaskSize;
        for (var row = 0; row < PageBorderArtSpriteMasks.MaskSize; row++)
        {
            var runStart = -1;
            byte material = 0;
            for (var column = 0; column <= PageBorderArtSpriteMasks.MaskSize; column++)
            {
                var next = column < PageBorderArtSpriteMasks.MaskSize
                    ? mask[row * PageBorderArtSpriteMasks.MaskSize + column]
                    : (byte)0;
                if (next != material && runStart >= 0)
                {
                    var runLength = column - runStart;
                    var shade = material == 1 ? (byte)0xC0 : (byte)0xFF;
                    fills.Add(horizontal
                        ? new PageBorderArtFillRectangle(
                            x + runStart * scale,
                            y + row * scale,
                            runLength * scale,
                            scale,
                            shade, shade, shade)
                        : new PageBorderArtFillRectangle(
                            x + row * scale,
                            y + runStart * scale,
                            scale,
                            runLength * scale,
                            shade, shade, shade));
                    runStart = -1;
                }
                if (next != material)
                {
                    material = next;
                    if (material != 0)
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

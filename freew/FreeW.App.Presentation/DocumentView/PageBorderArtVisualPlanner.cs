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
    public const int ApplesArtId = 1;
    public const int ShadowedSquaresArtId = 57;
    public const int ShorebirdTracksArtId = 83;
    public const int DecorativeArchArtId = 89;
    public const int BatsArtId = 37;
    public const int PapyrusArtId = 92;
    public const int VineArtId = 47;
    public const int WeavingRibbonArtId = 95;
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
            new(inset - 1, inset, size, railHeight, 0, 0, 0),
            new(frameWidth - inset - size, inset, size, railHeight, 0, 0, 0),
        };
        var polygons = new List<PageBorderArtPolygon>();
        AddRibbonHorizontalStripes(polygons, inset, inset, railWidth, size, slash: true, phaseDip: size * 0.375);
        AddRibbonHorizontalStripes(polygons, inset, frameHeight - inset - size, railWidth, size, slash: true, phaseDip: 0);
        AddRibbonVerticalStripes(polygons, inset - 1, inset, railHeight, size, slash: false);
        AddRibbonVerticalStripes(polygons, frameWidth - inset - size, inset, railHeight, size, slash: false);
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
        AddVineRail(polygons, horizontalStart, horizontalLength, inset, size, horizontal: true, reverseAcross: false);
        AddVineRail(polygons, horizontalStart, horizontalLength, frameHeight - inset - size, size, horizontal: true, reverseAcross: false);
        AddVineRail(polygons, verticalStart, verticalLength, inset, size, horizontal: false, reverseAcross: true);
        AddVineRail(polygons, verticalStart, verticalLength, frameWidth - inset - size, size, horizontal: false, reverseAcross: true);
        AddVineCorner(polygons, inset, inset, size);
        AddVineCorner(polygons, frameWidth - inset - size, inset, size);
        AddVineCorner(polygons, inset, frameHeight - inset - size, size);
        AddVineCorner(polygons, frameWidth - inset - size, frameHeight - inset - size, size);

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
        List<PageBorderArtPolygon> polygons,
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
            PageBorderArtPoint Point(double along, double across)
            {
                var scaledAlong = along * size / 32.0;
                var scaledAcross = across * size / 32.0;
                if (reverseAcross)
                    scaledAcross = size - scaledAcross;
                return horizontal
                    ? new PageBorderArtPoint(motifStart + scaledAlong, acrossStart + scaledAcross)
                    : new PageBorderArtPoint(acrossStart + scaledAcross, motifStart + scaledAlong);
            }

            AddWhitePolygon(polygons, Point,
                (0, 24), (7, 24), (13, 21), (19, 15), (26, 12), (33, 12), (40, 15), (48, 15),
                (48, 20), (40, 20), (33, 17), (27, 17), (22, 20), (16, 25), (8, 29), (0, 29));
            AddWhitePolygon(polygons, Point,
                (24, 11), (28, 5), (36, 4), (43, 8), (38, 13), (31, 14));
            AddWhitePolygon(polygons, Point,
                (20, 18), (27, 20), (34, 25), (31, 30), (23, 29), (18, 24));
            AddWhitePolygon(polygons, Point,
                (4, 19), (0, 14), (4, 10), (10, 13), (10, 17));
        }
    }

    private static void AddVineCorner(
        List<PageBorderArtPolygon> polygons,
        double x,
        double y,
        double size)
    {
        var scale = size / 32.0;
        PageBorderArtPoint Point(double px, double py) => new(x + px * scale, y + py * scale);
        AddWhitePolygon(polygons, Point, (16, 16), (11, 10), (16, 2), (21, 10));
        AddWhitePolygon(polygons, Point, (16, 16), (22, 11), (30, 16), (22, 21));
        AddWhitePolygon(polygons, Point, (16, 16), (21, 22), (16, 30), (11, 22));
        AddWhitePolygon(polygons, Point, (16, 16), (10, 21), (2, 16), (10, 11));
        polygons.Add(new PageBorderArtPolygon(
            [Point(13, 13), Point(19, 13), Point(19, 19), Point(13, 19)],
            0xB2, 0xB2, 0xB2));
    }

    private static void AddWhitePolygon(
        List<PageBorderArtPolygon> polygons,
        Func<double, double, PageBorderArtPoint> point,
        params (double X, double Y)[] coordinates) =>
        polygons.Add(new PageBorderArtPolygon(
            coordinates.Select(coordinate => point(coordinate.X, coordinate.Y)).ToList(),
            0xFF, 0xFF, 0xFF));

    private static void AddRibbonHorizontalStripes(
        List<PageBorderArtPolygon> polygons,
        double x,
        double y,
        double width,
        double size,
        bool slash,
        double phaseDip)
    {
        var end = x + width;
        for (var tileX = x + phaseDip; tileX < end; tileX += size)
        {
            var points = slash
                ? new[]
                {
                    (tileX, y + size * 0.96875),
                    (tileX, y + size),
                    (tileX + size * 0.34375, y + size),
                    (tileX + size, y + size * 0.34375),
                    (tileX + size, y),
                    (tileX + size * 0.65625, y),
                }
                : new[]
                {
                    (tileX, y),
                    (tileX + size * 0.34375, y),
                    (tileX + size, y + size * 0.96875),
                    (tileX + size, y + size),
                    (tileX + size * 0.65625, y + size),
                    (tileX, y + size * 0.34375),
                };
            AddClampedRibbonPolygon(polygons, points, x, y, end, y + size, 0xFF);
            AddClampedRibbonPolygon(
                polygons,
                new[]
                {
                    (tileX + size * 0.3125, y + size * 0.15625),
                    (tileX + size * 0.40625, y + size * 0.28125),
                    (tileX + size * 0.40625, y + size * 0.625),
                    (tileX + size * 0.3125, y + size * 0.71875),
                    (tileX + size * 0.15625, y + size * 0.90625),
                    (tileX + size * 0.09375, y + size * 0.84375),
                    (tileX + size * 0.0625, y + size * 0.625),
                    (tileX + size * 0.0625, y + size * 0.40625),
                    (tileX + size * 0.15625, y + size * 0.3125),
                },
                x,
                y,
                end,
                y + size,
                0xC0);
        }
    }

    private static void AddRibbonVerticalStripes(
        List<PageBorderArtPolygon> polygons,
        double x,
        double y,
        double height,
        double size,
        bool slash)
    {
        var end = y + height;
        for (var tileY = y; tileY < end; tileY += size)
        {
            var points = slash
                ? new[]
                {
                    (x, tileY + size * 0.96875),
                    (x, tileY + size),
                    (x + size * 0.34375, tileY + size),
                    (x + size, tileY + size * 0.34375),
                    (x + size, tileY),
                    (x + size * 0.65625, tileY),
                }
                : new[]
                {
                    (x, tileY),
                    (x + size * 0.34375, tileY),
                    (x + size, tileY + size * 0.96875),
                    (x + size, tileY + size),
                    (x + size * 0.65625, tileY + size),
                    (x, tileY + size * 0.34375),
                };
            AddClampedRibbonPolygon(polygons, points, x, y, x + size, end, 0xFF);
            AddClampedRibbonPolygon(
                polygons,
                new[]
                {
                    (x + size * 0.15625, tileY + size * 0.6875),
                    (x + size * 0.28125, tileY + size * 0.59375),
                    (x + size * 0.625, tileY + size * 0.59375),
                    (x + size * 0.71875, tileY + size * 0.6875),
                    (x + size * 0.90625, tileY + size * 0.84375),
                    (x + size * 0.84375, tileY + size * 0.90625),
                    (x + size * 0.625, tileY + size * 0.9375),
                    (x + size * 0.40625, tileY + size * 0.9375),
                    (x + size * 0.3125, tileY + size * 0.84375),
                },
                x,
                y,
                x + size,
                end,
                0xC0);
        }
    }

    private static void AddClampedRibbonPolygon(
        List<PageBorderArtPolygon> polygons,
        IEnumerable<(double X, double Y)> points,
        double left,
        double top,
        double right,
        double bottom,
        byte gray) =>
        polygons.Add(new PageBorderArtPolygon(
            points.Select(point => new PageBorderArtPoint(
                Math.Clamp(point.X, left, right),
                Math.Clamp(point.Y, top, bottom))).ToList(),
            gray,
            gray,
            gray));

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

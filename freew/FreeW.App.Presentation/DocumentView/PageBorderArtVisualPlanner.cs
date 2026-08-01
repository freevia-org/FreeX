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

public sealed record PageBorderDecorativeArchPlan(
    IReadOnlyList<PageBorderArtFillRectangle> Fills,
    IReadOnlyList<PageBorderArtCubicStroke> Strokes);

public static class PageBorderArtVisualPlanner
{
    public const int ApplesArtId = 1;
    public const int ShadowedSquaresArtId = 57;
    public const int ShorebirdTracksArtId = 83;
    public const int DecorativeArchArtId = 89;
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

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

public static class PageBorderArtVisualPlanner
{
    public const int ApplesArtId = 1;
    public const int ShadowedSquaresArtId = 57;
    public const int ShorebirdTracksArtId = 83;
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

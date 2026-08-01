namespace FreeW.App.Presentation.DocumentView;

public sealed record PageBorderAppleMotif(
    double Xdip,
    double Ydip,
    double SizeDip);

public static class PageBorderArtVisualPlanner
{
    public const int ApplesArtId = 1;
    public const byte AppleFillRed = 0xB5;
    public const byte AppleStemRed = 0x66;
    public const byte AppleHighlightRed = 0xD8;
    public const byte AppleHighlightGreen = 0x59;
    public const byte AppleHighlightBlue = 0x59;

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

        var frameWidth = Math.Max(0, frameWidthDip);
        var frameHeight = Math.Max(0, frameHeightDip);
        var inset = Math.Max(0, edgeInsetDip);
        var motifSize = Math.Clamp(
            Math.Max(0, modelWidthPt) * ArtSizeUnitsPerModelPoint * DipPerPoint,
            MinimumMotifSizeDip,
            MaximumMotifSizeDip);
        var horizontalLength = frameWidth - 2 * inset;
        var verticalLength = frameHeight - 2 * inset;
        if (horizontalLength < motifSize || verticalLength < motifSize)
        {
            motifs = [];
            return true;
        }

        var result = new List<PageBorderAppleMotif>();
        AddEdge(result, inset, inset, horizontalLength, motifSize, horizontal: true);
        AddEdge(result, inset, frameHeight - inset - motifSize, horizontalLength, motifSize, horizontal: true);
        AddEdge(result, inset, inset, verticalLength, motifSize, horizontal: false, skipEnds: true);
        AddEdge(result, frameWidth - inset - motifSize, inset, verticalLength, motifSize, horizontal: false, skipEnds: true);
        motifs = result;
        return true;
    }

    private static void AddEdge(
        List<PageBorderAppleMotif> motifs,
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
            motifs.Add(new PageBorderAppleMotif(
                horizontal ? x + index * step : x,
                horizontal ? y : y + index * step,
                motifSize));
        }
    }
}

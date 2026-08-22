namespace Free.Shared.Pdf.Import;

/// <summary>Geometry needed to cluster one extracted PDF glyph.</summary>
public readonly record struct PdfTextGlyphMetrics(
    string? Text,
    double BaselineY,
    double Left,
    double FontSize);

/// <summary>A baseline-aligned line whose glyphs are ordered left to right.</summary>
public sealed record PdfTextLine<TGlyph>(
    double BaselineY,
    double? ModalFontSize,
    IReadOnlyList<TGlyph> Glyphs);

/// <summary>Top-to-bottom lines and the page-level measurements used to form them.</summary>
public sealed record PdfTextLineClustering<TGlyph>(
    double? ModalFontSize,
    double BaselineTolerance,
    IReadOnlyList<PdfTextLine<TGlyph>> Lines);

/// <summary>
/// Groups positioned PDF glyphs using running-mean baselines and half-point font buckets without depending
/// on a particular PDF parser.
/// </summary>
public static class PdfTextLineClusterer
{
    public const double DefaultFontSize = 12.0;
    private const double MinimumBaselineTolerance = 3.0;
    private const double BaselineToleranceFontSizeFactor = 0.5;

    /// <summary>Returns the first modal positive font size, rounded to the nearest half point.</summary>
    public static double? CalculateModalFontSize<TGlyph>(
        IReadOnlyList<TGlyph> glyphs,
        Func<TGlyph, PdfTextGlyphMetrics> getMetrics)
    {
        ArgumentNullException.ThrowIfNull(glyphs);
        ArgumentNullException.ThrowIfNull(getMetrics);

        return CalculateModalFontSize(glyphs.Select(getMetrics));
    }

    /// <summary>Clusters non-empty glyphs using a running-mean baseline.</summary>
    public static PdfTextLineClustering<TGlyph> Cluster<TGlyph>(
        IReadOnlyList<TGlyph> glyphs,
        Func<TGlyph, PdfTextGlyphMetrics> getMetrics)
    {
        ArgumentNullException.ThrowIfNull(glyphs);
        ArgumentNullException.ThrowIfNull(getMetrics);

        var positionedGlyphs = glyphs
            .Select(glyph => new PositionedGlyph<TGlyph>(glyph, getMetrics(glyph)))
            .ToArray();
        var modalFontSize = CalculateModalFontSize(positionedGlyphs.Select(item => item.Metrics));
        var baselineTolerance = Math.Max(
            (modalFontSize ?? DefaultFontSize) * BaselineToleranceFontSizeFactor,
            MinimumBaselineTolerance);

        var sortedGlyphs = positionedGlyphs
            .Where(item => !string.IsNullOrEmpty(item.Metrics.Text))
            .OrderByDescending(item => item.Metrics.BaselineY)
            .ThenBy(item => item.Metrics.Left);
        var lines = new List<LineBuilder<TGlyph>>();

        foreach (var glyph in sortedGlyphs)
        {
            LineBuilder<TGlyph>? nearestLine = null;
            var nearestDelta = double.MaxValue;

            foreach (var line in lines)
            {
                var delta = Math.Abs(line.BaselineY - glyph.Metrics.BaselineY);
                if (delta <= baselineTolerance && delta < nearestDelta)
                {
                    nearestLine = line;
                    nearestDelta = delta;
                }
            }

            if (nearestLine is null)
            {
                nearestLine = new LineBuilder<TGlyph>();
                lines.Add(nearestLine);
            }

            nearestLine.Add(glyph);
        }

        var clusteredLines = lines
            .OrderByDescending(line => line.BaselineY)
            .Select(line => line.Build())
            .ToArray();
        return new PdfTextLineClustering<TGlyph>(modalFontSize, baselineTolerance, clusteredLines);
    }

    private static double? CalculateModalFontSize(IEnumerable<PdfTextGlyphMetrics> metrics)
    {
        var modalGroup = metrics
            .Where(item => item.FontSize > 0)
            .GroupBy(item => Math.Round(item.FontSize * 2) / 2)
            .OrderByDescending(group => group.Count())
            .FirstOrDefault();
        return modalGroup?.Key;
    }

    private sealed class LineBuilder<TGlyph>
    {
        private readonly List<PositionedGlyph<TGlyph>> _glyphs = [];
        private double _baselineSum;

        public double BaselineY => _glyphs.Count == 0 ? 0 : _baselineSum / _glyphs.Count;

        public void Add(PositionedGlyph<TGlyph> glyph)
        {
            _glyphs.Add(glyph);
            _baselineSum += glyph.Metrics.BaselineY;
        }

        public PdfTextLine<TGlyph> Build()
        {
            var orderedGlyphs = _glyphs
                .OrderBy(item => item.Metrics.Left)
                .Select(item => item.Glyph)
                .ToArray();
            var modalFontSize = CalculateModalFontSize(_glyphs.Select(item => item.Metrics));
            return new PdfTextLine<TGlyph>(BaselineY, modalFontSize, orderedGlyphs);
        }
    }

    private sealed record PositionedGlyph<TGlyph>(
        TGlyph Glyph,
        PdfTextGlyphMetrics Metrics);
}

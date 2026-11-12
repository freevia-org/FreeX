using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using FreeW.App.Presentation.DocumentView.MathLayout;

namespace FreeW.App.Host.Editing;

/// <summary>
/// The WPF side of the shared equation layout engine: Cambria Math glyph metrics from the WPF
/// <see cref="GlyphTypeface"/> plus the font's OpenType MATH table (constants, italic corrections,
/// accent attachments, size variants and assemblies). When Cambria Math is not installed it falls back to
/// <see cref="EstimatedMathLayoutFont"/> metrics and text drawn with the fallback family.
/// </summary>
internal sealed class WpfMathLayoutFont : IMathLayoutFont
{
    internal const string FallbackFamily = "Cambria Math, Cambria, Segoe UI Symbol, Times New Roman";

    private static readonly Lazy<WpfMathLayoutFont> SharedInstance = new(Create);
    private static readonly Typeface FallbackTypeface = new(
        new FontFamily(FallbackFamily), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private readonly OpenTypeMathTable? _math;
    private readonly IMathLayoutFont _estimated = EstimatedMathLayoutFont.Instance;

    private WpfMathLayoutFont(GlyphTypeface? glyphTypeface, OpenTypeMathTable? math)
    {
        GlyphTypeface = glyphTypeface;
        _math = math;
    }

    public static WpfMathLayoutFont Instance => SharedInstance.Value;

    /// <summary>Cambria Math's glyph typeface, or null when the font is unavailable.</summary>
    public GlyphTypeface? GlyphTypeface { get; }

    public MathFontConstants Constants => _math?.Constants ?? MathFontConstants.CambriaMath;

    public double MinConnectorOverlap => _math?.MinConnectorOverlap ?? _estimated.MinConnectorOverlap;

    private static WpfMathLayoutFont Create()
    {
        try
        {
            var typeface = new Typeface(new FontFamily("Cambria Math"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            if (!typeface.TryGetGlyphTypeface(out var glyphTypeface)
                || !glyphTypeface.FamilyNames.Values.Any(name => string.Equals(name, "Cambria Math", StringComparison.OrdinalIgnoreCase)))
            {
                return new WpfMathLayoutFont(null, null);
            }

            byte[] data;
            using (var stream = glyphTypeface.GetFontStream())
            using (var buffer = new MemoryStream())
            {
                stream.CopyTo(buffer);
                data = buffer.ToArray();
            }

            var faceIndex = -1;
            var fragment = glyphTypeface.FontUri?.Fragment;
            if (!string.IsNullOrEmpty(fragment)
                && int.TryParse(fragment.TrimStart('#'), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                faceIndex = parsed;
            }

            var math = OpenTypeMathTable.TryLoad(data, faceIndex) ?? OpenTypeMathTable.TryLoad(data);
            return new WpfMathLayoutFont(glyphTypeface, math);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            return new WpfMathLayoutFont(null, null);
        }
    }

    /// <summary>Maps every code point of <paramref name="text"/> to a Cambria Math glyph, or fails.</summary>
    public bool TryMapGlyphs(string text, out ushort[] glyphs)
    {
        glyphs = [];
        if (GlyphTypeface is null)
            return false;

        var map = GlyphTypeface.CharacterToGlyphMap;
        var result = new List<ushort>(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            if (!map.TryGetValue(rune.Value, out var glyph))
                return false;
            result.Add(glyph);
        }

        glyphs = [.. result];
        return true;
    }

    public MathTextMetrics MeasureText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return default;
        if (!TryMapGlyphs(text, out var glyphs))
            return GlyphTypeface is null ? _estimated.MeasureText(text) : MeasureWithFallbackFamily(text);

        double x = 0, ascent = double.NegativeInfinity, descent = double.NegativeInfinity;
        double inkLeft = double.PositiveInfinity, inkRight = double.NegativeInfinity;
        double italic = 0;
        foreach (var glyph in glyphs)
        {
            var metrics = MeasureGlyph(glyph);
            if (metrics.InkRight > metrics.InkLeft)
            {
                ascent = Math.Max(ascent, metrics.Ascent);
                descent = Math.Max(descent, metrics.Descent);
                inkLeft = Math.Min(inkLeft, x + metrics.InkLeft);
                inkRight = Math.Max(inkRight, x + metrics.InkRight);
            }

            italic = metrics.ItalicCorrection;
            x += metrics.Width;
        }

        if (double.IsNegativeInfinity(ascent))
            return new MathTextMetrics(x, 0, 0, 0, 0, x);

        return new MathTextMetrics(x, ascent, descent, italic, inkLeft, inkRight);
    }

    public bool TryGetGlyph(int codePoint, out int glyphId)
    {
        glyphId = 0;
        if (GlyphTypeface is null || !GlyphTypeface.CharacterToGlyphMap.TryGetValue(codePoint, out var glyph))
            return false;
        glyphId = glyph;
        return true;
    }

    public MathTextMetrics MeasureGlyph(int glyphId)
    {
        var typeface = GlyphTypeface;
        if (typeface is null || glyphId < 0 || glyphId >= typeface.GlyphCount)
            return default;

        var id = (ushort)glyphId;
        var advance = typeface.AdvanceWidths[id];
        var left = typeface.LeftSideBearings[id];
        var right = typeface.RightSideBearings[id];
        var blackBoxHeight = typeface.AdvanceHeights[id] - typeface.TopSideBearings[id] - typeface.BottomSideBearings[id];
        if (advance - left - right <= 0 || blackBoxHeight <= 0)
            return new MathTextMetrics(advance, 0, 0, 0, 0, 0);

        var ascent = typeface.Baseline - typeface.TopSideBearings[id];
        var descent = typeface.DistancesFromHorizontalBaselineToBlackBoxBottom[id];
        var italic = _math?.ItalicCorrection(glyphId) ?? Math.Max(0, -right);
        return new MathTextMetrics(advance, ascent, descent, italic, left, advance - right);
    }

    public IReadOnlyList<MathGlyphVariant> GetVariants(int glyphId, bool vertical) =>
        _math?.Variants(glyphId, vertical) ?? [];

    public MathGlyphAssembly? GetAssembly(int glyphId, bool vertical) =>
        _math?.Assembly(glyphId, vertical);

    public double? GetTopAccentAttachment(int glyphId) =>
        _math?.TopAccentAttachment(glyphId);

    /// <summary>Formatted text for characters Cambria Math does not carry, at 1 DIP per em.</summary>
    internal static FormattedText FallbackText(string text, double emSize, Brush brush, double pixelsPerDip) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, FallbackTypeface, Math.Max(emSize, 0.01), brush, pixelsPerDip);

    private static MathTextMetrics MeasureWithFallbackFamily(string text)
    {
        const double Scale = 100.0;
        var formatted = FallbackText(text, Scale, Brushes.Black, 1.0);
        var inkBottom = formatted.Height + formatted.OverhangAfter;
        var inkTop = inkBottom - formatted.Extent;
        var width = formatted.WidthIncludingTrailingWhitespace;
        return new MathTextMetrics(
            width / Scale,
            (formatted.Baseline - inkTop) / Scale,
            (inkBottom - formatted.Baseline) / Scale,
            0,
            formatted.OverhangLeading / Scale,
            (width - formatted.OverhangTrailing) / Scale);
    }
}

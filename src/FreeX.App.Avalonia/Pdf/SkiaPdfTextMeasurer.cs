using FreeX.App.Presentation.Text;
using SkiaSharp;

namespace FreeX.App.Avalonia.Pdf;

/// <summary>
/// font-text-measurement-F1: measures text with SkiaSharp's own <see cref="SKFont.MeasureText(string)"/>
/// -- the exact API <c>Free.Shared.Pdf.Skia.SkiaPdfWriter</c>'s <c>FallbackTextRenderer</c> uses to draw
/// every <c>PdfText</c> op (see <c>SkiaPdfWriter.cs</c>'s <c>PdfTypefaceSet.For</c> +
/// <c>FallbackTextRenderer.DrawText</c>) -- so the text position <c>WorkbookPdfContentBuilder</c>
/// precomputes for Center/Right/Justify/Distributed alignment agrees with what actually gets drawn,
/// instead of disagreeing with it the way the character-count heuristic
/// (<c>WorkbookPdfContentBuilder.PortablePdfTextMeasurer</c>) does.
/// <para>
/// Deliberately uses plain SkiaSharp typeface resolution rather than Avalonia's
/// <c>FormattedText</c>/<c>AvaloniaTextMeasurer</c>: that path requires
/// <c>Avalonia.Platform.IFontManagerImpl</c> to be registered (i.e. a running Avalonia application),
/// which the PDF export pipeline does not otherwise depend on -- <see cref="Free.Shared.Pdf.Skia.SkiaPdfWriter"/>
/// itself already draws with raw SkiaSharp with no Avalonia platform dependency, so this measurer keeps
/// that same independence instead of adding a new one.
/// </para>
/// </summary>
public sealed class SkiaPdfTextMeasurer : ITextMeasurer, IDisposable
{
    private readonly Dictionary<(string Family, bool Bold, bool Italic), SKTypeface> _typefaceCache = new();

    public TextSize Measure(string? text, string? fontFamily, double fontSize, bool bold, bool italic)
    {
        if (string.IsNullOrEmpty(text))
            return TextSize.Empty;

        var typeface = ResolveTypeface(fontFamily, bold, italic);
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        using var font = new SKFont(typeface, (float)(fontSize > 0 ? fontSize : 1));
        var maxWidth = 0.0;
        foreach (var line in lines)
            maxWidth = Math.Max(maxWidth, font.MeasureText(line));
        return new TextSize(maxWidth, lines.Length * fontSize * 1.2);
    }

    // Mirrors SkiaPdfWriter's PdfTypefaceSet.For resolution/fallback chain (family -> default family
    // -> SKTypeface.Default) so the SAME typeface this measurer sizes against is the one the writer
    // actually draws with.
    private SKTypeface ResolveTypeface(string? fontFamily, bool bold, bool italic)
    {
        var family = string.IsNullOrWhiteSpace(fontFamily) ? string.Empty : fontFamily.Trim();
        var key = (family, bold, italic);
        if (_typefaceCache.TryGetValue(key, out var cached))
            return cached;

        var weight = bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
        var slant = italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
        var typeface =
            SKTypeface.FromFamilyName(family.Length == 0 ? null : family, weight, SKFontStyleWidth.Normal, slant)
            ?? SKTypeface.FromFamilyName(null, weight, SKFontStyleWidth.Normal, slant)
            ?? SKTypeface.Default;
        _typefaceCache[key] = typeface;
        return typeface;
    }

    public void Dispose()
    {
        foreach (var typeface in _typefaceCache.Values.Distinct())
        {
            if (ReferenceEquals(typeface, SKTypeface.Default))
                continue;
            typeface.Dispose();
        }
        _typefaceCache.Clear();
    }
}

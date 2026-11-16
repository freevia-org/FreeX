using System.Globalization;
using System.Text;
using System.Xml;
using Free.Shared.Xps;
using SkiaSharp;

namespace Free.Shared.Pdf.Skia;

/// <summary>
/// Packages the Skia-rendered page artwork as XPS and adds transparent native glyph runs
/// for text with embeddable fonts. The glyph layer preserves the artwork's paint order.
/// </summary>
public static class SkiaXpsWriter
{
    private static readonly string[] SearchFontFamilies = ["Arial", "Liberation Sans", "DejaVu Sans"];

    public static byte[] WriteToBytes(
        PdfContentDocument document,
        ICollection<string>? diagnostics = null,
        int dpi = 144)
    {
        ArgumentNullException.ThrowIfNull(document);
        SkiaPdfWriter.ValidateRasterDpi(dpi);

        using var typefaces = SkiaPdfWriter.PdfTypefaceSet.Create();
        using var textRenderer = new SkiaPdfWriter.FallbackTextRenderer();
        var fonts = new SkiaXpsFontResources();
        var overlays = new Dictionary<PdfText, XpsGlyphRun>(ReferenceEqualityComparer.Instance);
        var textWarnings = new HashSet<string>(StringComparer.Ordinal);
        var pages = new List<PdfContentPage>(document.Pages.Count);
        foreach (var page in document.Pages)
        {
            if (!double.IsFinite(page.WidthPoints) || !double.IsFinite(page.HeightPoints) ||
                page.WidthPoints <= 0 || page.HeightPoints <= 0)
            {
                throw new ArgumentException("XPS requires finite, positive page dimensions.", nameof(document));
            }

            using var image = SkiaPdfWriter.RasterizePage(page, dpi, typefaces, textRenderer, diagnostics);
            using var png = image.Encode(SKEncodedImageFormat.Png, 100)
                ?? throw new InvalidOperationException("Could not encode the XPS page artwork.");
            var ops = new List<PdfDrawOp>
            {
                new PdfImage(0, 0, page.WidthPoints, page.HeightPoints, png.ToArray(), "image/png"),
            };
            AppendSelectableText(page.Ops, ops, typefaces, textRenderer, fonts, overlays, textWarnings);
            pages.Add(page with { Ops = ops });
        }

        var package = PortableXpsWriter.WriteToBytes(
            document with { Pages = pages },
            new XpsWriterOptions { GlyphOverlays = overlays });
        if (diagnostics is not null)
        {
            foreach (var warning in textWarnings)
                diagnostics.Add(warning);
        }
        return package;
    }

    private static void AppendSelectableText(
        IReadOnlyList<PdfDrawOp> source,
        List<PdfDrawOp> target,
        SkiaPdfWriter.PdfTypefaceSet typefaces,
        SkiaPdfWriter.FallbackTextRenderer textRenderer,
        SkiaXpsFontResources fonts,
        Dictionary<PdfText, XpsGlyphRun> overlays,
        HashSet<string> warnings)
    {
        foreach (var op in source)
        {
            // Effects paint derivative copies of their children. The original text is collected
            // separately, so a shadow, reflection or glow does not add duplicate searchable text.
            switch (op)
            {
                case PdfText text when !string.IsNullOrEmpty(text.Text) &&
                                       float.IsFinite((float)text.FontSize) && (float)text.FontSize > 0:
                    AppendTextRuns(text, target, typefaces, textRenderer, fonts, overlays, warnings);
                    break;
                case PdfRotationGroup rotation:
                {
                    var children = new List<PdfDrawOp>();
                    AppendSelectableText(rotation.Ops, children, typefaces, textRenderer, fonts, overlays, warnings);
                    if (children.Count > 0)
                        target.Add(rotation with { Ops = children });
                    break;
                }
                case PdfClipGroup clip when clip.Width > 0 && clip.Height > 0:
                {
                    var children = new List<PdfDrawOp>();
                    AppendSelectableText(clip.Ops, children, typefaces, textRenderer, fonts, overlays, warnings);
                    if (children.Count > 0)
                        target.Add(clip with { Ops = children });
                    break;
                }
                case PdfOpacityGroup opacity when SkiaPdfWriter.ToAlphaByte(opacity.Opacity) > 0:
                    AppendSelectableText(opacity.Ops, target, typefaces, textRenderer, fonts, overlays, warnings);
                    break;
            }
        }
    }

    private static void AppendTextRuns(
        PdfText text,
        List<PdfDrawOp> target,
        SkiaPdfWriter.PdfTypefaceSet typefaces,
        SkiaPdfWriter.FallbackTextRenderer textRenderer,
        SkiaXpsFontResources fonts,
        Dictionary<PdfText, XpsGlyphRun> overlays,
        HashSet<string> warnings)
    {
        var typeface = typefaces.For(text.FontFamily, text.Face);
        var x = (float)text.X;
        var index = 0;
        while (index < text.Text.Length)
        {
            var runTypeface = textRenderer.ResolveRun(text.Text, index, typeface, out var end);
            var run = text.Text[index..end];
            using var font = new SKFont(runTypeface, (float)text.FontSize);
            if (IsValidXmlText(run))
            {
                var glyphIds = font.GetGlyphs(run);
                var resource = fonts.For(runTypeface) ??
                               FindSearchFont(run, text.Face, typefaces, fonts, out glyphIds);
                if (resource is null)
                {
                    var name = string.IsNullOrEmpty(runTypeface.FamilyName) ? "default" : runTypeface.FamilyName;
                    warnings.Add(
                        $"XPS could not embed the font '{name}' or a substitute. Its text remains in the page artwork.");
                }
                else
                {
                    var indices = BuildIndices(run, glyphIds, font.GetGlyphWidths(run), font.Size);
                    var glyphText = text with { X = x, Text = run };
                    overlays.Add(glyphText, new XpsGlyphRun(resource, indices));
                    target.Add(glyphText);
                }
            }
            else
            {
                warnings.Add("Text with characters unsupported in XPS markup remains in the page artwork.");
            }

            x += font.MeasureText(run);
            index = end;
        }
    }

    private static XpsFontResource? FindSearchFont(
        string text,
        PdfFontFace face,
        SkiaPdfWriter.PdfTypefaceSet typefaces,
        SkiaXpsFontResources fonts,
        out ushort[] glyphIds)
    {
        // A search-layer font may differ from the painted font when the latter is unavailable
        // for embedding. It contributes cmap glyphs only; advances still come from the artwork.
        foreach (var family in SearchFontFamilies)
        {
            var typeface = typefaces.For(family, face);
            using var font = new SKFont(typeface, 1);
            var candidate = font.GetGlyphs(text);
            if (!candidate.Contains((ushort)0) && fonts.For(typeface) is { } resource)
            {
                glyphIds = candidate;
                return resource;
            }
        }
        glyphIds = [];
        return null;
    }

    private static bool IsValidXmlText(string text)
    {
        try
        {
            XmlConvert.VerifyXmlChars(text);
            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static string BuildIndices(string text, ushort[] glyphIds, float[] advances, float fontSize)
    {
        var indices = new StringBuilder();
        var glyphIndex = 0;
        var exactPosition = 0d;
        var roundedPosition = 0d;
        foreach (var character in text.EnumerateRunes())
        {
            if (glyphIndex > 0)
                indices.Append(';');
            if (character.Utf16SequenceLength == 2)
                indices.Append("(2:1)");
            // XPS advance widths are in hundredths of the em. Correct for rounding already
            // applied to earlier glyphs so a long run does not accumulate a placement error.
            exactPosition += advances[glyphIndex] * 100d / fontSize;
            var advance = Math.Round(exactPosition - roundedPosition, 3);
            roundedPosition += advance;
            indices.Append(CultureInfo.InvariantCulture, $"{glyphIds[glyphIndex]},{advance:0.###}");
            glyphIndex++;
        }
        return indices.ToString();
    }
}

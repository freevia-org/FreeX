using Free.Shared.Pdf;
using SkiaSharp;

namespace Free.Shared.Pdf.Skia;

/// <summary>
/// SkiaSharp PDF backend for the shared draw-op model. Unlike the dependency-free portable WinAnsi
/// writer, Skia shapes text (HarfBuzz) and <b>automatically embeds/subsets</b> the fonts it draws,
/// so non-WinAnsi text (Cyrillic, Greek, CJK, accented Latin) renders without bundling a font.
///
/// <para>
/// Consumes the same <see cref="PdfContentDocument"/> as <see cref="PortablePdfWriter"/>, so a
/// caller can route between Unicode (Skia) and dependency-free (portable) output with identical
/// geometry — only text fidelity differs. This is the content-agnostic core of FreeX's original
/// <c>SkiaPdfDocumentExporter</c>, lifted to the shared tier.
/// </para>
/// </summary>
public static class SkiaPdfWriter
{
    /// <summary>
    /// Renders <paramref name="document"/> to <paramref name="stream"/> via Skia's PDF backend.
    /// Draw ops are interpreted in PDF user space (origin bottom-left, y-up) and mapped to Skia's
    /// top-left, y-down canvas, matching the portable writer's geometry.
    /// </summary>
    public static int Write(PdfContentDocument document, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanWrite)
            throw new ArgumentException("PDF export requires a writable stream.", nameof(stream));
        if (document.Pages.Count == 0)
            throw new InvalidOperationException("PDF export requires at least one rendered page.");

        if (stream.CanSeek)
        {
            stream.Position = 0;
            stream.SetLength(0);
        }

        using var regular = SKTypeface.FromFamilyName(
            null, SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
            ?? SKTypeface.Default;
        using var bold = SKTypeface.FromFamilyName(
            null, SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
            ?? regular;

        var pageCount = 0;
        using var textRenderer = new FallbackTextRenderer();
        var metadata = BuildMetadata(document.Properties);
        using (var pdf = SKDocument.CreatePdf(stream, metadata))
        {
            foreach (var page in document.Pages)
            {
                var canvas = pdf.BeginPage((float)page.WidthPoints, (float)page.HeightPoints);
                canvas.Clear(SKColors.White);
                RenderPage(canvas, page, regular, bold, textRenderer);
                pdf.EndPage();
                pageCount++;
            }

            pdf.Close();
        }

        return pageCount;
    }

    private static SKDocumentPdfMetadata BuildMetadata(PdfDocumentProperties? properties)
    {
        var metadata = new SKDocumentPdfMetadata();
        if (properties is null)
            return metadata;

        if (!string.IsNullOrWhiteSpace(properties.Title))
            metadata.Title = properties.Title;
        if (!string.IsNullOrWhiteSpace(properties.Author))
            metadata.Author = properties.Author;
        if (!string.IsNullOrWhiteSpace(properties.Subject))
            metadata.Subject = properties.Subject;
        if (!string.IsNullOrWhiteSpace(properties.Keywords))
            metadata.Keywords = properties.Keywords;
        if (!string.IsNullOrWhiteSpace(properties.Creator))
            metadata.Creator = properties.Creator;
        return metadata;
    }

    private static void RenderPage(
        SKCanvas canvas,
        PdfContentPage page,
        SKTypeface regular,
        SKTypeface bold,
        FallbackTextRenderer textRenderer)
    {
        var pageHeight = (float)page.HeightPoints;
        using var fillPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var strokePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke };
        using var textPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };

        foreach (var op in page.Ops)
        {
            switch (op)
            {
                case PdfFillRect fill:
                {
                    // PDF y-up rect (x,y = bottom-left) -> Skia y-down rect.
                    var top = pageHeight - (float)(fill.Y + fill.Height);
                    fillPaint.Color = ToSkColor(fill.Color);
                    canvas.DrawRect(new SKRect((float)fill.X, top, (float)(fill.X + fill.Width), top + (float)fill.Height), fillPaint);
                    break;
                }

                case PdfStrokeRect stroke:
                {
                    var top = pageHeight - (float)(stroke.Y + stroke.Height);
                    strokePaint.Color = ToSkColor(stroke.Color);
                    strokePaint.StrokeWidth = (float)stroke.LineWidth;
                    canvas.DrawRect(new SKRect((float)stroke.X, top, (float)(stroke.X + stroke.Width), top + (float)stroke.Height), strokePaint);
                    break;
                }

                case PdfText text:
                {
                    if (string.IsNullOrEmpty(text.Text))
                        break;

                    // PDF text origin is the baseline (y-up). Skia DrawText baseline is y-down.
                    var baseline = pageHeight - (float)text.Y;
                    textPaint.Color = ToSkColor(text.Color);
                    var typeface = text.Face == PdfFontFace.Bold ? bold : regular;
                    textRenderer.DrawText(canvas, text.Text, (float)text.X, baseline, typeface, (float)text.FontSize, textPaint);
                    break;
                }

                case PdfLine line:
                {
                    // PDF coordinates are y-up; flip y for Skia's y-down canvas.
                    strokePaint.Color = ToSkColor(line.Color);
                    strokePaint.StrokeWidth = (float)line.LineWidth;
                    canvas.DrawLine(
                        (float)line.X1, pageHeight - (float)line.Y1,
                        (float)line.X2, pageHeight - (float)line.Y2,
                        strokePaint);
                    break;
                }

                case PdfImage image:
                {
                    if (!IsSupportedImageContentType(image.ContentType) || image.ImageBytes.Length == 0)
                        break;

                    using var data = SKData.CreateCopy(image.ImageBytes);
                    using var skImage = SKImage.FromEncodedData(data);
                    if (skImage is null)
                        break;

                    var top = pageHeight - (float)(image.Y + image.Height);
                    var left = (float)image.X;
                    var width = (float)image.Width;
                    var height = (float)image.Height;
                    canvas.Save();
                    if (Math.Abs(image.RotationDegrees) > 0.001)
                    {
                        canvas.Translate(left + width / 2f, top + height / 2f);
                        canvas.RotateDegrees((float)image.RotationDegrees);
                        canvas.DrawImage(skImage, new SKRect(-width / 2f, -height / 2f, width / 2f, height / 2f));
                    }
                    else
                    {
                        canvas.DrawImage(skImage, new SKRect(left, top, left + width, top + height));
                    }

                    canvas.Restore();
                    break;
                }
            }
        }
    }

    private static SKColor ToSkColor(PdfColor color) => new(color.R, color.G, color.B);

    private static bool IsSupportedImageContentType(string? contentType)
    {
        var normalized = contentType?.Split(';', 2)[0].Trim();
        return normalized is not null &&
               (normalized.Equals("image/png", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("image/jpg", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Draws text with per-codepoint font fallback: characters the base typeface cannot render
    /// (e.g. CJK with a Latin default font) are drawn with a system typeface resolved via
    /// <see cref="SKFontManager.MatchCharacter(string, int)"/>. Skia embeds whatever it draws, so
    /// the fallback glyphs are subset into the PDF too. Fallback typefaces are cached by family
    /// and disposed with the renderer.
    /// </summary>
    private sealed class FallbackTextRenderer : IDisposable
    {
        private readonly SKFontManager _fontManager = SKFontManager.Default;
        private readonly Dictionary<string, SKTypeface> _fallbackByFamily = new(StringComparer.Ordinal);

        public void DrawText(
            SKCanvas canvas,
            string text,
            float x,
            float baseline,
            SKTypeface baseTypeface,
            float sizePoints,
            SKPaint paint)
        {
            if (string.IsNullOrEmpty(text))
                return;

            var cursorX = x;
            var index = 0;
            while (index < text.Length)
            {
                var runTypeface = Resolve(baseTypeface, CodepointAt(text, index));
                var runStart = index;
                index += AdvanceLength(text, index);
                while (index < text.Length &&
                       ReferenceEquals(Resolve(baseTypeface, CodepointAt(text, index)), runTypeface))
                {
                    index += AdvanceLength(text, index);
                }

                var run = text.Substring(runStart, index - runStart);
                using var font = new SKFont(runTypeface, sizePoints);
                canvas.DrawText(run, cursorX, baseline, font, paint);
                cursorX += font.MeasureText(run);
            }
        }

        private SKTypeface Resolve(SKTypeface baseTypeface, int codepoint)
        {
            if (baseTypeface.GetGlyph(codepoint) != 0)
                return baseTypeface;

            var match = _fontManager.MatchCharacter(baseTypeface.FamilyName, codepoint);
            if (match is null)
                return baseTypeface;

            if (_fallbackByFamily.TryGetValue(match.FamilyName, out var cached))
            {
                if (!ReferenceEquals(cached, match))
                    match.Dispose();
                return cached;
            }

            _fallbackByFamily[match.FamilyName] = match;
            return match;
        }

        private static int CodepointAt(string text, int index) =>
            char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1])
                ? char.ConvertToUtf32(text[index], text[index + 1])
                : text[index];

        private static int AdvanceLength(string text, int index) =>
            char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1])
                ? 2
                : 1;

        public void Dispose()
        {
            foreach (var typeface in _fallbackByFamily.Values)
                typeface.Dispose();
            _fallbackByFamily.Clear();
        }
    }
}

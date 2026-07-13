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
            RenderDrawOp(canvas, op, pageHeight, regular, bold, fillPaint, strokePaint, textPaint, textRenderer);
    }

    private static void RenderDrawOp(
        SKCanvas canvas,
        PdfDrawOp op,
        float pageHeight,
        SKTypeface regular,
        SKTypeface bold,
        SKPaint fillPaint,
        SKPaint strokePaint,
        SKPaint textPaint,
        FallbackTextRenderer textRenderer)
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

            case PdfFillRectLinearGradient fill:
            {
                var top = pageHeight - (float)(fill.Y + fill.Height);
                ApplyLinearGradient(fillPaint, fill.Gradient, pageHeight, fill.FallbackColor);
                canvas.DrawRect(new SKRect((float)fill.X, top, (float)(fill.X + fill.Width), top + (float)fill.Height), fillPaint);
                fillPaint.Shader = null;
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

            case PdfStrokeRectLinearGradient stroke:
            {
                var top = pageHeight - (float)(stroke.Y + stroke.Height);
                ApplyLinearGradient(strokePaint, stroke.Gradient, pageHeight, stroke.FallbackColor);
                strokePaint.StrokeWidth = (float)stroke.LineWidth;
                canvas.DrawRect(new SKRect((float)stroke.X, top, (float)(stroke.X + stroke.Width), top + (float)stroke.Height), strokePaint);
                strokePaint.Shader = null;
                break;
            }

            case PdfFillEllipse fillEllipse:
            {
                var top = pageHeight - (float)(fillEllipse.Y + fillEllipse.Height);
                fillPaint.Color = ToSkColor(fillEllipse.Color);
                canvas.DrawOval(new SKRect(
                    (float)fillEllipse.X,
                    top,
                    (float)(fillEllipse.X + fillEllipse.Width),
                    top + (float)fillEllipse.Height), fillPaint);
                break;
            }

            case PdfFillEllipseLinearGradient fillEllipse:
            {
                var top = pageHeight - (float)(fillEllipse.Y + fillEllipse.Height);
                ApplyLinearGradient(fillPaint, fillEllipse.Gradient, pageHeight, fillEllipse.FallbackColor);
                canvas.DrawOval(new SKRect(
                    (float)fillEllipse.X,
                    top,
                    (float)(fillEllipse.X + fillEllipse.Width),
                    top + (float)fillEllipse.Height), fillPaint);
                fillPaint.Shader = null;
                break;
            }

            case PdfStrokeEllipse strokeEllipse:
            {
                var top = pageHeight - (float)(strokeEllipse.Y + strokeEllipse.Height);
                strokePaint.Color = ToSkColor(strokeEllipse.Color);
                strokePaint.StrokeWidth = (float)strokeEllipse.LineWidth;
                canvas.DrawOval(new SKRect(
                    (float)strokeEllipse.X,
                    top,
                    (float)(strokeEllipse.X + strokeEllipse.Width),
                    top + (float)strokeEllipse.Height), strokePaint);
                break;
            }

            case PdfStrokeEllipseLinearGradient strokeEllipse:
            {
                var top = pageHeight - (float)(strokeEllipse.Y + strokeEllipse.Height);
                ApplyLinearGradient(strokePaint, strokeEllipse.Gradient, pageHeight, strokeEllipse.FallbackColor);
                strokePaint.StrokeWidth = (float)strokeEllipse.LineWidth;
                canvas.DrawOval(new SKRect(
                    (float)strokeEllipse.X,
                    top,
                    (float)(strokeEllipse.X + strokeEllipse.Width),
                    top + (float)strokeEllipse.Height), strokePaint);
                strokePaint.Shader = null;
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

            case PdfLineLinearGradient line:
            {
                ApplyLinearGradient(strokePaint, line.Gradient, pageHeight, line.FallbackColor);
                strokePaint.StrokeWidth = (float)line.LineWidth;
                canvas.DrawLine(
                    (float)line.X1, pageHeight - (float)line.Y1,
                    (float)line.X2, pageHeight - (float)line.Y2,
                    strokePaint);
                strokePaint.Shader = null;
                break;
            }

            case PdfFilledTriangle triangle:
            {
                fillPaint.Color = ToSkColor(triangle.Color);
                using var path = new SKPath();
                path.MoveTo((float)triangle.X1, pageHeight - (float)triangle.Y1);
                path.LineTo((float)triangle.X2, pageHeight - (float)triangle.Y2);
                path.LineTo((float)triangle.X3, pageHeight - (float)triangle.Y3);
                path.Close();
                canvas.DrawPath(path, fillPaint);
                break;
            }

            case PdfPath pdfPath:
            {
                using var skPath = ToSkPath(pdfPath, pageHeight);
                if (pdfPath.FillColor is { } fill)
                {
                    fillPaint.Color = ToSkColor(fill);
                    canvas.DrawPath(skPath, fillPaint);
                }

                if (pdfPath.StrokeColor is { } stroke)
                {
                    strokePaint.Color = ToSkColor(stroke);
                    strokePaint.StrokeWidth = (float)Math.Max(0.1, pdfPath.StrokeWidth);
                    canvas.DrawPath(skPath, strokePaint);
                }

                break;
            }

            case PdfPathLinearGradient pdfPath:
            {
                using var skPath = ToSkPath(pdfPath.Contours, pageHeight);
                if (pdfPath.FillFallbackColor is { } fillFallback)
                {
                    if (pdfPath.FillGradient is { } fillGradient)
                        ApplyLinearGradient(fillPaint, fillGradient, pageHeight, fillFallback);
                    else
                        fillPaint.Color = ToSkColor(fillFallback);
                    canvas.DrawPath(skPath, fillPaint);
                    fillPaint.Shader = null;
                }

                if (pdfPath.StrokeFallbackColor is { } strokeFallback)
                {
                    if (pdfPath.StrokeGradient is { } strokeGradient)
                        ApplyLinearGradient(strokePaint, strokeGradient, pageHeight, strokeFallback);
                    else
                        strokePaint.Color = ToSkColor(strokeFallback);
                    strokePaint.StrokeWidth = (float)Math.Max(0.1, pdfPath.StrokeWidth);
                    canvas.DrawPath(skPath, strokePaint);
                    strokePaint.Shader = null;
                }

                break;
            }

            case PdfRotationGroup group:
            {
                if (group.Ops.Count == 0)
                    break;

                var centerX = (float)group.CenterX;
                var centerY = pageHeight - (float)group.CenterY;
                canvas.Save();
                canvas.Translate(centerX, centerY);
                canvas.RotateDegrees((float)group.RotationDegrees);
                canvas.Translate(-centerX, -centerY);
                foreach (var child in group.Ops)
                    RenderDrawOp(canvas, child, pageHeight, regular, bold, fillPaint, strokePaint, textPaint, textRenderer);
                canvas.Restore();
                break;
            }

            case PdfOpacityGroup group:
            {
                if (group.Ops.Count == 0)
                    break;

                using var layerPaint = new SKPaint
                {
                    Color = new SKColor(255, 255, 255, ToAlphaByte(group.Opacity)),
                };
                canvas.SaveLayer(layerPaint);
                foreach (var child in group.Ops)
                    RenderDrawOp(canvas, child, pageHeight, regular, bold, fillPaint, strokePaint, textPaint, textRenderer);
                canvas.Restore();
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

                using var transformedImage = ApplyColorEffects(skImage, image.ColorEffects);
                var drawImage = transformedImage ?? skImage;
                var top = pageHeight - (float)(image.Y + image.Height);
                var left = (float)image.X;
                var width = (float)image.Width;
                var height = (float)image.Height;
                using var imagePaint = CreateImagePaint(image.Opacity);
                canvas.Save();
                if (Math.Abs(image.RotationDegrees) > 0.001)
                {
                    canvas.Translate(left + width / 2f, top + height / 2f);
                    canvas.RotateDegrees((float)image.RotationDegrees);
                    var localRect = new SKRect(-width / 2f, -height / 2f, width / 2f, height / 2f);
                    ClipImage(canvas, image.ClipKind, localRect);
                    DrawImage(canvas, drawImage, image, localRect, imagePaint);
                }
                else
                {
                    var destRect = new SKRect(left, top, left + width, top + height);
                    ClipImage(canvas, image.ClipKind, destRect);
                    DrawImage(canvas, drawImage, image, destRect, imagePaint);
                }

                canvas.Restore();
                break;
            }
        }
    }

    private static void DrawImage(
        SKCanvas canvas,
        SKImage skImage,
        PdfImage image,
        SKRect destRect,
        SKPaint imagePaint)
    {
        if (TryGetSourceRect(skImage, image.SourceCrop, out var sourceRect))
            canvas.DrawImage(skImage, sourceRect, destRect, imagePaint);
        else
            canvas.DrawImage(skImage, destRect, imagePaint);
    }

    internal static SKImage? ApplyColorEffects(SKImage image, PdfImageColorEffects effects)
    {
        if (!effects.HasPixelEffects || image.Width <= 0 || image.Height <= 0)
            return null;

        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        if (!image.ReadPixels(info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0))
            return null;

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                var (r, g, b) = PdfImageColorEffectPixels.TransformRgb(color.Red, color.Green, color.Blue, effects);
                bitmap.SetPixel(x, y, new SKColor(r, g, b, color.Alpha));
            }
        }

        return SKImage.FromBitmap(bitmap);
    }

    internal static bool TryGetSourceRect(SKImage image, PdfImageSourceCrop crop, out SKRect sourceRect)
    {
        sourceRect = default;
        if (!crop.HasCrop || image.Width <= 0 || image.Height <= 0)
            return false;

        var sourceX = Clamp(
            (int)Math.Round(NormalizeCropFraction(crop.Left) * image.Width),
            0,
            image.Width - 1);
        var sourceY = Clamp(
            (int)Math.Round(NormalizeCropFraction(crop.Top) * image.Height),
            0,
            image.Height - 1);
        var sourceWidth = Clamp(
            (int)Math.Round((1.0 - NormalizeCropFraction(crop.Left) - NormalizeCropFraction(crop.Right)) * image.Width),
            1,
            image.Width - sourceX);
        var sourceHeight = Clamp(
            (int)Math.Round((1.0 - NormalizeCropFraction(crop.Top) - NormalizeCropFraction(crop.Bottom)) * image.Height),
            1,
            image.Height - sourceY);

        if (sourceX == 0 &&
            sourceY == 0 &&
            sourceWidth == image.Width &&
            sourceHeight == image.Height)
            return false;

        sourceRect = SKRect.Create(sourceX, sourceY, sourceWidth, sourceHeight);
        return true;
    }

    private static SKPath ToSkPath(PdfPath pdfPath, float pageHeight)
        => ToSkPath(pdfPath.Contours, pageHeight);

    private static SKPath ToSkPath(IReadOnlyList<PdfPathContour> contours, float pageHeight)
    {
        var path = new SKPath();
        foreach (var contour in contours)
        {
            path.MoveTo((float)contour.Start.X, pageHeight - (float)contour.Start.Y);
            foreach (var segment in contour.Segments)
            {
                switch (segment.Kind)
                {
                    case PdfPathSegmentKind.Line:
                        path.LineTo((float)segment.End.X, pageHeight - (float)segment.End.Y);
                        break;
                    case PdfPathSegmentKind.CubicBezier:
                        path.CubicTo(
                            (float)segment.Control1.X,
                            pageHeight - (float)segment.Control1.Y,
                            (float)segment.Control2.X,
                            pageHeight - (float)segment.Control2.Y,
                            (float)segment.End.X,
                            pageHeight - (float)segment.End.Y);
                        break;
                }
            }

            if (contour.Closed)
                path.Close();
        }

        return path;
    }

    private static void ApplyLinearGradient(
        SKPaint paint,
        PdfLinearGradient gradient,
        float pageHeight,
        PdfColor fallbackColor)
    {
        paint.Color = ToSkColor(fallbackColor);
        paint.Shader = null;
        if (!TryNormalizeGradient(gradient, out var stops))
            return;

        var colors = stops.Select(stop => ToSkColor(stop.Color)).ToArray();
        var positions = stops.Select(stop => (float)stop.Position).ToArray();
        paint.Shader = SKShader.CreateLinearGradient(
            new SKPoint((float)gradient.StartX, pageHeight - (float)gradient.StartY),
            new SKPoint((float)gradient.EndX, pageHeight - (float)gradient.EndY),
            colors,
            positions,
            SKShaderTileMode.Clamp);
    }

    private static bool TryNormalizeGradient(PdfLinearGradient gradient, out PdfGradientStop[] stops)
    {
        stops = [];
        if (!IsFinite(gradient.StartX) ||
            !IsFinite(gradient.StartY) ||
            !IsFinite(gradient.EndX) ||
            !IsFinite(gradient.EndY))
            return false;

        var dx = gradient.EndX - gradient.StartX;
        var dy = gradient.EndY - gradient.StartY;
        if ((dx * dx) + (dy * dy) < 0.000001)
            return false;

        stops = gradient.Stops
            .Where(stop => IsFinite(stop.Position))
            .Select(stop => new PdfGradientStop(Math.Clamp(stop.Position, 0.0, 1.0), stop.Color))
            .OrderBy(stop => stop.Position)
            .ToArray();
        if (stops.Length == 0)
            return false;
        if (stops.Length == 1)
            stops = [stops[0], new PdfGradientStop(1.0, stops[0].Color)];
        if (stops[0].Position > 0.0)
            stops = [new PdfGradientStop(0.0, stops[0].Color), .. stops];
        if (stops[^1].Position < 1.0)
            stops = [.. stops, new PdfGradientStop(1.0, stops[^1].Color)];
        return true;
    }

    private static SKPaint CreateImagePaint(double opacity) =>
        new()
        {
            IsAntialias = true,
            Color = new SKColor(255, 255, 255, ToAlphaByte(opacity)),
        };

    private static void ClipImage(SKCanvas canvas, PdfImageClipKind clipKind, SKRect bounds)
    {
        switch (clipKind)
        {
            case PdfImageClipKind.Ellipse:
            {
                using var path = new SKPath();
                path.AddOval(bounds);
                canvas.ClipPath(path, antialias: true);
                break;
            }
            case PdfImageClipKind.RoundedRectangle:
            {
                var radius = Math.Min(bounds.Width, bounds.Height) * 0.18f;
                using var roundRect = new SKRoundRect(bounds, radius, radius);
                canvas.ClipRoundRect(roundRect, antialias: true);
                break;
            }
            case PdfImageClipKind.Triangle:
            case PdfImageClipKind.Diamond:
            case PdfImageClipKind.Parallelogram:
            case PdfImageClipKind.Hexagon:
            case PdfImageClipKind.Chevron:
            {
                using var path = CreatePresetClipPath(clipKind, bounds);
                canvas.ClipPath(path, antialias: true);
                break;
            }
        }
    }

    internal static SKPath CreatePresetClipPath(PdfImageClipKind clipKind, SKRect bounds)
    {
        var path = new SKPath();
        var points = GetPresetClipPolygonPoints(clipKind, bounds);
        if (points.Length == 0)
            return path;

        path.MoveTo(points[0]);
        for (var i = 1; i < points.Length; i++)
            path.LineTo(points[i]);
        path.Close();
        return path;
    }

    private static SKPoint[] GetPresetClipPolygonPoints(PdfImageClipKind clipKind, SKRect bounds)
    {
        var midX = bounds.MidX;
        var midY = bounds.MidY;
        var quarterX = bounds.Left + bounds.Width * 0.25f;
        var threeQuarterX = bounds.Left + bounds.Width * 0.75f;

        return clipKind switch
        {
            PdfImageClipKind.Triangle =>
            [
                new SKPoint(midX, bounds.Top),
                new SKPoint(bounds.Right, bounds.Bottom),
                new SKPoint(bounds.Left, bounds.Bottom),
            ],
            PdfImageClipKind.Diamond =>
            [
                new SKPoint(midX, bounds.Top),
                new SKPoint(bounds.Right, midY),
                new SKPoint(midX, bounds.Bottom),
                new SKPoint(bounds.Left, midY),
            ],
            PdfImageClipKind.Parallelogram =>
            [
                new SKPoint(quarterX, bounds.Top),
                new SKPoint(bounds.Right, bounds.Top),
                new SKPoint(threeQuarterX, bounds.Bottom),
                new SKPoint(bounds.Left, bounds.Bottom),
            ],
            PdfImageClipKind.Hexagon =>
            [
                new SKPoint(quarterX, bounds.Top),
                new SKPoint(threeQuarterX, bounds.Top),
                new SKPoint(bounds.Right, midY),
                new SKPoint(threeQuarterX, bounds.Bottom),
                new SKPoint(quarterX, bounds.Bottom),
                new SKPoint(bounds.Left, midY),
            ],
            PdfImageClipKind.Chevron =>
            [
                new SKPoint(bounds.Left, bounds.Top),
                new SKPoint(threeQuarterX, bounds.Top),
                new SKPoint(bounds.Right, midY),
                new SKPoint(threeQuarterX, bounds.Bottom),
                new SKPoint(bounds.Left, bounds.Bottom),
                new SKPoint(quarterX, midY),
            ],
            _ => [],
        };
    }

    private static SKColor ToSkColor(PdfColor color) => new(color.R, color.G, color.B);

    private static byte ToAlphaByte(double opacity) =>
        (byte)Math.Clamp(Math.Round((double.IsFinite(opacity) ? opacity : 1.0) * 255.0), 0, 255);

    private static double NormalizeCropFraction(double value) =>
        double.IsFinite(value) ? value : 0.0;

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);

    private static int Clamp(int value, int min, int max) =>
        Math.Max(min, Math.Min(value, max));

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

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
    /// <summary>Serializes the shared content document to an embedded-font PDF.</summary>
    public static byte[] WriteToBytes(PdfContentDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        using var stream = new MemoryStream();
        Write(document, stream);
        return stream.ToArray();
    }

    /// <summary>
    /// Uses the Unicode-capable Skia backend when its native asset is available and retains the
    /// dependency-free writer as a platform fallback when Skia cannot initialize.
    /// </summary>
    public static byte[] WriteToBytesWithPortableFallback(PdfContentDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        try
        {
            return WriteToBytes(document);
        }
        catch (Exception ex) when (SkiaPdfAvailabilityHelper.IsSkiaUnavailable(ex))
        {
            return PortablePdfWriter.WriteToBytes(document);
        }
    }

    public static IReadOnlyList<byte[]> RenderPagesToPng(PdfContentDocument document, int dpi = 96)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Pages.Count == 0)
            throw new InvalidOperationException("Raster rendering requires at least one page.");
        if (dpi is < 36 or > 600)
            throw new ArgumentOutOfRangeException(nameof(dpi));

        using var regular = SKTypeface.FromFamilyName(
            null, SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
            ?? SKTypeface.Default;
        using var bold = SKTypeface.FromFamilyName(
            null, SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
            ?? regular;
        using var textRenderer = new FallbackTextRenderer();
        var scale = dpi / 72f;
        var pages = new List<byte[]>(document.Pages.Count);
        foreach (var page in document.Pages)
        {
            var width = Math.Max(1, (int)Math.Ceiling(page.WidthPoints * scale));
            var height = Math.Max(1, (int)Math.Ceiling(page.HeightPoints * scale));
            using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.White);
            canvas.Scale(scale, scale);
            RenderPage(canvas, page, regular, bold, textRenderer);
            canvas.Flush();
            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            pages.Add(encoded.ToArray());
        }
        return pages;
    }

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
                var top = (float)PdfRenderGeometry.ToCanvasTop(pageHeight, fill.Y, fill.Height);
                fillPaint.Color = ToSkColor(fill.Color);
                canvas.DrawRect(new SKRect((float)fill.X, top, (float)(fill.X + fill.Width), top + (float)fill.Height), fillPaint);
                break;
            }

            case PdfFillRectLinearGradient fill:
            {
                var top = (float)PdfRenderGeometry.ToCanvasTop(pageHeight, fill.Y, fill.Height);
                ApplyLinearGradient(fillPaint, fill.Gradient, pageHeight, fill.FallbackColor);
                canvas.DrawRect(new SKRect((float)fill.X, top, (float)(fill.X + fill.Width), top + (float)fill.Height), fillPaint);
                fillPaint.Shader = null;
                break;
            }

            case PdfFillRectPattern fill:
            {
                var top = (float)PdfRenderGeometry.ToCanvasTop(pageHeight, fill.Y, fill.Height);
                DrawPattern(canvas, fillPaint, fill.Pattern, () => canvas.DrawRect(new SKRect(
                    (float)fill.X,
                    top,
                    (float)(fill.X + fill.Width),
                    top + (float)fill.Height), fillPaint));
                break;
            }

            case PdfStrokeRect stroke:
            {
                var top = (float)PdfRenderGeometry.ToCanvasTop(pageHeight, stroke.Y, stroke.Height);
                strokePaint.Color = ToSkColor(stroke.Color);
                strokePaint.StrokeWidth = (float)stroke.LineWidth;
                using var strokeDash = CreateDashEffect(stroke.Dash);
                strokePaint.PathEffect = strokeDash;
                canvas.DrawRect(new SKRect((float)stroke.X, top, (float)(stroke.X + stroke.Width), top + (float)stroke.Height), strokePaint);
                strokePaint.PathEffect = null;
                break;
            }

            case PdfStrokeRectLinearGradient stroke:
            {
                var top = (float)PdfRenderGeometry.ToCanvasTop(pageHeight, stroke.Y, stroke.Height);
                ApplyLinearGradient(strokePaint, stroke.Gradient, pageHeight, stroke.FallbackColor);
                strokePaint.StrokeWidth = (float)stroke.LineWidth;
                using var gradientStrokeDash = CreateDashEffect(stroke.Dash);
                strokePaint.PathEffect = gradientStrokeDash;
                canvas.DrawRect(new SKRect((float)stroke.X, top, (float)(stroke.X + stroke.Width), top + (float)stroke.Height), strokePaint);
                strokePaint.PathEffect = null;
                strokePaint.Shader = null;
                break;
            }

            case PdfFillEllipse fillEllipse:
            {
                var top = (float)PdfRenderGeometry.ToCanvasTop(pageHeight, fillEllipse.Y, fillEllipse.Height);
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
                var top = (float)PdfRenderGeometry.ToCanvasTop(pageHeight, fillEllipse.Y, fillEllipse.Height);
                ApplyLinearGradient(fillPaint, fillEllipse.Gradient, pageHeight, fillEllipse.FallbackColor);
                canvas.DrawOval(new SKRect(
                    (float)fillEllipse.X,
                    top,
                    (float)(fillEllipse.X + fillEllipse.Width),
                    top + (float)fillEllipse.Height), fillPaint);
                fillPaint.Shader = null;
                break;
            }

            case PdfFillEllipsePattern fillEllipse:
            {
                var top = (float)PdfRenderGeometry.ToCanvasTop(pageHeight, fillEllipse.Y, fillEllipse.Height);
                DrawPattern(canvas, fillPaint, fillEllipse.Pattern, () => canvas.DrawOval(new SKRect(
                    (float)fillEllipse.X,
                    top,
                    (float)(fillEllipse.X + fillEllipse.Width),
                    top + (float)fillEllipse.Height), fillPaint));
                break;
            }

            case PdfStrokeEllipse strokeEllipse:
            {
                var top = (float)PdfRenderGeometry.ToCanvasTop(pageHeight, strokeEllipse.Y, strokeEllipse.Height);
                strokePaint.Color = ToSkColor(strokeEllipse.Color);
                strokePaint.StrokeWidth = (float)strokeEllipse.LineWidth;
                using var ellipseStrokeDash = CreateDashEffect(strokeEllipse.Dash);
                strokePaint.PathEffect = ellipseStrokeDash;
                canvas.DrawOval(new SKRect(
                    (float)strokeEllipse.X,
                    top,
                    (float)(strokeEllipse.X + strokeEllipse.Width),
                    top + (float)strokeEllipse.Height), strokePaint);
                strokePaint.PathEffect = null;
                break;
            }

            case PdfStrokeEllipseLinearGradient strokeEllipse:
            {
                var top = (float)PdfRenderGeometry.ToCanvasTop(pageHeight, strokeEllipse.Y, strokeEllipse.Height);
                ApplyLinearGradient(strokePaint, strokeEllipse.Gradient, pageHeight, strokeEllipse.FallbackColor);
                strokePaint.StrokeWidth = (float)strokeEllipse.LineWidth;
                using var gradientEllipseStrokeDash = CreateDashEffect(strokeEllipse.Dash);
                strokePaint.PathEffect = gradientEllipseStrokeDash;
                canvas.DrawOval(new SKRect(
                    (float)strokeEllipse.X,
                    top,
                    (float)(strokeEllipse.X + strokeEllipse.Width),
                    top + (float)strokeEllipse.Height), strokePaint);
                strokePaint.PathEffect = null;
                strokePaint.Shader = null;
                break;
            }

            case PdfText text:
            {
                if (string.IsNullOrEmpty(text.Text))
                    break;

                // PDF text origin is the baseline (y-up). Skia DrawText baseline is y-down.
                var baseline = (float)PdfRenderGeometry.ToCanvasY(pageHeight, text.Y);
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
                    (float)line.X1, (float)PdfRenderGeometry.ToCanvasY(pageHeight, line.Y1),
                    (float)line.X2, (float)PdfRenderGeometry.ToCanvasY(pageHeight, line.Y2),
                    strokePaint);
                break;
            }

            case PdfLineLinearGradient line:
            {
                ApplyLinearGradient(strokePaint, line.Gradient, pageHeight, line.FallbackColor);
                strokePaint.StrokeWidth = (float)line.LineWidth;
                canvas.DrawLine(
                    (float)line.X1, (float)PdfRenderGeometry.ToCanvasY(pageHeight, line.Y1),
                    (float)line.X2, (float)PdfRenderGeometry.ToCanvasY(pageHeight, line.Y2),
                    strokePaint);
                strokePaint.Shader = null;
                break;
            }

            case PdfFilledTriangle triangle:
            {
                fillPaint.Color = ToSkColor(triangle.Color);
                using var path = new SKPath();
                path.MoveTo((float)triangle.X1, (float)PdfRenderGeometry.ToCanvasY(pageHeight, triangle.Y1));
                path.LineTo((float)triangle.X2, (float)PdfRenderGeometry.ToCanvasY(pageHeight, triangle.Y2));
                path.LineTo((float)triangle.X3, (float)PdfRenderGeometry.ToCanvasY(pageHeight, triangle.Y3));
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
                    using var pathStrokeDash = CreateDashEffect(pdfPath.StrokeDash);
                    strokePaint.PathEffect = pathStrokeDash;
                    canvas.DrawPath(skPath, strokePaint);
                    strokePaint.PathEffect = null;
                }

                break;
            }

            case PdfPathPattern pdfPath:
            {
                using var skPath = ToSkPath(pdfPath.Contours, pageHeight);
                DrawPattern(canvas, fillPaint, pdfPath.Pattern, () => canvas.DrawPath(skPath, fillPaint));
                if (pdfPath.StrokeColor is { } stroke)
                {
                    strokePaint.Color = ToSkColor(stroke);
                    strokePaint.StrokeWidth = (float)Math.Max(0.1, pdfPath.StrokeWidth);
                    using var patternPathStrokeDash = CreateDashEffect(pdfPath.StrokeDash);
                    strokePaint.PathEffect = patternPathStrokeDash;
                    canvas.DrawPath(skPath, strokePaint);
                    strokePaint.PathEffect = null;
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
                    using var gradientPathStrokeDash = CreateDashEffect(pdfPath.StrokeDash);
                    strokePaint.PathEffect = gradientPathStrokeDash;
                    canvas.DrawPath(skPath, strokePaint);
                    strokePaint.PathEffect = null;
                    strokePaint.Shader = null;
                }

                break;
            }

            case PdfRotationGroup group:
            {
                if (group.Ops.Count == 0)
                    break;

                var centerX = (float)group.CenterX;
                var centerY = (float)PdfRenderGeometry.ToCanvasY(pageHeight, group.CenterY);
                canvas.Save();
                canvas.Translate(centerX, centerY);
                canvas.Scale(group.FlipH ? -1 : 1, group.FlipV ? -1 : 1);
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
                if (!PdfRenderGeometry.IsSupportedImageContentType(image.ContentType) || image.ImageBytes.Length == 0)
                    break;

                using var data = SKData.CreateCopy(image.ImageBytes);
                using var skImage = SKImage.FromEncodedData(data);
                if (skImage is null)
                    break;

                using var transformedImage = ApplyColorEffects(skImage, image.ColorEffects);
                var drawImage = transformedImage ?? skImage;
                var top = (float)PdfRenderGeometry.ToCanvasTop(pageHeight, image.Y, image.Height);
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
        if (!PdfRenderGeometry.TryGetImageSourceRect(image.Width, image.Height, crop, out var pdfRect))
            return false;

        sourceRect = SKRect.Create(pdfRect.X, pdfRect.Y, pdfRect.Width, pdfRect.Height);
        return true;
    }

    private static SKPath ToSkPath(PdfPath pdfPath, float pageHeight)
        => ToSkPath(pdfPath.Contours, pageHeight);

    private static SKPath ToSkPath(IReadOnlyList<PdfPathContour> contours, float pageHeight)
    {
        var path = new SKPath();
        foreach (var contour in contours)
        {
            path.MoveTo((float)contour.Start.X, (float)PdfRenderGeometry.ToCanvasY(pageHeight, contour.Start.Y));
            foreach (var segment in contour.Segments)
            {
                switch (segment.Kind)
                {
                    case PdfPathSegmentKind.Line:
                        path.LineTo((float)segment.End.X, (float)PdfRenderGeometry.ToCanvasY(pageHeight, segment.End.Y));
                        break;
                    case PdfPathSegmentKind.CubicBezier:
                        path.CubicTo(
                            (float)segment.Control1.X,
                            (float)PdfRenderGeometry.ToCanvasY(pageHeight, segment.Control1.Y),
                            (float)segment.Control2.X,
                            (float)PdfRenderGeometry.ToCanvasY(pageHeight, segment.Control2.Y),
                            (float)segment.End.X,
                            (float)PdfRenderGeometry.ToCanvasY(pageHeight, segment.End.Y));
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
        if (!PdfRenderGeometry.TryNormalizeGradient(gradient, out var stops))
            return;

        var colors = stops.Select(stop => ToSkColor(stop.Color)).ToArray();
        var positions = stops.Select(stop => (float)stop.Position).ToArray();
        paint.Shader = SKShader.CreateLinearGradient(
            new SKPoint((float)gradient.StartX, (float)PdfRenderGeometry.ToCanvasY(pageHeight, gradient.StartY)),
            new SKPoint((float)gradient.EndX, (float)PdfRenderGeometry.ToCanvasY(pageHeight, gradient.EndY)),
            colors,
            positions,
            SKShaderTileMode.Clamp);
    }

    private static void DrawPattern(SKCanvas canvas, SKPaint paint, PdfPatternFill pattern, Action draw)
    {
        using var bitmap = CreatePatternBitmap(pattern);
        using var shader = SKShader.CreateBitmap(bitmap, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat);
        paint.Shader = shader;
        draw();
        paint.Shader = null;
    }

    private static SKBitmap CreatePatternBitmap(PdfPatternFill pattern)
    {
        var width = Math.Max(1, (int)Math.Round(pattern.TileWidth));
        var height = Math.Max(1, (int)Math.Round(pattern.TileHeight));
        var scaleX = width / pattern.TileWidth;
        var scaleY = height / pattern.TileHeight;
        var unit = pattern.UnitScale * Math.Min(scaleX, scaleY);
        var midX = width / 2f;
        var midY = height / 2f;
        var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        using (var background = new SKPaint { Color = ToSkColor(pattern.Background), Style = SKPaintStyle.Fill, IsAntialias = true })
        using (var foreground = new SKPaint { Color = ToSkColor(pattern.Foreground), Style = SKPaintStyle.Stroke, StrokeWidth = (float)(pattern.StrokeWidth * Math.Min(scaleX, scaleY)), IsAntialias = true })
        {
            canvas.DrawRect(new SKRect(0, 0, width, height), background);
            switch (pattern.Kind)
            {
                case PdfPatternKind.Horizontal:
                    canvas.DrawLine(0, midY, width, midY, foreground);
                    break;
                case PdfPatternKind.Vertical:
                    canvas.DrawLine(midX, 0, midX, height, foreground);
                    break;
                case PdfPatternKind.DownDiagonal:
                    canvas.DrawLine(0, 0, width, height, foreground);
                    break;
                case PdfPatternKind.UpDiagonal:
                    canvas.DrawLine(0, height, width, 0, foreground);
                    break;
                case PdfPatternKind.Cross:
                    canvas.DrawLine(0, midY, width, midY, foreground);
                    canvas.DrawLine(midX, 0, midX, height, foreground);
                    break;
                case PdfPatternKind.Dot:
                    foreground.Style = SKPaintStyle.Fill;
                    canvas.DrawCircle(midX, midY, (float)(unit / 2), foreground);
                    break;
                case PdfPatternKind.Brick:
                    canvas.DrawLine(0, 0, width, 0, foreground);
                    canvas.DrawLine(6 * (float)unit, 4 * (float)unit, width, 4 * (float)unit, foreground);
                    canvas.DrawLine(0, 4 * (float)unit, 3 * (float)unit, 4 * (float)unit, foreground);
                    canvas.DrawLine(6 * (float)unit, 0, 6 * (float)unit, 4 * (float)unit, foreground);
                    canvas.DrawLine(0, 4 * (float)unit, 0, height, foreground);
                    canvas.DrawLine(width, 4 * (float)unit, width, height, foreground);
                    break;
                case PdfPatternKind.DiagonalCross:
                    canvas.DrawLine(0, 0, width, height, foreground);
                    canvas.DrawLine(width, 0, 0, height, foreground);
                    break;
            }
        }

        return bitmap;
    }

    private static SKPathEffect? CreateDashEffect(PdfDashPattern? dash)
    {
        if (dash is null)
            return null;

        var segments = dash.Segments
            .Where(segment => double.IsFinite(segment) && segment > 0)
            .Select(segment => (float)segment)
            .ToArray();
        return segments.Length == 0
            ? null
            : SKPathEffect.CreateDash(segments, (float)(double.IsFinite(dash.Phase) ? dash.Phase : 0));
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
                var radius = (float)PdfRenderGeometry.RoundedClipRadius(bounds.Width, bounds.Height);
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
        var pdfPoints = PdfRenderGeometry.GetPresetClipPolygonPoints(
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            clipKind);
        if (pdfPoints.Length == 0)
            return path;

        var canvasYSum = bounds.Top + bounds.Bottom;
        path.MoveTo((float)pdfPoints[0].X, canvasYSum - (float)pdfPoints[0].Y);
        for (var i = 1; i < pdfPoints.Length; i++)
            path.LineTo((float)pdfPoints[i].X, canvasYSum - (float)pdfPoints[i].Y);
        path.Close();
        return path;
    }

    private static SKColor ToSkColor(PdfColor color) => new(color.R, color.G, color.B);

    private static byte ToAlphaByte(double opacity) =>
        (byte)Math.Clamp(Math.Round((double.IsFinite(opacity) ? opacity : 1.0) * 255.0), 0, 255);

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

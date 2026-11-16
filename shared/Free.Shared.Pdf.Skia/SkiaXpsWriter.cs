using System.Globalization;
using System.Text;
using System.Xml;
using Free.Shared.Xps;
using SkiaSharp;

namespace Free.Shared.Pdf.Skia;

/// <summary>Publishes native XPS geometry, images, and text, compositing unsupported artwork in place.</summary>
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
        using var renderer = new DocumentRenderer(dpi, diagnostics);
        var pages = document.Pages.Select(renderer.RenderPage).ToArray();
        return PortableXpsWriter.WriteToBytes(
            document with { Pages = pages },
            new XpsWriterOptions { GlyphOverlays = renderer.GlyphRuns });
    }

    private sealed class DocumentRenderer(int dpi, ICollection<string>? diagnostics) : IDisposable
    {
        private readonly SkiaPdfWriter.PdfTypefaceSet _typefaces = SkiaPdfWriter.PdfTypefaceSet.Create();
        private readonly SkiaPdfWriter.FallbackTextRenderer _textRenderer = new();
        private readonly SkiaXpsFontResources _fonts = new();
        private readonly HashSet<string> _textWarnings = new(StringComparer.Ordinal);

        public Dictionary<PdfText, XpsGlyphRun> GlyphRuns { get; } = new(ReferenceEqualityComparer.Instance);

        public PdfContentPage RenderPage(PdfContentPage page)
        {
            if (!double.IsFinite(page.WidthPoints) || !double.IsFinite(page.HeightPoints) ||
                page.WidthPoints <= 0 || page.HeightPoints <= 0)
            {
                throw new ArgumentException("XPS requires finite, positive page dimensions.", nameof(page));
            }

            var ops = new List<PdfDrawOp>
            {
                new PdfFillRect(0, 0, page.WidthPoints, page.HeightPoints, new PdfColor(255, 255, 255))
            };
            AppendOperations(page, page.Ops, ops, allowRaster: true);
            return page with { Ops = ops };
        }

        private bool AppendOperations(
            PdfContentPage page,
            IReadOnlyList<PdfDrawOp> source,
            List<PdfDrawOp> target,
            bool allowRaster)
        {
            var adjacentRasterOps = new List<PdfDrawOp>();
            var nativeOps = new List<PdfDrawOp>();
            foreach (var op in source)
            {
                if (!TryAppendNative(page, op, nativeOps, allowRaster))
                {
                    if (!allowRaster)
                        return false;

                    adjacentRasterOps.Add(op);
                    continue;
                }

                if (nativeOps.Count == 0)
                    continue;

                FlushRasterOps();
                target.AddRange(nativeOps);
                nativeOps.Clear();
            }
            FlushRasterOps();
            return true;

            void FlushRasterOps()
            {
                if (adjacentRasterOps.Count == 0)
                    return;

                AppendRasterLayer(page, adjacentRasterOps, target);
                adjacentRasterOps.Clear();
            }
        }

        private bool TryAppendNative(PdfContentPage page, PdfDrawOp op, List<PdfDrawOp> target, bool allowRaster)
        {
            switch (op)
            {
                case PdfText text:
                    return TryAppendTextRuns(text, target, transparent: false);
                case PdfRotationGroup rotation:
                {
                    // Raster layers are page-bounded. A rotated child can bring previously
                    // off-page content into view, so rasterize the transformed group together.
                    var children = new List<PdfDrawOp>();
                    if (!AppendOperations(page, rotation.Ops, children, allowRaster: false))
                        return false;

                    if (children.Count > 0)
                        target.Add(rotation with { Ops = children });
                    return true;
                }
                case PdfClipGroup clip:
                {
                    if (clip.Width <= 0 || clip.Height <= 0)
                        return true;

                    var children = new List<PdfDrawOp>();
                    if (!AppendOperations(page, clip.Ops, children, allowRaster))
                        return false;

                    if (children.Count > 0)
                        target.Add(clip with { Ops = children });
                    return true;
                }
                case PdfOpacityGroup opacity:
                {
                    var alpha = SkiaPdfWriter.ToAlphaByte(opacity.Opacity);
                    if (alpha == 0)
                        return true;

                    var children = new List<PdfDrawOp>();
                    if (!AppendOperations(page, opacity.Ops, children, allowRaster))
                        return false;

                    if (children.Count > 0)
                        target.Add(opacity with { Ops = children, Opacity = alpha / 255d });
                    return true;
                }
                case PdfImage image:
                    if (!PortableXpsWriter.CanRenderImage(image))
                        return false;

                    // Keep original pixels for supported images. Let Skia diagnose invalid
                    // encodings through its existing rendering path.
                    using (var decoded = SKImage.FromEncodedData(image.ImageBytes))
                    {
                        if (decoded is null)
                            return false;
                    }
                    break;
                default:
                    if (!CanRenderShape(op))
                        return false;
                    break;
            }
            target.Add(op);
            return true;
        }

        private static bool CanRenderShape(PdfDrawOp op) => op switch
        {
            // Skia draws a device-pixel hairline for a zero-width stroke.
            PdfStrokeRect stroke => stroke.LineWidth > 0,
            PdfStrokeEllipse stroke => stroke.LineWidth > 0,
            PdfLine line => line.LineWidth > 0,
            PdfFillRectLinearGradient fill => PortableXpsWriter.CanRenderLinearGradient(fill.Gradient),
            PdfFillEllipseLinearGradient fill => PortableXpsWriter.CanRenderLinearGradient(fill.Gradient),
            PdfStrokeRectLinearGradient stroke =>
                stroke.LineWidth > 0 && PortableXpsWriter.CanRenderLinearGradient(stroke.Gradient),
            PdfStrokeEllipseLinearGradient stroke =>
                stroke.LineWidth > 0 && PortableXpsWriter.CanRenderLinearGradient(stroke.Gradient),
            PdfLineLinearGradient line =>
                line.LineWidth > 0 && PortableXpsWriter.CanRenderLinearGradient(line.Gradient),
            PdfPathLinearGradient path =>
                (path.FillFallbackColor is null || path.FillGradient is null ||
                    PortableXpsWriter.CanRenderLinearGradient(path.FillGradient)) &&
                (path.StrokeFallbackColor is null || path.StrokeGradient is null ||
                    PortableXpsWriter.CanRenderLinearGradient(path.StrokeGradient)),
            PdfFillRectPattern fill => PortableXpsPatternBrush.CanRender(fill.Pattern),
            PdfFillEllipsePattern fill => PortableXpsPatternBrush.CanRender(fill.Pattern),
            PdfPathPattern path => PortableXpsPatternBrush.CanRender(path.Pattern),
            PdfFillRect or PdfFillEllipse or PdfFilledTriangle or PdfPath => true,
            _ => false,
        };

        private void AppendRasterLayer(PdfContentPage page, List<PdfDrawOp> source, List<PdfDrawOp> target)
        {
            using var image = SkiaPdfWriter.RasterizePage(
                page with { Ops = source }, dpi, _typefaces, _textRenderer, diagnostics, transparentBackground: true);
            using var pixels = image.PeekPixels()
                ?? throw new InvalidOperationException("Could not read the XPS raster layer.");
            var bounds = GetVisibleBounds(pixels);
            if (bounds.IsEmpty)
                return;

            using var content = image.Subset(bounds)
                ?? throw new InvalidOperationException("Could not crop the XPS raster layer.");
            using var encoded = content.Encode(SKEncodedImageFormat.Png, 100)
                ?? throw new InvalidOperationException("Could not encode the XPS raster layer.");
            var pointsPerPixel = 72d / dpi;
            target.Add(new PdfImage(
                bounds.Left * pointsPerPixel,
                page.HeightPoints - bounds.Bottom * pointsPerPixel,
                bounds.Width * pointsPerPixel,
                bounds.Height * pointsPerPixel,
                encoded.ToArray(),
                "image/png"));
            AppendSelectableText(source, target);
        }

        private static SKRectI GetVisibleBounds(SKPixmap pixels)
        {
            var data = pixels.GetPixelSpan();
            var left = pixels.Width;
            var top = pixels.Height;
            var right = 0;
            var bottom = 0;
            for (var y = 0; y < pixels.Height; y++)
            {
                var row = data.Slice(y * pixels.RowBytes, pixels.Width * 4);
                for (var x = 0; x < pixels.Width; x++)
                {
                    if (row[x * 4 + 3] == 0)
                        continue;

                    left = Math.Min(left, x);
                    top = Math.Min(top, y);
                    right = Math.Max(right, x + 1);
                    bottom = y + 1;
                }
            }
            // Retain a transparent pixel at the boundary so viewer interpolation can
            // blend coverage smoothly after scaling the fallback layer.
            return right == 0 ? SKRectI.Empty : new SKRectI(
                Math.Max(0, left - 1), Math.Max(0, top - 1),
                Math.Min(pixels.Width, right + 1), Math.Min(pixels.Height, bottom + 1));
        }

        private void AppendSelectableText(IReadOnlyList<PdfDrawOp> source, List<PdfDrawOp> target)
        {
            foreach (var op in source)
            {
                // Effects draw derivative copies of text. The actual text is emitted
                // independently by the workbook renderer.
                switch (op)
                {
                    case PdfText text:
                        TryAppendTextRuns(text, target, transparent: true);
                        break;
                    case PdfRotationGroup rotation:
                    {
                        var children = new List<PdfDrawOp>();
                        AppendSelectableText(rotation.Ops, children);
                        if (children.Count > 0)
                            target.Add(rotation with { Ops = children });
                        break;
                    }
                    case PdfClipGroup clip when clip.Width > 0 && clip.Height > 0:
                    {
                        var children = new List<PdfDrawOp>();
                        AppendSelectableText(clip.Ops, children);
                        if (children.Count > 0)
                            target.Add(clip with { Ops = children });
                        break;
                    }
                    case PdfOpacityGroup opacity when SkiaPdfWriter.ToAlphaByte(opacity.Opacity) > 0:
                        AppendSelectableText(opacity.Ops, target);
                        break;
                }
            }
        }

        private bool TryAppendTextRuns(PdfText text, List<PdfDrawOp> target, bool transparent)
        {
            if (string.IsNullOrEmpty(text.Text) || !float.IsFinite((float)text.FontSize) || (float)text.FontSize <= 0)
                return true;

            var runs = new List<(PdfText Text, XpsGlyphRun Glyph)>();
            var typeface = _typefaces.For(text.FontFamily, text.Face);
            var x = (float)text.X;
            var index = 0;
            while (index < text.Text.Length)
            {
                var runTypeface = _textRenderer.ResolveRun(text.Text, index, typeface, out var end);
                var run = text.Text[index..end];
                using var font = new SKFont(runTypeface, (float)text.FontSize);
                if (IsValidXmlText(run))
                {
                    var glyphIds = font.GetGlyphs(run);
                    var resource = Array.IndexOf(glyphIds, (ushort)0) < 0 ? _fonts.For(runTypeface) : null;
                    if (transparent && resource is null)
                        resource = FindSearchFont(run, text.Face, out glyphIds);
                    if (resource is null)
                    {
                        if (!transparent)
                            return false;

                        var name = string.IsNullOrEmpty(runTypeface.FamilyName) ? "default" : runTypeface.FamilyName;
                        WarnOnce($"XPS could not embed the font '{name}' or a substitute. Its text remains in the page artwork.");
                    }
                    else
                    {
                        var indices = BuildIndices(run, glyphIds, font.GetGlyphWidths(run), font.Size);
                        runs.Add((text with { X = x, Text = run },
                            new XpsGlyphRun(resource, indices) { IsTransparent = transparent }));
                    }
                }
                else
                {
                    if (!transparent)
                        return false;

                    WarnOnce("Text with characters unsupported in XPS markup remains in the page artwork.");
                }

                x += font.MeasureText(run);
                index = end;
            }

            foreach (var (glyphText, glyph) in runs)
            {
                GlyphRuns.Add(glyphText, glyph);
                target.Add(glyphText);
            }
            return true;
        }

        private XpsFontResource? FindSearchFont(string text, PdfFontFace face, out ushort[] glyphIds)
        {
            // Substitute only in the invisible search layer. Preserve the original font's artwork
            // and advances.
            foreach (var family in SearchFontFamilies)
            {
                var typeface = _typefaces.For(family, face);
                using var font = new SKFont(typeface, 1);
                var candidate = font.GetGlyphs(text);
                if (!candidate.Contains((ushort)0) && _fonts.For(typeface) is { } resource)
                {
                    glyphIds = candidate;
                    return resource;
                }
            }
            glyphIds = [];
            return null;
        }

        private void WarnOnce(string warning)
        {
            if (diagnostics is not null && _textWarnings.Add(warning))
                diagnostics.Add(warning);
        }

        public void Dispose()
        {
            _textRenderer.Dispose();
            _typefaces.Dispose();
        }
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

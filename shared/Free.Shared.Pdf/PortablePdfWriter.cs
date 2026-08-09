using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Free.Shared.Pdf;

/// <summary>
/// Dependency-free WinAnsi (Helvetica) PDF writer. Serializes an app-agnostic
/// <see cref="PdfContentDocument"/> (draw-op pages) to PDF 1.7 bytes using only built-in
/// Type1 Helvetica faces — no font files, no native dependencies — so it runs anywhere including
/// fully headless environments.
///
/// <para>
/// This is the lossless extraction of FreeX's original <c>PortablePdfDocumentExporter</c> emitter:
/// the per-op content-stream operators, the object/xref/trailer layout, and the WinAnsi text
/// encoding are byte-for-byte identical, which is what keeps FreeX's pinned PDF tests green.
/// Text outside ASCII/WinAnsi throws (callers should preflight via
/// <see cref="PdfWinAnsiTextCapability"/>); geometry is supplied by the caller via draw ops.
/// </para>
/// </summary>
public static class PortablePdfWriter
{
    private static readonly Encoding PdfEncoding = Encoding.ASCII;
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly EffectBlurStamp[] EffectBlurStamps =
    [
        new(-1, -1, 0.08), new(0, -1, 0.12), new(1, -1, 0.08),
        new(-1, 0, 0.12), new(0, 0, 0.20), new(1, 0, 0.12),
        new(-1, 1, 0.08), new(0, 1, 0.12), new(1, 1, 0.08),
    ];
    /// <summary>
    /// Largest PNG accepted for embedding, in pixels. Keeps every width×height×channels buffer
    /// below <see cref="int.MaxValue"/>, so a declared size can never overflow into a negative
    /// allocation. Well past any image a real document embeds (this is ~8000×8000).
    /// </summary>
    private const long MaxPngPixelCount = 64_000_000L;

    private const int ReflectionPassCount = 12;
    private const string DeferredUnicodePdfPathRequirements =
        PdfWinAnsiTextCapability.DeferredUnicodePdfPathRequirements;

    /// <summary>Header comment written after the <c>%PDF-1.7</c> marker.</summary>
    public const string DefaultHeaderComment = "FreeX portable PDF";

    /// <summary>
    /// Serializes <paramref name="document"/> to <paramref name="stream"/>. Each page is rendered to
    /// a content stream from its draw ops; pages may differ in size. The writer overwrites a seekable
    /// stream from position 0.
    /// </summary>
    /// <param name="imageDiagnostics">
    /// Optional sink for non-fatal image warnings. An embedded image whose format this writer cannot
    /// decode (e.g. a CMYK JPEG or an interlaced PNG) is omitted from the page rather than failing the
    /// whole export; when this collection is supplied, one message per omitted image is appended to it
    /// so the loss is discoverable instead of silent.
    /// </param>
    public static void Write(
        PdfContentDocument document,
        Stream stream,
        string headerComment = DefaultHeaderComment,
        ICollection<string>? imageDiagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanWrite)
            throw new ArgumentException("Portable PDF export requires a writable stream.", nameof(stream));
        if (document.Pages.Count == 0)
            throw new InvalidOperationException("Portable PDF export requires at least one rendered page.");

        if (stream.CanSeek)
        {
            stream.Position = 0;
            stream.SetLength(0);
        }

        var fontResources = BuildFontResources(document);
        var imageResources = BuildImageResources(document, imageDiagnostics);
        var opacityResources = BuildOpacityResources(document);
        var patternResources = BuildPatternResources(document);
        var pages = document.Pages
            .Select(page => (
                Content: RenderContentStream(page.Ops, imageResources.ByOp, opacityResources.ByOpacity, patternResources),
                Width: page.WidthPoints,
                Height: page.HeightPoints,
                Links: BuildLinkAnnotations(page),
                Destinations: BuildNamedDestinations(page)))
            .ToArray();
        WritePdf(stream, pages, fontResources, imageResources.Resources, opacityResources.Resources, patternResources.Resources, headerComment);
    }

    /// <summary>Serializes <paramref name="document"/> to an in-memory byte array.</summary>
    /// <param name="imageDiagnostics">See <see cref="Write"/>.</param>
    public static byte[] WriteToBytes(
        PdfContentDocument document,
        string headerComment = DefaultHeaderComment,
        ICollection<string>? imageDiagnostics = null)
    {
        using var stream = new MemoryStream();
        Write(document, stream, headerComment, imageDiagnostics);
        return stream.ToArray();
    }

    private static string RenderContentStream(
        IReadOnlyList<PdfDrawOp> ops,
        IReadOnlyDictionary<PdfImage, PdfImageResource> imageResources,
        IReadOnlyDictionary<double, PdfOpacityResource> opacityResources,
        PatternResourceSet patternResources)
    {
        var content = new StringBuilder();
        foreach (var op in ops)
            AppendDrawOp(content, op, imageResources, opacityResources, patternResources);

        return content.ToString();
    }

    private static void AppendDrawOp(
        StringBuilder content,
        PdfDrawOp op,
        IReadOnlyDictionary<PdfImage, PdfImageResource> imageResources,
        IReadOnlyDictionary<double, PdfOpacityResource> opacityResources,
        PatternResourceSet patternResources,
        PdfColor? colorOverride = null)
    {
        switch (op)
        {
            case PdfFillRect fill:
                AppendFilledRectangle(content, fill.X, fill.Y, fill.Width, fill.Height, colorOverride ?? fill.Color);
                break;
            case PdfFillRectPattern fill:
                if (colorOverride is { } patternColor)
                    AppendFilledRectangle(content, fill.X, fill.Y, fill.Width, fill.Height, patternColor);
                else
                    AppendFilledRectanglePattern(content, fill.X, fill.Y, fill.Width, fill.Height, fill.Pattern, patternResources);
                break;
            case PdfFillRectLinearGradient fill:
                if (colorOverride is { } gradientColor)
                    AppendFilledRectangle(content, fill.X, fill.Y, fill.Width, fill.Height, gradientColor);
                else
                    AppendFilledRectangleLinearGradient(content, fill.X, fill.Y, fill.Width, fill.Height, fill.Gradient, patternResources, fill.FallbackColor);
                break;
            case PdfStrokeRect stroke:
                AppendStrokedRectangle(content, stroke.X, stroke.Y, stroke.Width, stroke.Height, colorOverride ?? stroke.Color, stroke.LineWidth, stroke.Dash);
                break;
            case PdfStrokeRectLinearGradient stroke:
                if (colorOverride is { } gradientStrokeColor)
                    AppendStrokedRectangle(content, stroke.X, stroke.Y, stroke.Width, stroke.Height, gradientStrokeColor, stroke.LineWidth, stroke.Dash);
                else
                    AppendStrokedRectangleLinearGradient(content, stroke.X, stroke.Y, stroke.Width, stroke.Height, stroke.Gradient, patternResources, stroke.FallbackColor, stroke.LineWidth, stroke.Dash);
                break;
            case PdfFillEllipse fillEllipse:
                AppendFilledEllipse(content, fillEllipse.X, fillEllipse.Y, fillEllipse.Width, fillEllipse.Height, colorOverride ?? fillEllipse.Color);
                break;
            case PdfFillEllipsePattern fillEllipse:
                if (colorOverride is { } ellipsePatternColor)
                    AppendFilledEllipse(content, fillEllipse.X, fillEllipse.Y, fillEllipse.Width, fillEllipse.Height, ellipsePatternColor);
                else
                    AppendFilledEllipsePattern(content, fillEllipse.X, fillEllipse.Y, fillEllipse.Width, fillEllipse.Height, fillEllipse.Pattern, patternResources);
                break;
            case PdfFillEllipseLinearGradient fillEllipse:
                if (colorOverride is { } ellipseGradientColor)
                    AppendFilledEllipse(content, fillEllipse.X, fillEllipse.Y, fillEllipse.Width, fillEllipse.Height, ellipseGradientColor);
                else
                    AppendFilledEllipseLinearGradient(content, fillEllipse.X, fillEllipse.Y, fillEllipse.Width, fillEllipse.Height, fillEllipse.Gradient, patternResources, fillEllipse.FallbackColor);
                break;
            case PdfStrokeEllipse strokeEllipse:
                AppendStrokedEllipse(
                    content,
                    strokeEllipse.X,
                    strokeEllipse.Y,
                    strokeEllipse.Width,
                    strokeEllipse.Height,
                    colorOverride ?? strokeEllipse.Color,
                    strokeEllipse.LineWidth,
                    strokeEllipse.Dash);
                break;
            case PdfStrokeEllipseLinearGradient strokeEllipse:
                if (colorOverride is { } ellipseStrokeColor)
                    AppendStrokedEllipse(content, strokeEllipse.X, strokeEllipse.Y, strokeEllipse.Width, strokeEllipse.Height, ellipseStrokeColor, strokeEllipse.LineWidth, strokeEllipse.Dash);
                else
                    AppendStrokedEllipseLinearGradient(content, strokeEllipse.X, strokeEllipse.Y, strokeEllipse.Width, strokeEllipse.Height, strokeEllipse.Gradient, patternResources, strokeEllipse.FallbackColor, strokeEllipse.LineWidth, strokeEllipse.Dash);
                break;
            case PdfText text:
                AppendText(content, text.X, text.Y, text.FontSize, FontResource(text.Face), colorOverride ?? text.Color, text.Text);
                break;
            case PdfLine line:
                AppendLine(content, line.X1, line.Y1, line.X2, line.Y2, colorOverride ?? line.Color, line.LineWidth);
                break;
            case PdfLineLinearGradient line:
                if (colorOverride is { } lineColor)
                    AppendLine(content, line.X1, line.Y1, line.X2, line.Y2, lineColor, line.LineWidth);
                else
                    AppendLineLinearGradient(content, line.X1, line.Y1, line.X2, line.Y2, line.Gradient, patternResources, line.FallbackColor, line.LineWidth);
                break;
            case PdfFilledTriangle triangle:
                AppendFilledTriangle(
                    content,
                    triangle.X1,
                    triangle.Y1,
                    triangle.X2,
                    triangle.Y2,
                    triangle.X3,
                    triangle.Y3,
                    colorOverride ?? triangle.Color);
                break;
            case PdfPath path:
                AppendPath(content, colorOverride is { } pathColor
                    ? path with { FillColor = path.FillColor is not null ? pathColor : null, StrokeColor = path.StrokeColor is not null ? pathColor : null }
                    : path);
                break;
            case PdfPathPattern path:
                if (colorOverride is { } pathPatternColor)
                    AppendPath(content, new PdfPath(path.Contours, pathPatternColor, path.StrokeColor is not null ? pathPatternColor : null, path.StrokeWidth, path.StrokeDash));
                else
                    AppendPathPattern(content, path, patternResources);
                break;
            case PdfPathLinearGradient path:
                if (colorOverride is { } pathGradientColor)
                    AppendPath(content, new PdfPath(path.Contours, path.FillFallbackColor is not null ? pathGradientColor : null, path.StrokeFallbackColor is not null ? pathGradientColor : null, path.StrokeWidth, path.StrokeDash));
                else
                    AppendPathLinearGradient(content, path, patternResources);
                break;
            case PdfRotationGroup group:
                AppendRotationGroup(content, group, imageResources, opacityResources, patternResources, colorOverride);
                break;
            case PdfClipGroup group:
                AppendClipGroup(content, group, imageResources, opacityResources, patternResources, colorOverride);
                break;
            case PdfOpacityGroup group:
                opacityResources.TryGetValue(PdfRenderGeometry.NormalizeOpacity(group.Opacity), out var groupOpacityResource);
                AppendOpacityGroup(content, group, groupOpacityResource?.ResourceName, imageResources, opacityResources, patternResources, colorOverride);
                break;
            case PdfEffectGroup group:
                AppendEffectGroup(content, group, imageResources, opacityResources, patternResources);
                break;
            case PdfImage image when imageResources.TryGetValue(image, out var resource):
                opacityResources.TryGetValue(PdfRenderGeometry.NormalizeOpacity(image.Opacity), out var imageOpacityResource);
                AppendImage(content, image, resource, imageOpacityResource?.ResourceName);
                break;
        }
    }

    private static string FontResource(PdfFontFace face) => face switch
    {
        PdfFontFace.Bold => "F2",
        PdfFontFace.Italic => "F3",
        PdfFontFace.BoldItalic => "F4",
        _ => "F1",
    };

    private static void WritePdf(
        Stream stream,
        IReadOnlyList<(string Content, double Width, double Height, IReadOnlyList<PdfLinkAnnotation> Links, IReadOnlyList<PdfNamedDestination> Destinations)> pages,
        IReadOnlyList<(string ResourceName, string BaseFont)> fontResources,
        IReadOnlyList<PdfImageResource> imageResources,
        IReadOnlyList<PdfOpacityResource> opacityResources,
        IReadOnlyList<PdfPatternResource> patternResources,
        string headerComment)
    {
        var objects = new List<PdfObject>();
        var firstPageObjectId = 3 + fontResources.Count + imageResources.Count + opacityResources.Count + patternResources.Count;
        var pageObjectIds = Enumerable.Range(0, pages.Count)
            .Select(index => firstPageObjectId + (index * 2))
            .ToArray();
        var destinations = new Dictionary<string, PdfResolvedDestination>(StringComparer.Ordinal);
        for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++)
        {
            foreach (var destination in pages[pageIndex].Destinations)
            {
                if (!destinations.ContainsKey(destination.Name))
                {
                    destinations[destination.Name] = new PdfResolvedDestination(
                        pageObjectIds[pageIndex],
                        destination.X,
                        pages[pageIndex].Height - destination.Y);
                }
            }
        }
        var validLinksByPage = pages
            .Select(page => page.Links
                .Where(link => !string.IsNullOrWhiteSpace(link.Uri)
                    || (link.DestinationName is { Length: > 0 } name && destinations.ContainsKey(name)))
                .ToArray())
            .ToArray();
        var nextAnnotationObjectId = firstPageObjectId + (pages.Count * 2);
        var annotationObjectIdsByPage = validLinksByPage
            .Select(links =>
            {
                var ids = Enumerable.Range(nextAnnotationObjectId, links.Length).ToArray();
                nextAnnotationObjectId += links.Length;
                return ids;
            })
            .ToArray();

        objects.Add(PdfObject.Ascii("<< /Type /Catalog /Pages 2 0 R >>"));
        objects.Add(PdfObject.Ascii($"<< /Type /Pages /Kids [{string.Join(" ", pageObjectIds.Select(id => $"{id} 0 R"))}] /Count {pages.Count} >>"));
        foreach (var font in fontResources)
            objects.Add(PdfObject.Ascii($"<< /Type /Font /Subtype /Type1 /BaseFont /{font.BaseFont} /Encoding /WinAnsiEncoding >>"));

        foreach (var image in imageResources)
            objects.Add(CreateImageObject(image));
        foreach (var opacity in opacityResources)
            objects.Add(CreateOpacityObject(opacity));
        foreach (var pattern in patternResources)
            objects.Add(CreatePatternObject(pattern));

        var fontResourceDictionary = string.Join(
            " ",
            fontResources.Select((font, index) => $"/{font.ResourceName} {index + 3} 0 R"));
        var imageResourceDictionary = string.Join(
            " ",
            imageResources.Select((image, index) => $"/{image.ResourceName} {index + 3 + fontResources.Count} 0 R"));
        var opacityResourceDictionary = string.Join(
            " ",
            opacityResources.Select((opacity, index) => $"/{opacity.ResourceName} {index + 3 + fontResources.Count + imageResources.Count} 0 R"));
        var patternResourceDictionary = string.Join(
            " ",
            patternResources.Select((pattern, index) => $"/{pattern.ResourceName} {index + 3 + fontResources.Count + imageResources.Count + opacityResources.Count} 0 R"));
        var xObjectResources = imageResources.Count == 0
            ? string.Empty
            : $" /XObject << {imageResourceDictionary} >>";
        var extGStateResources = opacityResources.Count == 0
            ? string.Empty
            : $" /ExtGState << {opacityResourceDictionary} >>";
        var patternResourceDictionaryText = patternResources.Count == 0
            ? string.Empty
            : $" /Pattern << {patternResourceDictionary} >>";

        for (var index = 0; index < pages.Count; index++)
        {
            var pageObjectId = pageObjectIds[index];
            var contentObjectId = pageObjectId + 1;
            var annotations = annotationObjectIdsByPage[index].Length == 0
                ? string.Empty
                : $" /Annots [{string.Join(" ", annotationObjectIdsByPage[index].Select(id => $"{id} 0 R"))}]";
            objects.Add(PdfObject.Ascii(
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {FormatNumber(pages[index].Width)} {FormatNumber(pages[index].Height)}] /Resources << /Font << {fontResourceDictionary} >>{xObjectResources}{extGStateResources}{patternResourceDictionaryText} >> /Contents {contentObjectId} 0 R{annotations} >>"));

            var pageStream = pages[index].Content.EndsWith("\n", StringComparison.Ordinal)
                ? pages[index].Content
                : pages[index].Content + "\n";
            objects.Add(PdfObject.Ascii($"<< /Length {PdfEncoding.GetByteCount(pageStream)} >>\nstream\n{pageStream}endstream"));
        }

        foreach (var links in validLinksByPage)
        foreach (var link in links)
        {
            var target = !string.IsNullOrWhiteSpace(link.Uri)
                ? $"/A << /S /URI /URI {EncodeTextOperand(link.Uri!)} >>"
                : destinations.TryGetValue(link.DestinationName!, out var destination)
                    ? $"/Dest [{destination.PageObjectId} 0 R /XYZ {FormatNumber(destination.X)} {FormatNumber(destination.Top)} null]"
                    : string.Empty;
            objects.Add(PdfObject.Ascii(
                $"<< /Type /Annot /Subtype /Link " +
                $"/Rect [{FormatNumber(link.Left)} {FormatNumber(link.Bottom)} {FormatNumber(link.Right)} {FormatNumber(link.Top)}] " +
                $"/H /I /F 4 /Border [0 0 0] " +
                $"/Contents {EncodeTextOperand(link.Tooltip ?? link.Uri ?? link.DestinationName ?? string.Empty)} " +
                $"{target} >>"));
        }

        WriteAscii(stream, $"%PDF-1.7\n% {headerComment}\n");
        var offsets = new List<long> { 0 };
        for (var objectIndex = 0; objectIndex < objects.Count; objectIndex++)
        {
            offsets.Add(stream.Position);
            WriteAscii(stream, $"{objectIndex + 1} 0 obj\n");
            stream.Write(objects[objectIndex].Bytes);
            WriteAscii(stream, "\nendobj\n");
        }

        var xrefOffset = stream.Position;
        WriteAscii(stream, $"xref\n0 {objects.Count + 1}\n");
        WriteAscii(stream, "0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
            WriteAscii(stream, $"{offset.ToString("0000000000", CultureInfo.InvariantCulture)} 00000 n \n");

        WriteAscii(
            stream,
            $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset.ToString(CultureInfo.InvariantCulture)}\n%%EOF\n");
    }

    private static IReadOnlyList<PdfLinkAnnotation> BuildLinkAnnotations(PdfContentPage page)
    {
        if (page.LinkOverlays is not { Count: > 0 })
            return [];

        var links = new List<PdfLinkAnnotation>(page.LinkOverlays.Count);
        foreach (var overlay in page.LinkOverlays)
        {
            if (!double.IsFinite(overlay.X)
                || !double.IsFinite(overlay.Y)
                || !double.IsFinite(overlay.Width)
                || !double.IsFinite(overlay.Height)
                || overlay.Width <= 0
                || overlay.Height <= 0)
                continue;

            var uri = overlay.Uri?.Trim();
            var destinationName = overlay.DestinationName?.Trim();
            if (string.IsNullOrEmpty(uri) && string.IsNullOrEmpty(destinationName))
                continue;

            var left = Math.Clamp(overlay.X, 0, page.WidthPoints);
            var right = Math.Clamp(overlay.X + overlay.Width, 0, page.WidthPoints);
            var top = Math.Clamp(page.HeightPoints - overlay.Y, 0, page.HeightPoints);
            var bottom = Math.Clamp(page.HeightPoints - (overlay.Y + overlay.Height), 0, page.HeightPoints);
            if (right <= left || top <= bottom)
                continue;

            links.Add(new PdfLinkAnnotation(left, bottom, right, top, uri, overlay.Tooltip, destinationName));
        }

        return links;
    }

    private static IReadOnlyList<PdfNamedDestination> BuildNamedDestinations(PdfContentPage page)
    {
        if (page.NamedDestinations is not { Count: > 0 })
            return [];

        return page.NamedDestinations
            .Where(destination => !string.IsNullOrWhiteSpace(destination.Name)
                && double.IsFinite(destination.X)
                && double.IsFinite(destination.Y))
            .Select(destination => destination with
            {
                Name = destination.Name.Trim(),
                X = Math.Clamp(destination.X, 0, page.WidthPoints),
                Y = Math.Clamp(destination.Y, 0, page.HeightPoints),
            })
            .ToArray();
    }

    private static IReadOnlyList<(string ResourceName, string BaseFont)> BuildFontResources(PdfContentDocument document)
    {
        var hasItalicText = document.Pages
            .SelectMany(page => page.Ops)
            .SelectMany(EnumerateOps)
            .OfType<PdfText>()
            .Any(text => text.Face is PdfFontFace.Italic or PdfFontFace.BoldItalic);

        return hasItalicText
            ? [
                ("F1", "Helvetica"),
                ("F2", "Helvetica-Bold"),
                ("F3", "Helvetica-Oblique"),
                ("F4", "Helvetica-BoldOblique"),
            ]
            : [
                ("F1", "Helvetica"),
                ("F2", "Helvetica-Bold"),
            ];
    }

    private static ImageResourceSet BuildImageResources(PdfContentDocument document, ICollection<string>? imageDiagnostics)
    {
        var byOp = new Dictionary<PdfImage, PdfImageResource>(ReferenceEqualityComparer.Instance);
        var resources = new List<PdfImageResource>();

        foreach (var image in document.Pages.SelectMany(page => page.Ops).SelectMany(EnumerateOps).OfType<PdfImage>())
        {
            if (byOp.ContainsKey(image))
                continue;
            if (!TryCreateImageResource($"Im{resources.Count + 1}", image, imageDiagnostics, out var resource))
                continue;

            resources.Add(resource);
            byOp.Add(image, resource);
        }

        return new ImageResourceSet(resources, byOp);
    }

    private static OpacityResourceSet BuildOpacityResources(PdfContentDocument document)
    {
        var byOpacity = new Dictionary<double, PdfOpacityResource>();
        var resources = new List<PdfOpacityResource>();

        foreach (var opacity in document.Pages
            .SelectMany(page => page.Ops)
            .SelectMany(EnumerateOps)
            .SelectMany(EnumerateOpacities)
            .Where(opacity => opacity < 1.0))
        {
            if (byOpacity.ContainsKey(opacity))
                continue;

            var resource = new PdfOpacityResource($"GS{resources.Count + 1}", opacity);
            resources.Add(resource);
            byOpacity.Add(opacity, resource);
        }

        return new OpacityResourceSet(resources, byOpacity);
    }

    private static PatternResourceSet BuildPatternResources(PdfContentDocument document)
    {
        var byGradient = new Dictionary<PdfLinearGradient, PdfPatternResource>(ReferenceEqualityComparer.Instance);
        var byPattern = new Dictionary<PdfPatternFill, PdfPatternResource>();
        var resources = new List<PdfPatternResource>();

        foreach (var gradient in document.Pages
            .SelectMany(page => page.Ops)
            .SelectMany(EnumerateOps)
            .SelectMany(EnumerateGradients))
        {
            if (byGradient.ContainsKey(gradient) || !TryCreateNormalizedGradient(gradient, out var normalized))
                continue;

            var resource = new PdfPatternResource($"P{resources.Count + 1}", normalized);
            resources.Add(resource);
            byGradient.Add(gradient, resource);
        }

        foreach (var pattern in document.Pages
            .SelectMany(page => page.Ops)
            .SelectMany(EnumerateOps)
            .SelectMany(EnumeratePatternFills))
        {
            if (byPattern.ContainsKey(pattern))
                continue;

            var resource = new PdfPatternResource($"P{resources.Count + 1}", Pattern: pattern);
            resources.Add(resource);
            byPattern.Add(pattern, resource);
        }

        return new PatternResourceSet(resources, byGradient, byPattern);
    }

    private static bool TryCreateImageResource(
        string resourceName,
        PdfImage image,
        ICollection<string>? imageDiagnostics,
        out PdfImageResource resource)
    {
        resource = default!;
        if (image.Width <= 0 || image.Height <= 0 || image.ImageBytes.Length == 0)
            return false;

        try
        {
            var contentType = NormalizeContentType(image.ContentType);
            resource = contentType switch
            {
                "image/png" => DecodePng(resourceName, image),
                "image/jpeg" or "image/jpg" => DecodeJpeg(resourceName, image.ImageBytes),
                _ => null!,
            };
        }
        catch (Exception ex) when (IsRecoverableImageDecodeException(ex))
        {
            imageDiagnostics?.Add(
                $"Image at ({FormatNumber(image.X)}, {FormatNumber(image.Y)}) [{image.ContentType}] could not be " +
                $"decoded and was omitted from the exported PDF: {ex.Message}");
            return false;
        }

        return resource is not null;
    }

    private static IEnumerable<PdfDrawOp> EnumerateOps(PdfDrawOp op)
    {
        yield return op;

        if (op is PdfRotationGroup group)
        {
            foreach (var child in group.Ops.SelectMany(EnumerateOps))
                yield return child;
        }

        if (op is PdfClipGroup clipGroup)
        {
            foreach (var child in clipGroup.Ops.SelectMany(EnumerateOps))
                yield return child;
        }

        if (op is PdfOpacityGroup opacityGroup)
        {
            foreach (var child in opacityGroup.Ops.SelectMany(EnumerateOps))
                yield return child;
        }

        if (op is PdfEffectGroup effectGroup)
        {
            foreach (var child in effectGroup.Ops.SelectMany(EnumerateOps))
                yield return child;
        }
    }

    private static IEnumerable<double> EnumerateOpacities(PdfDrawOp op)
    {
        switch (op)
        {
            case PdfImage image:
                yield return PdfRenderGeometry.NormalizeOpacity(image.Opacity);
                break;
            case PdfOpacityGroup group:
                yield return PdfRenderGeometry.NormalizeOpacity(group.Opacity);
                break;
            case PdfEffectGroup group:
                switch (group.Kind)
                {
                    case PdfEffectKind.Shadow:
                        foreach (var opacity in EnumerateEffectPassOpacities(
                                     group.Parameters.Opacity,
                                     group.Parameters.Radius * 0.18))
                            yield return opacity;
                        break;
                    case PdfEffectKind.Glow:
                        var glowRadius = Math.Max(1, group.Parameters.Radius);
                        for (var index = 3; index >= 1; index--)
                        {
                            var spread = glowRadius * index / 3;
                            foreach (var opacity in EnumerateEffectPassOpacities(
                                         group.Parameters.Opacity * (0.18 + 0.08 * (3 - index)),
                                         spread))
                                yield return opacity;
                        }
                        break;
                    case PdfEffectKind.SoftEdge:
                        var softEdgeRadius = Math.Max(1, group.Parameters.Radius);
                        for (var index = 3; index >= 1; index--)
                        {
                            var spread = softEdgeRadius * index / 3;
                            foreach (var opacity in EnumerateEffectPassOpacities(
                                         group.Parameters.Opacity * 0.12,
                                         spread))
                                yield return opacity;
                        }
                        break;
                    case PdfEffectKind.Reflection:
                        yield return PdfRenderGeometry.NormalizeOpacity(group.Parameters.Opacity);
                        for (var index = 0; index < ReflectionPassCount; index++)
                            yield return ReflectionPassOpacity(group, index);
                        break;
                    case PdfEffectKind.Bevel:
                        foreach (var band in PdfRenderGeometry.GetBevelBands(group))
                            yield return PdfRenderGeometry.NormalizeOpacity(
                                group.Parameters.Opacity * band.OpacityScale);
                        break;
                }
                break;
        }
    }

    private static IEnumerable<double> EnumerateEffectPassOpacities(double opacity, double spread)
    {
        if (spread <= 0)
        {
            yield return PdfRenderGeometry.NormalizeOpacity(opacity);
            yield break;
        }

        foreach (var stamp in EffectBlurStamps)
            yield return PdfRenderGeometry.NormalizeOpacity(opacity * stamp.Weight);
    }

    private static IEnumerable<PdfLinearGradient> EnumerateGradients(PdfDrawOp op)
    {
        switch (op)
        {
            case PdfFillRectLinearGradient fill:
                yield return fill.Gradient;
                break;
            case PdfStrokeRectLinearGradient stroke:
                yield return stroke.Gradient;
                break;
            case PdfFillEllipseLinearGradient fillEllipse:
                yield return fillEllipse.Gradient;
                break;
            case PdfStrokeEllipseLinearGradient strokeEllipse:
                yield return strokeEllipse.Gradient;
                break;
            case PdfLineLinearGradient line:
                yield return line.Gradient;
                break;
            case PdfPathLinearGradient path:
                if (path.FillGradient is { } fillGradient)
                    yield return fillGradient;
                if (path.StrokeGradient is { } strokeGradient)
                    yield return strokeGradient;
                break;
        }
    }

    private static IEnumerable<PdfPatternFill> EnumeratePatternFills(PdfDrawOp op)
    {
        switch (op)
        {
            case PdfFillRectPattern fill:
                yield return fill.Pattern;
                break;
            case PdfFillEllipsePattern fill:
                yield return fill.Pattern;
                break;
            case PdfPathPattern path:
                yield return path.Pattern;
                break;
        }
    }

    private static PdfObject CreateImageObject(PdfImageResource image)
    {
        var header =
            $"<< /Type /XObject /Subtype /Image /Width {image.PixelWidth} /Height {image.PixelHeight} " +
            $"/ColorSpace /{image.ColorSpace} /BitsPerComponent 8 /Filter /{image.Filter} /Length {image.Data.Length} >>\nstream\n";
        const string footer = "\nendstream";

        using var stream = new MemoryStream(PdfEncoding.GetByteCount(header) + image.Data.Length + PdfEncoding.GetByteCount(footer));
        stream.Write(PdfEncoding.GetBytes(header));
        stream.Write(image.Data);
        stream.Write(PdfEncoding.GetBytes(footer));
        return new PdfObject(stream.ToArray());
    }

    private static PdfObject CreateOpacityObject(PdfOpacityResource opacity) =>
        PdfObject.Ascii(
            $"<< /Type /ExtGState /ca {FormatNumber(opacity.Opacity)} /CA {FormatNumber(opacity.Opacity)} >>");

    private static PdfObject CreatePatternObject(PdfPatternResource pattern)
    {
        if (pattern.Pattern is { } tiledPattern)
            return CreateTiledPatternObject(tiledPattern);

        if (pattern.Gradient is null)
            throw new InvalidOperationException("PDF pattern resources require a gradient or tiled pattern.");

        var gradient = pattern.Gradient;
        var stops = gradient.Stops;
        var function = stops.Count == 2
            ? BuildLinearInterpolationFunction(stops[0].Color, stops[1].Color)
            : BuildStitchingFunction(stops);

        return PdfObject.Ascii(
            "<< /Type /Pattern /PatternType 2 " +
            "/Shading << /ShadingType 2 /ColorSpace /DeviceRGB " +
            $"/Coords [{FormatNumber(gradient.StartX)} {FormatNumber(gradient.StartY)} {FormatNumber(gradient.EndX)} {FormatNumber(gradient.EndY)}] " +
            $"/Function {function} /Extend [true true] >> >>");
    }

    private static PdfObject CreateTiledPatternObject(PdfPatternFill pattern)
    {
        var stream = new StringBuilder();
        stream.AppendLine("q");
        AppendRgb(stream, pattern.Background, "rg");
        stream.AppendLine($"0 0 {FormatNumber(pattern.TileWidth)} {FormatNumber(pattern.TileHeight)} re f");
        AppendRgb(stream, pattern.Foreground, "RG");
        stream.AppendLine($"{FormatNumber(pattern.StrokeWidth)} w");
        AppendPatternTileGeometry(stream, pattern);
        stream.AppendLine("Q");

        var bytes = PdfEncoding.GetBytes(stream.ToString());
        var header =
            "<< /Type /Pattern /PatternType 1 /PaintType 1 /TilingType 1 " +
            $"/BBox [0 0 {FormatNumber(pattern.TileWidth)} {FormatNumber(pattern.TileHeight)}] " +
            $"/XStep {FormatNumber(pattern.TileWidth)} /YStep {FormatNumber(pattern.TileHeight)} " +
            $"/Resources << >> /Length {bytes.Length} >>\nstream\n";
        var footer = "\nendstream";
        using var output = new MemoryStream(PdfEncoding.GetByteCount(header) + bytes.Length + PdfEncoding.GetByteCount(footer));
        output.Write(PdfEncoding.GetBytes(header));
        output.Write(bytes);
        output.Write(PdfEncoding.GetBytes(footer));
        return new PdfObject(output.ToArray());
    }

    private static void AppendPatternTileGeometry(StringBuilder content, PdfPatternFill pattern)
    {
        var width = pattern.TileWidth;
        var height = pattern.TileHeight;
        var unit = pattern.UnitScale;
        var midX = width / 2;
        var midY = height / 2;

        switch (pattern.Kind)
        {
            case PdfPatternKind.Horizontal:
                content.AppendLine($"0 {FormatNumber(midY)} m {FormatNumber(width)} {FormatNumber(midY)} l S");
                break;
            case PdfPatternKind.Vertical:
                content.AppendLine($"{FormatNumber(midX)} 0 m {FormatNumber(midX)} {FormatNumber(height)} l S");
                break;
            case PdfPatternKind.DownDiagonal:
                // The shared kind names the WPF screen-space direction. PDF uses y-up.
                content.AppendLine($"0 {FormatNumber(height)} m {FormatNumber(width)} 0 l S");
                break;
            case PdfPatternKind.UpDiagonal:
                content.AppendLine($"0 0 m {FormatNumber(width)} {FormatNumber(height)} l S");
                break;
            case PdfPatternKind.Cross:
                content.AppendLine($"0 {FormatNumber(midY)} m {FormatNumber(width)} {FormatNumber(midY)} l S");
                content.AppendLine($"{FormatNumber(midX)} 0 m {FormatNumber(midX)} {FormatNumber(height)} l S");
                break;
            case PdfPatternKind.Dot:
                AppendRgb(content, pattern.Foreground, "rg");
                AppendPatternEllipse(content, midX, midY, unit, unit);
                content.AppendLine("f");
                break;
            case PdfPatternKind.Brick:
                content.AppendLine($"0 0 m {FormatNumber(width)} 0 l S");
                content.AppendLine($"{FormatNumber(6 * unit)} {FormatNumber(4 * unit)} m {FormatNumber(width)} {FormatNumber(4 * unit)} l S");
                content.AppendLine($"0 {FormatNumber(4 * unit)} m {FormatNumber(3 * unit)} {FormatNumber(4 * unit)} l S");
                content.AppendLine($"{FormatNumber(6 * unit)} 0 m {FormatNumber(6 * unit)} {FormatNumber(4 * unit)} l S");
                content.AppendLine($"0 {FormatNumber(4 * unit)} m 0 {FormatNumber(height)} l S");
                content.AppendLine($"{FormatNumber(width)} {FormatNumber(4 * unit)} m {FormatNumber(width)} {FormatNumber(height)} l S");
                break;
            case PdfPatternKind.DiagonalCross:
                content.AppendLine($"0 {FormatNumber(height)} m {FormatNumber(width)} 0 l S");
                content.AppendLine($"0 0 m {FormatNumber(width)} {FormatNumber(height)} l S");
                break;
        }
    }

    private static void AppendPatternEllipse(StringBuilder content, double centerX, double centerY, double width, double height)
    {
        const double kappa = 0.5522847498307936;
        var rx = width / 2;
        var ry = height / 2;
        var ox = rx * kappa;
        var oy = ry * kappa;
        content.AppendLine($"{FormatNumber(centerX + rx)} {FormatNumber(centerY)} m");
        content.AppendLine($"{FormatNumber(centerX + rx)} {FormatNumber(centerY + oy)} {FormatNumber(centerX + ox)} {FormatNumber(centerY + ry)} {FormatNumber(centerX)} {FormatNumber(centerY + ry)} c");
        content.AppendLine($"{FormatNumber(centerX - ox)} {FormatNumber(centerY + ry)} {FormatNumber(centerX - rx)} {FormatNumber(centerY + oy)} {FormatNumber(centerX - rx)} {FormatNumber(centerY)} c");
        content.AppendLine($"{FormatNumber(centerX - rx)} {FormatNumber(centerY - oy)} {FormatNumber(centerX - ox)} {FormatNumber(centerY - ry)} {FormatNumber(centerX)} {FormatNumber(centerY - ry)} c");
        content.AppendLine($"{FormatNumber(centerX + ox)} {FormatNumber(centerY - ry)} {FormatNumber(centerX + rx)} {FormatNumber(centerY - oy)} {FormatNumber(centerX + rx)} {FormatNumber(centerY)} c");
    }

    private static string BuildStitchingFunction(IReadOnlyList<PdfGradientStop> stops)
    {
        var functions = new List<string>(stops.Count - 1);
        for (var i = 0; i < stops.Count - 1; i++)
            functions.Add(BuildLinearInterpolationFunction(stops[i].Color, stops[i + 1].Color));

        var bounds = string.Join(" ", stops.Skip(1).Take(stops.Count - 2).Select(stop => FormatNumber(stop.Position)));
        var encode = string.Join(" ", Enumerable.Repeat("0 1", stops.Count - 1));
        return $"<< /FunctionType 3 /Domain [0 1] /Functions [{string.Join(" ", functions)}] /Bounds [{bounds}] /Encode [{encode}] >>";
    }

    private static string BuildLinearInterpolationFunction(PdfColor start, PdfColor end) =>
        "<< /FunctionType 2 /Domain [0 1] " +
        $"/C0 [{FormatColorComponent(start.R)} {FormatColorComponent(start.G)} {FormatColorComponent(start.B)}] " +
        $"/C1 [{FormatColorComponent(end.R)} {FormatColorComponent(end.G)} {FormatColorComponent(end.B)}] /N 1 >>";

    private static bool TryCreateNormalizedGradient(PdfLinearGradient gradient, out PdfLinearGradient normalized)
    {
        normalized = gradient;
        if (!PdfRenderGeometry.TryNormalizeGradient(gradient, out var stops))
            return false;

        normalized = gradient with { Stops = stops };
        return true;
    }

    private static PdfImageResource DecodeJpeg(string resourceName, byte[] bytes)
    {
        var (width, height, components) = ReadJpegSize(bytes);
        var colorSpace = components switch
        {
            1 => "DeviceGray",
            3 => "DeviceRGB",
            _ => throw new NotSupportedException("Portable PDF image export supports grayscale and RGB JPEG images."),
        };

        return new PdfImageResource(resourceName, width, height, colorSpace, "DCTDecode", bytes);
    }

    private static PdfImageResource DecodePng(string resourceName, PdfImage image)
    {
        var decoded = DecodePngToPdfPixels(image.ImageBytes);
        if (image.ColorEffects.HasPixelEffects)
            decoded = ApplyColorEffects(decoded, image.ColorEffects);

        return new PdfImageResource(
            resourceName,
            decoded.Width,
            decoded.Height,
            decoded.ColorSpace,
            "FlateDecode",
            Deflate(decoded.Pixels));
    }

    private static PngPdfPixels DecodePngToPdfPixels(byte[] data)
    {
        if (data.Length < PngSignature.Length || !data.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature))
            throw new FormatException("Not a PNG image.");

        var position = PngSignature.Length;
        var width = 0;
        var height = 0;
        var bitDepth = 0;
        var colorType = 0;
        var interlace = 0;
        byte[]? palette = null;
        using var idat = new MemoryStream();

        while (position + 8 <= data.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(position, 4));
            position += 4;
            var chunkType = PdfEncoding.GetString(data, position, 4);
            position += 4;
            if (length < 0 || position + length > data.Length)
                throw new FormatException($"PNG chunk '{chunkType}' overruns the file.");

            switch (chunkType)
            {
                case "IHDR":
                    width = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(position, 4));
                    height = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(position + 4, 4));
                    bitDepth = data[position + 8];
                    colorType = data[position + 9];
                    interlace = data[position + 12];
                    break;
                case "PLTE":
                    palette = data.AsSpan(position, length).ToArray();
                    break;
                case "IDAT":
                    idat.Write(data, position, length);
                    break;
            }

            position += length + 4;
            if (chunkType == "IEND")
                break;
        }

        if (width <= 0 || height <= 0)
            throw new FormatException("PNG image is missing valid dimensions.");
        // width and height come straight from IHDR, and every buffer below is sized by multiplying
        // them. Each dimension can be individually plausible (say 40000 x 60000) while the product
        // overflows int and turns negative, at which point `new byte[...]` throws OverflowException
        // — which is not in IsRecoverableImageDecodeException's list, so it would escape the
        // per-image guard and abort the whole PDF export. Reject the image here instead, with an
        // exception the export already knows how to skip.
        if ((long)width * height > MaxPngPixelCount)
            throw new NotSupportedException("Portable PDF image export does not support PNG images this large.");

        // r132 widened this from 8-bit-only: 16-bit PNGs were previously rejected here and then
        // SILENTLY dropped by the caller, leaving a hole in the page with no error. They are now
        // accepted and downsampled to 8-bit below. The overflow guard above is a concurrent fix from
        // another session and is orthogonal -- both are kept deliberately; neither supersedes the
        // other.
        if (bitDepth is not (8 or 16))
            throw new NotSupportedException("Portable PDF image export supports only 8-bit and 16-bit PNG images.");
        if (interlace != 0)
            throw new NotSupportedException("Portable PDF image export does not support interlaced PNG images.");

        var channels = colorType switch
        {
            0 => 1,
            2 => 3,
            3 => 1,
            4 => 2,
            6 => 4,
            _ => throw new NotSupportedException($"Portable PDF image export does not support PNG color type {colorType}."),
        };

        // 16-bit samples are downsampled to 8-bit by keeping the most-significant (first, per PNG's
        // big-endian sample order) byte of each 2-byte channel value: a small precision loss, not a
        // dropped image.
        var bytesPerSample = bitDepth / 8;

        idat.Position = 0;
        var raw = Inflate(idat);
        var pixels = UnfilterPng(raw, width, height, channels * bytesPerSample);
        return ConvertPngPixelsToPdfPixels(pixels, width, height, colorType, channels, bytesPerSample, palette);
    }

    private static PngPdfPixels ConvertPngPixelsToPdfPixels(
        byte[] pixels,
        int width,
        int height,
        int colorType,
        int channels,
        int bytesPerSample,
        byte[]? palette)
    {
        var pixelCount = width * height;
        var bytesPerPixel = channels * bytesPerSample;

        byte Sample(int pixelIndex, int channelIndex) =>
            pixels[(pixelIndex * bytesPerPixel) + (channelIndex * bytesPerSample)];

        if (colorType is 0 or 4)
        {
            var gray = new byte[pixelCount];
            for (var i = 0; i < pixelCount; i++)
                gray[i] = Sample(i, 0);
            return new PngPdfPixels(width, height, "DeviceGray", gray);
        }

        var rgb = new byte[pixelCount * 3];
        for (var i = 0; i < pixelCount; i++)
        {
            var target = i * 3;
            switch (colorType)
            {
                case 2:
                case 6:
                    rgb[target] = Sample(i, 0);
                    rgb[target + 1] = Sample(i, 1);
                    rgb[target + 2] = Sample(i, 2);
                    break;
                case 3:
                    var paletteIndex = Sample(i, 0) * 3;
                    if (palette is null || paletteIndex + 2 >= palette.Length)
                        throw new FormatException("PNG palette index is out of range.");
                    rgb[target] = palette[paletteIndex];
                    rgb[target + 1] = palette[paletteIndex + 1];
                    rgb[target + 2] = palette[paletteIndex + 2];
                    break;
                default:
                    throw new NotSupportedException();
            }
        }

        return new PngPdfPixels(width, height, "DeviceRGB", rgb);
    }

    private static PngPdfPixels ApplyColorEffects(PngPdfPixels pixels, PdfImageColorEffects effects)
    {
        var transformed = pixels.Pixels.ToArray();
        switch (pixels.ColorSpace)
        {
            case "DeviceGray":
                PdfImageColorEffectPixels.ApplyToGray8(transformed, effects);
                break;
            case "DeviceRGB":
                PdfImageColorEffectPixels.ApplyToRgb24(transformed, effects);
                break;
            default:
                throw new NotSupportedException($"Portable PDF image color effects do not support {pixels.ColorSpace} PNG pixels.");
        }

        return pixels with { Pixels = transformed };
    }

    private static byte[] Inflate(Stream zlib)
    {
        using var output = new MemoryStream();
        using (var stream = new ZLibStream(zlib, CompressionMode.Decompress, leaveOpen: true))
            stream.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var stream = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            stream.Write(data, 0, data.Length);
        return output.ToArray();
    }

    /// <param name="bytesPerPixel">
    /// PNG's filter "bpp": the byte distance to the same channel in the previous pixel, i.e. the
    /// component count times the byte width of each sample (1 for 8-bit depth, 2 for 16-bit).
    /// </param>
    private static byte[] UnfilterPng(byte[] raw, int width, int height, int bytesPerPixel)
    {
        var stride = width * bytesPerPixel;
        var expected = height * (stride + 1);
        if (raw.Length < expected)
            throw new FormatException("PNG image data is truncated.");

        var output = new byte[height * stride];
        var input = 0;
        for (var row = 0; row < height; row++)
        {
            var filter = raw[input++];
            var rowStart = row * stride;
            for (var column = 0; column < stride; column++)
            {
                var rawValue = raw[input++];
                var left = column >= bytesPerPixel ? output[rowStart + column - bytesPerPixel] : 0;
                var up = row > 0 ? output[rowStart - stride + column] : 0;
                var upLeft = column >= bytesPerPixel && row > 0 ? output[rowStart - stride + column - bytesPerPixel] : 0;
                var value = filter switch
                {
                    0 => rawValue,
                    1 => rawValue + left,
                    2 => rawValue + up,
                    3 => rawValue + ((left + up) >> 1),
                    4 => rawValue + Paeth(left, up, upLeft),
                    _ => throw new FormatException($"PNG image uses unknown filter type {filter}."),
                };
                output[rowStart + column] = (byte)(value & 0xFF);
            }
        }

        return output;
    }

    private static int Paeth(int left, int up, int upLeft)
    {
        var prediction = left + up - upLeft;
        var leftDistance = Math.Abs(prediction - left);
        var upDistance = Math.Abs(prediction - up);
        var upLeftDistance = Math.Abs(prediction - upLeft);
        if (leftDistance <= upDistance && leftDistance <= upLeftDistance)
            return left;
        return upDistance <= upLeftDistance ? up : upLeft;
    }

    private static (int Width, int Height, int Components) ReadJpegSize(byte[] bytes)
    {
        if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8)
            throw new FormatException("Not a JPEG image.");

        var position = 2;
        while (position + 4 <= bytes.Length)
        {
            while (position < bytes.Length && bytes[position] == 0xFF)
                position++;
            if (position >= bytes.Length)
                break;

            var marker = bytes[position++];
            if (marker is 0xD9 or 0xDA)
                break;
            if (position + 2 > bytes.Length)
                break;

            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(position, 2));
            if (length < 2 || position + length > bytes.Length)
                throw new FormatException("JPEG segment overruns the file.");

            if (IsJpegStartOfFrame(marker))
            {
                if (length < 8)
                    throw new FormatException("JPEG start-of-frame segment is truncated.");
                var precision = bytes[position + 2];
                if (precision != 8)
                    throw new NotSupportedException("Portable PDF image export supports only 8-bit JPEG images.");

                var height = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(position + 3, 2));
                var width = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(position + 5, 2));
                var components = bytes[position + 7];
                return (width, height, components);
            }

            position += length;
        }

        throw new FormatException("JPEG image is missing a start-of-frame segment.");
    }

    private static bool IsJpegStartOfFrame(byte marker) =>
        marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF;

    private static void AppendImage(
        StringBuilder content,
        PdfImage image,
        PdfImageResource resource,
        string? opacityResourceName)
    {
        var hasSourceCrop = TryGetSourceCroppedImagePlacement(image, resource, out var placement);
        if (image.ClipKind != PdfImageClipKind.None || hasSourceCrop)
        {
            AppendClippedImage(
                content,
                image,
                resource.ResourceName,
                opacityResourceName,
                hasSourceCrop ? placement : null);
            return;
        }

        content.AppendLine("q");
        AppendOpacityState(content, opacityResourceName);
        var rotation = -image.RotationDegrees * Math.PI / 180d;
        var cos = Math.Cos(rotation);
        var sin = Math.Sin(rotation);
        var centerX = image.X + image.Width / 2d;
        var centerY = image.Y + image.Height / 2d;
        var a = cos * image.Width;
        var b = sin * image.Width;
        var c = -sin * image.Height;
        var d = cos * image.Height;
        var e = centerX - (cos * image.Width / 2d) + (sin * image.Height / 2d);
        var f = centerY - (sin * image.Width / 2d) - (cos * image.Height / 2d);
        content.AppendLine($"{FormatNumber(a)} {FormatNumber(b)} {FormatNumber(c)} {FormatNumber(d)} {FormatNumber(e)} {FormatNumber(f)} cm");
        content.AppendLine($"/{resource.ResourceName} Do");
        content.AppendLine("Q");
    }

    private static void AppendClippedImage(
        StringBuilder content,
        PdfImage image,
        string resourceName,
        string? opacityResourceName,
        PdfImagePlacement? croppedPlacement = null)
    {
        var placement = croppedPlacement ?? new PdfImagePlacement(image.X, image.Y, image.Width, image.Height);

        content.AppendLine("q");
        AppendOpacityState(content, opacityResourceName);
        if (Math.Abs(image.RotationDegrees) > 0.001)
            AppendRotationTransform(content, image.X + image.Width / 2d, image.Y + image.Height / 2d, image.RotationDegrees);

        AppendImageClipPath(content, image, croppedPlacement is not null);
        content.AppendLine($"{FormatNumber(placement.Width)} 0 0 {FormatNumber(placement.Height)} {FormatNumber(placement.X)} {FormatNumber(placement.Y)} cm");
        content.AppendLine($"/{resourceName} Do");
        content.AppendLine("Q");
    }

    private static bool TryGetSourceCroppedImagePlacement(
        PdfImage image,
        PdfImageResource resource,
        out PdfImagePlacement placement)
    {
        placement = default;
        if (!image.SourceCrop.HasCrop ||
            image.Width <= 0 ||
            image.Height <= 0 ||
            resource.PixelWidth <= 0 ||
            resource.PixelHeight <= 0)
            return false;

        if (!PdfRenderGeometry.TryGetImageSourceRect(
                resource.PixelWidth,
                resource.PixelHeight,
                image.SourceCrop,
                out var sourceRect))
            return false;

        var scaleX = image.Width / sourceRect.Width;
        var scaleY = image.Height / sourceRect.Height;
        var sourceBottom = resource.PixelHeight - sourceRect.Y - sourceRect.Height;
        placement = new PdfImagePlacement(
            image.X - sourceRect.X * scaleX,
            image.Y - sourceBottom * scaleY,
            resource.PixelWidth * scaleX,
            resource.PixelHeight * scaleY);
        return true;
    }

    private static void AppendRotationGroup(
        StringBuilder content,
        PdfRotationGroup group,
        IReadOnlyDictionary<PdfImage, PdfImageResource> imageResources,
        IReadOnlyDictionary<double, PdfOpacityResource> opacityResources,
        PatternResourceSet patternResources,
        PdfColor? colorOverride = null)
    {
        if (group.Ops.Count == 0)
            return;

        content.AppendLine("q");
        AppendRotationTransform(
            content,
            group.CenterX,
            group.CenterY,
            group.RotationDegrees,
            group.FlipH,
            group.FlipV);

        foreach (var op in group.Ops)
            AppendDrawOp(content, op, imageResources, opacityResources, patternResources, colorOverride);

        content.AppendLine("Q");
    }

    private static void AppendOpacityGroup(
        StringBuilder content,
        PdfOpacityGroup group,
        string? opacityResourceName,
        IReadOnlyDictionary<PdfImage, PdfImageResource> imageResources,
        IReadOnlyDictionary<double, PdfOpacityResource> opacityResources,
        PatternResourceSet patternResources,
        PdfColor? colorOverride = null)
    {
        if (group.Ops.Count == 0)
            return;

        content.AppendLine("q");
        AppendOpacityState(content, opacityResourceName);

        foreach (var op in group.Ops)
            AppendDrawOp(content, op, imageResources, opacityResources, patternResources, colorOverride);

        content.AppendLine("Q");
    }

    private static void AppendClipGroup(
        StringBuilder content,
        PdfClipGroup group,
        IReadOnlyDictionary<PdfImage, PdfImageResource> imageResources,
        IReadOnlyDictionary<double, PdfOpacityResource> opacityResources,
        PatternResourceSet patternResources,
        PdfColor? colorOverride = null)
    {
        if (group.Ops.Count == 0 || group.Width <= 0 || group.Height <= 0)
            return;

        content.AppendLine("q");
        content.AppendLine($"{FormatNumber(group.X)} {FormatNumber(group.Y)} {FormatNumber(group.Width)} {FormatNumber(group.Height)} re W n");
        foreach (var op in group.Ops)
            AppendDrawOp(content, op, imageResources, opacityResources, patternResources, colorOverride);
        content.AppendLine("Q");
    }

    private static void AppendEffectGroup(
        StringBuilder content,
        PdfEffectGroup group,
        IReadOnlyDictionary<PdfImage, PdfImageResource> imageResources,
        IReadOnlyDictionary<double, PdfOpacityResource> opacityResources,
        PatternResourceSet patternResources)
    {
        if (group.Ops.Count == 0)
            return;

        var parameters = group.Parameters;
        var opacity = PdfRenderGeometry.NormalizeOpacity(parameters.Opacity);
        switch (group.Kind)
        {
            case PdfEffectKind.Shadow:
                AppendEffectPass(content, group.Ops, parameters.Color, opacity,
                    parameters.OffsetX, parameters.OffsetY, parameters.Radius * 0.18,
                    imageResources, opacityResources, patternResources);
                break;

            case PdfEffectKind.Glow:
            {
                var radius = Math.Max(1, parameters.Radius);
                for (var index = 3; index >= 1; index--)
                {
                    var spread = radius * index / 3;
                    AppendEffectPass(content, group.Ops, parameters.Color, opacity * (0.18 + 0.08 * (3 - index)),
                        0, 0, spread, imageResources, opacityResources, patternResources);
                }
                break;
            }

            case PdfEffectKind.SoftEdge:
            {
                var radius = Math.Max(1, parameters.Radius);
                for (var index = 3; index >= 1; index--)
                {
                    var spread = radius * index / 3;
                    AppendEffectPass(content, group.Ops, parameters.Color, opacity * 0.12,
                        0, 0, spread, imageResources, opacityResources, patternResources);
                }
                break;
            }

            case PdfEffectKind.Reflection:
                AppendEffectReflection(content, group, imageResources, opacityResources, patternResources);
                break;

            case PdfEffectKind.Bevel:
                AppendEffectBevel(content, group, opacity, imageResources, opacityResources, patternResources);
                break;
        }
    }

    private static void AppendEffectBevel(
        StringBuilder content,
        PdfEffectGroup group,
        double opacity,
        IReadOnlyDictionary<PdfImage, PdfImageResource> imageResources,
        IReadOnlyDictionary<double, PdfOpacityResource> opacityResources,
        PatternResourceSet patternResources)
    {
        var shadowColor = group.Parameters.SecondaryColor ?? group.Parameters.Color;
        foreach (var band in PdfRenderGeometry.GetBevelBands(group))
        {
            var bandOpacity = PdfRenderGeometry.NormalizeOpacity(opacity * band.OpacityScale);
            var resourceName = opacityResources.TryGetValue(bandOpacity, out var resource)
                ? resource.ResourceName
                : null;
            content.AppendLine("q");
            AppendOpacityState(content, resourceName);
            content.AppendLine(
                $"{FormatNumber(group.BoundsX)} {FormatNumber(group.BoundsY)} {FormatNumber(group.BoundsWidth)} {FormatNumber(group.BoundsHeight)} re W n");
            content.AppendLine($"{FormatNumber(band.Points[0].X)} {FormatNumber(band.Points[0].Y)} m");
            for (var index = 1; index < band.Points.Count; index++)
                content.AppendLine($"{FormatNumber(band.Points[index].X)} {FormatNumber(band.Points[index].Y)} l");
            content.AppendLine("h W n");
            if (band.OffsetX != 0 || band.OffsetY != 0)
                content.AppendLine($"1 0 0 1 {FormatNumber(band.OffsetX)} {FormatNumber(band.OffsetY)} cm");
            var color = band.IsHighlight ? group.Parameters.Color : shadowColor;
            foreach (var op in group.Ops)
                AppendDrawOp(content, op, imageResources, opacityResources, patternResources, color);
            content.AppendLine("Q");
        }
    }

    private static void AppendEffectPass(
        StringBuilder content,
        IReadOnlyList<PdfDrawOp> ops,
        PdfColor? color,
        double opacity,
        double offsetX,
        double offsetY,
        double spread,
        IReadOnlyDictionary<PdfImage, PdfImageResource> imageResources,
        IReadOnlyDictionary<double, PdfOpacityResource> opacityResources,
        PatternResourceSet patternResources)
    {
        // PDF has no portable vector blur primitive. A symmetric weighted stamp kernel is a
        // deterministic approximation that keeps the blur centered instead of drifting along a
        // single diagonal as the old cumulative-translation fallback did.
        IReadOnlyList<EffectBlurStamp> stamps = spread > 0
            ? EffectBlurStamps
            : [new EffectBlurStamp(0, 0, 1)];
        foreach (var stamp in stamps)
        {
            var stampOpacity = PdfRenderGeometry.NormalizeOpacity(opacity * stamp.Weight);
            var resourceName = opacityResources.TryGetValue(stampOpacity, out var resource)
                ? resource.ResourceName
                : null;
            content.AppendLine("q");
            AppendOpacityState(content, resourceName);
            var stampOffsetX = offsetX + (spread * stamp.X);
            var stampOffsetY = offsetY + (spread * stamp.Y);
            if (stampOffsetX != 0 || stampOffsetY != 0)
                content.AppendLine($"1 0 0 1 {FormatNumber(stampOffsetX)} {FormatNumber(stampOffsetY)} cm");
            foreach (var op in ops)
                AppendDrawOp(content, op, imageResources, opacityResources, patternResources, color);
            content.AppendLine("Q");
        }
    }

    private static void AppendEffectReflection(
        StringBuilder content,
        PdfEffectGroup group,
        IReadOnlyDictionary<PdfImage, PdfImageResource> imageResources,
        IReadOnlyDictionary<double, PdfOpacityResource> opacityResources,
        PatternResourceSet patternResources)
    {
        var startOpacity = PdfRenderGeometry.NormalizeOpacity(group.Parameters.Opacity);
        var endOpacity = PdfRenderGeometry.NormalizeOpacity(group.Parameters.ReflectionEndOpacity);
        var startPosition = Math.Clamp(group.Parameters.ReflectionStartPosition, 0, 1);
        var endPosition = Math.Clamp(group.Parameters.ReflectionEndPosition, 0, 1);
        if (endPosition <= startPosition)
        {
            startPosition = 0;
            endPosition = 1;
        }

        for (var index = 0; index < ReflectionPassCount; index++)
        {
            var t0 = startPosition + (endPosition - startPosition) * index / ReflectionPassCount;
            var t1 = startPosition + (endPosition - startPosition) * (index + 1) / ReflectionPassCount;
            var opacity = PdfRenderGeometry.NormalizeOpacity(
                startOpacity + (endOpacity - startOpacity) * (index + 0.5) / ReflectionPassCount);
            var resourceName = opacityResources.TryGetValue(opacity, out var resource)
                ? resource.ResourceName
                : null;
            content.AppendLine("q");
            AppendOpacityState(content, resourceName);
            AppendReflectionTransform(content, group);
            AppendReflectionFadeBand(content, group, t0, t1);
            foreach (var op in group.Ops)
                AppendDrawOp(content, op, imageResources, opacityResources, patternResources, group.Parameters.Color);
            content.AppendLine("Q");
        }
    }

    private static double ReflectionPassOpacity(PdfEffectGroup group, int index)
    {
        var start = PdfRenderGeometry.NormalizeOpacity(group.Parameters.Opacity);
        var end = PdfRenderGeometry.NormalizeOpacity(group.Parameters.ReflectionEndOpacity);
        var opacity = start + (end - start) * (index + 0.5) / ReflectionPassCount;
        return PdfRenderGeometry.NormalizeOpacity(opacity);
    }

    private static void AppendReflectionFadeBand(
        StringBuilder content,
        PdfEffectGroup group,
        double startPosition,
        double endPosition)
    {
        var direction = (group.Parameters.ReflectionFadeDirectionDegrees - 90) * Math.PI / 180d;
        if (Math.Abs(Math.Sin(direction)) < 0.0001)
        {
            content.AppendLine($"{FormatNumber(group.BoundsX)} {FormatNumber(group.BoundsY + group.BoundsHeight * startPosition)} {FormatNumber(group.BoundsWidth)} {FormatNumber(group.BoundsHeight * (endPosition - startPosition))} re W n");
            return;
        }

        var centerX = group.BoundsX + group.BoundsWidth / 2;
        var centerY = group.BoundsY + group.BoundsHeight / 2;
        var diagonal = Math.Sqrt(group.BoundsWidth * group.BoundsWidth + group.BoundsHeight * group.BoundsHeight);
        var desiredAxisX = Math.Sin(direction);
        var desiredAxisY = -Math.Cos(direction);
        var transform = GetReflectionTransform(group);
        var determinant = transform.A * transform.D - transform.B * transform.C;
        var axisX = Math.Abs(determinant) < 0.000001
            ? desiredAxisX
            : (transform.D * desiredAxisX - transform.C * desiredAxisY) / determinant;
        var axisY = Math.Abs(determinant) < 0.000001
            ? -desiredAxisY
            : (-transform.B * desiredAxisX + transform.A * desiredAxisY) / determinant;
        var axisLength = Math.Sqrt(axisX * axisX + axisY * axisY);
        if (axisLength > 0.000001)
        {
            axisX /= axisLength;
            axisY /= axisLength;
        }
        var perpendicularX = -axisY;
        var perpendicularY = axisX;
        var bandCenter = (startPosition + endPosition) / 2 - 0.5;
        var halfAxis = (endPosition - startPosition) * diagonal / 2;
        var axisCenterX = centerX + axisX * bandCenter * diagonal;
        var axisCenterY = centerY + axisY * bandCenter * diagonal;
        var halfPerpendicular = diagonal / 2;
        var points = new[]
        {
            (X: axisCenterX - axisX * halfAxis - perpendicularX * halfPerpendicular,
             Y: axisCenterY - axisY * halfAxis - perpendicularY * halfPerpendicular),
            (X: axisCenterX + axisX * halfAxis - perpendicularX * halfPerpendicular,
             Y: axisCenterY + axisY * halfAxis - perpendicularY * halfPerpendicular),
            (X: axisCenterX + axisX * halfAxis + perpendicularX * halfPerpendicular,
             Y: axisCenterY + axisY * halfAxis + perpendicularY * halfPerpendicular),
            (X: axisCenterX - axisX * halfAxis + perpendicularX * halfPerpendicular,
             Y: axisCenterY - axisY * halfAxis + perpendicularY * halfPerpendicular),
        };

        // Keep the rotated fade strip bounded by the effect's declared object bounds before
        // applying the directional strip intersection.
        content.AppendLine($"{FormatNumber(group.BoundsX)} {FormatNumber(group.BoundsY)} {FormatNumber(group.BoundsWidth)} {FormatNumber(group.BoundsHeight)} re W n");
        content.AppendLine($"{FormatNumber(points[0].X)} {FormatNumber(points[0].Y)} m");
        for (var index = 1; index < points.Length; index++)
            content.AppendLine($"{FormatNumber(points[index].X)} {FormatNumber(points[index].Y)} l");
        content.AppendLine("h W n");
    }

    private static void AppendReflectionTransform(StringBuilder content, PdfEffectGroup group)
    {
        var transform = GetReflectionTransform(group);
        content.AppendLine(
            $"{FormatNumber(transform.A)} {FormatNumber(transform.B)} {FormatNumber(transform.C)} {FormatNumber(transform.D)} {FormatNumber(transform.E)} {FormatNumber(transform.F)} cm");
    }

    private static (double A, double B, double C, double D, double E, double F) GetReflectionTransform(
        PdfEffectGroup group)
    {
        var axisAngle = (group.Parameters.ReflectionDirectionDegrees - 90) * Math.PI / 180d;
        var cos = Math.Cos(axisAngle);
        var sin = Math.Sin(axisAngle);
        var skewX = Math.Tan(group.Parameters.ReflectionSkewXDegrees * Math.PI / 180d);
        var skewY = Math.Tan(group.Parameters.ReflectionSkewYDegrees * Math.PI / 180d);
        var sx = group.Parameters.ReflectionScaleX;
        var sy = group.Parameters.ReflectionScaleY;

        // Compose R(axis) * (skew * scale) * R(-axis), retaining the original PDF reflection
        // matrix when all optional Office reflection parameters are at their defaults.
        var localA = sx;
        var localB = skewY * sx;
        var localC = skewX * sy;
        var localD = sy;
        var a = cos * (localA * cos + localC * sin) - sin * (localB * cos + localD * sin);
        var b = sin * (localA * cos + localC * sin) + cos * (localB * cos + localD * sin);
        var c = cos * (-localA * sin + localC * cos) - sin * (-localB * sin + localD * cos);
        var d = sin * (-localA * sin + localC * cos) + cos * (-localB * sin + localD * cos);
        var centerX = group.BoundsX + group.BoundsWidth / 2;
        var centerY = group.BoundsY - group.Parameters.ReflectionGap / 2;
        var e = centerX - a * centerX - c * centerY;
        var f = centerY - b * centerX - d * centerY;
        return (a, b, c, d, e, f);
    }

    private static void AppendOpacityState(StringBuilder content, string? opacityResourceName)
    {
        if (!string.IsNullOrEmpty(opacityResourceName))
            content.AppendLine($"/{opacityResourceName} gs");
    }

    private static void AppendRotationTransform(
        StringBuilder content,
        double centerX,
        double centerY,
        double rotationDegrees,
        bool flipH = false,
        bool flipV = false)
    {
        var rotation = -rotationDegrees * Math.PI / 180d;
        var cos = Math.Cos(rotation);
        var sin = Math.Sin(rotation);
        var scaleX = flipH ? -1 : 1;
        var scaleY = flipV ? -1 : 1;
        var a = cos * scaleX;
        var b = sin * scaleX;
        var c = -sin * scaleY;
        var d = cos * scaleY;
        var e = centerX - (a * centerX) - (c * centerY);
        var f = centerY - (b * centerX) - (d * centerY);
        content.AppendLine(
            $"{FormatNumber(a)} {FormatNumber(b)} {FormatNumber(c)} {FormatNumber(d)} {FormatNumber(e)} {FormatNumber(f)} cm");
    }

    private static void AppendFilledRectangle(
        StringBuilder content,
        double x,
        double y,
        double width,
        double height,
        PdfColor color)
    {
        content.AppendLine("q");
        AppendRgb(content, color, "rg");
        content.AppendLine($"{FormatNumber(x)} {FormatNumber(y)} {FormatNumber(width)} {FormatNumber(height)} re f");
        content.AppendLine("Q");
    }

    private static void AppendFilledRectangleLinearGradient(
        StringBuilder content,
        double x,
        double y,
        double width,
        double height,
        PdfLinearGradient gradient,
        PatternResourceSet patternResources,
        PdfColor fallbackColor)
    {
        if (!patternResources.ByGradient.TryGetValue(gradient, out var pattern))
        {
            AppendFilledRectangle(content, x, y, width, height, fallbackColor);
            return;
        }

        content.AppendLine("q");
        AppendFillPattern(content, pattern.ResourceName);
        content.AppendLine($"{FormatNumber(x)} {FormatNumber(y)} {FormatNumber(width)} {FormatNumber(height)} re f");
        content.AppendLine("Q");
    }

    private static void AppendFilledRectanglePattern(
        StringBuilder content,
        double x,
        double y,
        double width,
        double height,
        PdfPatternFill patternFill,
        PatternResourceSet patternResources)
    {
        if (!patternResources.ByPattern.TryGetValue(patternFill, out var pattern))
        {
            AppendFilledRectangle(content, x, y, width, height, patternFill.Background);
            return;
        }

        content.AppendLine("q");
        AppendFillPattern(content, pattern.ResourceName);
        content.AppendLine($"{FormatNumber(x)} {FormatNumber(y)} {FormatNumber(width)} {FormatNumber(height)} re f");
        content.AppendLine("Q");
    }

    private static void AppendStrokedRectangle(
        StringBuilder content,
        double x,
        double y,
        double width,
        double height,
        PdfColor color,
        double lineWidth,
        PdfDashPattern? dash)
    {
        content.AppendLine("q");
        AppendRgb(content, color, "RG");
        content.AppendLine($"{FormatNumber(lineWidth)} w");
        AppendDashPattern(content, dash);
        content.AppendLine($"{FormatNumber(x)} {FormatNumber(y)} {FormatNumber(width)} {FormatNumber(height)} re S");
        content.AppendLine("Q");
    }

    private static void AppendStrokedRectangleLinearGradient(
        StringBuilder content,
        double x,
        double y,
        double width,
        double height,
        PdfLinearGradient gradient,
        PatternResourceSet patternResources,
        PdfColor fallbackColor,
        double lineWidth,
        PdfDashPattern? dash)
    {
        if (!patternResources.ByGradient.TryGetValue(gradient, out var pattern))
        {
            AppendStrokedRectangle(content, x, y, width, height, fallbackColor, lineWidth, dash);
            return;
        }

        content.AppendLine("q");
        AppendStrokePattern(content, pattern.ResourceName);
        content.AppendLine($"{FormatNumber(lineWidth)} w");
        AppendDashPattern(content, dash);
        content.AppendLine($"{FormatNumber(x)} {FormatNumber(y)} {FormatNumber(width)} {FormatNumber(height)} re S");
        content.AppendLine("Q");
    }

    private static void AppendFilledEllipse(
        StringBuilder content,
        double x,
        double y,
        double width,
        double height,
        PdfColor color)
    {
        if (width <= 0 || height <= 0)
            return;

        content.AppendLine("q");
        AppendRgb(content, color, "rg");
        AppendEllipsePath(content, x, y, width, height);
        content.AppendLine("f");
        content.AppendLine("Q");
    }

    private static void AppendFilledEllipseLinearGradient(
        StringBuilder content,
        double x,
        double y,
        double width,
        double height,
        PdfLinearGradient gradient,
        PatternResourceSet patternResources,
        PdfColor fallbackColor)
    {
        if (width <= 0 || height <= 0)
            return;
        if (!patternResources.ByGradient.TryGetValue(gradient, out var pattern))
        {
            AppendFilledEllipse(content, x, y, width, height, fallbackColor);
            return;
        }

        content.AppendLine("q");
        AppendFillPattern(content, pattern.ResourceName);
        AppendEllipsePath(content, x, y, width, height);
        content.AppendLine("f");
        content.AppendLine("Q");
    }

    private static void AppendFilledEllipsePattern(
        StringBuilder content,
        double x,
        double y,
        double width,
        double height,
        PdfPatternFill patternFill,
        PatternResourceSet patternResources)
    {
        if (width <= 0 || height <= 0)
            return;
        if (!patternResources.ByPattern.TryGetValue(patternFill, out var pattern))
        {
            AppendFilledEllipse(content, x, y, width, height, patternFill.Background);
            return;
        }

        content.AppendLine("q");
        AppendFillPattern(content, pattern.ResourceName);
        AppendEllipsePath(content, x, y, width, height);
        content.AppendLine("f");
        content.AppendLine("Q");
    }

    private static void AppendStrokedEllipse(
        StringBuilder content,
        double x,
        double y,
        double width,
        double height,
        PdfColor color,
        double lineWidth,
        PdfDashPattern? dash)
    {
        if (width <= 0 || height <= 0)
            return;

        content.AppendLine("q");
        AppendRgb(content, color, "RG");
        content.AppendLine($"{FormatNumber(lineWidth)} w");
        AppendDashPattern(content, dash);
        AppendEllipsePath(content, x, y, width, height);
        content.AppendLine("S");
        content.AppendLine("Q");
    }

    private static void AppendStrokedEllipseLinearGradient(
        StringBuilder content,
        double x,
        double y,
        double width,
        double height,
        PdfLinearGradient gradient,
        PatternResourceSet patternResources,
        PdfColor fallbackColor,
        double lineWidth,
        PdfDashPattern? dash)
    {
        if (width <= 0 || height <= 0)
            return;
        if (!patternResources.ByGradient.TryGetValue(gradient, out var pattern))
        {
            AppendStrokedEllipse(content, x, y, width, height, fallbackColor, lineWidth, dash);
            return;
        }

        content.AppendLine("q");
        AppendStrokePattern(content, pattern.ResourceName);
        content.AppendLine($"{FormatNumber(lineWidth)} w");
        AppendDashPattern(content, dash);
        AppendEllipsePath(content, x, y, width, height);
        content.AppendLine("S");
        content.AppendLine("Q");
    }

    private static void AppendEllipsePath(
        StringBuilder content,
        double x,
        double y,
        double width,
        double height)
    {
        const double kappa = 0.5522847498307936;
        var rx = width / 2d;
        var ry = height / 2d;
        var cx = x + rx;
        var cy = y + ry;
        var ox = rx * kappa;
        var oy = ry * kappa;

        content.AppendLine($"{FormatNumber(cx + rx)} {FormatNumber(cy)} m");
        content.AppendLine($"{FormatNumber(cx + rx)} {FormatNumber(cy + oy)} {FormatNumber(cx + ox)} {FormatNumber(cy + ry)} {FormatNumber(cx)} {FormatNumber(cy + ry)} c");
        content.AppendLine($"{FormatNumber(cx - ox)} {FormatNumber(cy + ry)} {FormatNumber(cx - rx)} {FormatNumber(cy + oy)} {FormatNumber(cx - rx)} {FormatNumber(cy)} c");
        content.AppendLine($"{FormatNumber(cx - rx)} {FormatNumber(cy - oy)} {FormatNumber(cx - ox)} {FormatNumber(cy - ry)} {FormatNumber(cx)} {FormatNumber(cy - ry)} c");
        content.AppendLine($"{FormatNumber(cx + ox)} {FormatNumber(cy - ry)} {FormatNumber(cx + rx)} {FormatNumber(cy - oy)} {FormatNumber(cx + rx)} {FormatNumber(cy)} c");
    }

    private static void AppendImageClipPath(StringBuilder content, PdfImage image, bool includeRectangularClip = false)
    {
        switch (image.ClipKind)
        {
            case PdfImageClipKind.Ellipse:
                AppendEllipsePath(content, image.X, image.Y, image.Width, image.Height);
                content.AppendLine("W n");
                break;
            case PdfImageClipKind.RoundedRectangle:
                AppendRoundedRectanglePath(content, image.X, image.Y, image.Width, image.Height);
                content.AppendLine("W n");
                break;
            case PdfImageClipKind.Triangle:
            case PdfImageClipKind.Diamond:
            case PdfImageClipKind.Parallelogram:
            case PdfImageClipKind.Hexagon:
            case PdfImageClipKind.Chevron:
                AppendPresetClipPolygonPath(content, image.X, image.Y, image.Width, image.Height, image.ClipKind);
                content.AppendLine("W n");
                break;
            case PdfImageClipKind.None when includeRectangularClip:
                content.AppendLine($"{FormatNumber(image.X)} {FormatNumber(image.Y)} {FormatNumber(image.Width)} {FormatNumber(image.Height)} re W n");
                break;
        }
    }

    private static void AppendPresetClipPolygonPath(
        StringBuilder content,
        double x,
        double y,
        double width,
        double height,
        PdfImageClipKind clipKind)
    {
        if (width <= 0 || height <= 0)
            return;

        var points = PdfRenderGeometry.GetPresetClipPolygonPoints(x, y, width, height, clipKind);
        if (points.Length == 0)
            return;

        content.AppendLine($"{FormatNumber(points[0].X)} {FormatNumber(points[0].Y)} m");
        for (var i = 1; i < points.Length; i++)
            content.AppendLine($"{FormatNumber(points[i].X)} {FormatNumber(points[i].Y)} l");
        content.AppendLine("h");
    }

    private static void AppendRoundedRectanglePath(
        StringBuilder content,
        double x,
        double y,
        double width,
        double height)
    {
        if (width <= 0 || height <= 0)
            return;

        const double kappa = 0.5522847498307936;
        var radius = Math.Min(width, height) * 0.18;
        radius = Math.Min(radius, Math.Min(width, height) / 2d);
        var offset = radius * kappa;
        var right = x + width;
        var top = y + height;

        content.AppendLine($"{FormatNumber(x + radius)} {FormatNumber(y)} m");
        content.AppendLine($"{FormatNumber(right - radius)} {FormatNumber(y)} l");
        content.AppendLine($"{FormatNumber(right - radius + offset)} {FormatNumber(y)} {FormatNumber(right)} {FormatNumber(y + radius - offset)} {FormatNumber(right)} {FormatNumber(y + radius)} c");
        content.AppendLine($"{FormatNumber(right)} {FormatNumber(top - radius)} l");
        content.AppendLine($"{FormatNumber(right)} {FormatNumber(top - radius + offset)} {FormatNumber(right - radius + offset)} {FormatNumber(top)} {FormatNumber(right - radius)} {FormatNumber(top)} c");
        content.AppendLine($"{FormatNumber(x + radius)} {FormatNumber(top)} l");
        content.AppendLine($"{FormatNumber(x + radius - offset)} {FormatNumber(top)} {FormatNumber(x)} {FormatNumber(top - radius + offset)} {FormatNumber(x)} {FormatNumber(top - radius)} c");
        content.AppendLine($"{FormatNumber(x)} {FormatNumber(y + radius)} l");
        content.AppendLine($"{FormatNumber(x)} {FormatNumber(y + radius - offset)} {FormatNumber(x + radius - offset)} {FormatNumber(y)} {FormatNumber(x + radius)} {FormatNumber(y)} c");
    }

    private static void AppendLine(
        StringBuilder content,
        double x1,
        double y1,
        double x2,
        double y2,
        PdfColor color,
        double lineWidth)
    {
        content.AppendLine("q");
        AppendRgb(content, color, "RG");
        content.AppendLine($"{FormatNumber(lineWidth)} w");
        content.AppendLine($"{FormatNumber(x1)} {FormatNumber(y1)} m");
        content.AppendLine($"{FormatNumber(x2)} {FormatNumber(y2)} l S");
        content.AppendLine("Q");
    }

    private static void AppendLineLinearGradient(
        StringBuilder content,
        double x1,
        double y1,
        double x2,
        double y2,
        PdfLinearGradient gradient,
        PatternResourceSet patternResources,
        PdfColor fallbackColor,
        double lineWidth)
    {
        if (!patternResources.ByGradient.TryGetValue(gradient, out var pattern))
        {
            AppendLine(content, x1, y1, x2, y2, fallbackColor, lineWidth);
            return;
        }

        content.AppendLine("q");
        AppendStrokePattern(content, pattern.ResourceName);
        content.AppendLine($"{FormatNumber(lineWidth)} w");
        content.AppendLine($"{FormatNumber(x1)} {FormatNumber(y1)} m");
        content.AppendLine($"{FormatNumber(x2)} {FormatNumber(y2)} l S");
        content.AppendLine("Q");
    }

    private static void AppendFilledTriangle(
        StringBuilder content,
        double x1,
        double y1,
        double x2,
        double y2,
        double x3,
        double y3,
        PdfColor color)
    {
        content.AppendLine("q");
        AppendRgb(content, color, "rg");
        content.AppendLine($"{FormatNumber(x1)} {FormatNumber(y1)} m");
        content.AppendLine($"{FormatNumber(x2)} {FormatNumber(y2)} l");
        content.AppendLine($"{FormatNumber(x3)} {FormatNumber(y3)} l f");
        content.AppendLine("Q");
    }

    private static void AppendPath(StringBuilder content, PdfPath path)
    {
        if (path.Contours.Count == 0 || (path.FillColor is null && path.StrokeColor is null))
            return;

        content.AppendLine("q");
        if (path.FillColor is { } fill)
            AppendRgb(content, fill, "rg");
        if (path.StrokeColor is { } stroke)
        {
            AppendRgb(content, stroke, "RG");
            content.AppendLine($"{FormatNumber(Math.Max(0.1, path.StrokeWidth))} w");
            AppendDashPattern(content, path.StrokeDash);
        }

        foreach (var contour in path.Contours)
        {
            content.AppendLine($"{FormatNumber(contour.Start.X)} {FormatNumber(contour.Start.Y)} m");
            foreach (var segment in contour.Segments)
            {
                switch (segment.Kind)
                {
                    case PdfPathSegmentKind.Line:
                        content.AppendLine($"{FormatNumber(segment.End.X)} {FormatNumber(segment.End.Y)} l");
                        break;
                    case PdfPathSegmentKind.CubicBezier:
                        content.AppendLine(
                            $"{FormatNumber(segment.Control1.X)} {FormatNumber(segment.Control1.Y)} " +
                            $"{FormatNumber(segment.Control2.X)} {FormatNumber(segment.Control2.Y)} " +
                            $"{FormatNumber(segment.End.X)} {FormatNumber(segment.End.Y)} c");
                        break;
                }
            }

            if (contour.Closed)
                content.AppendLine("h");
        }

        content.AppendLine(path.FillColor is not null && path.StrokeColor is not null
            ? "B"
            : path.FillColor is not null ? "f" : "S");
        content.AppendLine("Q");
    }

    private static void AppendPathLinearGradient(
        StringBuilder content,
        PdfPathLinearGradient path,
        PatternResourceSet patternResources)
    {
        if (path.Contours.Count == 0 || (path.FillGradient is null && path.StrokeGradient is null))
            return;

        content.AppendLine("q");
        PdfPatternResource? fillPattern = null;
        var hasFillPattern = path.FillGradient is { } fillGradient &&
            patternResources.ByGradient.TryGetValue(fillGradient, out fillPattern);
        PdfPatternResource? strokePattern = null;
        var hasStrokePattern = path.StrokeGradient is { } strokeGradient &&
            patternResources.ByGradient.TryGetValue(strokeGradient, out strokePattern);

        if (hasFillPattern)
            AppendFillPattern(content, fillPattern!.ResourceName);
        else if (path.FillFallbackColor is { } fillFallback)
            AppendRgb(content, fillFallback, "rg");

        if (hasStrokePattern)
        {
            AppendStrokePattern(content, strokePattern!.ResourceName);
            content.AppendLine($"{FormatNumber(Math.Max(0.1, path.StrokeWidth))} w");
            AppendDashPattern(content, path.StrokeDash);
        }
        else if (path.StrokeFallbackColor is { } strokeFallback)
        {
            AppendRgb(content, strokeFallback, "RG");
            content.AppendLine($"{FormatNumber(Math.Max(0.1, path.StrokeWidth))} w");
            AppendDashPattern(content, path.StrokeDash);
        }

        AppendPathContours(content, path.Contours);

        var hasFill = hasFillPattern || path.FillFallbackColor is not null;
        var hasStroke = hasStrokePattern || path.StrokeFallbackColor is not null;
        content.AppendLine(hasFill && hasStroke ? "B" : hasFill ? "f" : "S");
        content.AppendLine("Q");
    }

    private static void AppendPathPattern(
        StringBuilder content,
        PdfPathPattern path,
        PatternResourceSet patternResources)
    {
        if (path.Contours.Count == 0)
            return;
        if (!patternResources.ByPattern.TryGetValue(path.Pattern, out var pattern))
        {
            AppendPath(content, new PdfPath(path.Contours, path.Pattern.Background, path.StrokeColor, path.StrokeWidth, path.StrokeDash));
            return;
        }

        content.AppendLine("q");
        AppendFillPattern(content, pattern.ResourceName);
        if (path.StrokeColor is { } stroke)
        {
            AppendRgb(content, stroke, "RG");
            content.AppendLine($"{FormatNumber(Math.Max(0.1, path.StrokeWidth))} w");
            AppendDashPattern(content, path.StrokeDash);
        }

        AppendPathContours(content, path.Contours);
        content.AppendLine(path.StrokeColor is not null ? "B" : "f");
        content.AppendLine("Q");
    }

    private static void AppendPathContours(StringBuilder content, IReadOnlyList<PdfPathContour> contours)
    {
        foreach (var contour in contours)
        {
            content.AppendLine($"{FormatNumber(contour.Start.X)} {FormatNumber(contour.Start.Y)} m");
            foreach (var segment in contour.Segments)
            {
                switch (segment.Kind)
                {
                    case PdfPathSegmentKind.Line:
                        content.AppendLine($"{FormatNumber(segment.End.X)} {FormatNumber(segment.End.Y)} l");
                        break;
                    case PdfPathSegmentKind.CubicBezier:
                        content.AppendLine(
                            $"{FormatNumber(segment.Control1.X)} {FormatNumber(segment.Control1.Y)} " +
                            $"{FormatNumber(segment.Control2.X)} {FormatNumber(segment.Control2.Y)} " +
                            $"{FormatNumber(segment.End.X)} {FormatNumber(segment.End.Y)} c");
                        break;
                }
            }

            if (contour.Closed)
                content.AppendLine("h");
        }
    }

    private static void AppendDashPattern(StringBuilder content, PdfDashPattern? dash)
    {
        if (dash is null || dash.Segments.Count == 0)
            return;

        var segments = dash.Segments
            .Where(segment => double.IsFinite(segment) && segment > 0)
            .Select(FormatNumber)
            .ToArray();
        if (segments.Length == 0)
            return;

        var phase = double.IsFinite(dash.Phase) ? dash.Phase : 0;
        content.AppendLine($"[{string.Join(" ", segments)}] {FormatNumber(phase)} d");
    }

    private static void AppendText(
        StringBuilder content,
        double x,
        double y,
        double fontSize,
        string fontResource,
        PdfColor color,
        string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var textOperand = EncodeTextOperand(text);
        AppendRgb(content, color, "rg");
        content.AppendLine("BT");
        content.AppendLine($"/{fontResource} {FormatNumber(fontSize)} Tf");
        content.AppendLine($"1 0 0 1 {FormatNumber(x)} {FormatNumber(y)} Tm");
        content.AppendLine($"{textOperand} Tj");
        content.AppendLine("ET");
    }

    private static void AppendRgb(StringBuilder content, PdfColor color, string operatorName) =>
        content.AppendLine(
            $"{FormatNumber(color.R / 255d)} {FormatNumber(color.G / 255d)} {FormatNumber(color.B / 255d)} {operatorName}");

    private static void AppendFillPattern(StringBuilder content, string resourceName)
    {
        content.AppendLine("/Pattern cs");
        content.AppendLine($"/{resourceName} scn");
    }

    private static void AppendStrokePattern(StringBuilder content, string resourceName)
    {
        content.AppendLine("/Pattern CS");
        content.AppendLine($"/{resourceName} SCN");
    }

    private static string EncodeTextOperand(string text)
    {
        var normalized = NormalizePdfText(text);
        if (!RequiresWinAnsiHexText(normalized))
            return $"({EscapePdfLiteralText(normalized)})";

        return $"<{EncodeWinAnsiHexText(normalized)}>";
    }

    private static string NormalizePdfText(string text) =>
        PdfWinAnsiTextCapability.NormalizePdfText(text);

    private static bool RequiresWinAnsiHexText(string text) => text.Any(ch => ch is < ' ' or > '~');

    private static string EscapePdfLiteralText(string text)
    {
        var escaped = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '\\':
                    escaped.Append(@"\\");
                    break;
                case '(':
                    escaped.Append(@"\(");
                    break;
                case ')':
                    escaped.Append(@"\)");
                    break;
                case >= ' ' and <= '~':
                    escaped.Append(ch);
                    break;
                default:
                    throw new InvalidOperationException("Portable PDF ASCII text path received unsupported text.");
            }
        }

        return escaped.ToString();
    }

    private static string EncodeWinAnsiHexText(string text)
    {
        var hex = new StringBuilder(text.Length * 2);
        foreach (var ch in text)
            hex.Append(EncodeWinAnsiByte(ch).ToString("X2", CultureInfo.InvariantCulture));

        return hex.ToString();
    }

    private static byte EncodeWinAnsiByte(char ch)
    {
        if (PdfWinAnsiTextCapability.TryEncodeWinAnsiByte(ch, out var value))
            return value;

        throw new InvalidOperationException(
            "Portable PDF export currently supports ASCII and WinAnsi text only; " +
            $"characters outside the built-in Helvetica/WinAnsi set require the deferred embedded-font Unicode PDF path. {DeferredUnicodePdfPathRequirements}");
    }

    private static string FormatNumber(double value) =>
        (Math.Abs(value) < 0.0005 ? 0d : value).ToString("0.###", CultureInfo.InvariantCulture);

    private static string FormatColorComponent(byte value) =>
        FormatNumber(value / 255d);

    private static void WriteAscii(Stream stream, string text) =>
        stream.Write(PdfEncoding.GetBytes(text));

    private static string NormalizeContentType(string? contentType) =>
        contentType?.Split(';', 2)[0].Trim().ToLowerInvariant() ?? string.Empty;

    private static bool IsRecoverableImageDecodeException(Exception ex) =>
        ex is FormatException
            or InvalidDataException
            or NotSupportedException
            or ArgumentException
            or IOException;

    private sealed record ImageResourceSet(
        IReadOnlyList<PdfImageResource> Resources,
        IReadOnlyDictionary<PdfImage, PdfImageResource> ByOp);

    private sealed record OpacityResourceSet(
        IReadOnlyList<PdfOpacityResource> Resources,
        IReadOnlyDictionary<double, PdfOpacityResource> ByOpacity);

    private sealed record PatternResourceSet(
        IReadOnlyList<PdfPatternResource> Resources,
        IReadOnlyDictionary<PdfLinearGradient, PdfPatternResource> ByGradient,
        IReadOnlyDictionary<PdfPatternFill, PdfPatternResource> ByPattern);

    private sealed record PdfImageResource(
        string ResourceName,
        int PixelWidth,
        int PixelHeight,
        string ColorSpace,
        string Filter,
        byte[] Data);

    private sealed record PdfOpacityResource(string ResourceName, double Opacity);

    private sealed record PdfLinkAnnotation(
        double Left,
        double Bottom,
        double Right,
        double Top,
        string? Uri,
        string? Tooltip,
        string? DestinationName);

    private sealed record PdfResolvedDestination(int PageObjectId, double X, double Top);

    private sealed record PdfPatternResource(
        string ResourceName,
        PdfLinearGradient? Gradient = null,
        PdfPatternFill? Pattern = null);

    private readonly record struct EffectBlurStamp(double X, double Y, double Weight);

    private readonly record struct PdfImagePlacement(double X, double Y, double Width, double Height);

    private sealed record PngPdfPixels(
        int Width,
        int Height,
        string ColorSpace,
        byte[] Pixels);

    private sealed record PdfObject(byte[] Bytes)
    {
        public static PdfObject Ascii(string text) => new(PdfEncoding.GetBytes(text));
    }
}

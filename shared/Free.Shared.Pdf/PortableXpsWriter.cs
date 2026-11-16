using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Free.Shared.Pdf;

namespace Free.Shared.Xps;

/// <summary>An embedded font part. Bytes for an obfuscated part must already be encoded.</summary>
public sealed record XpsFontResource(string PackagePath, byte[] FontBytes)
{
    public XpsFontResource(string packagePath, byte[] fontBytes, int fontFaceIndex)
        : this(packagePath, fontBytes) => FontFaceIndex = fontFaceIndex;

    public int FontFaceIndex { get; init; }

    public bool IsRestricted { get; init; }

    internal string PartUri => "/" + PackagePath.TrimStart('/', '\\').Replace('\\', '/');

    public string FontUri => FontFaceIndex == 0
        ? PartUri
        : FormattableString.Invariant($"{PartUri}#{FontFaceIndex}");
}

public sealed record XpsWriterOptions(XpsFontResource? TextFont = null)
{
    /// <summary>Text operations mapped to their own embedded glyph runs, with optional transparent overlays.</summary>
    public IReadOnlyDictionary<PdfText, XpsGlyphRun>? GlyphOverlays { get; init; }
}

public sealed record XpsGlyphRun(XpsFontResource Font, string? Indices = null, bool IsTransparent = true);

public sealed record XpsExportabilityReport(
    bool IsExportable,
    IReadOnlyList<string> Requirements,
    int PageCount,
    int TextOperationCount,
    int ImageOperationCount);

public sealed class XpsUnsupportedContentException : InvalidOperationException
{
    public XpsUnsupportedContentException(XpsExportabilityReport report)
        : base("The shared fixed-layout model cannot be represented as XPS: " +
               string.Join("; ", report.Requirements))
    {
        Report = report;
    }

    public XpsExportabilityReport Report { get; }
}

/// <summary>
/// Writes a standards-shaped OPC XPS package from the shared fixed-layout draw-op model.
/// This is deliberately separate from PDF: it never copies or relabels PDF bytes.
///
/// Ordinary text requires a caller-supplied <see cref="XpsWriterOptions.TextFont"/>. Each text operation can also use an embedded
/// <see cref="XpsGlyphRun"/> with explicit glyph indices and advances.
/// </summary>
public static class PortableXpsWriter
{
    private const double XpsUnitsPerPoint = 96d / 72d;
    // XPS 1.0.1 Table 18–1 recommends these minimum consumer limits.
    private const int MaxGradientStops = 100;
    private const double MaxGradientCoordinate = 1e12;
    private const string CorePropertiesPath = "docProps/core.xml";
    private static readonly XNamespace Xps = "http://schemas.microsoft.com/xps/2005/06";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace ContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace CoreProperties = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
    private static readonly XNamespace DublinCore = "http://purl.org/dc/elements/1.1/";

    /// <summary>Checks whether the gradient can be written as a native brush.</summary>
    public static bool CanRenderLinearGradient(PdfLinearGradient gradient) =>
        TryGetGradientStops(gradient, out _);

    public static XpsExportabilityReport Analyze(
        PdfContentDocument document,
        XpsWriterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        options ??= new XpsWriterOptions();

        var requirements = new HashSet<string>(StringComparer.Ordinal);
        var textCount = 0;
        var imageCount = 0;
        foreach (var page in document.Pages)
        {
            foreach (var op in page.Ops)
                AnalyzeOperation(op, options, requirements, ref textCount, ref imageCount);
        }

        return new XpsExportabilityReport(
            requirements.Count == 0 && document.Pages.Count > 0,
            requirements.ToArray(),
            document.Pages.Count,
            textCount,
            imageCount);
    }

    public static byte[] WriteToBytes(PdfContentDocument document, XpsWriterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        options ??= new XpsWriterOptions();
        var report = Analyze(document, options);
        if (!report.IsExportable)
            throw new XpsUnsupportedContentException(report);

        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var properties = BuildCoreProperties(document.Properties);
            WriteEntry(archive, "_rels/.rels", BuildRootRelationships(properties is not null));
            if (properties is not null)
                WriteEntry(archive, CorePropertiesPath, properties);

            WriteEntry(archive, "FixedDocSeq.fdseq", BuildFixedDocumentSequence());
            WriteEntry(archive, "Documents/1/FixedDocument.fdoc", BuildFixedDocument(document.Pages.Count));
            var fonts = new Dictionary<string, XpsFontResource>(StringComparer.OrdinalIgnoreCase);
            if (options.TextFont is { } font)
                fonts.Add(font.PartUri, font);

            var imageIndex = 0;
            var language = document.Properties?.Language?.Trim();
            if (string.IsNullOrEmpty(language))
                language = "und";
            for (var pageIndex = 0; pageIndex < document.Pages.Count; pageIndex++)
            {
                var page = document.Pages[pageIndex];
                var resources = new PageResources(fonts, imageIndex);
                var fixedPage = BuildFixedPage(page, language, options, resources);
                var pageNumber = pageIndex + 1;
                WriteEntry(archive, $"Documents/1/Pages/{pageNumber}.fpage", fixedPage);
                var targets = resources.FontPartUris.Concat(resources.Images.Select(image => "/" + image.Path));
                var relationships = BuildPartRelationships(targets, Xps.NamespaceName + "/required-resource");
                if (relationships.HasElements)
                    WriteEntry(archive, $"Documents/1/Pages/_rels/{pageNumber}.fpage.rels", relationships);
                foreach (var image in resources.Images)
                    WriteEntry(archive, image.Path, image.Bytes);
                imageIndex += resources.Images.Count;
            }

            foreach (var (partUri, resource) in fonts)
                WriteEntry(archive, partUri, resource.FontBytes);
            var restrictedFonts = fonts.Where(part => part.Value.IsRestricted).Select(part => part.Key);
            var documentRelationships = BuildPartRelationships(restrictedFonts, Xps.NamespaceName + "/restricted-font");
            if (documentRelationships.HasElements)
                WriteEntry(archive, "Documents/1/_rels/FixedDocument.fdoc.rels", documentRelationships);

            WriteEntry(archive, "[Content_Types].xml", BuildContentTypes(document.Pages.Count, fonts.Keys, properties is not null));
        }

        return stream.ToArray();
    }

    private static void AnalyzeOperation(
        PdfDrawOp op,
        XpsWriterOptions options,
        HashSet<string> requirements,
        ref int textCount,
        ref int imageCount)
    {
        var dash = op switch
        {
            PdfStrokeRect value => value.Dash,
            PdfStrokeRectLinearGradient value => value.Dash,
            PdfStrokeEllipse value => value.Dash,
            PdfStrokeEllipseLinearGradient value => value.Dash,
            PdfPath { StrokeColor: not null } value => value.StrokeDash,
            PdfPathPattern { StrokeColor: not null } value => value.StrokeDash,
            PdfPathLinearGradient value when value.StrokeGradient is not null || value.StrokeFallbackColor is not null => value.StrokeDash,
            _ => null
        };
        var dashWidth = op switch
        {
            PdfStrokeRect value => value.LineWidth,
            PdfStrokeRectLinearGradient value => value.LineWidth,
            PdfStrokeEllipse value => value.LineWidth,
            PdfStrokeEllipseLinearGradient value => value.LineWidth,
            PdfPath value => Math.Max(0.1, value.StrokeWidth),
            PdfPathPattern value => Math.Max(0.1, value.StrokeWidth),
            PdfPathLinearGradient value => Math.Max(0.1, value.StrokeWidth),
            _ => 1
        };
        if (dash is not null && !CanRenderDash(dash, dashWidth))
            requirements.Add("PdfDashPattern requires representable finite nonnegative segments, a positive segment, and a finite phase");
        switch (op)
        {
            case PdfText text:
                textCount++;
                if (options.TextFont is null && options.GlyphOverlays?.ContainsKey(text) != true)
                {
                    requirements.Add(
                        "PdfText requires an embedded XPS font resource; the current renderer exposes text strings but no font bytes/glyph mapping");
                }
                break;
            case PdfImage image:
                imageCount++;
                if (!TryGetSupportedImageSize(image, out _, out _))
                    requirements.Add("PdfImage requires readable PNG or JPEG bytes without clip, color effects, or rotation");
                break;
            case PdfFillRectPattern { Pattern: var pattern } when !PortableXpsPatternBrush.CanRender(pattern):
            case PdfFillEllipsePattern { Pattern: var ellipsePattern } when !PortableXpsPatternBrush.CanRender(ellipsePattern):
            case PdfPathPattern { Pattern: var pathPattern } when !PortableXpsPatternBrush.CanRender(pathPattern):
                requirements.Add("PdfPatternFill requires positive tile extents within the XPS coordinate range");
                break;
            case PdfRotationGroup group:
                foreach (var child in group.Ops)
                    AnalyzeOperation(child, options, requirements, ref textCount, ref imageCount);
                break;
            case PdfClipGroup group:
                foreach (var child in group.Ops)
                    AnalyzeOperation(child, options, requirements, ref textCount, ref imageCount);
                break;
            case PdfOpacityGroup group:
                foreach (var child in group.Ops)
                    AnalyzeOperation(child, options, requirements, ref textCount, ref imageCount);
                break;
            case PdfPathLinearGradient gradient when
                (gradient.FillGradient is { } fill && !CanRenderLinearGradient(fill) && gradient.FillFallbackColor is null) ||
                (gradient.StrokeGradient is { } stroke && !CanRenderLinearGradient(stroke) && gradient.StrokeFallbackColor is null):
                requirements.Add("PdfPathLinearGradient requires a representable gradient or a solid fallback");
                break;
            case PdfFillRect or PdfFillRectPattern or PdfFillRectLinearGradient or
                PdfStrokeRect or PdfStrokeRectLinearGradient or PdfFillEllipse or PdfFillEllipsePattern or
                PdfFillEllipseLinearGradient or PdfStrokeEllipse or PdfStrokeEllipseLinearGradient or
                PdfLine or PdfLineLinearGradient or PdfFilledTriangle or PdfPath or PdfPathPattern or PdfPathLinearGradient:
                break;
            default:
                requirements.Add($"Unsupported draw operation: {op.GetType().Name}");
                break;
        }
    }

    private static XElement BuildFixedPage(
        PdfContentPage page,
        string language,
        XpsWriterOptions options,
        PageResources resources)
    {
        var fixedPage = new XElement(
            Xps + "FixedPage",
            new XAttribute(XNamespace.Xml + "lang", language),
            new XAttribute("Width", F(page.WidthPoints * XpsUnitsPerPoint)),
            new XAttribute("Height", F(page.HeightPoints * XpsUnitsPerPoint)));
        if (page.Ops.Count > 0)
        {
            // Page content uses PDF points. One scale applied to its canvas also converts
            // stroke weights, glyph advances and the translations in nested transforms.
            var scale = TransformNumber(XpsUnitsPerPoint);
            fixedPage.Add(BuildCanvas(page.Ops, page.HeightPoints, options, resources,
                new XAttribute("RenderTransform", $"{scale},0,0,{scale},0,0")));
        }

        return fixedPage;
    }

    private static XElement? BuildOperation(
        PdfDrawOp op,
        double pageHeight,
        XpsWriterOptions options,
        PageResources resources)
    {
        return op switch
        {
            PdfFillRect fill => PathElement(RectPath(fill.X, fill.Y, fill.Width, fill.Height, pageHeight), Fill(fill.Color)),
            PdfFillRectPattern fill => PathElement(RectPath(fill.X, fill.Y, fill.Width, fill.Height, pageHeight),
                PortableXpsPatternBrush.Build(fill.Pattern)),
            PdfFillRectLinearGradient fill => PathElement(
                RectPath(fill.X, fill.Y, fill.Width, fill.Height, pageHeight),
                Fill(fill.Gradient, fill.FallbackColor, pageHeight)),
            PdfStrokeRect stroke => PathElement(
                RectPath(stroke.X, stroke.Y, stroke.Width, stroke.Height, pageHeight),
                stroke: Stroke(stroke.Color, stroke.LineWidth), dash: stroke.Dash),
            PdfStrokeRectLinearGradient stroke => PathElement(
                RectPath(stroke.X, stroke.Y, stroke.Width, stroke.Height, pageHeight),
                stroke: Stroke(stroke.Gradient, stroke.FallbackColor, stroke.LineWidth, pageHeight), dash: stroke.Dash),
            PdfFillEllipse ellipse => PathElement(
                EllipsePath(ellipse.X, ellipse.Y, ellipse.Width, ellipse.Height, pageHeight), Fill(ellipse.Color)),
            PdfFillEllipsePattern ellipse => PathElement(EllipsePath(ellipse.X, ellipse.Y, ellipse.Width, ellipse.Height, pageHeight),
                PortableXpsPatternBrush.Build(ellipse.Pattern)),
            PdfFillEllipseLinearGradient ellipse => PathElement(
                EllipsePath(ellipse.X, ellipse.Y, ellipse.Width, ellipse.Height, pageHeight),
                Fill(ellipse.Gradient, ellipse.FallbackColor, pageHeight)),
            PdfStrokeEllipse ellipse => PathElement(
                EllipsePath(ellipse.X, ellipse.Y, ellipse.Width, ellipse.Height, pageHeight),
                stroke: Stroke(ellipse.Color, ellipse.LineWidth), dash: ellipse.Dash),
            PdfStrokeEllipseLinearGradient ellipse => PathElement(
                EllipsePath(ellipse.X, ellipse.Y, ellipse.Width, ellipse.Height, pageHeight),
                stroke: Stroke(ellipse.Gradient, ellipse.FallbackColor, ellipse.LineWidth, pageHeight), dash: ellipse.Dash),
            PdfLine line => PathElement(
                $"M {F(line.X1)},{F(FlipY(line.Y1, pageHeight))} L {F(line.X2)},{F(FlipY(line.Y2, pageHeight))}",
                stroke: Stroke(line.Color, line.LineWidth)),
            PdfLineLinearGradient line => PathElement(
                $"M {F(line.X1)},{F(FlipY(line.Y1, pageHeight))} L {F(line.X2)},{F(FlipY(line.Y2, pageHeight))}",
                stroke: Stroke(line.Gradient, line.FallbackColor, line.LineWidth, pageHeight)),
            PdfFilledTriangle triangle => PathElement(
                $"M {F(triangle.X1)},{F(FlipY(triangle.Y1, pageHeight))} L {F(triangle.X2)},{F(FlipY(triangle.Y2, pageHeight))} L {F(triangle.X3)},{F(FlipY(triangle.Y3, pageHeight))} Z",
                Fill(triangle.Color)),
            PdfPath path => BuildPath(path, pageHeight),
            PdfPathPattern path => PathElement(PathData(path.Contours, pageHeight), PortableXpsPatternBrush.Build(path.Pattern),
                path.StrokeColor is { } color ? Stroke(color, Math.Max(0.1, path.StrokeWidth)) : null,
                dash: path.StrokeDash),
            PdfPathLinearGradient path => BuildPath(path, pageHeight),
            PdfText text => BuildText(text, pageHeight, options, resources),
            PdfImage image => BuildImage(image, pageHeight, resources),
            PdfRotationGroup rotation => BuildCanvas(rotation.Ops, pageHeight, options, resources,
                new XAttribute("RenderTransform", RotationMatrix(rotation, pageHeight))),
            PdfClipGroup clip => BuildClippedCanvas(clip, pageHeight, options, resources),
            PdfOpacityGroup opacity => BuildCanvas(opacity.Ops, pageHeight, options, resources,
                new XAttribute("Opacity", F(Math.Clamp(opacity.Opacity, 0, 1)))),
            _ => throw new XpsUnsupportedContentException(new XpsExportabilityReport(
                false, [$"Unsupported draw operation: {op.GetType().Name}"], 1, 0, 0)),
        };
    }

    private static XElement BuildPath(PdfPath path, double pageHeight) =>
        PathElement(PathData(path.Contours, pageHeight),
            path.FillColor is { } fill ? Fill(fill) : null,
            path.StrokeColor is { } stroke ? Stroke(stroke, Math.Max(0.1, path.StrokeWidth)) : null,
            dash: path.StrokeDash);

    private static XElement BuildPath(PdfPathLinearGradient path, double pageHeight) =>
        PathElement(PathData(path.Contours, pageHeight),
            OptionalGradientBrush(path.FillGradient, path.FillFallbackColor, pageHeight, null),
            OptionalGradientBrush(path.StrokeGradient, path.StrokeFallbackColor, pageHeight, Math.Max(0.1, path.StrokeWidth)),
            dash: path.StrokeDash);

    private static XElement? OptionalGradientBrush(PdfLinearGradient? gradient, PdfColor? fallback, double pageHeight, double? width)
    {
        XElement? brush = gradient is not null && TryGetGradientStops(gradient, out var stops)
            ? LinearGradientBrush(gradient, stops, pageHeight)
            : fallback is { } color ? Fill(color) : null;
        if (brush is not null && width is { } value)
            brush.SetAttributeValue("data-stroke-thickness", TransformNumber(value));
        return brush;
    }

    private static XElement BuildText(
        PdfText text,
        double pageHeight,
        XpsWriterOptions options,
        PageResources resources)
    {
        var overlay = options.GlyphOverlays?.GetValueOrDefault(text);
        var font = overlay?.Font ?? options.TextFont!;
        resources.AddFont(font);
        // The XPS markup parser treats a leading brace as a markup extension.
        var unicodeString = text.Text.StartsWith('{') ? "{}" + text.Text : text.Text;
        var style = overlay is not null ? null : text.Face switch
        {
            PdfFontFace.Bold => "BoldSimulation",
            PdfFontFace.Italic => "ItalicSimulation",
            PdfFontFace.BoldItalic => "BoldItalicSimulation",
            _ => null,
        };
        return new XElement(
            Xps + "Glyphs",
            new XAttribute("FontUri", font.FontUri),
            new XAttribute("FontRenderingEmSize", F(text.FontSize)),
            new XAttribute("Fill", overlay?.IsTransparent == true ? "#00000000" : Color(text.Color)),
            new XAttribute("OriginX", F(text.X)),
            new XAttribute("OriginY", F(FlipY(text.Y, pageHeight))),
            new XAttribute("UnicodeString", unicodeString),
            overlay?.Indices is { } indices ? new XAttribute("Indices", indices) : null,
            style is null ? null : new XAttribute("StyleSimulations", style));
    }

    private static XElement BuildImage(
        PdfImage image,
        double pageHeight,
        PageResources resources)
    {
        if (!TryGetSupportedImageSize(image, out var pixelWidth, out var pixelHeight))
            throw new InvalidOperationException("Only untransformed PNG and JPEG images can be written to XPS.");
        var path = resources.AddImage(image);

        var placement = PlaceImage(image, pixelWidth, pixelHeight);
        return new XElement(
            Xps + "Path",
            new XAttribute(
                "Data",
                RectPath(placement.X, placement.Y, placement.Width, placement.Height, pageHeight)),
            new XElement(
                Xps + "Path.Fill",
                new XElement(
                    Xps + "ImageBrush",
                    new XAttribute("ImageSource", "/" + path),
                    new XAttribute(
                        "Viewport",
                        $"{F(placement.X)},{F(pageHeight - placement.Y - placement.Height)},{F(placement.Width)},{F(placement.Height)}"),
                    new XAttribute("ViewportUnits", "Absolute"),
                    new XAttribute("Viewbox", placement.Viewbox),
                    new XAttribute("ViewboxUnits", "Absolute"))));
    }

    /// <summary>
    /// Resolves an <c>a:srcRect</c> crop into the two things XPS can express: the visible source
    /// region becomes the brush <c>Viewbox</c> (in source image units), and the
    /// destination rectangle shrinks for negative insets, which pad the image inside its frame
    /// rather than cropping it. Both halves come from the shared plan the PDF adapters use, so the
    /// same crop lands on the same source pixels in every backend.
    /// </summary>
    private static XpsImagePlacement PlaceImage(PdfImage image, int pixelWidth, int pixelHeight)
    {
        var dpi = image.ContentType.Equals("image/png", StringComparison.OrdinalIgnoreCase)
            ? ReadPngDpi(image.ImageBytes)
            : ReadJpegDpi(image.ImageBytes);
        // XPS measures ImageBrush.Viewbox in 1/96 inch at the source image's own resolution.
        var sourceScaleX = dpi.X > 0 ? 96d / dpi.X : 1d;
        var sourceScaleY = dpi.Y > 0 ? 96d / dpi.Y : 1d;
        var full = new XpsImagePlacement(image.X, image.Y, image.Width, image.Height,
            FormatViewbox(0, 0, pixelWidth, pixelHeight));
        if (!image.SourceCrop.HasCrop || image.Width <= 0 || image.Height <= 0)
            return full;

        var plan = PdfRenderGeometry.GetImageCropPlan(pixelWidth, pixelHeight, image.SourceCrop);
        if (!plan.HasCrop)
            return full;

        var width = image.Width * (1.0 - plan.DestinationInsetLeft - plan.DestinationInsetRight);
        var height = image.Height * (1.0 - plan.DestinationInsetTop - plan.DestinationInsetBottom);
        if (width <= 0 || height <= 0)
            return full;

        // The destination is still expressed in PDF's y-up coordinates here, while the
        // source viewbox starts from the image's top-left pixel.
        return new XpsImagePlacement(
            image.X + plan.DestinationInsetLeft * image.Width,
            image.Y + plan.DestinationInsetBottom * image.Height,
            width,
            height,
            FormatViewbox(plan.SourceX, plan.SourceY, plan.SourceWidth, plan.SourceHeight));

        string FormatViewbox(int x, int y, int width, int height) =>
            $"{TransformNumber(x * sourceScaleX)},{TransformNumber(y * sourceScaleY)}," +
            $"{TransformNumber(width * sourceScaleX)},{TransformNumber(height * sourceScaleY)}";
    }

    private readonly record struct XpsImagePlacement(
        double X,
        double Y,
        double Width,
        double Height,
        string Viewbox);

    private static (double X, double Y) ReadPngDpi(ReadOnlySpan<byte> bytes)
    {
        // Dimensions have already been validated. PNG stores absolute density in pixels per metre.
        var chunks = bytes[8..];
        while (chunks.Length >= 12)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(chunks);
            if (length > (uint)(chunks.Length - 12))
                break;

            var type = chunks.Slice(4, 4);
            if (type.SequenceEqual("IDAT"u8))
                break;
            if (type.SequenceEqual("pHYs"u8) && length == 9 && chunks[16] == 1)
            {
                const double metresPerInch = 0.0254;
                return (BinaryPrimitives.ReadUInt32BigEndian(chunks[8..]) * metresPerInch,
                    BinaryPrimitives.ReadUInt32BigEndian(chunks[12..]) * metresPerInch);
            }

            chunks = chunks[(12 + (int)length)..];
        }

        return default;
    }

    private static (double X, double Y) ReadJpegDpi(ReadOnlySpan<byte> bytes)
    {
        var headers = bytes[2..];
        while (headers.Length >= 4 && headers[0] == 0xFF)
        {
            var marker = headers[1];
            if (marker == 0xFF)
            {
                headers = headers[1..];
                continue;
            }
            if (marker is 0xD9 or 0xDA)
                break;

            var length = BinaryPrimitives.ReadUInt16BigEndian(headers[2..]);
            if (length < 2 || length > headers.Length - 2)
                break;

            var data = headers.Slice(4, length - 2);
            if (marker == 0xE0 && data.Length >= 12 && data.StartsWith("JFIF\0"u8) && data[7] is 1 or 2)
            {
                var unitsPerInch = data[7] == 1 ? 1d : 2.54;
                var x = BinaryPrimitives.ReadUInt16BigEndian(data[8..]);
                var y = BinaryPrimitives.ReadUInt16BigEndian(data[10..]);
                if (x > 0 || y > 0)
                    return (x * unitsPerInch, y * unitsPerInch);
            }
            if (marker == 0xE1 && data.StartsWith("Exif\0\0"u8))
            {
                var exifDpi = ReadExifDpi(data[6..]);
                if (exifDpi.X > 0 || exifDpi.Y > 0)
                    return exifDpi;
            }

            headers = headers[(2 + length)..];
        }

        return default;
    }

    private static (double X, double Y) ReadExifDpi(ReadOnlySpan<byte> data)
    {
        var littleEndian = data.StartsWith("II"u8);
        if (data.Length < 8 || (!littleEndian && !data.StartsWith("MM"u8)) || UInt16(data[2..], littleEndian) != 42)
            return default;

        var directoryOffset = UInt32(data[4..], littleEndian);
        if (directoryOffset > (uint)(data.Length - 2))
            return default;

        var directory = data[(int)directoryOffset..];
        var entryCount = Math.Min((int)UInt16(directory, littleEndian), (directory.Length - 2) / 12);
        double x = 0;
        double y = 0;
        var units = 2;
        for (var index = 0; index < entryCount; index++)
        {
            var entry = directory.Slice(2 + index * 12, 12);
            if (UInt32(entry[4..], littleEndian) != 1)
                continue;

            var tag = UInt16(entry, littleEndian);
            var type = UInt16(entry[2..], littleEndian);
            if (tag == 0x0128 && type == 3)
                units = UInt16(entry[8..], littleEndian);
            if (tag is not (0x011A or 0x011B) || type != 5)
                continue;

            var offset = UInt32(entry[8..], littleEndian);
            if (offset > (uint)(data.Length - 8))
                continue;

            var rational = data[(int)offset..];
            var denominator = UInt32(rational[4..], littleEndian);
            if (denominator == 0)
                continue;

            var dpi = (double)UInt32(rational, littleEndian) / denominator;
            if (tag == 0x011A)
                x = dpi;
            else
                y = dpi;
        }

        return units switch
        {
            2 => (x, y),
            3 => (x * 2.54, y * 2.54),
            _ => default,
        };

        static ushort UInt16(ReadOnlySpan<byte> value, bool littleEndian) => littleEndian
            ? BinaryPrimitives.ReadUInt16LittleEndian(value)
            : BinaryPrimitives.ReadUInt16BigEndian(value);

        static uint UInt32(ReadOnlySpan<byte> value, bool littleEndian) => littleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(value)
            : BinaryPrimitives.ReadUInt32BigEndian(value);
    }

    private sealed class PageResources(
        IDictionary<string, XpsFontResource> fonts,
        int firstImageIndex)
    {
        public List<(string Path, byte[] Bytes)> Images { get; } = [];
        public HashSet<string> FontPartUris { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void AddFont(XpsFontResource font)
        {
            var uri = font.PartUri;
            FontPartUris.Add(uri);
            if (!fonts.TryGetValue(uri, out var existing))
            {
                fonts.Add(uri, font);
                return;
            }

            if (!ReferenceEquals(font.FontBytes, existing.FontBytes) &&
                !font.FontBytes.AsSpan().SequenceEqual(existing.FontBytes))
                throw new InvalidOperationException($"Different XPS font resources use the same part URI: {uri}");
            if (font.IsRestricted && !existing.IsRestricted)
                fonts[uri] = existing with { IsRestricted = true };
        }

        public string AddImage(PdfImage image)
        {
            var extension = image.ContentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) ? "jpg" : "png";
            var path = FormattableString.Invariant($"Resources/Images/{firstImageIndex + Images.Count + 1}.{extension}");
            Images.Add((path, image.ImageBytes));
            return path;
        }
    }

    private static XElement BuildCanvas(
        IReadOnlyList<PdfDrawOp> operations,
        double pageHeight,
        XpsWriterOptions options,
        PageResources resources,
        XAttribute attribute)
    {
        var canvas = new XElement(Xps + "Canvas", attribute);
        foreach (var operation in operations)
            canvas.Add(BuildOperation(operation, pageHeight, options, resources));
        return canvas;
    }

    private static XElement? BuildClippedCanvas(
        PdfClipGroup clip,
        double pageHeight,
        XpsWriterOptions options,
        PageResources resources)
    {
        if (Finite(clip.Width) <= 0 || Finite(clip.Height) <= 0)
            return null;

        return BuildCanvas(clip.Ops, pageHeight, options, resources,
            new XAttribute("Clip", RectPath(clip.X, clip.Y, clip.Width, clip.Height, pageHeight)));
    }

    private static string RotationMatrix(PdfRotationGroup rotation, double pageHeight)
    {
        var (sin, cos) = Math.SinCos(rotation.RotationDegrees * Math.PI / 180);
        var flipX = rotation.FlipH ? -1 : 1;
        var flipY = rotation.FlipV ? -1 : 1;
        var m11 = cos * flipX;
        var m12 = sin * flipY;
        var m21 = -sin * flipX;
        var m22 = cos * flipY;
        var centerX = rotation.CenterX;
        var centerY = FlipY(rotation.CenterY, pageHeight);
        var offsetX = centerX - centerX * m11 - centerY * m21;
        var offsetY = centerY - centerX * m12 - centerY * m22;
        return $"{TransformNumber(m11)},{TransformNumber(m12)},{TransformNumber(m21)},{TransformNumber(m22)}," +
            $"{TransformNumber(offsetX)},{TransformNumber(offsetY)}";
    }

    private static XElement PathElement(
        string data, XElement? fill = null, XElement? stroke = null, PdfDashPattern? dash = null)
    {
        var path = new XElement(Xps + "Path", new XAttribute("Data", data));
        if (fill is not null)
            SetPathBrush(path, "Fill", fill);
        if (stroke is not null)
        {
            var thickness = stroke.Attribute("data-stroke-thickness");
            if (thickness is not null)
            {
                path.SetAttributeValue("StrokeThickness", thickness.Value);
                path.SetAttributeValue("StrokeMiterLimit", "4");
                if (dash is not null)
                    ApplyDashPattern(path, dash, (double)thickness);
                thickness.Remove();
            }

            SetPathBrush(path, "Stroke", stroke);
        }
        return path;
    }

    private static void ApplyDashPattern(XElement path, PdfDashPattern dash, double strokeWidth)
    {
        if (strokeWidth <= 0)
            return;
        if (dash.Segments.Count == 0)
            return;
        var segments = dash.Segments.Count % 2 == 0 ? dash.Segments.ToArray() : dash.Segments.Concat(dash.Segments).ToArray();

        // The shared pattern is measured in points. XPS measures its lengths and phase
        // in multiples of the stroke thickness.
        path.SetAttributeValue("StrokeDashArray", string.Join(" ", segments.Select(segment => TransformNumber(segment / strokeWidth))));
        if (dash.Phase != 0)
            path.SetAttributeValue("StrokeDashOffset", TransformNumber(dash.Phase / strokeWidth));
    }

    /// <summary>Whether a PDF dash pattern can be represented as an XPS stroke dash array.</summary>
    public static bool CanRenderDash(PdfDashPattern? dash, double width)
    {
        if (dash is null) return true;
        if (dash.Segments.Count == 0) return true;
        if (!double.IsFinite(width) || width <= 0 || !double.IsFinite(dash.Phase) ||
            !RepresentableDashRatio(dash.Phase / width)) return false;
        return dash.Segments.All(segment => double.IsFinite(segment) && segment >= 0 && RepresentableDashRatio(segment / width)) &&
            dash.Segments.Any(segment => segment > 0);
    }

    private static bool RepresentableDashRatio(double ratio) => double.IsFinite(ratio) &&
        Math.Abs(ratio) <= MaxGradientCoordinate && (ratio == 0 || Math.Abs(ratio) >= 1e-12);

    private static void SetPathBrush(XElement path, string propertyName, XElement brush)
    {
        if (brush.Name == Xps + "SolidColorBrush")
            path.SetAttributeValue(propertyName, brush.Attribute("Color")!.Value);
        else
            path.Add(new XElement(Xps + ("Path." + propertyName), brush));
    }

    private static XElement Fill(PdfColor color) => new(Xps + "SolidColorBrush", new XAttribute("Color", Color(color)));

    private static XElement Fill(PdfLinearGradient? gradient, PdfColor fallbackColor, double pageHeight) =>
        gradient is not null && TryGetGradientStops(gradient, out var stops)
            ? LinearGradientBrush(gradient, stops, pageHeight)
            : Fill(fallbackColor);

    private static XElement Stroke(PdfColor color, double width) =>
        Stroke(null, color, width, pageHeight: 0);

    private static XElement Stroke(PdfLinearGradient? gradient, PdfColor fallbackColor, double width, double pageHeight)
    {
        var brush = Fill(gradient, fallbackColor, pageHeight);
        brush.SetAttributeValue("data-stroke-thickness", TransformNumber(width));
        return brush;
    }

    private static bool TryGetGradientStops(PdfLinearGradient gradient, out PdfGradientStop[] stops)
    {
        stops = [];
        return Math.Abs(gradient.StartX) <= MaxGradientCoordinate &&
            Math.Abs(gradient.StartY) <= MaxGradientCoordinate &&
            Math.Abs(gradient.EndX) <= MaxGradientCoordinate &&
            Math.Abs(gradient.EndY) <= MaxGradientCoordinate &&
            PdfRenderGeometry.TryNormalizeGradient(gradient, out stops) &&
            stops.Length <= MaxGradientStops;
    }

    private static XElement LinearGradientBrush(
        PdfLinearGradient gradient,
        IReadOnlyList<PdfGradientStop> stops,
        double pageHeight) =>
        new(
            Xps + "LinearGradientBrush",
            new XAttribute("MappingMode", "Absolute"),
            new XAttribute("ColorInterpolationMode", "SRgbLinearInterpolation"),
            new XAttribute("SpreadMethod", "Pad"),
            new XAttribute("StartPoint", $"{TransformNumber(gradient.StartX)},{TransformNumber(FlipY(gradient.StartY, pageHeight))}"),
            new XAttribute("EndPoint", $"{TransformNumber(gradient.EndX)},{TransformNumber(FlipY(gradient.EndY, pageHeight))}"),
            new XElement(
                Xps + "LinearGradientBrush.GradientStops",
                stops.Select(stop => new XElement(
                    Xps + "GradientStop",
                    new XAttribute("Color", Color(stop.Color)),
                    new XAttribute("Offset", TransformNumber(stop.Position))))));

    private static string RectPath(double x, double y, double width, double height, double pageHeight) =>
        $"M {F(x)},{F(FlipY(y + height, pageHeight))} L {F(x + width)},{F(FlipY(y + height, pageHeight))} L {F(x + width)},{F(FlipY(y, pageHeight))} L {F(x)},{F(FlipY(y, pageHeight))} Z";

    private static string EllipsePath(double x, double y, double width, double height, double pageHeight)
    {
        var cx = x + width / 2;
        var cy = FlipY(y + height / 2, pageHeight);
        var rx = width / 2;
        var ry = height / 2;
        var k = 0.5522847498;
        return $"M {F(cx + rx)},{F(cy)} C {F(cx + rx)},{F(cy + k * ry)} {F(cx + k * rx)},{F(cy + ry)} {F(cx)},{F(cy + ry)} C {F(cx - k * rx)},{F(cy + ry)} {F(cx - rx)},{F(cy + k * ry)} {F(cx - rx)},{F(cy)} C {F(cx - rx)},{F(cy - k * ry)} {F(cx - k * rx)},{F(cy - ry)} {F(cx)},{F(cy - ry)} C {F(cx + k * rx)},{F(cy - ry)} {F(cx + rx)},{F(cy - k * ry)} {F(cx + rx)},{F(cy)} Z";
    }

    private static string PathData(IReadOnlyList<PdfPathContour> contours, double pageHeight)
    {
        if (contours.Count == 0)
            return string.Empty;

        var builder = new StringBuilder("F1 ");
        foreach (var contour in contours)
        {
            builder.Append("M ").Append(F(contour.Start.X)).Append(',').Append(F(FlipY(contour.Start.Y, pageHeight)));
            foreach (var segment in contour.Segments)
            {
                if (segment.Kind == PdfPathSegmentKind.CubicBezier)
                {
                    builder.Append(" C ").Append(F(segment.Control1.X)).Append(',').Append(F(FlipY(segment.Control1.Y, pageHeight)))
                        .Append(' ').Append(F(segment.Control2.X)).Append(',').Append(F(FlipY(segment.Control2.Y, pageHeight)))
                        .Append(' ').Append(F(segment.End.X)).Append(',').Append(F(FlipY(segment.End.Y, pageHeight)));
                }
                else
                {
                    builder.Append(" L ").Append(F(segment.End.X)).Append(',').Append(F(FlipY(segment.End.Y, pageHeight)));
                }
            }
            if (contour.Closed)
                builder.Append(" Z");
        }
        return builder.ToString();
    }

    internal static bool CanRenderImage(PdfImage image) => TryGetSupportedImageSize(image, out _, out _);

    private static bool TryGetSupportedImageSize(PdfImage image, out int width, out int height)
    {
        width = 0;
        height = 0;
        return image.RotationDegrees == 0 &&
            image.ClipKind == PdfImageClipKind.None &&
            image.Opacity == 1 &&
            !image.ColorEffects.HasPixelEffects &&
            PdfImageDimensions.TryReadSize(image.ImageBytes, image.ContentType, out width, out height);
    }

    private static XElement BuildContentTypes(int pageCount, IEnumerable<string> fontPartUris, bool hasCoreProperties)
    {
        var root = new XElement(
            ContentTypes + "Types",
            new XElement(ContentTypes + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(ContentTypes + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
            new XElement(ContentTypes + "Default", new XAttribute("Extension", "png"), new XAttribute("ContentType", "image/png")),
            new XElement(ContentTypes + "Default", new XAttribute("Extension", "jpg"), new XAttribute("ContentType", "image/jpeg")),
            new XElement(ContentTypes + "Default", new XAttribute("Extension", "jpeg"), new XAttribute("ContentType", "image/jpeg")),
            new XElement(ContentTypes + "Override", new XAttribute("PartName", "/FixedDocSeq.fdseq"), new XAttribute("ContentType", "application/vnd.ms-package.xps-fixeddocumentsequence+xml")),
            new XElement(ContentTypes + "Override", new XAttribute("PartName", "/Documents/1/FixedDocument.fdoc"), new XAttribute("ContentType", "application/vnd.ms-package.xps-fixeddocument+xml")));
        for (var i = 0; i < pageCount; i++)
            root.Add(new XElement(ContentTypes + "Override", new XAttribute("PartName", $"/Documents/1/Pages/{i + 1}.fpage"), new XAttribute("ContentType", "application/vnd.ms-package.xps-fixedpage+xml")));

        if (hasCoreProperties)
            root.Add(new XElement(ContentTypes + "Override", new XAttribute("PartName", "/" + CorePropertiesPath),
                new XAttribute("ContentType", "application/vnd.openxmlformats-package.core-properties+xml")));
        foreach (var fontUri in fontPartUris)
        {
            var fontType = fontUri.EndsWith(".odttf", StringComparison.OrdinalIgnoreCase) ||
                fontUri.EndsWith(".odttc", StringComparison.OrdinalIgnoreCase)
                ? "application/vnd.ms-package.obfuscated-opentype"
                : "application/vnd.ms-opentype";
            root.Add(new XElement(ContentTypes + "Override", new XAttribute("PartName", fontUri),
                new XAttribute("ContentType", fontType)));
        }
        return root;
    }

    private static XElement BuildRootRelationships(bool hasCoreProperties) => new(
        Rel + "Relationships",
        Relationship(1, Xps.NamespaceName + "/fixedrepresentation", "/FixedDocSeq.fdseq"),
        hasCoreProperties
            ? Relationship(2, Rel.NamespaceName + "/metadata/core-properties", "/" + CorePropertiesPath)
            : null);

    private static XElement BuildPartRelationships(IEnumerable<string> targets, string type) => new(
        Rel + "Relationships",
        targets.Select((target, index) => Relationship(index + 1, type, target)));

    private static XElement Relationship(int index, string type, string target) => new(
        Rel + "Relationship",
        new XAttribute("Id", FormattableString.Invariant($"rId{index}")),
        new XAttribute("Type", type),
        new XAttribute("Target", target));

    private static XElement? BuildCoreProperties(PdfDocumentProperties? properties)
    {
        if (properties is null)
            return null;

        var core = new XElement(CoreProperties + "coreProperties",
            new XAttribute(XNamespace.Xmlns + "cp", CoreProperties.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "dc", DublinCore.NamespaceName),
            Property(DublinCore + "title", properties.Title),
            Property(DublinCore + "creator", properties.Author),
            Property(DublinCore + "subject", properties.Subject),
            Property(CoreProperties + "keywords", properties.Keywords));
        return core.HasElements ? core : null;

        static XElement? Property(XName name, string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : new XElement(name, value.Trim());
    }

    private static XElement BuildFixedDocumentSequence() => new(
        Xps + "FixedDocumentSequence",
        new XElement(Xps + "DocumentReference", new XAttribute("Source", "/Documents/1/FixedDocument.fdoc")));

    private static XElement BuildFixedDocument(int pageCount) => new(
        Xps + "FixedDocument",
        Enumerable.Range(1, pageCount).Select(i =>
            new XElement(Xps + "PageContent", new XAttribute("Source", $"/Documents/1/Pages/{i}.fpage"))));

    private static void WriteEntry(ZipArchive archive, string path, XElement content) =>
        WriteEntry(archive, path, Encoding.UTF8.GetBytes(content.ToString(SaveOptions.DisableFormatting)));

    private static void WriteEntry(ZipArchive archive, string path, byte[] bytes)
    {
        var entry = archive.CreateEntry(NormalizePackagePath(path), CompressionLevel.Optimal);
        entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var stream = entry.Open();
        stream.Write(bytes);
    }

    private static string NormalizePackagePath(string path) => path.TrimStart('/').Replace('\\', '/');

    private static string Color(PdfColor color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static double FlipY(double y, double pageHeight) => pageHeight - y;

    private static string F(double value) => Finite(value).ToString("0.###", CultureInfo.InvariantCulture);

    private static string TransformNumber(double value) =>
        Finite(value).ToString("0.############", CultureInfo.InvariantCulture);

    // r469: NaN and Infinity used to be formatted straight into the markup, giving a FixedPage with
    // Width="NaN" and paths reading `M NaN,NaN L ...`. Neither is a number in the abbreviated
    // geometry syntax, so the package is a well-formed OPC container holding an unparseable page.
    //
    // This deliberately throws InvalidOperationException and NOT XpsUnsupportedContentException,
    // even though the writer's other refusals use that type. FreeWAvaloniaXpsExport catches
    // XpsUnsupportedContentException and retries by rasterising the SAME document through Skia,
    // which was measured to accept a non-finite rectangle and return a page -- so raising the
    // writer's own type would convert a broken coordinate into a silently blank page. The fallback
    // exists for content XPS cannot REPRESENT, not for input that is not a number.
    private static double Finite(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new InvalidOperationException(
                $"XPS export received a non-finite coordinate ({value}). Writing it would produce a " +
                "package that looks valid but cannot be opened, so the export is refused.");
        }

        return value;
    }
}

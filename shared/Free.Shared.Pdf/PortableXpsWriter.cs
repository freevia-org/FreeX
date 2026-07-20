using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Free.Shared.Pdf;

namespace Free.Shared.Xps;

public sealed record XpsFontResource(string PackagePath, byte[] FontBytes)
{
    public string FontUri => PackagePath.StartsWith('/') ? PackagePath : "/" + PackagePath;
}

public sealed record XpsWriterOptions(XpsFontResource? TextFont = null);

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
/// The existing FreeW Avalonia model does not carry an embedded font resource, glyph indices, or
/// font-subsetting metadata. Therefore text pages require an explicit <see cref="XpsFontResource"/>
/// and fail truthfully when one is absent. Vector geometry and supported PNG/JPEG images are still
/// emitted as real FixedPage content, which is the maximum truthful shared adapter for the model.
/// </summary>
public static class PortableXpsWriter
{
    private static readonly XNamespace Xps = "http://schemas.microsoft.com/xps/2005/06";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace ContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";

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
            WriteEntry(archive, "[Content_Types].xml", BuildContentTypes(document, options));
            WriteEntry(archive, "_rels/.rels", BuildRootRelationships());
            WriteEntry(archive, "FixedDocSeq.fdseq", BuildFixedDocumentSequence(document.Pages.Count));
            WriteEntry(archive, "Documents/1/FixedDocument.fdoc", BuildFixedDocument(document.Pages.Count));
            if (options.TextFont is { } font)
                WriteEntry(archive, NormalizePackagePath(font.PackagePath), font.FontBytes);

            var imageIndex = 0;
            for (var pageIndex = 0; pageIndex < document.Pages.Count; pageIndex++)
            {
                var page = document.Pages[pageIndex];
                var images = new List<(string Path, byte[] Bytes)>();
                var fixedPage = BuildFixedPage(page, options, images, ref imageIndex);
                WriteEntry(
                    archive,
                    $"Documents/1/Pages/{pageIndex + 1}.fpage",
                    Encoding.UTF8.GetBytes(fixedPage.ToString(SaveOptions.DisableFormatting)));
                foreach (var image in images)
                    WriteEntry(archive, image.Path, image.Bytes);
            }
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
        switch (op)
        {
            case PdfText:
                textCount++;
                if (options.TextFont is null)
                {
                    requirements.Add(
                        "PdfText requires an embedded XPS font resource; the current Avalonia renderer exposes text strings but no font bytes/glyph mapping");
                }
                break;
            case PdfImage image:
                imageCount++;
                if (!IsSupportedImage(image))
                    requirements.Add("PdfImage requires PNG or JPEG bytes without crop, clip, color effects, or rotation");
                break;
            case PdfRotationGroup group:
                foreach (var child in group.Ops)
                    AnalyzeOperation(child, options, requirements, ref textCount, ref imageCount);
                break;
            case PdfOpacityGroup group:
                foreach (var child in group.Ops)
                    AnalyzeOperation(child, options, requirements, ref textCount, ref imageCount);
                break;
            default:
                break;
        }
    }

    private static XElement BuildFixedPage(
        PdfContentPage page,
        XpsWriterOptions options,
        List<(string Path, byte[] Bytes)> images,
        ref int imageIndex)
    {
        var fixedPage = new XElement(
            Xps + "FixedPage",
            new XAttribute("Width", F(page.WidthPoints)),
            new XAttribute("Height", F(page.HeightPoints)));
        foreach (var op in page.Ops)
            fixedPage.Add(BuildOperation(op, page.WidthPoints, page.HeightPoints, options, images, ref imageIndex));
        return fixedPage;
    }

    private static XElement BuildOperation(
        PdfDrawOp op,
        double pageWidth,
        double pageHeight,
        XpsWriterOptions options,
        List<(string Path, byte[] Bytes)> images,
        ref int imageIndex)
    {
        return op switch
        {
            PdfFillRect fill => PathElement(RectPath(fill.X, fill.Y, fill.Width, fill.Height, pageHeight), Fill(fill.Color)),
            PdfFillRectLinearGradient fill => PathElement(
                RectPath(fill.X, fill.Y, fill.Width, fill.Height, pageHeight), Fill(fill.FallbackColor)),
            PdfStrokeRect stroke => PathElement(
                RectPath(stroke.X, stroke.Y, stroke.Width, stroke.Height, pageHeight),
                Stroke(stroke.Color, stroke.LineWidth)),
            PdfStrokeRectLinearGradient stroke => PathElement(
                RectPath(stroke.X, stroke.Y, stroke.Width, stroke.Height, pageHeight),
                Stroke(stroke.FallbackColor, stroke.LineWidth)),
            PdfFillEllipse ellipse => PathElement(
                EllipsePath(ellipse.X, ellipse.Y, ellipse.Width, ellipse.Height, pageHeight), Fill(ellipse.Color)),
            PdfFillEllipseLinearGradient ellipse => PathElement(
                EllipsePath(ellipse.X, ellipse.Y, ellipse.Width, ellipse.Height, pageHeight), Fill(ellipse.FallbackColor)),
            PdfStrokeEllipse ellipse => PathElement(
                EllipsePath(ellipse.X, ellipse.Y, ellipse.Width, ellipse.Height, pageHeight),
                Stroke(ellipse.Color, ellipse.LineWidth)),
            PdfStrokeEllipseLinearGradient ellipse => PathElement(
                EllipsePath(ellipse.X, ellipse.Y, ellipse.Width, ellipse.Height, pageHeight),
                Stroke(ellipse.FallbackColor, ellipse.LineWidth)),
            PdfLine line => PathElement(
                $"M {F(line.X1)},{F(FlipY(line.Y1, pageHeight))} L {F(line.X2)},{F(FlipY(line.Y2, pageHeight))}",
                Stroke(line.Color, line.LineWidth)),
            PdfLineLinearGradient line => PathElement(
                $"M {F(line.X1)},{F(FlipY(line.Y1, pageHeight))} L {F(line.X2)},{F(FlipY(line.Y2, pageHeight))}",
                Stroke(line.FallbackColor, line.LineWidth)),
            PdfFilledTriangle triangle => PathElement(
                $"M {F(triangle.X1)},{F(FlipY(triangle.Y1, pageHeight))} L {F(triangle.X2)},{F(FlipY(triangle.Y2, pageHeight))} L {F(triangle.X3)},{F(FlipY(triangle.Y3, pageHeight))} Z",
                Fill(triangle.Color)),
            PdfPath path => BuildPath(path, pageHeight),
            PdfPathLinearGradient path => BuildPath(path, pageHeight),
            PdfText text => BuildText(text, pageHeight, options),
            PdfImage image => BuildImage(image, pageHeight, images, ref imageIndex),
            PdfRotationGroup rotation => BuildCanvas(rotation.Ops, pageWidth, pageHeight, options, images, ref imageIndex, rotation),
            PdfOpacityGroup opacity => BuildCanvas(opacity.Ops, pageWidth, pageHeight, options, images, ref imageIndex, opacity),
            _ => throw new XpsUnsupportedContentException(new XpsExportabilityReport(
                false, [$"Unsupported draw operation: {op.GetType().Name}"], 1, 0, 0)),
        };
    }

    private static XElement BuildPath(PdfPath path, double pageHeight) =>
        PathElement(PathData(path.Contours, pageHeight),
            path.FillColor is { } fill ? Fill(fill) : null,
            path.StrokeColor is { } stroke ? Stroke(stroke, path.StrokeWidth) : null);

    private static XElement BuildPath(PdfPathLinearGradient path, double pageHeight) =>
        PathElement(PathData(path.Contours, pageHeight),
            path.FillFallbackColor is { } fill ? Fill(fill) : null,
            path.StrokeFallbackColor is { } stroke ? Stroke(stroke, path.StrokeWidth) : null);

    private static XElement BuildText(PdfText text, double pageHeight, XpsWriterOptions options)
    {
        if (options.TextFont is null)
            throw new InvalidOperationException("PdfText cannot be written without an embedded XPS font resource.");
        return new XElement(
            Xps + "Glyphs",
            new XAttribute("FontUri", options.TextFont.FontUri),
            new XAttribute("FontRenderingEmSize", F(text.FontSize)),
            new XAttribute("Fill", Color(text.Color)),
            new XAttribute("OriginX", F(text.X)),
            new XAttribute("OriginY", F(FlipY(text.Y, pageHeight))),
            new XAttribute("UnicodeString", text.Text),
            text.Face switch
            {
                PdfFontFace.Bold => new XAttribute("StyleSimulations", "BoldSimulation"),
                PdfFontFace.Italic => new XAttribute("StyleSimulations", "ItalicSimulation"),
                PdfFontFace.BoldItalic => new XAttribute("StyleSimulations", "BoldSimulation,ItalicSimulation"),
                _ => null,
            });
    }

    private static XElement BuildImage(
        PdfImage image,
        double pageHeight,
        List<(string Path, byte[] Bytes)> images,
        ref int imageIndex)
    {
        if (!IsSupportedImage(image))
            throw new InvalidOperationException("Only untransformed PNG and JPEG images can be written to XPS.");
        var extension = image.ContentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) ? "jpg" : "png";
        var path = $"Resources/Images/{++imageIndex}.{extension}";
        images.Add((path, image.ImageBytes));
        return new XElement(
            Xps + "Path",
            new XAttribute("Data", RectPath(image.X, image.Y, image.Width, image.Height, pageHeight)),
            new XElement(
                Xps + "Path.Fill",
                new XElement(
                    Xps + "ImageBrush",
                    new XAttribute("ImageSource", "/" + path),
                    new XAttribute("Viewport", $"{F(image.X)},{F(pageHeight - image.Y - image.Height)},{F(image.Width)},{F(image.Height)}"),
                    new XAttribute("ViewportUnits", "Absolute"),
                    new XAttribute("Viewbox", "0,0,1,1"),
                    new XAttribute("ViewboxUnits", "RelativeToBoundingBox"))));
    }

    private static XElement BuildCanvas(
        IReadOnlyList<PdfDrawOp> operations,
        double pageWidth,
        double pageHeight,
        XpsWriterOptions options,
        List<(string Path, byte[] Bytes)> images,
        ref int imageIndex,
        PdfRotationGroup rotation)
    {
        var canvas = new XElement(Xps + "Canvas");
        var angle = -rotation.RotationDegrees;
        var centerX = rotation.CenterX;
        var centerY = FlipY(rotation.CenterY, pageHeight);
        canvas.Add(new XElement(
            Xps + "Canvas.RenderTransform",
            new XElement(
                Xps + "MatrixTransform",
                new XAttribute("Matrix", $"{F(Math.Cos(angle * Math.PI / 180))},{F(Math.Sin(angle * Math.PI / 180))},{F(-Math.Sin(angle * Math.PI / 180))},{F(Math.Cos(angle * Math.PI / 180))},{F(centerX)},{F(centerY)}"))));
        foreach (var operation in operations)
            canvas.Add(BuildOperation(operation, pageWidth, pageHeight, options, images, ref imageIndex));
        return canvas;
    }

    private static XElement BuildCanvas(
        IReadOnlyList<PdfDrawOp> operations,
        double pageWidth,
        double pageHeight,
        XpsWriterOptions options,
        List<(string Path, byte[] Bytes)> images,
        ref int imageIndex,
        PdfOpacityGroup opacity)
    {
        var canvas = new XElement(Xps + "Canvas", new XAttribute("Opacity", F(Math.Clamp(opacity.Opacity, 0, 1))));
        foreach (var operation in operations)
            canvas.Add(BuildOperation(operation, pageWidth, pageHeight, options, images, ref imageIndex));
        return canvas;
    }

    private static XElement PathElement(string data, XElement? fill = null, XElement? stroke = null)
    {
        var path = new XElement(Xps + "Path", new XAttribute("Data", data));
        if (fill is not null)
            path.SetAttributeValue("Fill", fill.Attribute("Color")?.Value);
        if (stroke is not null)
        {
            var thickness = stroke.Attribute("data-stroke-thickness")?.Value;
            path.SetAttributeValue("Stroke", stroke.Attribute("Color")?.Value);
            if (thickness is not null)
                path.SetAttributeValue("StrokeThickness", thickness);
        }
        return path;
    }

    private static XElement Fill(PdfColor color) => new(Xps + "SolidColorBrush", new XAttribute("Color", Color(color)));

    private static XElement Stroke(PdfColor color, double width) =>
        new(Xps + "SolidColorBrush", new XAttribute("Color", Color(color)), new XAttribute("data-stroke-thickness", F(width)));

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
        var builder = new StringBuilder();
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

    private static bool IsSupportedImage(PdfImage image) =>
        (image.ContentType.Equals("image/png", StringComparison.OrdinalIgnoreCase) ||
         image.ContentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase)) &&
        image.RotationDegrees == 0 &&
        image.ClipKind == PdfImageClipKind.None &&
        image.Opacity == 1 &&
        !image.SourceCrop.HasCrop &&
        !image.ColorEffects.HasPixelEffects;

    private static XElement BuildContentTypes(PdfContentDocument document, XpsWriterOptions options)
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
        for (var i = 0; i < document.Pages.Count; i++)
            root.Add(new XElement(ContentTypes + "Override", new XAttribute("PartName", $"/Documents/1/Pages/{i + 1}.fpage"), new XAttribute("ContentType", "application/vnd.ms-package.xps-fixedpage+xml")));
        if (options.TextFont is { } font)
            root.Add(new XElement(ContentTypes + "Default", new XAttribute("Extension", Path.GetExtension(font.PackagePath).TrimStart('.')), new XAttribute("ContentType", "application/vnd.ms-opentype")));
        return root;
    }

    private static XElement BuildRootRelationships() => new(
        Rel + "Relationships",
        new XElement(Rel + "Relationship",
            new XAttribute("Id", "rId1"),
            new XAttribute("Type", "http://schemas.microsoft.com/xps/2005/06/fixedrepresentation"),
            new XAttribute("Target", "/FixedDocSeq.fdseq")));

    private static XElement BuildFixedDocumentSequence(int pageCount) => new(
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

    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}

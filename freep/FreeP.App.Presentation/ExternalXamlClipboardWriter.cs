using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Writes the bounded FlowDocument subset used by the shared XamlPackage clipboard parser.
/// The package carries inline image parts when the bounded model fragment exposes them.
/// FreeP-only OLE data and other renderer-owned resources remain in the private clipboard payload.
/// </summary>
internal static class ExternalXamlClipboardWriter
{
    private const string XamlNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string XmlNamespace = "http://www.w3.org/XML/1998/namespace";
    private const string PackageRelationshipsNamespace =
        "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string PackageContentTypesNamespace =
        "http://schemas.openxmlformats.org/package/2006/content-types";
    private const string XamlEntryRelationshipType =
        "http://schemas.microsoft.com/wpf/2005/10/xaml/entry";
    private const string XamlComponentRelationshipType =
        "http://schemas.microsoft.com/wpf/2005/10/xaml/component";
    private const string XamlContentType = "application/vnd.ms-wpf.xaml+xml";
    private const string RelationshipsContentType =
        "application/vnd.openxmlformats-package.relationships+xml";

    public static byte[] Serialize(InCanvasRichClipboardPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        using var output = new MemoryStream();
        var images = new List<PackageImage>();
        using (var package = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var stream = package.CreateEntry("Xaml/Document.xaml", CompressionLevel.Fastest).Open())
            using (var writer = XmlWriter.Create(stream, new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                OmitXmlDeclaration = false,
                Indent = false,
            }))
            {
                writer.WriteStartElement("Section", XamlNamespace);
                writer.WriteAttributeString("xml", "space", XmlNamespace, "preserve");
                WriteBodyBlocks(writer, payload.Body, images);
                writer.WriteEndElement();
            }

            foreach (var image in images)
            {
                using var imageStream = package.CreateEntry(image.Path, CompressionLevel.Fastest).Open();
                imageStream.Write(image.Bytes, 0, image.Bytes.Length);
            }

            WriteTextEntry(package, "_rels/.rels", RootRelationships());
            if (images.Count > 0)
                WriteTextEntry(package, "Xaml/_rels/Document.xaml.rels", ImageRelationships(images));
            WriteTextEntry(package, "[Content_Types].xml", ContentTypes(images));
        }

        return output.ToArray();
    }

    private static void WriteTextEntry(ZipArchive package, string name, string value)
    {
        using var stream = package.CreateEntry(name, CompressionLevel.Fastest).Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(value.Replace("\r\n", "", StringComparison.Ordinal).Replace('\n', ' '));
    }

    private static void WriteBodyBlocks(
        XmlWriter writer,
        TextBody body,
        List<PackageImage> images)
    {
        if (body.Paragraphs.Count == 0)
        {
            WriteParagraph(writer, new Paragraph(), [], images);
            return;
        }

        foreach (var paragraph in body.Paragraphs)
            WriteParagraphBlocks(writer, paragraph, images);
    }

    private static void WriteParagraphBlocks(
        XmlWriter writer,
        Paragraph paragraph,
        List<PackageImage> images)
    {
        var segment = new List<Run>();
        bool emittedBlock = false;
        foreach (var run in paragraph.Runs)
        {
            if (run.InlineTable is not { Table.Rows.Count: > 0 } table)
            {
                segment.Add(run);
                continue;
            }

            if (segment.Count > 0)
            {
                WriteParagraph(writer, paragraph, segment, images);
                segment.Clear();
            }

            WriteTable(writer, table.Table, images);
            emittedBlock = true;
        }

        if (segment.Count > 0 || !emittedBlock)
            WriteParagraph(writer, paragraph, segment, images);
    }

    private static void WriteParagraph(
        XmlWriter writer,
        Paragraph paragraph,
        IReadOnlyList<Run> runs,
        List<PackageImage> images)
    {
        writer.WriteStartElement("Paragraph", XamlNamespace);
        writer.WriteAttributeString("TextAlignment", paragraph.Align switch
        {
            TextAlign.Center => "Center",
            TextAlign.Right => "Right",
            TextAlign.Justify or TextAlign.Distributed => "Justify",
            _ => "Left",
        });
        if (paragraph.RightToLeft is { } rightToLeft)
            writer.WriteAttributeString("FlowDirection", rightToLeft ? "RightToLeft" : "LeftToRight");
        if (paragraph.MarginLeftEmu is { } margin)
            writer.WriteAttributeString("Margin", FormatDip(margin) + ",0,0,0");
        if (paragraph.IndentEmu is { } indent)
            writer.WriteAttributeString("TextIndent", FormatDip(indent));

        foreach (var run in runs)
            WriteRun(writer, run, images);

        writer.WriteEndElement();
    }

    private static void WriteRun(XmlWriter writer, Run run, List<PackageImage> images)
    {
        if (run.Hyperlink?.Url is { Length: > 0 } url)
        {
            writer.WriteStartElement("Hyperlink", XamlNamespace);
            writer.WriteAttributeString("NavigateUri", url);
            if (run.Hyperlink.Tooltip is { Length: > 0 } tooltip)
                writer.WriteAttributeString("ToolTip", tooltip);
            WriteRunCore(writer, run, images);
            writer.WriteEndElement();
            return;
        }

        WriteRunCore(writer, run, images);
    }

    private static void WriteRunCore(XmlWriter writer, Run run, List<PackageImage> images)
    {
        if (run.InlineImage is { Bytes.Length: > 0 } image
            && TryGetImageFormat(image.ContentType, out var extension, out var contentType))
        {
            WriteInlineImage(writer, run, image, extension, contentType, images);
            return;
        }

        writer.WriteStartElement("Run", XamlNamespace);
        writer.WriteAttributeString("xml", "space", XmlNamespace, "preserve");
        if (run.FontFamily is { Length: > 0 })
            writer.WriteAttributeString("FontFamily", run.FontFamily);
        if (run.FontSizePt is > 0 and var fontSize)
            writer.WriteAttributeString("FontSize", ToDip(fontSize).ToString("0.###", CultureInfo.InvariantCulture));
        if (run.Bold)
            writer.WriteAttributeString("FontWeight", "Bold");
        if (run.Italic)
            writer.WriteAttributeString("FontStyle", "Italic");
        var decorations = new List<string>(capacity: 2);
        if (run.Underline)
            decorations.Add("Underline");
        if (run.Strikethrough)
            decorations.Add("Strikethrough");
        if (decorations.Count > 0)
            writer.WriteAttributeString("TextDecorations", string.Join(", ", decorations));
        if (run.Color?.Resolved is { } color)
            writer.WriteAttributeString("Foreground", FormatColor(color));
        if (run.RightToLeft is { } rightToLeft)
            writer.WriteAttributeString("FlowDirection", rightToLeft ? "RightToLeft" : "LeftToRight");
        if (run.BaselineOffset is > 0)
            writer.WriteAttributeString("BaselineAlignment", "Superscript");
        else if (run.BaselineOffset is < 0)
            writer.WriteAttributeString("BaselineAlignment", "Subscript");
        writer.WriteString(run.Text);
        writer.WriteEndElement();
    }

    private static void WriteInlineImage(
        XmlWriter writer,
        Run run,
        ImagePart image,
        string extension,
        string contentType,
        List<PackageImage> images)
    {
        var packageImage = new PackageImage(
            $"Xaml/Image{images.Count + 1}{extension}",
            image.Bytes.ToArray(),
            contentType);
        images.Add(packageImage);

        writer.WriteStartElement("InlineUIContainer", XamlNamespace);
        writer.WriteStartElement("Image", XamlNamespace);
        if (run.InlineImageWidthEmu is > 0 and var width)
            writer.WriteAttributeString("Width", FormatDip(width / 9525.0));
        if (run.InlineImageHeightEmu is > 0 and var height)
            writer.WriteAttributeString("Height", FormatDip(height / 9525.0));
        writer.WriteStartElement("Image.Source", XamlNamespace);
        writer.WriteStartElement("BitmapImage", XamlNamespace);
        writer.WriteAttributeString("UriSource", $"./{Path.GetFileName(packageImage.Path)}");
        writer.WriteAttributeString("CacheOption", "OnLoad");
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteTable(
        XmlWriter writer,
        TableShape table,
        List<PackageImage> images)
    {
        writer.WriteStartElement("Table", XamlNamespace);
        writer.WriteStartElement("TableRowGroup", XamlNamespace);
        foreach (var row in table.Rows)
        {
            writer.WriteStartElement("TableRow", XamlNamespace);
            foreach (var cell in row.Cells)
            {
                if (cell.HMerge || cell.VMerge)
                    continue;

                writer.WriteStartElement("TableCell", XamlNamespace);
                if (cell.GridSpan > 1)
                    writer.WriteAttributeString("ColumnSpan", cell.GridSpan.ToString(CultureInfo.InvariantCulture));
                if (cell.RowSpan > 1)
                    writer.WriteAttributeString("RowSpan", cell.RowSpan.ToString(CultureInfo.InvariantCulture));
                WriteCellStyle(writer, cell);
                var body = cell.TextBody;
                if (body is null || body.Paragraphs.Count == 0)
                    WriteParagraph(writer, new Paragraph(), [], images);
                else
                    WriteBodyBlocks(writer, body, images);
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteCellStyle(XmlWriter writer, TableCell cell)
    {
        if (cell.Fill is ShapeFill.Solid solid)
            writer.WriteAttributeString("Background", FormatColor(solid.Color.Resolved));

        var padding = new[]
        {
            ToDip(cell.InsetLeftPt),
            ToDip(cell.InsetTopPt),
            ToDip(cell.InsetRightPt),
            ToDip(cell.InsetBottomPt),
        };
        if (padding.Any(value => value > 0))
            writer.WriteAttributeString("Padding", string.Join(",", padding.Select(FormatDip)));

        var borders = cell.Borders;
        var visible = new[] { borders?.Left, borders?.Top, borders?.Right, borders?.Bottom }
            .OfType<ShapeOutline.Visible>()
            .FirstOrDefault();
        if (visible is not null)
        {
            writer.WriteAttributeString("BorderBrush", FormatColor(visible.Color.Resolved));
            writer.WriteAttributeString(
                "BorderThickness",
                string.Join(",", new[]
                {
                    ToDip(borders?.Left),
                    ToDip(borders?.Top),
                    ToDip(borders?.Right),
                    ToDip(borders?.Bottom),
                }.Select(FormatDip)));
        }
    }

    private static double ToDip(double? points) => Math.Max(0, (points ?? 0) / 0.75);

    private static double ToDip(ShapeOutline? outline) => outline is ShapeOutline.Visible visible
        ? ToDip(visible.WidthPt)
        : 0;

    private static string FormatDip(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string FormatColor(SrgbColor color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static string RootRelationships() =>
        $"<?xml version=\"1.0\" encoding=\"utf-8\"?><Relationships xmlns=\"{PackageRelationshipsNamespace}\"><Relationship Type=\"{XamlEntryRelationshipType}\" Target=\"/Xaml/Document.xaml\" Id=\"rId1\" /></Relationships>";

    private static string ImageRelationships(IReadOnlyList<PackageImage> images) =>
        $"<?xml version=\"1.0\" encoding=\"utf-8\"?><Relationships xmlns=\"{PackageRelationshipsNamespace}\">{string.Concat(images.Select((image, index) => $"<Relationship Type=\"{XamlComponentRelationshipType}\" Target=\"/{image.Path}\" Id=\"rId{index + 1}\" />"))}</Relationships>";

    private static string ContentTypes(IReadOnlyList<PackageImage> images) =>
        $"<?xml version=\"1.0\" encoding=\"utf-8\"?><Types xmlns=\"{PackageContentTypesNamespace}\"><Default Extension=\"xaml\" ContentType=\"{XamlContentType}\" />{string.Concat(images.GroupBy(image => image.Extension, StringComparer.OrdinalIgnoreCase).Select(group => $"<Default Extension=\"{group.Key}\" ContentType=\"{group.First().ContentType}\" />"))}<Default Extension=\"rels\" ContentType=\"{RelationshipsContentType}\" /></Types>";

    private static bool TryGetImageFormat(
        string? contentType,
        out string extension,
        out string normalizedContentType)
    {
        switch (contentType?.Trim().ToLowerInvariant())
        {
            case "image/png":
                extension = ".png";
                normalizedContentType = "image/png";
                return true;
            case "image/jpeg":
            case "image/jpg":
                extension = ".jpg";
                normalizedContentType = "image/jpeg";
                return true;
            case "image/gif":
                extension = ".gif";
                normalizedContentType = "image/gif";
                return true;
            case "image/bmp":
                extension = ".bmp";
                normalizedContentType = "image/bmp";
                return true;
            case "image/tiff":
                extension = ".tif";
                normalizedContentType = "image/tiff";
                return true;
            default:
                extension = string.Empty;
                normalizedContentType = string.Empty;
                return false;
        }
    }

    private sealed record PackageImage(string Path, byte[] Bytes, string ContentType)
    {
        public string Extension => System.IO.Path.GetExtension(Path).TrimStart('.');
    }
}

using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Writes the bounded FlowDocument subset used by the shared XamlPackage clipboard parser.
/// The package is intentionally resource-free; FreeP-only images, OLE data, and other
/// renderer-owned resources remain in the private clipboard payload.
/// </summary>
internal static class ExternalXamlClipboardWriter
{
    private const string XamlNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string XmlNamespace = "http://www.w3.org/XML/1998/namespace";

    public static byte[] Serialize(InCanvasRichClipboardPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        using var output = new MemoryStream();
        using (var package = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteTextEntry(package, "_rels/.rels", """
                <?xml version="1.0" encoding="utf-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Type="http://schemas.microsoft.com/wpf/2005/10/xaml/entry" Target="/Xaml/Document.xaml" Id="rId1" /></Relationships>
                """);
            WriteTextEntry(package, "[Content_Types].xml", """
                <?xml version="1.0" encoding="utf-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="xaml" ContentType="application/vnd.ms-wpf.xaml+xml" /><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml" /></Types>
                """);
            using var stream = package.CreateEntry("Xaml/Document.xaml", CompressionLevel.Fastest).Open();
            using var writer = XmlWriter.Create(stream, new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                OmitXmlDeclaration = false,
                Indent = false,
            });
            writer.WriteStartElement("Section", XamlNamespace);
            writer.WriteAttributeString("xml", "space", XmlNamespace, "preserve");
            WriteBodyBlocks(writer, payload.Body);
            writer.WriteEndElement();
        }

        return output.ToArray();
    }

    private static void WriteTextEntry(ZipArchive package, string name, string value)
    {
        using var stream = package.CreateEntry(name, CompressionLevel.Fastest).Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(value.Replace("\r\n", "", StringComparison.Ordinal).Replace('\n', ' '));
    }

    private static void WriteBodyBlocks(XmlWriter writer, TextBody body)
    {
        if (body.Paragraphs.Count == 0)
        {
            WriteParagraph(writer, new Paragraph(), []);
            return;
        }

        foreach (var paragraph in body.Paragraphs)
            WriteParagraphBlocks(writer, paragraph);
    }

    private static void WriteParagraphBlocks(XmlWriter writer, Paragraph paragraph)
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
                WriteParagraph(writer, paragraph, segment);
                segment.Clear();
            }

            WriteTable(writer, table.Table);
            emittedBlock = true;
        }

        if (segment.Count > 0 || !emittedBlock)
            WriteParagraph(writer, paragraph, segment);
    }

    private static void WriteParagraph(
        XmlWriter writer,
        Paragraph paragraph,
        IReadOnlyList<Run> runs)
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
            WriteRun(writer, run);

        writer.WriteEndElement();
    }

    private static void WriteRun(XmlWriter writer, Run run)
    {
        if (run.Hyperlink?.Url is { Length: > 0 } url)
        {
            writer.WriteStartElement("Hyperlink", XamlNamespace);
            writer.WriteAttributeString("NavigateUri", url);
            if (run.Hyperlink.Tooltip is { Length: > 0 } tooltip)
                writer.WriteAttributeString("ToolTip", tooltip);
            WriteRunCore(writer, run);
            writer.WriteEndElement();
            return;
        }

        WriteRunCore(writer, run);
    }

    private static void WriteRunCore(XmlWriter writer, Run run)
    {
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
        if (run.Underline)
            writer.WriteAttributeString("TextDecorations", "Underline");
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

    private static void WriteTable(XmlWriter writer, TableShape table)
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
                    WriteParagraph(writer, new Paragraph(), []);
                else
                    WriteBodyBlocks(writer, body);
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
}

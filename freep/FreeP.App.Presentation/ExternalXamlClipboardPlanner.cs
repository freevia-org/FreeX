using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using Free.Shared.AppServices;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Converts the bounded FlowDocument subset commonly carried by WPF XamlPackage clipboard data
/// into the renderer-neutral rich-text payload. Package resources and unsupported controls are
/// deliberately ignored; callers can continue to RTF or plain-text fallback. FlowDocument tables
/// use the same tab-delimited row projection as the external RTF path because TextBody has no
/// inline table node.
/// </summary>
public static class ExternalXamlClipboardPlanner
{
    public const int MaxPackageBytes = 8 * 1024 * 1024;
    public const int MaxXmlBytes = 8 * 1024 * 1024;
    public const int MaxOutputCharacters = 1_000_000;
    public const int MaxTableCellsPerRow = 4096;
    private const long EmuPerDip = 9525;

    public static InCanvasRichClipboardPayload? TryParseXamlPackage(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 } || bytes.Length > MaxPackageBytes)
            return null;

        try
        {
            using var package = new ZipArchive(
                new MemoryStream(bytes, writable: false),
                ZipArchiveMode.Read,
                leaveOpen: false);
            foreach (var entry in package.Entries
                         .Where(entry => entry.FullName.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(entry => entry.FullName.Length))
            {
                if (entry.Length <= 0 || entry.Length > MaxXmlBytes)
                    continue;

                using var stream = entry.Open();
                using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
                var xml = reader.ReadToEnd();
                var payload = TryParseXaml(xml, source => ResolveImage(package, entry, source));
                if (payload is not null)
                    return payload;
            }
        }
        catch
        {
            // Clipboard data is untrusted. A malformed package must not interrupt paste.
        }

        return null;
    }

    internal static InCanvasRichClipboardPayload? TryParseXaml(
        string? xml,
        Func<string, (byte[]? Bytes, string? ContentType)>? resolveImage = null)
    {
        if (string.IsNullOrWhiteSpace(xml) || xml.Length > MaxXmlBytes)
            return null;

        try
        {
            var document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
            var images = document
                .Descendants()
                .Where(element => element.Name.LocalName.Equals("Image", StringComparison.OrdinalIgnoreCase))
                .Select(element => new
                {
                    Element = element,
                    Source = AttributeValue(element, "Source"),
                })
                .Where(static image => !string.IsNullOrWhiteSpace(image.Source))
                .Select(image =>
                {
                    var resolved = resolveImage?.Invoke(image.Source!) ?? (null, null);
                    return (
                        resolved.Bytes,
                        resolved.ContentType,
                        WidthEmu: ReadImageExtentEmu(image.Element, "Width"),
                        HeightEmu: ReadImageExtentEmu(image.Element, "Height"));
                })
                .Where(static result => result.Bytes is { Length: > 0 })
                .Select(static result => new InCanvasRichClipboardImage(
                    result.Bytes!,
                    result.ContentType ?? "application/octet-stream",
                    result.WidthEmu,
                    result.HeightEmu))
                .ToArray();
            var blockElements = document
                .Descendants()
                .Where(element => element.Name.LocalName == "Table"
                    ? !element.Ancestors().Any(ancestor => ancestor.Name.LocalName == "Table")
                    : element.Name.LocalName == "Paragraph"
                        && !element.Ancestors().Any(ancestor => ancestor.Name.LocalName == "Table"))
                .ToArray();
            if (blockElements.Length == 0 && images.Length == 0)
                return null;

            var body = new TextBody();
            var outputCharacters = 0;
            var tableCellStyles = new List<InCanvasRichClipboardTableCellStyle>();
            bool containsTable = blockElements.Any(element => element.Name.LocalName == "Table");
            foreach (var element in blockElements)
            {
                if (element.Name.LocalName == "Table")
                    ReadTable(element, body, tableCellStyles, ref outputCharacters);
                else
                    ReadParagraph(element, body, ref outputCharacters);

                if (outputCharacters > MaxOutputCharacters)
                    return null;
            }

            if (body.Paragraphs.All(static paragraph => paragraph.Runs.Count == 0)
                && images.Length == 0)
                return null;

            var firstImage = images.FirstOrDefault();
            return new InCanvasRichClipboardPayload(
                body,
                InCanvasTextEditPlanner.ExtractPlainText(body),
                ImageBytes: firstImage?.Bytes,
                ImageContentType: firstImage?.ContentType,
                ContainsTable: containsTable,
                TableCellStyles: tableCellStyles.Count == 0 ? null : tableCellStyles,
                ImagePayloads: images);
        }
        catch
        {
            return null;
        }
    }

    private static void ReadParagraph(
        XElement element,
        TextBody body,
        ref int outputCharacters)
    {
        var paragraph = new Paragraph();
        var style = ReadStyle(element, default);
        ApplyParagraphProperties(element, paragraph);
        ApplyListProperties(element, paragraph);
        ReadInlineNodes(element, paragraph, style, ref outputCharacters);
        body.Paragraphs.Add(paragraph);
    }

    private static void ApplyListProperties(XElement paragraphElement, Paragraph paragraph)
    {
        var listItem = paragraphElement.Ancestors()
            .FirstOrDefault(element => element.Name.LocalName == "ListItem");
        var list = listItem?.Ancestors()
            .FirstOrDefault(element => element.Name.LocalName == "List");
        if (listItem is null || list is null
            || listItem.Descendants().FirstOrDefault(element => element.Name.LocalName == "Paragraph")
                != paragraphElement)
        {
            return;
        }

        paragraph.Level = Math.Clamp(
            paragraphElement.Ancestors().Count(element => element.Name.LocalName == "List") - 1,
            0,
            8);

        switch (AttributeValue(list, "MarkerStyle")?.Trim().ToLowerInvariant())
        {
            case "decimal":
                paragraph.BulletKind = BulletKind.Auto;
                paragraph.AutoNumType = AutoNumType.ArabicPeriod;
                break;
            case "lowerlatin":
            case "loweralpha":
                paragraph.BulletKind = BulletKind.Auto;
                paragraph.AutoNumType = AutoNumType.AlphaLcPeriod;
                break;
            case "upperlatin":
            case "upperalpha":
                paragraph.BulletKind = BulletKind.Auto;
                paragraph.AutoNumType = AutoNumType.AlphaUcPeriod;
                break;
            case "lowerroman":
                paragraph.BulletKind = BulletKind.Auto;
                paragraph.AutoNumType = AutoNumType.RomanLcPeriod;
                break;
            case "upperroman":
                paragraph.BulletKind = BulletKind.Auto;
                paragraph.AutoNumType = AutoNumType.RomanUcPeriod;
                break;
            case "none":
                paragraph.BulletKind = BulletKind.None;
                paragraph.BulletSuppressed = true;
                return;
            case "circle":
                paragraph.BulletKind = BulletKind.Char;
                paragraph.BulletChar = "\u25E6";
                break;
            case "square":
            case "box":
                paragraph.BulletKind = BulletKind.Char;
                paragraph.BulletChar = "\u25AA";
                break;
            case "disc":
            case null:
            case "":
                paragraph.BulletKind = BulletKind.Char;
                paragraph.BulletChar = "\u2022";
                break;
            default:
                return;
        }

        var firstListItem = list.Elements()
            .FirstOrDefault(element => element.Name.LocalName == "ListItem");
        if (paragraph.BulletKind == BulletKind.Auto
            && ReferenceEquals(firstListItem, listItem)
            && TryReadInt(AttributeValue(list, "StartIndex"), out var startIndex))
        {
            paragraph.AutoNumStartAt = Math.Clamp(startIndex, 1, 999_999);
            paragraph.AutoNumStartAtSpecified = true;
        }
    }

    private static void ReadTable(
        XElement table,
        TextBody body,
        List<InCanvasRichClipboardTableCellStyle> tableCellStyles,
        ref int outputCharacters)
    {
        var rows = table
            .Descendants()
            .Where(element => element.Name.LocalName == "TableRow"
                && !element.Ancestors().Any(ancestor => ancestor.Name.LocalName == "TableRow"))
            .ToArray();

        foreach (var row in rows)
        {
            var cells = row
                .Descendants()
                .Where(element => element.Name.LocalName == "TableCell"
                    && !element.Ancestors().Any(ancestor =>
                        ancestor.Name.LocalName == "TableCell"))
                .ToArray();
            if (cells.Length == 0)
                continue;
            if (cells.Length > MaxTableCellsPerRow)
                throw new InvalidDataException("XamlPackage table cell limit exceeded.");

            var paragraph = new Paragraph();
            for (var cellIndex = 0; cellIndex < cells.Length; cellIndex++)
            {
                if (cellIndex > 0)
                    AddRun("\t", paragraph, default, ref outputCharacters);

                var cell = cells[cellIndex];
                tableCellStyles.Add(ReadTableCellStyle(cell));
                var cellParagraphs = cell
                    .Descendants()
                    .Where(element => element.Name.LocalName == "Paragraph"
                        && element.Ancestors().FirstOrDefault(ancestor =>
                            ancestor.Name.LocalName == "TableCell") == cell)
                    .ToArray();
                for (var paragraphIndex = 0; paragraphIndex < cellParagraphs.Length; paragraphIndex++)
                {
                    if (paragraphIndex > 0)
                        AddRun("\n", paragraph, default, ref outputCharacters);

                    var cellParagraph = cellParagraphs[paragraphIndex];
                    ReadInlineNodes(
                        cellParagraph,
                        paragraph,
                        ReadStyle(cellParagraph, default),
                        ref outputCharacters);
                }
            }

            body.Paragraphs.Add(paragraph);
        }
    }

    private static InCanvasRichClipboardTableCellStyle ReadTableCellStyle(XElement cell)
    {
        int? fillRgb = TryReadRgb(AttributeValue(cell, "Background"));
        var padding = ReadThickness(AttributeValue(cell, "Padding"));
        var borderThickness = ReadThickness(AttributeValue(cell, "BorderThickness"));
        int? borderRgb = TryReadRgb(AttributeValue(cell, "BorderBrush"));
        InCanvasRichClipboardTableBorder? MakeBorder(double thicknessDip) =>
            borderRgb is { } rgb && thicknessDip > 0
                ? new InCanvasRichClipboardTableBorder(rgb, thicknessDip * 0.75)
                : null;

        var vertical = AttributeValue(cell, "VerticalContentAlignment")
            ?? AttributeValue(cell, "VerticalAlignment");
        TableCellAnchor? anchor = vertical?.ToLowerInvariant() switch
        {
            "center" => TableCellAnchor.Middle,
            "bottom" => TableCellAnchor.Bottom,
            "top" => TableCellAnchor.Top,
            _ => null,
        };

        return new InCanvasRichClipboardTableCellStyle(
            FillRgb: fillRgb,
            Left: MakeBorder(borderThickness.Left),
            Right: MakeBorder(borderThickness.Right),
            Top: MakeBorder(borderThickness.Top),
            Bottom: MakeBorder(borderThickness.Bottom),
            Anchor: anchor,
            InsetLeftPt: padding.Left * 0.75,
            InsetRightPt: padding.Right * 0.75,
            InsetTopPt: padding.Top * 0.75,
            InsetBottomPt: padding.Bottom * 0.75);
    }

    private static void ReadInlineNodes(
        XElement element,
        Paragraph paragraph,
        XamlTextStyle inherited,
        ref int outputCharacters)
    {
        var style = ReadStyle(element, inherited);
        foreach (var node in element.Nodes())
        {
            if (node is XText text)
            {
                AddText(text.Value, paragraph, style, ref outputCharacters);
                continue;
            }

            if (node is not XElement child)
                continue;

            switch (child.Name.LocalName)
            {
                case "LineBreak":
                    AddRun("\n", paragraph, style, ref outputCharacters);
                    break;
                case "Paragraph":
                    // Nested paragraphs belong to a different block and are handled by the
                    // outer document walk; never duplicate their text in the parent.
                    break;
                default:
                    ReadInlineElement(child, paragraph, style, ref outputCharacters);
                    break;
            }
        }
    }

    private static void ReadInlineElement(
        XElement element,
        Paragraph paragraph,
        XamlTextStyle inherited,
        ref int outputCharacters)
    {
        var style = ReadStyle(element, inherited);
        if (element.Name.LocalName == "Run"
            && element.Attribute("Text") is { } textAttribute)
        {
            AddText(textAttribute.Value, paragraph, style, ref outputCharacters);
        }

        if (element.Name.LocalName == "LineBreak")
        {
            AddRun("\n", paragraph, style, ref outputCharacters);
            return;
        }

        foreach (var node in element.Nodes())
        {
            if (node is XText text)
                AddText(text.Value, paragraph, style, ref outputCharacters);
            else if (node is XElement child && child.Name.LocalName != "Paragraph")
                ReadInlineElement(child, paragraph, style, ref outputCharacters);
        }
    }

    private static void AddText(
        string text,
        Paragraph paragraph,
        XamlTextStyle style,
        ref int outputCharacters)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(text))
            return;

        AddRun(text, paragraph, style, ref outputCharacters);
    }

    private static void AddRun(
        string text,
        Paragraph paragraph,
        XamlTextStyle style,
        ref int outputCharacters)
    {
        if (text.Length == 0)
            return;

        outputCharacters = checked(outputCharacters + text.Length);
        paragraph.Runs.Add(new Run
        {
            Text = text,
            FontFamily = style.FontFamily,
            FontSizePt = style.FontSizePt,
            Bold = style.Bold,
            BoldSet = style.BoldSet,
            Italic = style.Italic,
            ItalicSet = style.ItalicSet,
            Underline = style.Underline,
            Strikethrough = style.Strikethrough,
            Color = style.Color,
            Hyperlink = style.Hyperlink,
        });
    }

    private static XamlTextStyle ReadStyle(XElement element, XamlTextStyle inherited)
    {
        var style = inherited;
        var family = AttributeValue(element, "FontFamily");
        if (!string.IsNullOrWhiteSpace(family))
            style = style with { FontFamily = family.Trim() };

        if (TryReadDouble(element, "FontSize", out var dipSize) && dipSize > 0)
            style = style with { FontSizePt = dipSize * 0.75 };

        var weight = AttributeValue(element, "FontWeight");
        if (!string.IsNullOrWhiteSpace(weight))
        {
            var bold = weight.Equals("Bold", StringComparison.OrdinalIgnoreCase)
                || (int.TryParse(weight, NumberStyles.Integer, CultureInfo.InvariantCulture, out var weightValue)
                    && weightValue >= 700);
            style = style with { Bold = bold, BoldSet = true };
        }

        var fontStyle = AttributeValue(element, "FontStyle");
        if (!string.IsNullOrWhiteSpace(fontStyle))
            style = style with
            {
                Italic = fontStyle.Equals("Italic", StringComparison.OrdinalIgnoreCase)
                    || fontStyle.Equals("Oblique", StringComparison.OrdinalIgnoreCase),
                ItalicSet = true,
            };

        var decorations = AttributeValue(element, "TextDecorations");
        if (!string.IsNullOrWhiteSpace(decorations))
            style = style with
            {
                Underline = decorations.Contains("Underline", StringComparison.OrdinalIgnoreCase),
                Strikethrough = decorations.Contains("Strikethrough", StringComparison.OrdinalIgnoreCase),
            };

        var localName = element.Name.LocalName;
        if (localName.Equals("Bold", StringComparison.OrdinalIgnoreCase))
            style = style with { Bold = true, BoldSet = true };
        if (localName.Equals("Italic", StringComparison.OrdinalIgnoreCase))
            style = style with { Italic = true, ItalicSet = true };
        if (localName.Equals("Underline", StringComparison.OrdinalIgnoreCase))
            style = style with { Underline = true };

        var foreground = AttributeValue(element, "Foreground");
        if (TryParseColor(foreground, out var color))
            style = style with { Color = color };

        if (localName.Equals("Hyperlink", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrWhiteSpace(AttributeValue(element, "NavigateUri")))
        {
            var target = AttributeValue(element, "NavigateUri");
            var tooltip = AttributeValue(element, "ToolTip")
                ?? AttributeValue(element, "Tooltip");
            var hyperlink = ExternalUriLauncher.TryCreateAllowedUri(target ?? string.Empty, out var uri)
                ? new Hyperlink
                {
                    Url = uri.AbsoluteUri,
                    Tooltip = string.IsNullOrWhiteSpace(tooltip) ? null : tooltip,
                }
                : null;
            style = style with { Hyperlink = hyperlink };
        }

        return style;
    }

    private static void ApplyParagraphProperties(XElement element, Paragraph paragraph)
    {
        var alignment = AttributeValue(element, "TextAlignment");
        paragraph.Align = alignment?.ToLowerInvariant() switch
        {
            "center" => TextAlign.Center,
            "right" => TextAlign.Right,
            "justify" => TextAlign.Justify,
            _ when string.Equals(alignment, "left", StringComparison.OrdinalIgnoreCase) => TextAlign.Left,
            _ => null,
        };

        var margin = AttributeValue(element, "Margin")?.Split(',', StringSplitOptions.TrimEntries);
        if (margin is { Length: 4 })
        {
            if (TryParseDip(margin[0], out var left))
                paragraph.MarginLeftEmu = checked((long)Math.Round(left * EmuPerDip));
            if (TryParseDip(margin[1], out var top))
                paragraph.SpaceBeforePt = top * 0.75;
            if (TryParseDip(margin[3], out var bottom))
                paragraph.SpaceAfterPt = bottom * 0.75;
        }
    }

    private static string? AttributeValue(XElement element, string name) =>
        element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;

    private static bool TryReadDouble(XElement element, string name, out double value) =>
        TryParseDip(AttributeValue(element, name), out value);

    private static bool TryReadInt(string? value, out int result) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    private static bool TryParseDip(string? value, out double result) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
        && double.IsFinite(result);

    private static long? ReadImageExtentEmu(XElement element, string attributeName)
    {
        if (!TryParseDip(AttributeValue(element, attributeName), out var dip)
            || dip <= 0)
        {
            return null;
        }

        return Math.Clamp(
            (long)Math.Round(dip * EmuPerDip),
            9_525L,
            63_500_000_000L);
    }

    private static bool TryParseColor(string? value, out ThemeAwareColor? color)
    {
        color = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var text = value.Trim();
        if (!text.StartsWith('#'))
            return false;

        var hex = text[1..];
        if (hex.Length == 8
            && byte.TryParse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var alpha)
            && int.TryParse(hex[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            color = new ThemeAwareColor(SrgbColor.FromRgb(rgb), alpha);
            return true;
        }

        if (hex.Length == 6
            && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var opaqueRgb))
        {
            color = new ThemeAwareColor(SrgbColor.FromRgb(opaqueRgb));
            return true;
        }

        return false;
    }

    private static int? TryReadRgb(string? value)
    {
        if (!TryParseColor(value, out var color) || color is null)
            return null;

        var rgb = color.Resolved;
        return (rgb.R << 16) | (rgb.G << 8) | rgb.B;
    }

    private static (double Left, double Top, double Right, double Bottom) ReadThickness(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return default;

        var values = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(item => TryParseDip(item, out var dip) ? Math.Max(0, dip) : 0)
            .ToArray();
        return values.Length switch
        {
            1 => (values[0], values[0], values[0], values[0]),
            2 => (values[0], values[1], values[0], values[1]),
            4 => (values[0], values[1], values[2], values[3]),
            _ => default,
        };
    }

    private static (byte[]? Bytes, string? ContentType) ResolveImage(
        ZipArchive package,
        ZipArchiveEntry xamlEntry,
        string source)
    {
        if (source.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var comma = source.IndexOf(',');
            if (comma > 5
                && source[..comma].Contains(";base64", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    return (Convert.FromBase64String(source[(comma + 1)..]),
                        source[5..comma].Trim().ToLowerInvariant());
                }
                catch (FormatException)
                {
                    return (null, null);
                }
            }

            return (null, null);
        }

        var normalized = source.Trim();
        if (Uri.TryCreate(normalized, UriKind.Absolute, out var absolute))
            normalized = absolute.AbsolutePath;
        var marker = normalized.IndexOf(",,,", StringComparison.Ordinal);
        if (marker >= 0)
            normalized = normalized[(marker + 3)..];
        normalized = normalized.Split('?', '#')[0]
            .Replace('\\', '/')
            .TrimStart('/');
        if (Uri.TryCreate(normalized, UriKind.RelativeOrAbsolute, out var decodedUri))
            normalized = Uri.UnescapeDataString(decodedUri.ToString());

        var xamlDirectory = Path.GetDirectoryName(xamlEntry.FullName)?.Replace('\\', '/');
        var candidates = new List<string> { normalized };
        if (!string.IsNullOrEmpty(xamlDirectory))
            candidates.Add($"{xamlDirectory}/{normalized}".Trim('/'));

        var fileName = Path.GetFileName(normalized);
        if (!string.IsNullOrEmpty(fileName))
            candidates.Add(fileName);

        var entry = package.Entries.FirstOrDefault(candidate =>
            candidates.Any(path => string.Equals(
                candidate.FullName.TrimStart('/'),
                path,
                StringComparison.OrdinalIgnoreCase)));
        if (entry is null || entry.Length <= 0 || entry.Length > MaxXmlBytes)
            return (null, null);

        using var stream = entry.Open();
        using var memory = new MemoryStream((int)entry.Length);
        stream.CopyTo(memory);
        return (memory.ToArray(), ContentTypeFor(entry.FullName));
    }

    private static string ContentTypeFor(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".tif" or ".tiff" => "image/tiff",
            _ => "image/png",
        };

    private readonly record struct XamlTextStyle(
        string? FontFamily,
        double? FontSizePt,
        bool Bold,
        bool Italic,
        bool Underline,
        bool Strikethrough,
        bool BoldSet,
        bool ItalicSet,
        ThemeAwareColor? Color,
        Hyperlink? Hyperlink);
}

using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using Free.Shared.AppServices;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Converts the bounded FlowDocument subset commonly carried by WPF XamlPackage clipboard data
/// into the renderer-neutral rich-text payload. Unsupported package resources and controls are
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
    // Keep XamlPackage script runs on the same compact offset used by the
    // shared editor commands. The source format only exposes the semantic
    // alignment, not a numeric DrawingML percentage.
    private const int XamlScriptBaselineOffset = 10_000;
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

    /// <summary>
    /// Projects the renderer-neutral rich fragment to the bounded WPF XamlPackage clipboard
    /// format. FreeP-only resources remain available through the private clipboard payload.
    /// </summary>
    public static byte[] SerializeXamlPackage(InCanvasRichClipboardPayload payload) =>
        ExternalXamlClipboardWriter.Serialize(payload);

    internal static InCanvasRichClipboardPayload? TryParseXaml(
        string? xml,
        Func<string, (byte[]? Bytes, string? ContentType)>? resolveImage = null)
    {
        if (string.IsNullOrWhiteSpace(xml) || xml.Length > MaxXmlBytes)
            return null;

        try
        {
            var document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
            var resources = ReadResources(document);
            var images = document
                .Descendants()
                .Where(element => element.Name.LocalName.Equals("Image", StringComparison.OrdinalIgnoreCase))
                .Select(element => new
                {
                    Element = element,
                    Source = ImageSourceValue(element),
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
                    ? !element.Ancestors().Any(ancestor =>
                        ancestor.Name.LocalName is "Table" or "Paragraph")
                    : element.Name.LocalName == "Paragraph"
                        && !element.Ancestors().Any(ancestor =>
                            ancestor.Name.LocalName is "Table" or "InlineUIContainer"))
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
                    ReadTable(element, body, tableCellStyles, resources, resolveImage, ref outputCharacters);
                else
                    ReadParagraph(element, body, resources, resolveImage, ref outputCharacters);

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
        XamlResourceCatalog resources,
        Func<string, (byte[]? Bytes, string? ContentType)>? resolveImage,
        ref int outputCharacters)
    {
        var paragraph = new Paragraph();
        var inherited = ReadInheritedStyle(element, resources);
        var style = ReadStyle(element, inherited, resources);
        ApplyParagraphProperties(element, paragraph, style);
        paragraph.RightToLeft = style.RightToLeft;
        ApplyListProperties(element, paragraph);
        ReadInlineNodes(element, paragraph, inherited, resources, resolveImage, ref outputCharacters);
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
        XamlResourceCatalog resources,
        Func<string, (byte[]? Bytes, string? ContentType)>? resolveImage,
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
                    var inherited = ReadInheritedStyle(cellParagraph, resources);
                    ReadInlineNodes(
                        cellParagraph,
                        paragraph,
                        inherited,
                        resources,
                        resolveImage,
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
        XamlResourceCatalog resources,
        Func<string, (byte[]? Bytes, string? ContentType)>? resolveImage,
        ref int outputCharacters)
    {
        var style = ReadStyle(element, inherited, resources);
        foreach (var node in element.Nodes())
        {
            if (node is XText text)
            {
                AddText(
                    text.Value,
                    paragraph,
                    style,
                    ref outputCharacters,
                    preserveWhitespace: ShouldPreserveWhitespace(element));
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
                    ReadInlineElement(child, paragraph, style, resources, resolveImage, ref outputCharacters);
                    break;
            }
        }
    }

    private static void ReadInlineElement(
        XElement element,
        Paragraph paragraph,
        XamlTextStyle inherited,
        XamlResourceCatalog resources,
        Func<string, (byte[]? Bytes, string? ContentType)>? resolveImage,
        ref int outputCharacters)
    {
        var style = ReadStyle(element, inherited, resources);
        if (element.Name.LocalName.Equals("Table", StringComparison.OrdinalIgnoreCase))
        {
            var table = ReadInlineTable(element, resources, resolveImage, ref outputCharacters);
            if (table.Table.Rows.Count > 0)
            {
                paragraph.Runs.Add(new Run
                {
                    Text = "\uFFFC",
                    InlineTable = table,
                    FontFamily = style.FontFamily,
                    FontSizePt = style.FontSizePt,
                    Bold = style.Bold == true,
                    Italic = style.Italic == true,
                    Underline = style.Underline == true,
                    Color = style.Color,
                });
                outputCharacters++;
            }
            return;
        }
        if (element.Name.LocalName.Equals("Image", StringComparison.OrdinalIgnoreCase))
        {
            var source = ImageSourceValue(element);
            var resolved = !string.IsNullOrWhiteSpace(source)
                ? resolveImage?.Invoke(source!) ?? (null, null)
                : (null, null);
            if (resolved.Bytes is { Length: > 0 })
            {
                paragraph.Runs.Add(new Run
                {
                    Text = "\uFFFC",
                    InlineImage = new ImagePart
                    {
                        Bytes = resolved.Bytes,
                        ContentType = resolved.ContentType ?? "application/octet-stream",
                    },
                    InlineImageWidthEmu = ReadImageExtentEmu(element, "Width"),
                    InlineImageHeightEmu = ReadImageExtentEmu(element, "Height"),
                    FontFamily = style.FontFamily,
                    FontSizePt = style.FontSizePt,
                    Bold = style.Bold == true,
                    Italic = style.Italic == true,
                    Underline = style.Underline == true,
                    Color = style.Color,
                });
                outputCharacters++;
            }
            return;
        }
        if (element.Name.LocalName == "Run"
            && element.Attribute("Text") is { } textAttribute)
        {
            AddText(
                textAttribute.Value,
                paragraph,
                style,
                ref outputCharacters,
                preserveWhitespace: true);
        }

        if (element.Name.LocalName == "LineBreak")
        {
            AddRun("\n", paragraph, style, ref outputCharacters);
            return;
        }

        foreach (var node in element.Nodes())
        {
            if (node is XText text)
                AddText(
                    text.Value,
                    paragraph,
                    style,
                    ref outputCharacters,
                    preserveWhitespace: ShouldPreserveWhitespace(element));
            else if (node is XElement child && child.Name.LocalName != "Paragraph")
                ReadInlineElement(child, paragraph, style, resources, resolveImage, ref outputCharacters);
        }
    }

    private static InlineTableInfo ReadInlineTable(
        XElement table,
        XamlResourceCatalog resources,
        Func<string, (byte[]? Bytes, string? ContentType)>? resolveImage,
        ref int outputCharacters)
    {
        var result = new InlineTableInfo();
        var rows = table
            .Descendants()
            .Where(element => element.Name.LocalName == "TableRow"
                && element.Ancestors().FirstOrDefault(ancestor =>
                    ancestor.Name.LocalName == "Table") == table)
            .ToArray();

        int maximumColumns = 0;
        foreach (var rowElement in rows)
        {
            var row = new TableRow
            {
                HeightEmu = ReadImageExtentEmu(rowElement, "Height") ?? 0,
            };
            var cells = rowElement
                .Descendants()
                .Where(element => element.Name.LocalName == "TableCell"
                    && element.Ancestors().FirstOrDefault(ancestor =>
                        ancestor.Name.LocalName == "TableRow") == rowElement)
                .ToArray();
            if (cells.Length == 0)
                continue;
            if (cells.Length > MaxTableCellsPerRow)
                throw new InvalidDataException("XamlPackage inline table row limit exceeded.");

            foreach (var cellElement in cells)
            {
                var cellBody = new TextBody();
                var cellParagraphs = cellElement
                    .Descendants()
                    .Where(element => element.Name.LocalName == "Paragraph"
                        && element.Ancestors().FirstOrDefault(ancestor =>
                            ancestor.Name.LocalName == "TableCell") == cellElement)
                    .ToArray();
                foreach (var paragraphElement in cellParagraphs)
                {
                    var paragraph = new Paragraph();
                    var inherited = ReadInheritedStyle(paragraphElement, resources);
                    var paragraphStyle = ReadStyle(paragraphElement, inherited, resources);
                    ApplyParagraphProperties(paragraphElement, paragraph, paragraphStyle);
                    paragraph.RightToLeft = paragraphStyle.RightToLeft;
                    ReadInlineNodes(
                        paragraphElement,
                        paragraph,
                        inherited,
                        resources,
                        resolveImage,
                        ref outputCharacters);
                    if (paragraph.Runs.Count == 0)
                        paragraph.Runs.Add(new Run());
                    cellBody.Paragraphs.Add(paragraph);
                }
                if (cellBody.Paragraphs.Count == 0)
                    cellBody.Paragraphs.Add(new Paragraph { Runs = { new Run() } });

                var cell = new TableCell
                {
                    TextBody = cellBody,
                    GridSpan = Math.Max(1, ReadNullableInt(AttributeValue(cellElement, "ColumnSpan")) ?? 1),
                    RowSpan = Math.Max(1, ReadNullableInt(AttributeValue(cellElement, "RowSpan")) ?? 1),
                };
                ApplyInlineTableCellStyle(cell, ReadTableCellStyle(cellElement));
                row.Cells.Add(cell);
            }

            maximumColumns = Math.Max(maximumColumns, row.Cells.Sum(cell => cell.GridSpan));
            result.Table.Rows.Add(row);
        }

        if (maximumColumns > 0)
        {
            for (int index = 0; index < maximumColumns; index++)
                result.Table.ColumnWidthsEmu.Add(914400);
        }

        return result;
    }

    private static void ApplyInlineTableCellStyle(
        TableCell cell,
        InCanvasRichClipboardTableCellStyle style)
    {
        if (style.FillRgb is { } fill)
            cell.Fill = new ShapeFill.Solid(SrgbColor.FromRgb(fill));
        cell.Anchor = style.Anchor;
        cell.InsetLeftPt = style.InsetLeftPt;
        cell.InsetRightPt = style.InsetRightPt;
        cell.InsetTopPt = style.InsetTopPt;
        cell.InsetBottomPt = style.InsetBottomPt;
        if (style.Left is not null || style.Right is not null
            || style.Top is not null || style.Bottom is not null)
        {
            cell.Borders = new TableCellBorders
            {
                Left = ToInlineTableOutline(style.Left),
                Right = ToInlineTableOutline(style.Right),
                Top = ToInlineTableOutline(style.Top),
                Bottom = ToInlineTableOutline(style.Bottom),
            };
        }
    }

    private static ShapeOutline? ToInlineTableOutline(InCanvasRichClipboardTableBorder? border) =>
        border is null
            ? null
            : border.IsNone
                ? ShapeOutline.None.Instance
                : new ShapeOutline.Visible(
                    SrgbColor.FromRgb(border.ColorRgb),
                    border.WidthPt <= 0 ? 0.75 : border.WidthPt);

    private static bool ShouldPreserveWhitespace(XElement element)
    {
        if (element.AncestorsAndSelf().Any(ancestor =>
                string.Equals(
                    (string?)ancestor.Attribute(XNamespace.Xml + "space"),
                    "preserve",
                    StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // Pretty-printed Paragraph/FlowDocument whitespace is structural indentation.
        // Inline leaf content, however, can legitimately contain authored spaces.
        return !element.Elements().Any()
            && element.Name.LocalName is "Run" or "Span" or "Bold" or "Italic" or "Underline" or "Hyperlink";
    }

    private static void AddText(
        string text,
        Paragraph paragraph,
        XamlTextStyle style,
        ref int outputCharacters,
        bool preserveWhitespace = false)
    {
        if (string.IsNullOrEmpty(text)
            || (!preserveWhitespace && string.IsNullOrWhiteSpace(text)))
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
            BaselineOffset = style.BaselineOffset,
            RightToLeft = style.RightToLeft,
            Color = style.Color,
            TextFill = style.TextFillColor is { } textFill
                ? new ShapeFill.Solid(textFill)
                : null,
            Hyperlink = style.Hyperlink,
        });
    }

    private static XamlTextStyle ReadStyle(
        XElement element,
        XamlTextStyle inherited,
        XamlResourceCatalog resources)
    {
        var style = inherited;
        var styleReference = AttributeValue(element, "Style");
        if (TryReadResourceKey(styleReference, out var styleKey)
            && resources.Styles.TryGetValue(styleKey, out var resourceStyle))
        {
            style = ApplyStyleResource(
                style,
                styleKey,
                resources,
                new HashSet<string>(StringComparer.Ordinal));
        }

        var family = ResolveTextResource(
            AttributeValue(element, "FontFamily"),
            resources.FontFamilies);
        if (!string.IsNullOrWhiteSpace(family))
            style = style with { FontFamily = family.Trim() };

        var fontSize = ResolveTextResource(
            AttributeValue(element, "FontSize"),
            resources.FontSizes);
        if (TryParseDip(fontSize, out var dipSize) && dipSize > 0)
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

        if (TryReadBaselineOffset(AttributeValue(element, "BaselineAlignment"), out var baselineOffset))
            style = style with { BaselineOffset = baselineOffset };

        if (TryReadFlowDirection(AttributeValue(element, "FlowDirection"), out var rightToLeft))
            style = style with { RightToLeft = rightToLeft };

        if (TryReadTextAlignment(AttributeValue(element, "TextAlignment"), out var alignment))
            style = style with { ParagraphAlignment = alignment };

        var localName = element.Name.LocalName;
        if (localName.Equals("Bold", StringComparison.OrdinalIgnoreCase))
            style = style with { Bold = true, BoldSet = true };
        if (localName.Equals("Italic", StringComparison.OrdinalIgnoreCase))
            style = style with { Italic = true, ItalicSet = true };
        if (localName.Equals("Underline", StringComparison.OrdinalIgnoreCase))
            style = style with { Underline = true };

        var foreground = ResolveColorResource(AttributeValue(element, "Foreground"), resources.Colors);
        if (TryParseColor(foreground, out var color))
            style = style with { Color = color };

        var background = ResolveColorResource(AttributeValue(element, "Background"), resources.Colors);
        if (TryParseColor(background, out var textFillColor))
            style = style with { TextFillColor = textFillColor };

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

    private static XamlTextStyle ApplyStyleResource(
        XamlTextStyle style,
        string key,
        XamlResourceCatalog resources,
        HashSet<string> visited)
    {
        if (!visited.Add(key) || !resources.Styles.TryGetValue(key, out var resourceStyle))
            return style;

        if (TryReadResourceKey(resourceStyle.BasedOn, out var basedOnKey))
        {
            style = ApplyStyleResource(style, basedOnKey, resources, visited);
        }

        foreach (var setter in resourceStyle.Setters)
            style = ApplyStyleSetter(style, setter.Key, setter.Value, resources);

        return style;
    }

    private static XamlTextStyle ApplyStyleSetter(
        XamlTextStyle style,
        string property,
        string? value,
        XamlResourceCatalog resources)
    {
        var propertyName = property.Contains('.')
            ? property[(property.LastIndexOf('.') + 1)..]
            : property;
        switch (propertyName.ToLowerInvariant())
        {
            case "fontfamily":
                var family = ResolveTextResource(value, resources.FontFamilies);
                return string.IsNullOrWhiteSpace(family)
                    ? style
                    : style with { FontFamily = family.Trim() };

            case "fontsize":
                var fontSize = ResolveTextResource(value, resources.FontSizes);
                return TryParseDip(fontSize, out var dipSize) && dipSize > 0
                    ? style with { FontSizePt = dipSize * 0.75 }
                    : style;

            case "fontweight":
                var bold = !string.IsNullOrWhiteSpace(value)
                    && (value.Equals("Bold", StringComparison.OrdinalIgnoreCase)
                        || (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var weight)
                            && weight >= 700));
                return style with { Bold = bold, BoldSet = true };

            case "fontstyle":
                var italic = value?.Equals("Italic", StringComparison.OrdinalIgnoreCase) == true
                    || value?.Equals("Oblique", StringComparison.OrdinalIgnoreCase) == true;
                return style with { Italic = italic, ItalicSet = true };

            case "textdecorations":
                var decorations = value ?? string.Empty;
                return style with
                {
                    Underline = decorations.Contains("Underline", StringComparison.OrdinalIgnoreCase),
                    Strikethrough = decorations.Contains("Strikethrough", StringComparison.OrdinalIgnoreCase),
                };

            case "baselinealignment":
                return TryReadBaselineOffset(value, out var baselineOffset)
                    ? style with { BaselineOffset = baselineOffset }
                    : style;

            case "flowdirection":
                return TryReadFlowDirection(value, out var rightToLeft)
                    ? style with { RightToLeft = rightToLeft }
                    : style;

            case "textalignment":
                return TryReadTextAlignment(value, out var alignment)
                    ? style with { ParagraphAlignment = alignment }
                    : style;

            case "foreground":
                var foreground = ResolveColorResource(value, resources.Colors);
                return TryParseColor(foreground, out var color)
                    ? style with { Color = color }
                    : style;

            case "background":
                var background = ResolveColorResource(value, resources.Colors);
                return TryParseColor(background, out var textFillColor)
                    ? style with { TextFillColor = textFillColor }
                    : style;

            default:
                return style;
        }
    }

    private static XamlResourceCatalog ReadResources(XDocument document)
    {
        var colors = new Dictionary<string, string>(StringComparer.Ordinal);
        var fontFamilies = new Dictionary<string, string>(StringComparer.Ordinal);
        var fontSizes = new Dictionary<string, string>(StringComparer.Ordinal);
        var styles = new Dictionary<string, XamlStyleResource>(StringComparer.Ordinal);
        foreach (var resource in document.Descendants())
        {
            var key = AttributeValue(resource, "Key");
            if (string.IsNullOrWhiteSpace(key))
                continue;

            if (resource.Name.LocalName.Equals("SolidColorBrush", StringComparison.OrdinalIgnoreCase)
                && TryParseColor(AttributeValue(resource, "Color"), out _))
            {
                colors[key] = AttributeValue(resource, "Color")!;
            }
            else if (resource.Name.LocalName.Equals("FontFamily", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(resource.Value))
            {
                fontFamilies[key] = resource.Value.Trim();
            }
            else if (resource.Name.LocalName is "Double" or "Single" or "Decimal" or "Int32"
                && TryParseDip(resource.Value, out _))
            {
                fontSizes[key] = resource.Value.Trim();
            }
            else if (resource.Name.LocalName.Equals("Style", StringComparison.OrdinalIgnoreCase))
            {
                var setters = resource
                    .Elements()
                    .Where(element => element.Name.LocalName.Equals("Setter", StringComparison.OrdinalIgnoreCase))
                    .Select(element =>
                    {
                        var property = AttributeValue(element, "Property");
                        return (Property: property, Value: AttributeValue(element, "Value"));
                    })
                    .Where(setter => !string.IsNullOrWhiteSpace(setter.Property))
                    .ToDictionary(
                        setter => setter.Property!,
                        setter => setter.Value,
                        StringComparer.OrdinalIgnoreCase);
                styles[key] = new XamlStyleResource(
                    AttributeValue(resource, "BasedOn"),
                    setters);
            }
        }

        return new XamlResourceCatalog(colors, fontFamilies, fontSizes, styles);
    }

    private static bool TryReadResourceKey(string? value, out string key)
    {
        key = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var reference = value.Trim();
        if (reference.Length < 3 || reference[0] != '{' || reference[^1] != '}')
            return false;

        var parts = reference[1..^1]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || (!parts[0].Equals("StaticResource", StringComparison.OrdinalIgnoreCase)
                && !parts[0].Equals("DynamicResource", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        key = parts[1];
        return key.Length > 0;
    }

    private static string? ResolveTextResource(
        string? value,
        IReadOnlyDictionary<string, string> resources)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        var reference = value.Trim();
        if (reference.Length < 3 || reference[0] != '{' || reference[^1] != '}')
            return value;

        var parts = reference[1..^1]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || (!parts[0].Equals("StaticResource", StringComparison.OrdinalIgnoreCase)
                && !parts[0].Equals("DynamicResource", StringComparison.OrdinalIgnoreCase)))
        {
            return value;
        }

        return resources.TryGetValue(parts[1], out var resolved) ? resolved : value;
    }

    private static string? ResolveColorResource(
        string? value,
        IReadOnlyDictionary<string, string> colorResources)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        var reference = value.Trim();
        if (reference.Length < 3
            || reference[0] != '{'
            || reference[^1] != '}')
        {
            return value;
        }

        var parts = reference[1..^1]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || (!parts[0].Equals("StaticResource", StringComparison.OrdinalIgnoreCase)
                && !parts[0].Equals("DynamicResource", StringComparison.OrdinalIgnoreCase)))
        {
            return value;
        }

        return colorResources.TryGetValue(parts[1], out var color)
            ? color
            : value;
    }

    private static void ApplyParagraphProperties(
        XElement element,
        Paragraph paragraph,
        XamlTextStyle style)
    {
        paragraph.Align = style.ParagraphAlignment;

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

    private static int? ReadNullableInt(string? value) =>
        TryReadInt(value, out var result) ? result : null;

    private static bool TryParseDip(string? value, out double result) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
        && double.IsFinite(result);

    private static bool TryReadBaselineOffset(string? value, out int? baselineOffset)
    {
        baselineOffset = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        switch (value.Trim().ToLowerInvariant())
        {
            case "superscript":
                baselineOffset = XamlScriptBaselineOffset;
                return true;
            case "subscript":
                baselineOffset = -XamlScriptBaselineOffset;
                return true;
            case "baseline":
            case "normal":
                return true;
            default:
                return false;
        }
    }

    private static bool TryReadFlowDirection(string? value, out bool rightToLeft)
    {
        rightToLeft = false;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        switch (value.Trim().ToLowerInvariant())
        {
            case "righttoleft":
            case "rtl":
                rightToLeft = true;
                return true;
            case "lefttoright":
            case "ltr":
                return true;
            default:
                return false;
        }
    }

    private static bool TryReadTextAlignment(string? value, out TextAlign alignment)
    {
        alignment = TextAlign.Left;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        switch (value.Trim().ToLowerInvariant())
        {
            case "left":
                alignment = TextAlign.Left;
                return true;
            case "center":
                alignment = TextAlign.Center;
                return true;
            case "right":
                alignment = TextAlign.Right;
                return true;
            case "justify":
                alignment = TextAlign.Justify;
                return true;
            case "distributed":
                alignment = TextAlign.Distributed;
                return true;
            default:
                return false;
        }
    }

    private static XamlTextStyle ReadInheritedStyle(
        XElement element,
        XamlResourceCatalog resources)
    {
        var style = default(XamlTextStyle);
        foreach (var ancestor in element.Ancestors().Reverse())
            style = ReadStyle(ancestor, style, resources);
        return style;
    }

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
        while (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];
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

    private static string? ImageSourceValue(XElement element) =>
        AttributeValue(element, "Source")
        ?? element.Elements()
            .FirstOrDefault(child => child.Name.LocalName.Equals("Image.Source", StringComparison.OrdinalIgnoreCase))
            ?.DescendantsAndSelf()
            .Select(child => AttributeValue(child, "UriSource"))
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));

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
        ThemeAwareColor? TextFillColor,
        Hyperlink? Hyperlink,
        int? BaselineOffset = null,
        bool? RightToLeft = null,
        TextAlign? ParagraphAlignment = null);

    private sealed record XamlStyleResource(
        string? BasedOn,
        IReadOnlyDictionary<string, string?> Setters);

    private sealed record XamlResourceCatalog(
        IReadOnlyDictionary<string, string> Colors,
        IReadOnlyDictionary<string, string> FontFamilies,
        IReadOnlyDictionary<string, string> FontSizes,
        IReadOnlyDictionary<string, XamlStyleResource> Styles);
}

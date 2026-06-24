using System.Globalization;
using System.Net;
using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using FreeW.Core.Model;

namespace FreeW.Core.IO;

/// <summary>
/// Selects which HTML save flavour <see cref="HtmlFileAdapter"/> produces.
/// <para><see cref="Filtered"/> (the default) emits clean, minimal HTML5 — semantic elements, a small
/// inline style block, no Office-specific cruft. Suitable for general web use.</para>
/// <para><see cref="Full"/> adds Office round-trip scaffolding: namespace declarations on
/// <c>&lt;html&gt;</c>, a <c>Generator=FreeW</c> meta tag, and a richer CSS block that maps each
/// paragraph's <c>StyleId</c> to a class name carrying an <c>mso-style-name</c> annotation so that
/// re-opening the file can recover the heading/style identity.</para>
/// </summary>
public enum HtmlSaveMode
{
    /// <summary>Clean, minimal HTML5 — no Office-specific markup. This is the default.</summary>
    Filtered,

    /// <summary>HTML with Office round-trip scaffolding (namespace attrs, Generator meta, mso-style classes).</summary>
    Full,
}

public sealed class HtmlFileAdapter : IDocumentFileAdapter
{
    private readonly HtmlSaveMode _saveMode;

    /// <summary>Creates an adapter with <see cref="HtmlSaveMode.Filtered"/> (clean HTML5). This is the default.</summary>
    public HtmlFileAdapter() : this(HtmlSaveMode.Filtered) { }

    private HtmlFileAdapter(HtmlSaveMode saveMode) => _saveMode = saveMode;

    /// <summary>Returns an adapter that saves the "Web Page, Filtered" variant — clean, minimal HTML5.</summary>
    public static HtmlFileAdapter Filtered() => new(HtmlSaveMode.Filtered);

    /// <summary>
    /// Returns an adapter that saves the "Web Page" (full) variant — HTML with Office round-trip scaffolding:
    /// namespace declarations on <c>&lt;html&gt;</c>, a <c>Generator=FreeW</c> meta, and CSS classes for
    /// paragraph StyleIds carrying <c>mso-style-name</c> so that re-opening recovers the style identity.
    /// </summary>
    public static HtmlFileAdapter WebPage() => new(HtmlSaveMode.Full);

    public string Extension => ".html";
    public string FormatName => "HTML document";

    public IReadOnlyList<FileFormatDescriptor> Formats { get; } =
    [
        new(".html", "HTML document"),
        new(".htm", "HTML document"),
    ];

    public TextDocument Load(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
        return LoadHtml(reader.ReadToEnd(), static _ => null);
    }

    public void Save(TextDocument document, Stream stream)
    {
        var result = WriteHtml(document, HtmlImageMode.DataUri, _saveMode);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 4096, leaveOpen: true);
        writer.Write(result.Html);
    }

    internal static TextDocument LoadHtml(string html, Func<string, InlineImage?> imageResolver)
    {
        var parser = new HtmlParser();
        var htmlDocument = parser.ParseDocument(html);
        var document = new TextDocument();
        document.Blocks.Clear();

        // Build a map from CSS class name → StyleId for Full (Office) round-trip recovery.
        var msoStyleMap = BuildMsoStyleMap(htmlDocument);

        var body = htmlDocument.Body;
        if (body is null)
            return document;

        foreach (var block in ReadBlocks(body.ChildNodes, imageResolver, msoStyleMap))
            document.Blocks.Add(block);

        if (document.Blocks.Count == 0 && !string.IsNullOrWhiteSpace(body.TextContent))
            document.Blocks.Add(new Paragraph(NormalizeText(body.TextContent)));

        return document;
    }

    /// <summary>
    /// Parses the document's embedded &lt;style&gt; block(s) and builds a map from CSS class name to
    /// the StyleId value recovered from the <c>mso-style-name</c> annotation.  Returns an empty dictionary
    /// when no such annotations are present (Filtered output, external HTML, etc.).
    /// </summary>
    private static Dictionary<string, string> BuildMsoStyleMap(AngleSharp.Html.Dom.IHtmlDocument htmlDocument)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var styleEl in htmlDocument.QuerySelectorAll("style"))
        {
            var text = styleEl.TextContent;
            if (string.IsNullOrWhiteSpace(text))
                continue;

            // Find every CSS rule block: .ClassName { ... mso-style-name: value; ... }
            var pos = 0;
            while (pos < text.Length)
            {
                var dot = text.IndexOf('.', pos);
                if (dot < 0)
                    break;

                var brace = text.IndexOf('{', dot);
                if (brace < 0)
                    break;

                var closeBrace = text.IndexOf('}', brace);
                if (closeBrace < 0)
                    break;

                var selector = text[dot..brace].Trim();
                var declarations = text[(brace + 1)..closeBrace];

                // Only handle simple single-class selectors (.ClassName)
                if (selector.StartsWith('.') && !selector.Contains(' ') && !selector.Contains(','))
                {
                    var className = selector[1..];
                    var decl = HtmlCssFormatting.ParseDeclarations(declarations);
                    if (decl.TryGetValue("mso-style-name", out var styleName) && styleName.Length > 0)
                        map[className] = styleName;
                }

                pos = closeBrace + 1;
            }
        }

        return map;
    }

    internal static HtmlWriteResult WriteHtml(TextDocument document, HtmlImageMode imageMode, HtmlSaveMode saveMode = HtmlSaveMode.Filtered)
    {
        var images = new List<HtmlEmbeddedImage>();
        var body = new StringBuilder();
        WriteBlocks(body, document.Blocks, imageMode, images, saveMode);

        string html;
        if (saveMode == HtmlSaveMode.Full)
        {
            // Collect the distinct StyleIds that need CSS class definitions.
            var styleIds = CollectStyleIds(document.Blocks);
            var styleBlock = BuildFullStyleBlock(styleIds);
            html = "<!doctype html>\n"
                + "<html xmlns=\"http://www.w3.org/TR/REC-html40\" xmlns:o=\"urn:schemas-microsoft-com:office:office\" xmlns:w=\"urn:schemas-microsoft-com:office:word\">\n"
                + "<head>\n"
                + "<meta charset=\"utf-8\">\n"
                + "<meta name=\"Generator\" content=\"FreeW\">\n"
                + "<style>\n"
                + styleBlock
                + "body { font-family: Calibri, sans-serif; font-size: 11pt; }\n"
                + "table { border-collapse: collapse; }\n"
                + "td, th { border: 1px solid #777; padding: 3pt 5pt; vertical-align: top; }\n"
                + "</style>\n"
                + "</head>\n"
                + "<body>\n"
                + body
                + "</body>\n"
                + "</html>\n";
        }
        else
        {
            html = """
<!doctype html>
<html>
<head>
<meta charset="utf-8">
<style>
body { font-family: Calibri, sans-serif; font-size: 11pt; }
table { border-collapse: collapse; }
td, th { border: 1px solid #777; padding: 3pt 5pt; vertical-align: top; }
</style>
</head>
<body>
""" + body + """
</body>
</html>
""";
        }

        return new HtmlWriteResult(html, images);
    }

    /// <summary>Collects distinct StyleIds from all paragraphs in the block list (recursively).</summary>
    private static IReadOnlyList<string> CollectStyleIds(IEnumerable<Block> blocks)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        CollectStyleIdsInto(blocks, seen);
        return [.. seen];
    }

    private static void CollectStyleIdsInto(IEnumerable<Block> blocks, HashSet<string> seen)
    {
        foreach (var block in blocks)
        {
            if (block is Paragraph p && p.StyleId is { Length: > 0 } styleId)
                seen.Add(styleId);
            else if (block is Table t)
                foreach (var row in t.Rows)
                    foreach (var cell in row.Cells)
                        CollectStyleIdsInto(cell.Paragraphs, seen);
        }
    }

    /// <summary>
    /// Builds the CSS class definitions for each StyleId, including an <c>mso-style-name</c> annotation
    /// that the reader uses to recover the StyleId on re-open.
    /// </summary>
    private static string BuildFullStyleBlock(IReadOnlyList<string> styleIds)
    {
        if (styleIds.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var styleId in styleIds)
        {
            var className = StyleIdToClassName(styleId);
            sb.Append('.').Append(className).AppendLine(" {");
            sb.Append("  mso-style-name: ").Append(styleId).AppendLine(";");
            // Emit font-weight for known heading styles.
            if (styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase))
                sb.AppendLine("  font-weight: bold;");
            sb.AppendLine("}");
        }

        return sb.ToString();
    }

    /// <summary>Converts a StyleId string to a safe CSS class name (e.g. "Heading1" → "FreeW-Heading1").</summary>
    private static string StyleIdToClassName(string styleId) => "FreeW-" + styleId;

    private static IEnumerable<Block> ReadBlocks(IEnumerable<INode> nodes, Func<string, InlineImage?> imageResolver, IReadOnlyDictionary<string, string> msoStyleMap)
    {
        foreach (var node in nodes)
        {
            if (node is not IElement element)
            {
                if (!string.IsNullOrWhiteSpace(node.TextContent))
                    yield return new Paragraph(NormalizeText(node.TextContent));
                continue;
            }

            switch (element.LocalName.ToLowerInvariant())
            {
                case "p":
                    // For Full (Office) output a <p> may carry a FreeW-* class that encodes a StyleId.
                    yield return ReadParagraphWithClassStyle(element, ParagraphFormatting.Default, imageResolver, msoStyleMap);
                    break;
                case "h1":
                case "h2":
                case "h3":
                case "h4":
                case "h5":
                case "h6":
                    yield return ReadHeading(element, imageResolver, msoStyleMap);
                    break;
                case "ul":
                case "ol":
                    foreach (var item in ReadList(element, imageResolver, msoStyleMap))
                        yield return item;
                    break;
                case "table":
                    yield return ReadTable(element, imageResolver, msoStyleMap);
                    break;
                case "div":
                case "section":
                case "article":
                case "main":
                    foreach (var nested in ReadBlocks(element.ChildNodes, imageResolver, msoStyleMap))
                        yield return nested;
                    break;
                case "br":
                    yield return new Paragraph();
                    break;
                default:
                    if (!string.IsNullOrWhiteSpace(element.TextContent))
                        yield return ReadParagraphWithClassStyle(element, ParagraphFormatting.Default, imageResolver, msoStyleMap);
                    break;
            }
        }
    }

    /// <summary>
    /// Reads a paragraph element, checking whether its CSS class carries an <c>mso-style-name</c>
    /// annotation (Full round-trip) and recovering the StyleId from it when present.
    /// </summary>
    private static Paragraph ReadParagraphWithClassStyle(
        IElement element,
        ParagraphFormatting baseFormatting,
        Func<string, InlineImage?> imageResolver,
        IReadOnlyDictionary<string, string> msoStyleMap)
    {
        // Recover StyleId from FreeW-* CSS class (Full output round-trip).
        string? recoveredStyleId = null;
        var classAttr = element.GetAttribute("class");
        if (!string.IsNullOrEmpty(classAttr))
        {
            foreach (var cls in classAttr.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (msoStyleMap.TryGetValue(cls, out var styleId))
                {
                    recoveredStyleId = styleId;
                    break;
                }
            }
        }

        return ReadParagraph(element, baseFormatting, recoveredStyleId, imageResolver);
    }

    private static Paragraph ReadHeading(IElement element, Func<string, InlineImage?> imageResolver, IReadOnlyDictionary<string, string> msoStyleMap)
    {
        var level = int.TryParse(element.LocalName[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, 1, 6)
            : 1;

        // Prefer a recovered StyleId from an mso-style-name class over the inferred heading id.
        string? recoveredStyleId = null;
        var classAttr = element.GetAttribute("class");
        if (!string.IsNullOrEmpty(classAttr))
        {
            foreach (var cls in classAttr.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (msoStyleMap.TryGetValue(cls, out var sid))
                {
                    recoveredStyleId = sid;
                    break;
                }
            }
        }

        var styleId = recoveredStyleId ?? $"Heading{Math.Min(level, 3)}";
        var paragraph = ReadParagraph(element, ParagraphFormatting.Default, styleId, imageResolver);
        paragraph.Runs.ReplaceAll(run => run.Formatting.Bold ? run : new Run(run.Text, run.Formatting with { Bold = true })
        {
            Image = run.Image,
            HyperlinkUrl = run.HyperlinkUrl,
            HyperlinkAnchor = run.HyperlinkAnchor,
            HyperlinkTooltip = run.HyperlinkTooltip,
        });
        return paragraph;
    }

    private static IEnumerable<Paragraph> ReadList(IElement list, Func<string, InlineImage?> imageResolver, IReadOnlyDictionary<string, string> msoStyleMap)
    {
        var kind = list.LocalName.Equals("ol", StringComparison.OrdinalIgnoreCase) ? ListKind.Number : ListKind.Bullet;
        foreach (var item in list.Children.Where(child => child.LocalName.Equals("li", StringComparison.OrdinalIgnoreCase)))
        {
            yield return ReadParagraph(
                item,
                ParagraphFormatting.Default with { ListKind = kind },
                null,
                imageResolver);
        }
    }

    private static Paragraph ReadParagraph(
        IElement element,
        ParagraphFormatting baseFormatting,
        string? styleId,
        Func<string, InlineImage?> imageResolver)
    {
        var declarations = HtmlCssFormatting.ParseDeclarations(element.GetAttribute("style"));
        var formatting = baseFormatting;
        if (HtmlCssFormatting.ReadAlignment(declarations) is { } alignment)
            formatting = formatting with { Alignment = alignment };

        var paragraph = new Paragraph { Formatting = formatting, StyleId = styleId };
        AppendInline(paragraph, element.ChildNodes, RunFormatting.Default, imageResolver);
        return paragraph;
    }

    private static Table ReadTable(IElement element, Func<string, InlineImage?> imageResolver, IReadOnlyDictionary<string, string> msoStyleMap)
    {
        var table = new Table();
        var rowElements = GetDirectTableRows(element);
        var pendingRowspans = new Dictionary<int, PendingRowspan>();

        foreach (var rowElement in rowElements)
        {
            var row = new TableRow();
            var column = 0;
            foreach (var cellElement in rowElement.Children.Where(c =>
                         c.LocalName.Equals("td", StringComparison.OrdinalIgnoreCase) ||
                         c.LocalName.Equals("th", StringComparison.OrdinalIgnoreCase)))
            {
                while (pendingRowspans.TryGetValue(column, out var pending) && pending.RemainingRows > 0)
                {
                    row.Cells.Add(new TableCell
                    {
                        GridSpan = pending.GridSpan,
                        VerticalMerge = VerticalMergeState.Continue
                    });
                    pendingRowspans[column] = pending with { RemainingRows = pending.RemainingRows - 1 };
                    column += pending.GridSpan;
                }

                var cell = new TableCell();
                if (int.TryParse(cellElement.GetAttribute("colspan"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var colspan) && colspan > 1)
                    cell.GridSpan = colspan;
                if (int.TryParse(cellElement.GetAttribute("rowspan"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var rowspan) && rowspan > 1)
                {
                    cell.VerticalMerge = VerticalMergeState.Restart;
                    pendingRowspans[column] = new PendingRowspan(rowspan - 1, Math.Max(1, cell.GridSpan));
                }

                var paragraphs = ReadCellParagraphs(cellElement.ChildNodes, imageResolver, msoStyleMap);
                if (paragraphs.Count == 0)
                    paragraphs.Add(new Paragraph(NormalizeText(cellElement.TextContent)));
                cell.Paragraphs.AddRange(paragraphs);

                if (cellElement.LocalName.Equals("th", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var run in cell.Paragraphs.SelectMany(p => p.Runs))
                        run.Formatting = run.Formatting with { Bold = true };
                }

                row.Cells.Add(cell);
                column += Math.Max(1, cell.GridSpan);
            }

            while (pendingRowspans.TryGetValue(column, out var pending) && pending.RemainingRows > 0)
            {
                row.Cells.Add(new TableCell
                {
                    GridSpan = pending.GridSpan,
                    VerticalMerge = VerticalMergeState.Continue
                });
                pendingRowspans[column] = pending with { RemainingRows = pending.RemainingRows - 1 };
                column += pending.GridSpan;
            }

            table.Rows.Add(row);
        }

        return table;
    }

    private static List<Paragraph> ReadCellParagraphs(IEnumerable<INode> childNodes, Func<string, InlineImage?> imageResolver, IReadOnlyDictionary<string, string> msoStyleMap)
    {
        var paragraphs = new List<Paragraph>();
        var inlineNodes = new List<INode>();

        void FlushInline()
        {
            if (inlineNodes.Count == 0)
                return;

            var paragraph = new Paragraph();
            AppendInline(paragraph, inlineNodes, RunFormatting.Default, imageResolver);
            if (paragraph.Runs.Count > 0)
                paragraphs.Add(paragraph);
            inlineNodes.Clear();
        }

        foreach (var node in childNodes)
        {
            if (node is not IElement element || !IsTableCellBlockElement(element))
            {
                inlineNodes.Add(node);
                continue;
            }

            FlushInline();
            switch (element.LocalName.ToLowerInvariant())
            {
                case "table":
                    var nestedTable = ReadTable(element, imageResolver, msoStyleMap);
                    if (TablePlainText(nestedTable) is { Length: > 0 } nestedText)
                        paragraphs.Add(new Paragraph(nestedText));
                    break;
                case "div":
                case "section":
                case "article":
                case "main":
                    paragraphs.AddRange(ReadCellParagraphs(element.ChildNodes, imageResolver, msoStyleMap));
                    break;
                default:
                    foreach (var block in ReadBlocks(new[] { element }, imageResolver, msoStyleMap))
                    {
                        switch (block)
                        {
                            case Paragraph paragraph:
                                paragraphs.Add(paragraph);
                                break;
                            case Table table when TablePlainText(table) is { Length: > 0 } text:
                                paragraphs.Add(new Paragraph(text));
                                break;
                        }
                    }
                    break;
            }
        }

        FlushInline();
        return paragraphs;
    }

    private static bool IsTableCellBlockElement(IElement element) =>
        element.LocalName.ToLowerInvariant() switch
        {
            "p" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or
            "ul" or "ol" or "table" or "div" or "section" or "article" or "main" => true,
            _ => false
        };

    private static string TablePlainText(Table table)
        => string.Join(
            "\n",
            table.Rows.Select(row =>
                string.Join("\t", row.Cells.Select(cell => cell.PlainText))).Where(text => text.Length > 0));

    private static List<IElement> GetDirectTableRows(IElement tableElement)
    {
        var rows = new List<IElement>();
        foreach (var child in tableElement.Children.OfType<IElement>())
        {
            if (child.LocalName.Equals("tr", StringComparison.OrdinalIgnoreCase))
            {
                rows.Add(child);
                continue;
            }

            if (!child.LocalName.Equals("thead", StringComparison.OrdinalIgnoreCase) &&
                !child.LocalName.Equals("tbody", StringComparison.OrdinalIgnoreCase) &&
                !child.LocalName.Equals("tfoot", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            rows.AddRange(child.Children.OfType<IElement>()
                .Where(row => row.LocalName.Equals("tr", StringComparison.OrdinalIgnoreCase)));
        }

        return rows;
    }

    private static void AppendInline(
        Paragraph paragraph,
        IEnumerable<INode> nodes,
        RunFormatting inherited,
        Func<string, InlineImage?> imageResolver)
    {
        foreach (var node in nodes)
        {
            if (node is IText textNode)
            {
                var text = NormalizeText(textNode.Data);
                if (text.Length > 0)
                    paragraph.Runs.Add(new Run(text, inherited));
                continue;
            }

            if (node is not IElement element)
                continue;

            var formatting = ApplyElementFormatting(element, inherited);
            switch (element.LocalName.ToLowerInvariant())
            {
                case "br":
                    paragraph.Runs.Add(new Run("\n", formatting));
                    break;
                case "img":
                    if (TryReadImage(element, imageResolver) is { } image)
                        paragraph.Runs.Add(new Run(string.Empty, formatting) { Image = image });
                    break;
                case "a":
                    var before = paragraph.Runs.Count;
                    AppendInline(paragraph, element.ChildNodes, formatting, imageResolver);
                    var href = element.GetAttribute("href");
                    if (!string.IsNullOrWhiteSpace(href))
                    {
                        foreach (var run in paragraph.Runs.Skip(before))
                            run.HyperlinkUrl = href;
                    }
                    break;
                default:
                    AppendInline(paragraph, element.ChildNodes, formatting, imageResolver);
                    break;
            }
        }
    }

    private static RunFormatting ApplyElementFormatting(IElement element, RunFormatting inherited)
    {
        var result = inherited;
        switch (element.LocalName.ToLowerInvariant())
        {
            case "b":
            case "strong":
                result = result with { Bold = true };
                break;
            case "i":
            case "em":
                result = result with { Italic = true };
                break;
            case "u":
                result = result with { Underline = true };
                break;
            case "s":
            case "strike":
            case "del":
                result = result with { Strikethrough = true };
                break;
            case "sup":
                result = result with { VerticalAlign = VerticalAlign.Superscript };
                break;
            case "sub":
                result = result with { VerticalAlign = VerticalAlign.Subscript };
                break;
        }

        return HtmlCssFormatting.ApplyToRun(result, HtmlCssFormatting.ParseDeclarations(element.GetAttribute("style")));
    }

    private static InlineImage? TryReadImage(IElement element, Func<string, InlineImage?> imageResolver)
    {
        var src = element.GetAttribute("src");
        if (string.IsNullOrWhiteSpace(src))
            return null;

        InlineImage? image = null;
        if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            image = ReadDataUriImage(src);
        else if (src.StartsWith("cid:", StringComparison.OrdinalIgnoreCase))
            image = imageResolver(src[4..]);
        else
            image = imageResolver(src);

        if (image is null)
            return null;

        image = CloneImage(image);
        if (TryReadLengthPt(element.GetAttribute("width"), out var width))
            image.WidthPt = width;
        if (TryReadLengthPt(element.GetAttribute("height"), out var height))
            image.HeightPt = height;
        image.AltText = element.GetAttribute("alt");
        return image;
    }

    private static InlineImage CloneImage(InlineImage image) =>
        new(image.Bytes, image.WidthPt, image.HeightPt, image.Format)
        {
            AltText = image.AltText,
            Wrapping = image.Wrapping,
            HorizontalOffsetPt = image.HorizontalOffsetPt,
            VerticalOffsetPt = image.VerticalOffsetPt,
            HorizontalAnchor = image.HorizontalAnchor,
            VerticalAnchor = image.VerticalAnchor
        };

    private static InlineImage? ReadDataUriImage(string src)
    {
        var comma = src.IndexOf(',');
        if (comma < 0)
            return null;

        var header = src[..comma];
        if (!header.Contains(";base64", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            var bytes = Convert.FromBase64String(src[(comma + 1)..]);
            var format = ImageFormatFromMime(header);
            return new InlineImage(bytes, 72, 72, format);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static ImageFormat ImageFormatFromMime(string header) =>
        header.ToLowerInvariant() switch
        {
            var h when h.Contains("image/jpeg") || h.Contains("image/jpg") => ImageFormat.Jpeg,
            var h when h.Contains("image/gif") => ImageFormat.Gif,
            var h when h.Contains("image/bmp") => ImageFormat.Bmp,
            var h when h.Contains("image/tiff") => ImageFormat.Tiff,
            _ => ImageFormat.Png
        };

    private static bool TryReadLengthPt(string? value, out double pt) =>
        HtmlCssFormatting.TryParseLengthPt(value, out pt);

    private static void WriteBlocks(StringBuilder sb, IReadOnlyList<Block> blocks, HtmlImageMode imageMode, List<HtmlEmbeddedImage> images, HtmlSaveMode saveMode = HtmlSaveMode.Filtered)
    {
        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] is Paragraph paragraph && paragraph.Formatting.ListKind is ListKind.Bullet or ListKind.Number)
            {
                var kind = paragraph.Formatting.ListKind;
                sb.Append(kind == ListKind.Number ? "<ol>" : "<ul>");
                while (i < blocks.Count && blocks[i] is Paragraph item && item.Formatting.ListKind == kind)
                {
                    sb.Append("<li>");
                    WriteRuns(sb, item.Runs, imageMode, images);
                    sb.AppendLine("</li>");
                    i++;
                }
                sb.AppendLine(kind == ListKind.Number ? "</ol>" : "</ul>");
                i--;
                continue;
            }

            switch (blocks[i])
            {
                case Paragraph p:
                    WriteParagraph(sb, p, imageMode, images, saveMode);
                    break;
                case Table table:
                    WriteTable(sb, table, imageMode, images, saveMode);
                    break;
            }
        }
    }

    private static void WriteParagraph(StringBuilder sb, Paragraph paragraph, HtmlImageMode imageMode, List<HtmlEmbeddedImage> images, HtmlSaveMode saveMode = HtmlSaveMode.Filtered)
    {
        var tag = HeadingTag(paragraph.StyleId) ?? "p";
        var style = HtmlCssFormatting.ParagraphStyle(paragraph.Formatting);
        sb.Append('<').Append(tag);

        // In Full (Office) mode: emit a CSS class for non-heading StyleIds so the reader can recover them.
        if (saveMode == HtmlSaveMode.Full && paragraph.StyleId is { Length: > 0 } styleId && HeadingTag(styleId) is null)
        {
            var className = StyleIdToClassName(styleId);
            sb.Append(" class=\"").Append(WebUtility.HtmlEncode(className)).Append('"');
        }

        if (style.Length > 0)
            sb.Append(" style=\"").Append(WebUtility.HtmlEncode(style)).Append('"');
        sb.Append('>');
        WriteRuns(sb, paragraph.Runs, imageMode, images);
        sb.Append("</").Append(tag).AppendLine(">");
    }

    private static string? HeadingTag(string? styleId) =>
        styleId?.ToLowerInvariant() switch
        {
            "heading1" => "h1",
            "heading2" => "h2",
            "heading3" => "h3",
            _ => null
        };

    private static void WriteTable(StringBuilder sb, Table table, HtmlImageMode imageMode, List<HtmlEmbeddedImage> images, HtmlSaveMode saveMode = HtmlSaveMode.Filtered)
    {
        sb.AppendLine("<table>");
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            sb.AppendLine("<tr>");
            for (var cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
            {
                var cell = row.Cells[cellIndex];
                if (cell.VerticalMerge == VerticalMergeState.Continue)
                    continue;

                sb.Append("<td");
                if (cell.GridSpan > 1)
                    sb.Append(" colspan=\"").Append(cell.GridSpan.ToString(CultureInfo.InvariantCulture)).Append('"');
                var rowspan = cell.VerticalMerge == VerticalMergeState.Restart
                    ? CalculateRowspan(table, rowIndex, cellIndex)
                    : 1;
                if (rowspan > 1)
                    sb.Append(" rowspan=\"").Append(rowspan.ToString(CultureInfo.InvariantCulture)).Append('"');
                sb.Append('>');

                if (cell.Paragraphs.Count == 1)
                    WriteRuns(sb, cell.Paragraphs[0].Runs, imageMode, images);
                else
                    foreach (var paragraph in cell.Paragraphs)
                        WriteParagraph(sb, paragraph, imageMode, images, saveMode);

                sb.AppendLine("</td>");
            }
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</table>");
    }

    private static int CalculateRowspan(Table table, int rowIndex, int cellIndex)
    {
        var row = table.Rows[rowIndex];
        var gridColumn = GridColumnOf(row, cellIndex);
        var rowspan = 1;
        for (var nextRowIndex = rowIndex + 1; nextRowIndex < table.Rows.Count; nextRowIndex++)
        {
            if (FindCellStartingAtGridColumn(table.Rows[nextRowIndex], gridColumn) is not { VerticalMerge: VerticalMergeState.Continue })
                break;
            rowspan++;
        }

        return rowspan;
    }

    private static int GridColumnOf(TableRow row, int cellIndex)
    {
        var column = 0;
        for (var index = 0; index < cellIndex && index < row.Cells.Count; index++)
            column += Math.Max(1, row.Cells[index].GridSpan);
        return column;
    }

    private static TableCell? FindCellStartingAtGridColumn(TableRow row, int gridColumn)
    {
        var column = 0;
        foreach (var cell in row.Cells)
        {
            if (column == gridColumn)
                return cell;
            column += Math.Max(1, cell.GridSpan);
        }

        return null;
    }

    private static void WriteRuns(StringBuilder sb, IEnumerable<Run> runs, HtmlImageMode imageMode, List<HtmlEmbeddedImage> images)
    {
        foreach (var run in runs)
        {
            if (run.FootnoteId.HasValue || run.EndnoteId.HasValue || run.CommentId.HasValue)
                continue;

            var textOrImage = new StringBuilder();
            if (run.Image is { } image)
                WriteImage(textOrImage, image, imageMode, images);
            else
                textOrImage.Append(WebUtility.HtmlEncode(run.Text).Replace("\n", "<br>"));

            var content = textOrImage.ToString();
            if (content.Length == 0)
                continue;

            var formatting = run.Formatting;
            if (formatting.Bold)
                content = "<strong>" + content + "</strong>";
            if (formatting.Italic)
                content = "<em>" + content + "</em>";
            if (formatting.Underline)
                content = "<u>" + content + "</u>";
            if (formatting.Strikethrough)
                content = "<s>" + content + "</s>";
            if (formatting.VerticalAlign == VerticalAlign.Superscript)
                content = "<sup>" + content + "</sup>";
            if (formatting.VerticalAlign == VerticalAlign.Subscript)
                content = "<sub>" + content + "</sub>";

            var style = HtmlCssFormatting.RunStyle(formatting);
            if (style.Length > 0)
                content = "<span style=\"" + WebUtility.HtmlEncode(style) + "\">" + content + "</span>";

            if (!string.IsNullOrWhiteSpace(run.HyperlinkUrl))
                content = "<a href=\"" + WebUtility.HtmlEncode(run.HyperlinkUrl) + "\">" + content + "</a>";

            sb.Append(content);
        }
    }

    private static void WriteImage(StringBuilder sb, InlineImage image, HtmlImageMode imageMode, List<HtmlEmbeddedImage> images)
    {
        var ext = InlineImage.ExtensionFor(image.Format);
        var mime = MimeTypeFor(image.Format);
        string src;
        if (imageMode == HtmlImageMode.Cid)
        {
            var cid = $"image{images.Count + 1}@freew.local";
            images.Add(new HtmlEmbeddedImage(cid, mime, ext, image.Bytes));
            src = "cid:" + cid;
        }
        else
        {
            src = $"data:{mime};base64,{Convert.ToBase64String(image.Bytes)}";
        }

        sb.Append("<img src=\"").Append(WebUtility.HtmlEncode(src)).Append('"')
            .Append(" width=\"").Append(FormatPt(image.WidthPt)).Append("pt\"")
            .Append(" height=\"").Append(FormatPt(image.HeightPt)).Append("pt\"");
        if (!string.IsNullOrWhiteSpace(image.AltText))
            sb.Append(" alt=\"").Append(WebUtility.HtmlEncode(image.AltText)).Append('"');
        sb.Append('>');
    }

    internal static string MimeTypeFor(ImageFormat format) => format switch
    {
        ImageFormat.Jpeg => "image/jpeg",
        ImageFormat.Gif => "image/gif",
        ImageFormat.Bmp => "image/bmp",
        ImageFormat.Tiff => "image/tiff",
        _ => "image/png"
    };

    private static string NormalizeText(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n');

    private static string FormatPt(double pt) =>
        pt.ToString("0.##", CultureInfo.InvariantCulture);
}

internal enum HtmlImageMode
{
    DataUri,
    Cid,
}

internal sealed record HtmlEmbeddedImage(string ContentId, string MimeType, string Extension, byte[] Bytes);

internal sealed record HtmlWriteResult(string Html, IReadOnlyList<HtmlEmbeddedImage> Images);

internal readonly record struct PendingRowspan(int RemainingRows, int GridSpan);

internal static class ListExtensions
{
    public static void ReplaceAll<T>(this IList<T> list, Func<T, T> replace)
    {
        for (var i = 0; i < list.Count; i++)
            list[i] = replace(list[i]);
    }
}

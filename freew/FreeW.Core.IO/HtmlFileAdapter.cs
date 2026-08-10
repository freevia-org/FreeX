using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
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
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly Regex MetaTagRegex = new(@"<meta\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CharsetAttrRegex = new(@"charset\s*=\s*[""']?\s*([^""'\s;/>]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Encoding Windows1252;

    static HtmlFileAdapter()
    {
        // Legacy HTML charsets (windows-1252, shift_jis, gb2312, ...) live in the code-pages provider,
        // not the default net10.0 encoding set. Register it once so Encoding.GetEncoding(name) resolves them.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Windows1252 = Encoding.GetEncoding(1252, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);
    }

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
    public string FormatName => _saveMode == HtmlSaveMode.Full ? "Web Page" : "Web Page, Filtered";

    public IReadOnlyList<FileFormatDescriptor> Formats =>
    [
        new(".html", FormatName),
        new(".htm", FormatName),
    ];

    public TextDocument Load(Stream stream)
    {
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        var bytes = copy.ToArray();
        return LoadHtml(DecodeBytes(bytes), static _ => null);
    }

    /// <summary>
    /// Decodes raw HTML bytes into text, honoring the document's declared encoding rather than blindly
    /// assuming UTF-8. Resolution order: a Unicode byte-order-mark; failing that, the charset declared in
    /// the document head (<c>&lt;meta charset="..."&gt;</c> or the legacy
    /// <c>&lt;meta http-equiv="Content-Type" content="text/html; charset=..."&gt;</c> form, found via an
    /// ASCII/Latin-1-safe preliminary scan — safe because ASCII-range bytes are stable across every
    /// encoding a browser/Word will actually emit for HTML, including the legacy code pages this targets);
    /// failing that, strict UTF-8 with a Windows-1252 fallback for bomless legacy files (matching
    /// <see cref="PlainTextFileAdapter"/>'s convention).
    /// </summary>
    internal static string DecodeBytes(byte[] bytes)
    {
        if (TryReadBom(bytes, out var bomEncoding, out var bomLength))
            return bomEncoding.GetString(bytes, bomLength, bytes.Length - bomLength);

        if (TryDetectDeclaredCharset(bytes, out var declaredEncoding))
            return declaredEncoding.GetString(bytes);

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Windows1252.GetString(bytes);
        }
    }

    private static bool TryReadBom(byte[] bytes, out Encoding encoding, out int length)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            length = 3;
            return true;
        }

        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00)
        {
            encoding = new UTF32Encoding(bigEndian: false, byteOrderMark: false);
            length = 4;
            return true;
        }

        if (bytes.Length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)
        {
            encoding = new UTF32Encoding(bigEndian: true, byteOrderMark: false);
            length = 4;
            return true;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            encoding = Encoding.Unicode; // UTF-16 LE
            length = 2;
            return true;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            encoding = Encoding.BigEndianUnicode; // UTF-16 BE
            length = 2;
            return true;
        }

        encoding = null!;
        length = 0;
        return false;
    }

    /// <summary>
    /// Scans the first 4KB of the document — decoded byte-for-byte as Latin-1 so the scan itself never
    /// throws and never mis-reads ASCII-range charset markup, regardless of the file's real encoding — for
    /// a <c>&lt;meta&gt;</c> tag declaring a charset, then resolves that name via
    /// <see cref="Encoding.GetEncoding(string)"/>.
    /// </summary>
    private static bool TryDetectDeclaredCharset(byte[] bytes, out Encoding encoding)
    {
        var scanLength = Math.Min(bytes.Length, 4096);
        var head = Encoding.Latin1.GetString(bytes, 0, scanLength);

        foreach (Match metaMatch in MetaTagRegex.Matches(head))
        {
            var charsetMatch = CharsetAttrRegex.Match(metaMatch.Value);
            if (!charsetMatch.Success)
                continue;

            var name = charsetMatch.Groups[1].Value.Trim();
            if (name.Length == 0)
                continue;

            // "utf8" (no hyphen) is a common authoring typo; .NET only recognizes the hyphenated form.
            if (name.Equals("utf8", StringComparison.OrdinalIgnoreCase))
                name = "utf-8";

            try
            {
                encoding = Encoding.GetEncoding(name);
                return true;
            }
            catch (ArgumentException)
            {
                // Unrecognized/unsupported charset name — keep scanning other meta tags, then fall back.
            }
        }

        encoding = null!;
        return false;
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
        ReadWordNoteNumberingOptions(htmlDocument, document);

        var body = htmlDocument.Body;
        if (body is null)
            return document;

        ReadNoteStores(document, body, imageResolver, msoStyleMap);
        foreach (var block in ReadBlocks(body.ChildNodes, imageResolver, msoStyleMap))
            document.Blocks.Add(block);

        if (document.Blocks.Count == 0)
        {
            var fallbackText = string.Concat(body.ChildNodes
                .Where(node => node is not IElement element || !IsNoteStorageElement(element))
                .Select(node => node.TextContent));
            if (!string.IsNullOrWhiteSpace(fallbackText))
                document.Blocks.Add(new Paragraph(NormalizeText(fallbackText)));
        }

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

    private static void ReadWordNoteNumberingOptions(
        AngleSharp.Html.Dom.IHtmlDocument htmlDocument,
        TextDocument document)
    {
        var css = string.Join('\n', htmlDocument.QuerySelectorAll("style").Select(element => element.TextContent));
        ReadWordNoteNumberingOptions(css, "footnote", document.FootnoteNumbering);
        ReadWordNoteNumberingOptions(css, "endnote", document.EndnoteNumbering);
    }

    private static void ReadWordNoteNumberingOptions(
        string css,
        string kind,
        NoteNumberingOptions options)
    {
        if (TryReadCssProperty(css, $"mso-{kind}-numbering-style", out var style))
        {
            options.NumberFormat = style.Trim().ToLowerInvariant() switch
            {
                "roman-lower" => NoteNumberFormat.LowerRoman,
                "roman-upper" => NoteNumberFormat.UpperRoman,
                "alpha-lower" => NoteNumberFormat.LowerLetter,
                "alpha-upper" => NoteNumberFormat.UpperLetter,
                "chicago" or "symbol" => NoteNumberFormat.Chicago,
                _ => NoteNumberFormat.Decimal
            };
        }

        if (TryReadCssProperty(css, $"mso-{kind}-numbering-start", out var start)
            && int.TryParse(start, NumberStyles.Integer, CultureInfo.InvariantCulture, out var startAt)
            && startAt > 0)
        {
            options.StartAt = startAt;
        }

        if (TryReadCssProperty(css, $"mso-{kind}-numbering-restart", out var restart))
        {
            options.NumberRestart = restart.Trim().ToLowerInvariant() switch
            {
                "each-page" => NoteNumberRestart.EachPage,
                "each-section" => NoteNumberRestart.EachSection,
                _ => NoteNumberRestart.Continuous
            };
        }
    }

    private static bool TryReadCssProperty(string css, string property, out string value)
    {
        value = string.Empty;
        var start = css.IndexOf(property, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return false;

        var colon = css.IndexOf(':', start + property.Length);
        if (colon < 0)
            return false;

        var end = css.IndexOfAny([';', '}'], colon + 1);
        if (end < 0)
            end = css.Length;
        value = css[(colon + 1)..end].Trim();
        return value.Length > 0;
    }

    private static string BuildNoteNumberingCss(TextDocument document)
    {
        if (document.FootnoteNumbering.IsDefault && document.EndnoteNumbering.IsDefault)
            return string.Empty;

        var css = new StringBuilder("@page {\n");
        AppendNoteNumberingCss(css, "footnote", document.FootnoteNumbering);
        AppendNoteNumberingCss(css, "endnote", document.EndnoteNumbering);
        css.AppendLine("}");
        return css.ToString();
    }

    private static void AppendNoteNumberingCss(
        StringBuilder css,
        string kind,
        NoteNumberingOptions options)
    {
        if (options.NumberFormat != NoteNumberFormat.Decimal)
        {
            var style = options.NumberFormat switch
            {
                NoteNumberFormat.LowerRoman => "roman-lower",
                NoteNumberFormat.UpperRoman => "roman-upper",
                NoteNumberFormat.LowerLetter => "alpha-lower",
                NoteNumberFormat.UpperLetter => "alpha-upper",
                NoteNumberFormat.Chicago => "chicago",
                _ => "arabic"
            };
            css.Append("  mso-").Append(kind).Append("-numbering-style:").Append(style).AppendLine(";");
        }

        if (options.StartAt != 1)
        {
            css.Append("  mso-").Append(kind).Append("-numbering-start:")
                .Append(options.StartAt.ToString(CultureInfo.InvariantCulture)).AppendLine(";");
        }

        if (options.NumberRestart != NoteNumberRestart.Continuous)
        {
            css.Append("  mso-").Append(kind).Append("-numbering-restart:")
                .Append(options.NumberRestart == NoteNumberRestart.EachPage ? "each-page" : "each-section")
                .AppendLine(";");
        }
    }

    internal static HtmlWriteResult WriteHtml(TextDocument document, HtmlImageMode imageMode, HtmlSaveMode saveMode = HtmlSaveMode.Filtered)
    {
        var images = new List<HtmlEmbeddedImage>();
        var body = new StringBuilder();
        var noteLabels = BuildNoteMarkerLabels(document);
        WriteBlocks(body, document.Blocks, imageMode, images, noteLabels, saveMode);
        WriteNoteStores(body, document, imageMode, images, noteLabels, saveMode);
        var noteNumberingCss = BuildNoteNumberingCss(document);

        string html;
        if (saveMode == HtmlSaveMode.Full)
        {
            // Collect the distinct StyleIds that need CSS class definitions.
            var styleIds = CollectStyleIds(document);
            var styleBlock = BuildFullStyleBlock(styleIds);
            html = "<!doctype html>\n"
                + "<html xmlns=\"http://www.w3.org/TR/REC-html40\" xmlns:o=\"urn:schemas-microsoft-com:office:office\" xmlns:w=\"urn:schemas-microsoft-com:office:word\">\n"
                + "<head>\n"
                + "<meta charset=\"utf-8\">\n"
                + "<meta name=\"Generator\" content=\"FreeW\">\n"
                + "<style>\n"
                + styleBlock
                + noteNumberingCss
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
""" + noteNumberingCss + """
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
    private static IReadOnlyList<string> CollectStyleIds(TextDocument document)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        CollectStyleIdsInto(document.Blocks, seen);
        CollectStyleIdsInto(document.Footnotes.Values.SelectMany(note => note.Content), seen);
        CollectStyleIdsInto(document.Endnotes.Values.SelectMany(note => note.Content), seen);
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

            if (IsNoteStorageElement(element))
                continue;

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
                    if (TryReadNoteReference(element, out var endnote, out var noteId))
                    {
                        var noteFormatting = formatting with { VerticalAlign = VerticalAlign.Superscript };
                        var reference = endnote
                            ? Run.EndnoteReference(noteId, noteFormatting)
                            : Run.FootnoteReference(noteId, noteFormatting);
                        var visibleMark = NormalizeText(element.TextContent).Trim().Trim('[', ']');
                        if (visibleMark.Length > 0)
                            reference.Text = visibleMark;
                        paragraph.Runs.Add(reference);
                        break;
                    }
                    if (IsAutomaticNoteBacklink(element))
                        break;

                    var before = paragraph.Runs.Count;
                    AppendInline(paragraph, element.ChildNodes, formatting, imageResolver);
                    var href = element.GetAttribute("href");
                    if (!string.IsNullOrWhiteSpace(href))
                    {
                        foreach (var run in paragraph.Runs.Skip(before))
                        {
                            if (href.StartsWith('#'))
                                run.HyperlinkAnchor = href[1..];
                            else
                                run.HyperlinkUrl = href;
                            run.HyperlinkTooltip = element.GetAttribute("title");
                        }
                    }
                    break;
                default:
                    AppendInline(paragraph, element.ChildNodes, formatting, imageResolver);
                    break;
            }
        }
    }

    private static void ReadNoteStores(
        TextDocument document,
        IElement body,
        Func<string, InlineImage?> imageResolver,
        IReadOnlyDictionary<string, string> msoStyleMap)
    {
        foreach (var store in body.QuerySelectorAll("[data-freew-note-store]"))
        {
            var kind = store.GetAttribute("data-freew-note-store");
            var options = string.Equals(kind, "endnotes", StringComparison.OrdinalIgnoreCase)
                ? document.EndnoteNumbering
                : document.FootnoteNumbering;
            ReadNoteNumberingOptions(store, options);
        }

        foreach (var element in body.QuerySelectorAll("[data-freew-note-kind], [style]"))
        {
            if (!TryReadNoteBodyIdentity(element, out var endnote, out var id))
                continue;

            var paragraphs = ReadBlocks(element.ChildNodes, imageResolver, msoStyleMap)
                .OfType<Paragraph>()
                .ToList();
            if (paragraphs.Count == 0)
                paragraphs.Add(new Paragraph());

            var automaticReference = bool.TryParse(
                    element.GetAttribute("data-freew-automatic-reference"),
                    out var parsedAutomaticReference)
                ? parsedAutomaticReference
                : element.QuerySelectorAll("a").Any(IsAutomaticNoteBacklink);
            if (endnote)
            {
                var note = new Endnote(id) { HasAutomaticReferenceMark = automaticReference };
                note.Content.AddRange(paragraphs);
                document.Endnotes.TryAdd(id, note);
            }
            else
            {
                var note = new Footnote(id) { HasAutomaticReferenceMark = automaticReference };
                note.Content.AddRange(paragraphs);
                document.Footnotes.TryAdd(id, note);
            }
        }
    }

    private static void ReadNoteNumberingOptions(IElement store, NoteNumberingOptions options)
    {
        if (Enum.TryParse<NoteNumberFormat>(
                store.GetAttribute("data-freew-number-format"),
                ignoreCase: true,
                out var numberFormat))
        {
            options.NumberFormat = numberFormat;
        }

        if (int.TryParse(
                store.GetAttribute("data-freew-start-at"),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var startAt)
            && startAt > 0)
        {
            options.StartAt = startAt;
        }

        if (Enum.TryParse<NoteNumberRestart>(
                store.GetAttribute("data-freew-number-restart"),
                ignoreCase: true,
                out var numberRestart))
        {
            options.NumberRestart = numberRestart;
        }
    }

    private static bool IsNoteStorageElement(IElement element)
    {
        if (element.HasAttribute("data-freew-note-store"))
            return true;

        var declarations = HtmlCssFormatting.ParseDeclarations(element.GetAttribute("style"));
        return declarations.TryGetValue("mso-element", out var value)
            && value.Trim().ToLowerInvariant() is "footnote-list" or "endnote-list" or "footnote" or "endnote";
    }

    private static bool TryReadNoteBodyIdentity(IElement element, out bool endnote, out int id)
    {
        endnote = false;
        id = 0;
        if (element.LocalName.Equals("a", StringComparison.OrdinalIgnoreCase))
            return false;

        var kind = element.GetAttribute("data-freew-note-kind");
        if (int.TryParse(
                element.GetAttribute("data-freew-note-id"),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out id)
            && id > 0
            && (string.Equals(kind, "footnote", StringComparison.OrdinalIgnoreCase)
                || string.Equals(kind, "endnote", StringComparison.OrdinalIgnoreCase)))
        {
            endnote = string.Equals(kind, "endnote", StringComparison.OrdinalIgnoreCase);
            return true;
        }

        var declarations = HtmlCssFormatting.ParseDeclarations(element.GetAttribute("style"));
        if (!declarations.TryGetValue("mso-element", out var msoElement)
            || msoElement.Trim().ToLowerInvariant() is not ("footnote" or "endnote"))
        {
            return false;
        }

        endnote = msoElement.Trim().Equals("endnote", StringComparison.OrdinalIgnoreCase);
        return TryParseNoteToken(element.GetAttribute("id"), out var tokenEndnote, out id)
            && tokenEndnote == endnote;
    }

    private static bool TryReadNoteReference(IElement element, out bool endnote, out int id)
    {
        endnote = false;
        id = 0;
        var kind = element.GetAttribute("data-freew-note-kind");
        if (int.TryParse(
                element.GetAttribute("data-freew-note-id"),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out id)
            && id > 0
            && (string.Equals(kind, "footnote", StringComparison.OrdinalIgnoreCase)
                || string.Equals(kind, "endnote", StringComparison.OrdinalIgnoreCase)))
        {
            endnote = string.Equals(kind, "endnote", StringComparison.OrdinalIgnoreCase);
            return true;
        }

        if (!HasWordNoteReferenceSignature(element))
            return false;

        return TryParseNoteToken(element.GetAttribute("href"), out endnote, out id);
    }

    private static bool HasWordNoteReferenceSignature(IElement element)
    {
        var declarations = HtmlCssFormatting.ParseDeclarations(element.GetAttribute("style"));
        if (declarations.ContainsKey("mso-footnote-id"))
            return true;

        return HasWordNoteReferenceClass(element);
    }

    private static bool HasWordNoteReferenceClass(IElement element)
    {
        var classes = element.ClassList;
        return classes.Any(name =>
            name.Equals("MsoFootnoteReference", StringComparison.OrdinalIgnoreCase)
            || name.Equals("MsoEndnoteReference", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsAutomaticNoteBacklink(IElement element)
    {
        var token = element.GetAttribute("href")?.Trim().TrimStart('#').TrimStart('_');
        if (token?.StartsWith("ftnref", StringComparison.OrdinalIgnoreCase) != true
            && token?.StartsWith("ednref", StringComparison.OrdinalIgnoreCase) != true)
        {
            return false;
        }

        return element.QuerySelectorAll("[style]").Any(descendant =>
        {
            var declarations = HtmlCssFormatting.ParseDeclarations(descendant.GetAttribute("style"));
            return declarations.TryGetValue("mso-special-character", out var value)
                && value.Trim().ToLowerInvariant() is "footnote" or "endnote";
        });
    }

    private static bool TryParseNoteToken(string? raw, out bool endnote, out int id)
    {
        endnote = false;
        id = 0;
        var token = raw?.Trim().TrimStart('#').TrimStart('_');
        if (string.IsNullOrWhiteSpace(token)
            || token.Contains("ref", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string digits;
        if (token.StartsWith("ftn", StringComparison.OrdinalIgnoreCase))
        {
            digits = token[3..];
        }
        else if (token.StartsWith("edn", StringComparison.OrdinalIgnoreCase))
        {
            endnote = true;
            digits = token[3..];
        }
        else
        {
            return false;
        }

        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out id) && id > 0;
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

    private static void WriteBlocks(
        StringBuilder sb,
        IReadOnlyList<Block> blocks,
        HtmlImageMode imageMode,
        List<HtmlEmbeddedImage> images,
        IReadOnlyDictionary<HtmlNoteKey, string> noteLabels,
        HtmlSaveMode saveMode = HtmlSaveMode.Filtered)
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
                    WriteRuns(sb, item.Runs, imageMode, images, noteLabels);
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
                    WriteParagraph(sb, p, imageMode, images, noteLabels, saveMode);
                    break;
                case Table table:
                    WriteTable(sb, table, imageMode, images, noteLabels, saveMode);
                    break;
            }
        }
    }

    private static void WriteParagraph(
        StringBuilder sb,
        Paragraph paragraph,
        HtmlImageMode imageMode,
        List<HtmlEmbeddedImage> images,
        IReadOnlyDictionary<HtmlNoteKey, string> noteLabels,
        HtmlSaveMode saveMode = HtmlSaveMode.Filtered,
        string? prefixHtml = null,
        string? semanticClass = null)
    {
        var tag = HeadingTag(paragraph.StyleId) ?? "p";
        var style = HtmlCssFormatting.ParagraphStyle(paragraph.Formatting);
        sb.Append('<').Append(tag);

        var classes = new List<string>();
        if (!string.IsNullOrWhiteSpace(semanticClass))
            classes.Add(semanticClass);

        // In Full (Office) mode: emit a CSS class for non-heading StyleIds so the reader can recover them.
        if (saveMode == HtmlSaveMode.Full && paragraph.StyleId is { Length: > 0 } styleId && HeadingTag(styleId) is null)
            classes.Add(StyleIdToClassName(styleId));

        if (classes.Count > 0)
            sb.Append(" class=\"").Append(WebUtility.HtmlEncode(string.Join(' ', classes))).Append('"');

        if (style.Length > 0)
            sb.Append(" style=\"").Append(WebUtility.HtmlEncode(style)).Append('"');
        sb.Append('>');
        if (prefixHtml is not null)
            sb.Append(prefixHtml);
        WriteRuns(sb, paragraph.Runs, imageMode, images, noteLabels);
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

    private static void WriteTable(
        StringBuilder sb,
        Table table,
        HtmlImageMode imageMode,
        List<HtmlEmbeddedImage> images,
        IReadOnlyDictionary<HtmlNoteKey, string> noteLabels,
        HtmlSaveMode saveMode = HtmlSaveMode.Filtered)
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
                    WriteRuns(sb, cell.Paragraphs[0].Runs, imageMode, images, noteLabels);
                else
                    foreach (var paragraph in cell.Paragraphs)
                        WriteParagraph(sb, paragraph, imageMode, images, noteLabels, saveMode);

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

    private static IReadOnlyDictionary<HtmlNoteKey, string> BuildNoteMarkerLabels(TextDocument document)
    {
        var labels = new Dictionary<HtmlNoteKey, string>();
        var footnoteSequence = Math.Max(1, document.FootnoteNumbering.StartAt);
        var endnoteSequence = Math.Max(1, document.EndnoteNumbering.StartAt);

        foreach (var block in document.Blocks)
        {
            foreach (var paragraph in EnumerateHtmlParagraphs(block))
            {
                foreach (var run in paragraph.Runs)
                {
                    if (run.IsPageBreak)
                    {
                        if (document.FootnoteNumbering.NumberRestart == NoteNumberRestart.EachPage)
                            footnoteSequence = Math.Max(1, document.FootnoteNumbering.StartAt);
                        if (document.EndnoteNumbering.NumberRestart == NoteNumberRestart.EachPage)
                            endnoteSequence = Math.Max(1, document.EndnoteNumbering.StartAt);
                    }

                    if (run.FootnoteId is { } footnoteId)
                    {
                        var key = new HtmlNoteKey(Endnote: false, footnoteId);
                        if (!labels.ContainsKey(key))
                        {
                            if (document.Footnotes.TryGetValue(footnoteId, out var footnote)
                                && !footnote.HasAutomaticReferenceMark
                                && !string.IsNullOrWhiteSpace(run.Text)
                                && !string.Equals(
                                    run.Text,
                                    footnoteId.ToString(CultureInfo.InvariantCulture),
                                    StringComparison.Ordinal))
                            {
                                labels[key] = run.Text;
                            }
                            else
                            {
                                labels[key] = FormatNoteMarker(
                                    footnoteSequence++,
                                    document.FootnoteNumbering.NumberFormat);
                            }
                        }
                    }

                    if (run.EndnoteId is { } endnoteId)
                    {
                        var key = new HtmlNoteKey(Endnote: true, endnoteId);
                        if (!labels.ContainsKey(key))
                        {
                            if (document.Endnotes.TryGetValue(endnoteId, out var endnote)
                                && !endnote.HasAutomaticReferenceMark
                                && !string.IsNullOrWhiteSpace(run.Text)
                                && !string.Equals(
                                    run.Text,
                                    endnoteId.ToString(CultureInfo.InvariantCulture),
                                    StringComparison.Ordinal))
                            {
                                labels[key] = run.Text;
                            }
                            else
                            {
                                labels[key] = FormatNoteMarker(
                                    endnoteSequence++,
                                    document.EndnoteNumbering.NumberFormat);
                            }
                        }
                    }
                }

                if (paragraph.SectionBreak is not null)
                {
                    if (document.FootnoteNumbering.NumberRestart == NoteNumberRestart.EachSection)
                        footnoteSequence = Math.Max(1, document.FootnoteNumbering.StartAt);
                    if (document.EndnoteNumbering.NumberRestart == NoteNumberRestart.EachSection)
                        endnoteSequence = Math.Max(1, document.EndnoteNumbering.StartAt);
                }
            }
        }

        AddOrphanNoteLabels(
            labels,
            endnote: false,
            document.Footnotes.Keys,
            document.FootnoteNumbering);
        AddOrphanNoteLabels(
            labels,
            endnote: true,
            document.Endnotes.Keys,
            document.EndnoteNumbering);
        return labels;
    }

    private static IEnumerable<Paragraph> EnumerateHtmlParagraphs(Block block)
    {
        if (block is Paragraph paragraph)
        {
            yield return paragraph;
            yield break;
        }

        if (block is not Table table)
            yield break;

        foreach (var cellParagraph in table.Rows
                     .SelectMany(row => row.Cells)
                     .SelectMany(cell => cell.Paragraphs))
        {
            yield return cellParagraph;
        }
    }

    private static void AddOrphanNoteLabels(
        Dictionary<HtmlNoteKey, string> labels,
        bool endnote,
        IEnumerable<int> noteIds,
        NoteNumberingOptions numbering)
    {
        var sequence = Math.Max(1, numbering.StartAt);
        foreach (var id in noteIds.OrderBy(id => id))
        {
            var key = new HtmlNoteKey(endnote, id);
            labels.TryAdd(key, FormatNoteMarker(sequence, numbering.NumberFormat));
            sequence++;
        }
    }

    private static string NoteLabel(
        IReadOnlyDictionary<HtmlNoteKey, string> labels,
        bool endnote,
        int id) =>
        labels.TryGetValue(new HtmlNoteKey(endnote, id), out var label)
            ? label
            : id.ToString(CultureInfo.InvariantCulture);

    private static string FormatNoteMarker(int value, NoteNumberFormat format)
    {
        var number = Math.Max(1, value);
        return format switch
        {
            NoteNumberFormat.LowerRoman => ToRoman(number).ToLowerInvariant(),
            NoteNumberFormat.UpperRoman => ToRoman(number),
            NoteNumberFormat.LowerLetter => ToLetter(number, lower: true),
            NoteNumberFormat.UpperLetter => ToLetter(number, lower: false),
            NoteNumberFormat.Chicago => ToChicago(number),
            _ => number.ToString(CultureInfo.InvariantCulture)
        };
    }

    private static string ToRoman(int value)
    {
        (int Value, string Symbol)[] map =
        [
            (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"),
            (100, "C"), (90, "XC"), (50, "L"), (40, "XL"),
            (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")
        ];
        var remaining = Math.Clamp(value, 1, 3999);
        var result = new StringBuilder();
        foreach (var (number, symbol) in map)
        {
            while (remaining >= number)
            {
                result.Append(symbol);
                remaining -= number;
            }
        }
        return result.ToString();
    }

    private static string ToLetter(int value, bool lower)
    {
        var characters = new List<char>();
        while (value > 0)
        {
            value--;
            characters.Insert(0, (char)((lower ? 'a' : 'A') + value % 26));
            value /= 26;
        }
        return new string([.. characters]);
    }

    private static string ToChicago(int value)
    {
        string[] symbols = ["*", "\u2020", "\u2021", "\u00A7"];
        var symbol = symbols[(value - 1) % symbols.Length];
        var repeat = (value - 1) / symbols.Length + 1;
        return string.Concat(Enumerable.Repeat(symbol, repeat));
    }

    private static void WriteNoteStores(
        StringBuilder sb,
        TextDocument document,
        HtmlImageMode imageMode,
        List<HtmlEmbeddedImage> images,
        IReadOnlyDictionary<HtmlNoteKey, string> noteLabels,
        HtmlSaveMode saveMode)
    {
        WriteNoteStore(
            sb,
            endnote: false,
            document.Footnotes.Values
                .OrderBy(note => note.Id)
                .Select(note => (note.Id, note.HasAutomaticReferenceMark, (IReadOnlyList<Paragraph>)note.Content)),
            document.FootnoteNumbering,
            imageMode,
            images,
            noteLabels,
            saveMode);
        WriteNoteStore(
            sb,
            endnote: true,
            document.Endnotes.Values
                .OrderBy(note => note.Id)
                .Select(note => (note.Id, note.HasAutomaticReferenceMark, (IReadOnlyList<Paragraph>)note.Content)),
            document.EndnoteNumbering,
            imageMode,
            images,
            noteLabels,
            saveMode);
    }

    private static void WriteNoteStore(
        StringBuilder sb,
        bool endnote,
        IEnumerable<(int Id, bool AutomaticReference, IReadOnlyList<Paragraph> Content)> notes,
        NoteNumberingOptions numbering,
        HtmlImageMode imageMode,
        List<HtmlEmbeddedImage> images,
        IReadOnlyDictionary<HtmlNoteKey, string> noteLabels,
        HtmlSaveMode saveMode)
    {
        var materialized = notes.ToArray();
        if (materialized.Length == 0)
            return;

        var prefix = endnote ? "edn" : "ftn";
        var kind = endnote ? "endnote" : "footnote";
        sb.Append("<div style=\"mso-element:").Append(kind).Append("-list\"")
            .Append(" data-freew-note-store=\"").Append(kind).Append("s\"")
            .Append(" data-freew-number-format=\"").Append(numbering.NumberFormat).Append('"')
            .Append(" data-freew-start-at=\"").Append(numbering.StartAt.ToString(CultureInfo.InvariantCulture)).Append('"')
            .Append(" data-freew-number-restart=\"").Append(numbering.NumberRestart).AppendLine("\">");

        foreach (var note in materialized)
        {
            var idText = note.Id.ToString(CultureInfo.InvariantCulture);
            sb.Append("<div style=\"mso-element:").Append(kind).Append("\"")
                .Append(" id=\"").Append(prefix).Append(idText).Append('"')
                .Append(" data-freew-note-kind=\"").Append(kind).Append('"')
                .Append(" data-freew-note-id=\"").Append(idText).Append('"')
                .Append(" data-freew-automatic-reference=\"")
                .Append(note.AutomaticReference ? "true" : "false")
                .AppendLine("\">");

            var backlink = note.AutomaticReference
                ? BuildNoteBacklink(endnote, note.Id, NoteLabel(noteLabels, endnote, note.Id))
                : null;
            if (note.Content.Count == 0)
            {
                WriteParagraph(
                    sb,
                    new Paragraph(),
                    imageMode,
                    images,
                    noteLabels,
                    saveMode,
                    backlink,
                    endnote ? "MsoEndnoteText" : "MsoFootnoteText");
            }
            else
            {
                for (var index = 0; index < note.Content.Count; index++)
                {
                    WriteParagraph(
                        sb,
                        note.Content[index],
                        imageMode,
                        images,
                        noteLabels,
                        saveMode,
                        index == 0 ? backlink : null,
                        endnote ? "MsoEndnoteText" : "MsoFootnoteText");
                }
            }

            sb.AppendLine("</div>");
        }

        sb.AppendLine("</div>");
    }

    private static void WriteNoteReference(
        StringBuilder sb,
        bool endnote,
        int id,
        string label,
        RunFormatting formatting)
    {
        var prefix = endnote ? "edn" : "ftn";
        var kind = endnote ? "endnote" : "footnote";
        var idText = id.ToString(CultureInfo.InvariantCulture);
        var runStyle = HtmlCssFormatting.RunStyle(formatting);
        var anchor = new StringBuilder();
        anchor.Append("<a style=\"mso-footnote-id:").Append(prefix).Append(idText);
        if (runStyle.Length > 0)
            anchor.Append(';').Append(WebUtility.HtmlEncode(runStyle));
        anchor.Append('"')
            .Append(" href=\"#_").Append(prefix).Append(idText).Append("\"")
            .Append(" name=\"_").Append(prefix).Append("ref").Append(idText).Append("\"")
            .Append(" class=\"Mso").Append(endnote ? "Endnote" : "Footnote").Append("Reference\"")
            .Append(" data-freew-note-kind=\"").Append(kind).Append('"')
            .Append(" data-freew-note-id=\"").Append(idText).Append("\"><sup>")
            .Append(WebUtility.HtmlEncode(label)).Append("</sup></a>");

        var content = anchor.ToString();
        if (formatting.Bold)
            content = "<strong>" + content + "</strong>";
        if (formatting.Italic)
            content = "<em>" + content + "</em>";
        if (formatting.Underline)
            content = "<u>" + content + "</u>";
        if (formatting.Strikethrough)
            content = "<s>" + content + "</s>";
        sb.Append(content);
    }

    private static string BuildNoteBacklink(bool endnote, int id, string label)
    {
        var prefix = endnote ? "edn" : "ftn";
        var idText = id.ToString(CultureInfo.InvariantCulture);
        return "<a href=\"#_" + prefix + "ref" + idText + "\" name=\"_" + prefix + idText
            + "\" class=\"Mso" + (endnote ? "Endnote" : "Footnote") + "Reference\"><span style=\"mso-special-character:"
            + (endnote ? "endnote" : "footnote") + "\"><sup>" + WebUtility.HtmlEncode(label)
            + "</sup></span></a>";
    }

    private static void WriteRuns(
        StringBuilder sb,
        IEnumerable<Run> runs,
        HtmlImageMode imageMode,
        List<HtmlEmbeddedImage> images,
        IReadOnlyDictionary<HtmlNoteKey, string> noteLabels)
    {
        foreach (var run in runs)
        {
            if (run.FootnoteId is { } footnoteId)
            {
                WriteNoteReference(
                    sb,
                    endnote: false,
                    footnoteId,
                    NoteLabel(noteLabels, endnote: false, footnoteId),
                    run.Formatting);
                continue;
            }
            if (run.EndnoteId is { } endnoteId)
            {
                WriteNoteReference(
                    sb,
                    endnote: true,
                    endnoteId,
                    NoteLabel(noteLabels, endnote: true, endnoteId),
                    run.Formatting);
                continue;
            }
            if (run.CommentId.HasValue)
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

            var hyperlinkTarget = !string.IsNullOrWhiteSpace(run.HyperlinkUrl)
                ? run.HyperlinkUrl
                : !string.IsNullOrWhiteSpace(run.HyperlinkAnchor)
                    ? "#" + run.HyperlinkAnchor
                    : null;
            if (hyperlinkTarget is not null)
            {
                var title = !string.IsNullOrWhiteSpace(run.HyperlinkTooltip)
                    ? " title=\"" + WebUtility.HtmlEncode(run.HyperlinkTooltip) + "\""
                    : string.Empty;
                content = "<a href=\"" + WebUtility.HtmlEncode(hyperlinkTarget) + "\"" + title + ">"
                    + content + "</a>";
            }

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

internal readonly record struct HtmlNoteKey(bool Endnote, int Id);

internal readonly record struct PendingRowspan(int RemainingRows, int GridSpan);

internal static class ListExtensions
{
    public static void ReplaceAll<T>(this IList<T> list, Func<T, T> replace)
    {
        for (var i = 0; i < list.Count; i++)
            list[i] = replace(list[i]);
    }
}

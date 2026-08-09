using System.Globalization;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Free.Shared.Drawing;
using Free.Shared.Opc;
using FreeW.Core.Model;

namespace FreeW.Core.IO;

/// <summary>
/// Reads Word 2003 WordprocessingML (<c>&lt;w:wordDocument&gt;</c> root in the
/// <c>http://schemas.microsoft.com/office/word/2003/wordml</c> namespace) — the single-file XML format Word
/// 2003 saved as "XML Document". This is a <em>different</em> schema from both <c>.docx</c> and Flat OPC
/// (which <see cref="WordXmlFileAdapter"/> handles), so it gets a hand-written reader rather than reusing
/// <see cref="DocxReader"/>.
///
/// Per docs/planning/freew-file-formats.md §5.6, this maps the common modelled subset onto
/// <see cref="TextDocument"/>:
/// <list type="bullet">
///   <item><c>w:body/w:p/w:r/w:t</c> → <see cref="Paragraph"/> / <see cref="Run"/> / text;</item>
///   <item><c>w:rPr</c> (<c>w:b</c>/<c>w:i</c>/<c>w:u</c>/<c>w:sz</c>/<c>w:color</c>) → <see cref="RunFormatting"/>;</item>
///   <item><c>w:pPr</c> (<c>w:jc</c> alignment, <c>w:ind</c> indents) → <see cref="ParagraphFormatting"/>;</item>
///   <item><c>w:tbl</c>/<c>w:tr</c>/<c>w:tc</c> → <see cref="Table"/>;</item>
///   <item><c>w:sectPr</c> (page size/margins) → <see cref="PageSettings"/>.</item>
/// </list>
/// It is <strong>read-only</strong>. Deliberately out of scope (dropped on read, never silently faked):
/// fields, footnotes/endnotes, comments, images (<c>w:pict</c>/VML + <c>w:binData</c>), styles beyond the
/// direct run/paragraph formatting above, SmartArt/charts, and modern comment threads. Untrusted XML is
/// parsed with <see cref="SecureXmlReaderSettings"/> (DTD prohibited).
/// </summary>
public static class Wordml2003Reader
{
    /// <summary>The Word 2003 WordprocessingML namespace (the <c>w:</c> prefix in those files).</summary>
    public static readonly XNamespace W = "http://schemas.microsoft.com/office/word/2003/wordml";

    /// <summary>The root element name of a Word 2003 WordML document.</summary>
    public static readonly XName RootName = W + "wordDocument";

    // OOXML measurements: twentieths of a point (dxa) for indents/page geometry, half-points for font size.
    private const double TwipsPerPoint = 20.0;

    /// <summary>
    /// Reads a Word 2003 WordML document from <paramref name="stream"/>. Does not close the caller's stream.
    /// Throws <see cref="InvalidDataException"/> when the root is not <c>&lt;w:wordDocument&gt;</c>.
    /// </summary>
    public static TextDocument Read(Stream stream)
    {
        XDocument xml;
        using (var reader = XmlReader.Create(stream, SecureXmlReaderSettings.Create()))
            xml = XDocument.Load(reader);

        return Read(xml.Root);
    }

    /// <summary>
    /// Reads a Word 2003 WordML document from an already-parsed <paramref name="root"/> element (used by
    /// <see cref="WordXmlFileAdapter"/> after it sniffs the root to dispatch). Throws
    /// <see cref="InvalidDataException"/> when the root is not <c>&lt;w:wordDocument&gt;</c>.
    /// </summary>
    public static TextDocument Read(XElement? root)
    {
        if (root is null || root.Name != RootName)
        {
            throw new InvalidDataException(
                "Not a Word 2003 WordprocessingML document: missing a <w:wordDocument> root in the " +
                "2003 wordml namespace.");
        }

        var document = new TextDocument();

        var body = root.Element(W + "body");
        if (body is null)
            return document;

        foreach (var block in ReadBlocks(body))
            document.Blocks.Add(block);

        // A body-level w:sectPr (the trailing section properties) carries the page geometry.
        var sectPr = body.Element(W + "sectPr");
        if (sectPr is not null)
            ApplyPageSettings(sectPr, document.Page);

        return document;
    }

    private static IEnumerable<Block> ReadBlocks(XElement container)
    {
        foreach (var element in container.Elements())
        {
            if (element.Name == W + "p")
                yield return ReadParagraph(element);
            else if (element.Name == W + "tbl")
                yield return ReadTable(element);
            // Other body-level elements (w:sectPr, w:proofErr, …) are not block content here.
        }
    }

    private static Paragraph ReadParagraph(XElement p)
    {
        var paragraph = new Paragraph();

        var pPr = p.Element(W + "pPr");
        if (pPr is not null)
            paragraph.Formatting = ReadParagraphFormatting(pPr);

        foreach (var run in ReadRuns(p))
            paragraph.Runs.Add(run);

        return paragraph;
    }

    private static IEnumerable<Run> ReadRuns(XElement p)
    {
        // Hyperlinks wrap runs (w:hlink in 2003); descend into them so their text is not lost.
        foreach (var element in p.Elements())
        {
            if (element.Name == W + "r")
            {
                var run = ReadRun(element);
                if (run is not null)
                    yield return run;
            }
            else if (element.Name == W + "hlink")
            {
                var url = (string?)element.Attribute(W + "dest");
                var anchor = (string?)element.Attribute(W + "bookmark");
                var tooltip = (string?)element.Attribute(W + "tooltip");
                foreach (var child in element.Elements(W + "r"))
                {
                    var run = ReadRun(child);
                    if (run is not null)
                    {
                        if (!string.IsNullOrEmpty(url))
                            run.HyperlinkUrl = url;
                        else if (!string.IsNullOrEmpty(anchor))
                            run.HyperlinkAnchor = anchor;
                        run.HyperlinkTooltip = tooltip;
                        yield return run;
                    }
                }
            }
        }
    }

    private static Run? ReadRun(XElement r)
    {
        var text = ReadRunText(r);
        if (text.Length == 0)
            return null;

        var rPr = r.Element(W + "rPr");
        var formatting = rPr is not null ? ReadRunFormatting(rPr) : RunFormatting.Default;
        return new Run(text, formatting);
    }

    private static string ReadRunText(XElement r)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var element in r.Elements())
        {
            if (element.Name == W + "t")
                builder.Append(element.Value);
            else if (element.Name == W + "tab")
                builder.Append('\t');
            else if (element.Name == W + "br" || element.Name == W + "cr")
                builder.Append('\n');
        }
        return builder.ToString();
    }

    private static RunFormatting ReadRunFormatting(XElement rPr)
    {
        var bold = IsToggleOn(rPr.Element(W + "b"));
        var italic = IsToggleOn(rPr.Element(W + "i"));
        var underline = ReadUnderline(rPr.Element(W + "u"));
        var strike = IsToggleOn(rPr.Element(W + "strike"));

        string? fontFamily = null;
        var rFonts = rPr.Element(W + "rFonts");
        if (rFonts is not null)
            fontFamily = (string?)rFonts.Attribute(W + "ascii") ?? (string?)rFonts.Attribute(W + "h-ansi");

        // w:sz is in half-points; absent means "no explicit run size" (inherits the document
        // default), but an explicit w:sz val="0" is a real (if degenerate) explicit value and must
        // not be folded into "absent". Route through the shared helper (matches DocxReader's
        // handling of the identical w:sz attribute in the .docx path) instead of reimplementing the
        // half-points conversion locally.
        double? fontSizePt = DrawingMlCoordinateUnits.HalfPointsToPoints(
            rPr.Element(W + "sz")?.Attribute(W + "val")?.Value);

        string? colorHex = NormalizeColor((string?)rPr.Element(W + "color")?.Attribute(W + "val"));

        var vertAlignVal = (string?)rPr.Element(W + "vertAlign")?.Attribute(W + "val");
        var verticalAlign = vertAlignVal switch
        {
            "superscript" => VerticalAlign.Superscript,
            "subscript" => VerticalAlign.Subscript,
            _ => VerticalAlign.Baseline,
        };

        return new RunFormatting
        {
            Bold = bold,
            Italic = italic,
            Underline = underline,
            Strikethrough = strike,
            FontFamily = string.IsNullOrEmpty(fontFamily) ? null : fontFamily,
            FontSizePt = fontSizePt,
            ColorHex = colorHex,
            VerticalAlign = verticalAlign,
        };
    }

    private static ParagraphFormatting ReadParagraphFormatting(XElement pPr)
    {
        var alignment = ReadAlignment((string?)pPr.Element(W + "jc")?.Attribute(W + "val"));

        double indentLeft = 0, indentRight = 0, firstLine = 0;
        var ind = pPr.Element(W + "ind");
        if (ind is not null)
        {
            indentLeft = TwipsToPoints(AttrDouble(ind, "left"));
            indentRight = TwipsToPoints(AttrDouble(ind, "right"));
            // first-line indents; a hanging indent is a negative first-line offset.
            var firstLineTwips = AttrDouble(ind, "first-line");
            var hangingTwips = AttrDouble(ind, "hanging");
            if (firstLineTwips is { } fl)
                firstLine = TwipsToPoints(fl);
            else if (hangingTwips is { } hang)
                firstLine = -TwipsToPoints(hang);
        }

        return new ParagraphFormatting
        {
            Alignment = alignment,
            IndentLeftPt = indentLeft,
            IndentRightPt = indentRight,
            FirstLineIndentPt = firstLine,
        };
    }

    private static Table ReadTable(XElement tbl)
    {
        var table = new Table();
        foreach (var tr in tbl.Elements(W + "tr"))
        {
            var row = new TableRow();
            foreach (var tc in tr.Elements(W + "tc"))
            {
                var cell = new TableCell();
                foreach (var block in ReadBlocks(tc))
                {
                    if (block is Paragraph paragraph)
                        cell.Paragraphs.Add(paragraph);
                    // Nested tables are outside the modelled subset for 2003 read; their paragraphs
                    // (if any) are not flattened here to avoid inventing structure.
                }
                // A w:tc with no paragraph still exists structurally; keep one empty paragraph so the
                // cell is non-degenerate, matching how an empty docx cell holds an empty w:p.
                if (cell.Paragraphs.Count == 0)
                    cell.Paragraphs.Add(new Paragraph());
                row.Cells.Add(cell);
            }
            table.Rows.Add(row);
        }
        return table;
    }

    private static void ApplyPageSettings(XElement sectPr, PageSettings page)
    {
        var pgSz = sectPr.Element(W + "pgSz");
        if (pgSz is not null)
        {
            if (TwipsToPoints(AttrDouble(pgSz, "w")) is var w && w > 0)
                page.WidthPt = w;
            if (TwipsToPoints(AttrDouble(pgSz, "h")) is var h && h > 0)
                page.HeightPt = h;
            page.Landscape = string.Equals(
                (string?)pgSz.Attribute(W + "orient"), "landscape", StringComparison.OrdinalIgnoreCase);
        }

        var pgMar = sectPr.Element(W + "pgMar");
        if (pgMar is not null)
        {
            page.MarginLeftPt = TwipsToPoints(AttrDouble(pgMar, "left"));
            page.MarginRightPt = TwipsToPoints(AttrDouble(pgMar, "right"));
            page.MarginTopPt = TwipsToPoints(AttrDouble(pgMar, "top"));
            page.MarginBottomPt = TwipsToPoints(AttrDouble(pgMar, "bottom"));
        }
    }

    // --- helpers ------------------------------------------------------------

    /// <summary>
    /// A 2003 toggle element (<c>w:b</c>, <c>w:i</c>, …) is on when present unless its <c>w:val</c> explicitly
    /// turns it off ("off"/"false"/"0").
    /// </summary>
    private static bool IsToggleOn(XElement? element)
    {
        if (element is null)
            return false;
        var val = (string?)element.Attribute(W + "val");
        if (val is null)
            return true;
        return val is not ("off" or "false" or "0");
    }

    private static bool ReadUnderline(XElement? u)
    {
        if (u is null)
            return false;
        var val = (string?)u.Attribute(W + "val");
        // No val implies an underline; "none" turns it off.
        return val is not ("none" or "off" or "false" or "0");
    }

    private static TextAlignment ReadAlignment(string? jc) => jc switch
    {
        "center" => TextAlignment.Center,
        "right" => TextAlignment.Right,
        "both" or "distribute" or "justify" => TextAlignment.Justify,
        _ => TextAlignment.Left,
    };

    private static string? NormalizeColor(string? color)
    {
        if (string.IsNullOrEmpty(color) || string.Equals(color, "auto", StringComparison.OrdinalIgnoreCase))
            return null;
        var hex = color.StartsWith('#') ? color[1..] : color;
        if (hex.Length != 6)
            return null;
        return "#" + hex.ToUpperInvariant();
    }

    private static double? AttrDouble(XElement? element, string? localName = null)
    {
        if (element is null)
            return null;
        var attr = localName is null ? element.Attribute(W + "val") : element.Attribute(W + localName);
        if (attr is null)
            return null;
        return double.TryParse(attr.Value, NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static double TwipsToPoints(double? twips) => twips is { } t ? t / TwipsPerPoint : 0;
}

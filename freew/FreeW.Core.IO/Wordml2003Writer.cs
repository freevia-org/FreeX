using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using FreeW.Core.Model;

namespace FreeW.Core.IO;

/// <summary>
/// Writes a <see cref="TextDocument"/> as a Word 2003 WordprocessingML (<c>&lt;w:wordDocument&gt;</c> root in
/// the <c>http://schemas.microsoft.com/office/word/2003/wordml</c> namespace) single-file XML document.
/// This is the exact inverse of <see cref="Wordml2003Reader"/>: every element the reader consumes is emitted
/// here, so a document serialised by this writer round-trips faithfully through the reader.
///
/// Subset written (mirrors the subset read):
/// <list type="bullet">
///   <item><c>w:body/w:p/w:r/w:t</c> for paragraphs and their runs;</item>
///   <item><c>w:rPr</c> (<c>w:b</c>/<c>w:i</c>/<c>w:u</c>/<c>w:strike</c>/<c>w:rFonts</c>/<c>w:sz</c>/<c>w:color</c>) for run formatting;</item>
///   <item><c>w:pPr</c> (<c>w:jc</c>, <c>w:ind</c>) for paragraph formatting;</item>
///   <item><c>w:tbl</c>/<c>w:tr</c>/<c>w:tc</c> for tables;</item>
///   <item><c>w:sectPr</c> (page size/margins) from <see cref="PageSettings"/>.</item>
/// </list>
/// Features outside this subset (images, footnotes, comments, numbering, etc.) are silently dropped —
/// mirroring how the reader drops them on load.
/// </summary>
public static class Wordml2003Writer
{
    // The Word 2003 WordML namespace — same constant as in the reader.
    private static readonly XNamespace W = Wordml2003Reader.W;

    // OOXML measurements: twentieths-of-a-point (dxa) for indents/page geometry; half-points for font size.
    private const double TwipsPerPoint = 20.0;

    /// <summary>
    /// Writes <paramref name="document"/> to <paramref name="stream"/> as a Word 2003 WordprocessingML XML
    /// document. Does not close the caller's stream. The output includes the
    /// <c>&lt;?mso-application progid="Word.Document"?&gt;</c> processing instruction and is encoded UTF-8.
    /// </summary>
    public static void Write(TextDocument document, Stream stream)
    {
        var xml = BuildDocument(document);

        using var writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = false,
            CloseOutput = false,
        });
        xml.Save(writer);
    }

    /// <summary>
    /// Builds the complete <see cref="XDocument"/> for the Word 2003 WordML output, including the
    /// <c>&lt;?mso-application progid="Word.Document"?&gt;</c> processing instruction. Exposed for
    /// unit-test inspection.
    /// </summary>
    public static XDocument BuildDocument(TextDocument document)
    {
        var root = BuildWordDocument(document);

        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XProcessingInstruction("mso-application", "progid=\"Word.Document\""),
            root);
    }

    // -----------------------------------------------------------------------
    // Root element
    // -----------------------------------------------------------------------

    private static XElement BuildWordDocument(TextDocument document)
    {
        // <w:wordDocument xmlns:w="http://schemas.microsoft.com/office/word/2003/wordml">
        var wordDocument = new XElement(W + "wordDocument",
            new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName));

        var body = new XElement(W + "body");
        wordDocument.Add(body);

        // Shared across every paragraph in the document so bookmark ids stay unique document-wide (a
        // fresh per-paragraph counter would collide across paragraphs/cells).
        var nextBookmarkId = 0;

        // Body-level content blocks.
        foreach (var block in document.Blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    body.Add(BuildParagraph(paragraph, ref nextBookmarkId));
                    break;
                case Table table:
                    body.Add(BuildTable(table, ref nextBookmarkId));
                    break;
                // Other block kinds (unsupported by 2003 WordML writer) are skipped.
            }
        }

        // Trailing w:sectPr for page geometry.
        body.Add(BuildSectPr(document.Page));

        return wordDocument;
    }

    // -----------------------------------------------------------------------
    // Paragraphs
    // -----------------------------------------------------------------------

    private static XElement BuildParagraph(Paragraph paragraph, ref int nextBookmarkId)
    {
        var p = new XElement(W + "p");

        // w:pPr — only emitted when there is something non-default to write.
        var pPr = BuildParagraphProperties(paragraph.Formatting);
        if (pPr is not null)
            p.Add(pPr);

        // w:bookmarkStart — mark this paragraph as a named target *before* its runs so an internal
        // hyperlink elsewhere in the document (w:hlink w:bookmark="Name", see BuildHyperlink) has
        // somewhere to land. Without this, every internal link in the exported file is broken: the
        // reference is written but the target marker never was. w:bookmarkEnd closes each one after the
        // runs, mirroring how DocxWriter marks a whole-paragraph bookmark.
        var openBookmarkIds = new List<int>();
        foreach (var name in paragraph.BookmarkNames)
        {
            if (string.IsNullOrEmpty(name))
                continue;

            var id = nextBookmarkId++;
            openBookmarkIds.Add(id);
            p.Add(new XElement(W + "bookmarkStart",
                new XAttribute(W + "id", id),
                new XAttribute(W + "name", name)));
        }

        // w:r elements for each run.
        foreach (var run in paragraph.Runs)
        {
            var r = BuildRun(run);
            if (r is not null)
                p.Add(BuildHyperlink(run, r));
        }

        foreach (var id in openBookmarkIds)
            p.Add(new XElement(W + "bookmarkEnd", new XAttribute(W + "id", id)));

        return p;
    }

    private static XElement BuildHyperlink(Run run, XElement content)
    {
        if (string.IsNullOrEmpty(run.HyperlinkUrl) && string.IsNullOrEmpty(run.HyperlinkAnchor))
            return content;

        var hyperlink = new XElement(W + "hlink", content);
        if (!string.IsNullOrEmpty(run.HyperlinkUrl))
            hyperlink.Add(new XAttribute(W + "dest", run.HyperlinkUrl!));
        else
            hyperlink.Add(new XAttribute(W + "bookmark", run.HyperlinkAnchor!));
        if (!string.IsNullOrEmpty(run.HyperlinkTooltip))
            hyperlink.Add(new XAttribute(W + "tooltip", run.HyperlinkTooltip));
        return hyperlink;
    }

    private static XElement? BuildParagraphProperties(ParagraphFormatting fmt)
    {
        var elements = new List<XElement>();

        // w:jc — alignment
        var jc = AlignmentToken(fmt.Alignment);
        if (jc is not null)
            elements.Add(new XElement(W + "jc", new XAttribute(W + "val", jc)));

        // w:ind — indents
        if (fmt.IndentLeftPt != 0 || fmt.IndentRightPt != 0 || fmt.FirstLineIndentPt != 0)
        {
            var ind = new XElement(W + "ind");
            if (fmt.IndentLeftPt != 0)
                ind.Add(new XAttribute(W + "left", PointsToTwips(fmt.IndentLeftPt)));
            if (fmt.IndentRightPt != 0)
                ind.Add(new XAttribute(W + "right", PointsToTwips(fmt.IndentRightPt)));
            if (fmt.FirstLineIndentPt > 0)
                ind.Add(new XAttribute(W + "first-line", PointsToTwips(fmt.FirstLineIndentPt)));
            else if (fmt.FirstLineIndentPt < 0)
                ind.Add(new XAttribute(W + "hanging", PointsToTwips(-fmt.FirstLineIndentPt)));
            elements.Add(ind);
        }

        if (elements.Count == 0)
            return null;

        var pPr = new XElement(W + "pPr");
        pPr.Add(elements);
        return pPr;
    }

    // -----------------------------------------------------------------------
    // Runs
    // -----------------------------------------------------------------------

    private static XElement? BuildRun(Run run)
    {
        // Runs that carry only non-text content (images, equations, shapes, …) have no 2003 WordML
        // representation; emit them only when they have literal text.
        if (run.Image is not null || run.Equation is not null || run.Shape is not null ||
            run.WordArt is not null || run.Chart is not null || run.SmartArt is not null ||
            run.EmbeddedObject is not null || run.PreservedDrawing is not null)
        {
            if (string.IsNullOrEmpty(run.Text))
                return null;
        }

        var r = new XElement(W + "r");

        // w:rPr
        var rPr = BuildRunProperties(run.Formatting);
        if (rPr is not null)
            r.Add(rPr);

        // w:t — the run text.
        var text = run.Text;
        if (!string.IsNullOrEmpty(text))
        {
            var t = new XElement(W + "t", text);
            // Preserve leading/trailing whitespace with xml:space="preserve" when present.
            if (char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1]))
                t.Add(new XAttribute(XNamespace.Xml + "space", "preserve"));
            r.Add(t);
        }

        // A run with no rPr and no text is structurally empty; omit it.
        if (!r.HasElements)
            return null;

        return r;
    }

    private static XElement? BuildRunProperties(RunFormatting fmt)
    {
        var elements = new List<XElement>();

        if (fmt.Bold)
            elements.Add(new XElement(W + "b"));
        if (fmt.Italic)
            elements.Add(new XElement(W + "i"));
        if (fmt.Underline)
            elements.Add(new XElement(W + "u", new XAttribute(W + "val", "single")));
        if (fmt.Strikethrough)
            elements.Add(new XElement(W + "strike"));

        if (!string.IsNullOrEmpty(fmt.FontFamily))
        {
            elements.Add(new XElement(W + "rFonts",
                new XAttribute(W + "ascii", fmt.FontFamily),
                new XAttribute(W + "h-ansi", fmt.FontFamily)));
        }

        if (fmt.FontSizePt is { } sizePt && sizePt > 0)
        {
            // w:sz is in half-points.
            var halfPoints = (int)Math.Round(sizePt * 2, MidpointRounding.AwayFromZero);
            elements.Add(new XElement(W + "sz", new XAttribute(W + "val", halfPoints)));
        }

        if (!string.IsNullOrEmpty(fmt.ColorHex))
        {
            // Strip the leading '#' if present; emit as 6-char RRGGBB uppercase.
            var hex = fmt.ColorHex!.StartsWith('#') ? fmt.ColorHex[1..] : fmt.ColorHex;
            elements.Add(new XElement(W + "color", new XAttribute(W + "val", hex.ToUpperInvariant())));
        }

        // w:vertAlign — superscript/subscript. Schema position: near the end of rPr, matching the DOCX
        // EG_RPrBase ordering (after w:color, before w:rtl/w:cs).
        if (fmt.VerticalAlign is VerticalAlign.Superscript or VerticalAlign.Subscript)
            elements.Add(new XElement(W + "vertAlign",
                new XAttribute(W + "val", fmt.VerticalAlign == VerticalAlign.Superscript ? "superscript" : "subscript")));

        if (elements.Count == 0)
            return null;

        var rPr = new XElement(W + "rPr");
        rPr.Add(elements);
        return rPr;
    }

    // -----------------------------------------------------------------------
    // Tables
    // -----------------------------------------------------------------------

    private static XElement BuildTable(Table table, ref int nextBookmarkId)
    {
        var tbl = new XElement(W + "tbl");

        foreach (var row in table.Rows)
        {
            var tr = new XElement(W + "tr");
            foreach (var cell in row.Cells)
            {
                var tc = new XElement(W + "tc");
                foreach (var paragraph in cell.Paragraphs)
                    tc.Add(BuildParagraph(paragraph, ref nextBookmarkId));
                // A cell with no paragraphs gets one empty placeholder paragraph, mirroring the reader.
                if (!tc.HasElements)
                    tc.Add(new XElement(W + "p"));
                tr.Add(tc);
            }
            tbl.Add(tr);
        }

        return tbl;
    }

    // -----------------------------------------------------------------------
    // Section properties (page geometry)
    // -----------------------------------------------------------------------

    private static XElement BuildSectPr(PageSettings page)
    {
        var sectPr = new XElement(W + "sectPr");

        // w:pgSz — page dimensions and orientation.
        var pgSz = new XElement(W + "pgSz",
            new XAttribute(W + "w", PointsToTwips(page.WidthPt)),
            new XAttribute(W + "h", PointsToTwips(page.HeightPt)));
        if (page.Landscape)
            pgSz.Add(new XAttribute(W + "orient", "landscape"));
        sectPr.Add(pgSz);

        // w:pgMar — page margins.
        sectPr.Add(new XElement(W + "pgMar",
            new XAttribute(W + "top", PointsToTwips(page.MarginTopPt)),
            new XAttribute(W + "right", PointsToTwips(page.MarginRightPt)),
            new XAttribute(W + "bottom", PointsToTwips(page.MarginBottomPt)),
            new XAttribute(W + "left", PointsToTwips(page.MarginLeftPt))));

        return sectPr;
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static string? AlignmentToken(TextAlignment alignment) => alignment switch
    {
        TextAlignment.Center => "center",
        TextAlignment.Right => "right",
        TextAlignment.Justify => "both",
        _ => null, // Left is the default; don't emit w:jc.
    };

    /// <summary>Converts points to twips (twentieths of a point), rounded to nearest integer.</summary>
    private static int PointsToTwips(double points) =>
        (int)Math.Round(points * TwipsPerPoint, MidpointRounding.AwayFromZero);
}

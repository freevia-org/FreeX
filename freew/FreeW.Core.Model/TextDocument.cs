namespace FreeW.Core.Model;

/// <summary>
/// An inline raster image carried by a <see cref="Run"/>. Modelled at the run level (rather than as
/// a block) so it round-trips through docx as an inline w:drawing without touching paragraph storage.
/// PNG bytes only; size is in points to match the rest of the FreeW unit model.
/// </summary>
public sealed class InlineImage(byte[] pngBytes, double widthPt, double heightPt)
{
    /// <summary>The raw PNG image bytes (the only supported format).</summary>
    public byte[] PngBytes { get; } = pngBytes;
    public double WidthPt { get; set; } = widthPt;
    public double HeightPt { get; set; } = heightPt;
}

/// <summary>
/// A contiguous span of text sharing one run formatting, or — when <see cref="Image"/> is set — an
/// inline image anchored in the run flow. An image run carries no text (<see cref="Text"/> is empty).
/// </summary>
public sealed class Run(string text, RunFormatting? formatting = null)
{
    public string Text { get; set; } = text;
    public RunFormatting Formatting { get; set; } = formatting ?? RunFormatting.Default;

    /// <summary>Optional inline image. When non-null this run renders/serialises as a picture.</summary>
    public InlineImage? Image { get; set; }

    /// <summary>
    /// Optional external hyperlink target (absolute URL). When non-null the run is wrapped in a
    /// w:hyperlink on save, with the URL stored as an external relationship, and rendered as a link.
    /// </summary>
    public string? HyperlinkUrl { get; set; }

    /// <summary>
    /// When set, this run is a simple field rather than literal text — e.g. a PAGE field whose value
    /// is the current page number. The run's <see cref="Text"/> doubles as cached/fallback display
    /// text (the last computed value), so non-field-aware consumers still render something sensible.
    /// </summary>
    public RunFieldKind FieldKind { get; set; } = RunFieldKind.None;

    /// <summary>
    /// When set, this run is a footnote reference marker pointing at the footnote with this id in
    /// <see cref="TextDocument.Footnotes"/>. It carries no literal text of its own; the marker number
    /// is the id. Serialises as a superscript run wrapping a w:footnoteReference w:id="N".
    /// </summary>
    public int? FootnoteId { get; set; }

    /// <summary>Creates a run that carries an inline image instead of text.</summary>
    public static Run FromImage(InlineImage image) => new(string.Empty) { Image = image };

    /// <summary>Creates a page-number field run (renders as the current page number).</summary>
    public static Run PageNumberField(RunFormatting? formatting = null) =>
        new("1", formatting) { FieldKind = RunFieldKind.PageNumber };

    /// <summary>
    /// Creates a footnote-reference run for the footnote with id <paramref name="footnoteId"/>. The
    /// run renders as a superscript marker; its <see cref="Text"/> mirrors the id for field-unaware
    /// consumers. The matching content lives in <see cref="TextDocument.Footnotes"/>.
    /// </summary>
    public static Run FootnoteReference(int footnoteId, RunFormatting? formatting = null) =>
        new(footnoteId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            formatting ?? new RunFormatting { VerticalAlign = VerticalAlign.Superscript })
        {
            FootnoteId = footnoteId
        };
}

/// <summary>
/// A single footnote: an id (matching a body <see cref="Run.FootnoteId"/>) and its block content,
/// a list of paragraphs. Maps onto a w:footnote element inside word/footnotes.xml.
/// </summary>
public sealed class Footnote(int id)
{
    public int Id { get; } = id;
    public List<Paragraph> Content { get; } = [];

    public Footnote(int id, string text) : this(id) => Content.Add(new Paragraph(text));

    public string PlainText => string.Join("\n", Content.Select(p => p.PlainText));
}

/// <summary>
/// The kind of simple field a <see cref="Run"/> represents. <see cref="None"/> is an ordinary text
/// run; <see cref="PageNumber"/> maps to a WordprocessingML PAGE field (w:fldSimple w:instr=" PAGE ").
/// </summary>
public enum RunFieldKind
{
    None,
    PageNumber
}

/// <summary>
/// A top-level document block. The document body is an ordered sequence of blocks; today that is
/// paragraphs and tables, mirroring how WordprocessingML interleaves w:p and w:tbl inside w:body.
/// </summary>
public abstract class Block
{
}

/// <summary>A paragraph: an ordered sequence of runs plus paragraph formatting and an optional style.</summary>
public sealed class Paragraph : Block
{
    public List<Run> Runs { get; } = [];
    public ParagraphFormatting Formatting { get; set; } = ParagraphFormatting.Default;
    public string? StyleId { get; set; }

    public Paragraph() { }

    public Paragraph(string text)
    {
        if (text.Length > 0)
            Runs.Add(new Run(text));
    }

    public string PlainText => string.Concat(Runs.Select(r => r.Text));
}

/// <summary>A single table cell: a list of paragraphs (matching w:tc, which holds block content).</summary>
public sealed class TableCell
{
    public List<Paragraph> Paragraphs { get; } = [];

    public TableCell() { }

    public TableCell(string text) => Paragraphs.Add(new Paragraph(text));

    public string PlainText => string.Join("\n", Paragraphs.Select(p => p.PlainText));
}

/// <summary>A table row: an ordered sequence of cells (w:tr).</summary>
public sealed class TableRow
{
    public List<TableCell> Cells { get; } = [];
}

/// <summary>Minimal table-level formatting. Currently just whether cell borders are drawn.</summary>
public sealed record TableFormatting
{
    public bool Borders { get; init; } = true;

    public static readonly TableFormatting Default = new();
}

/// <summary>A table block: rows of cells, each cell holding paragraphs (w:tbl / w:tr / w:tc).</summary>
public sealed class Table : Block
{
    public List<TableRow> Rows { get; } = [];
    public TableFormatting Formatting { get; set; } = TableFormatting.Default;

    public Table() { }

    /// <summary>Create a uniform <paramref name="rows"/>x<paramref name="columns"/> table of empty cells.</summary>
    public static Table Create(int rows, int columns)
    {
        var table = new Table();
        for (var r = 0; r < rows; r++)
        {
            var row = new TableRow();
            for (var c = 0; c < columns; c++)
                row.Cells.Add(new TableCell(string.Empty));
            table.Rows.Add(row);
        }
        return table;
    }

    public int RowCount => Rows.Count;

    public int ColumnCount => Rows.Count == 0 ? 0 : Rows.Max(r => r.Cells.Count);
}

/// <summary>
/// Document-level metadata, mapping onto the OPC core properties part (docProps/core.xml). All
/// fields are optional; timestamps are explicit (never auto-stamped at construction) so the model
/// and writer stay deterministic. The writer emits only the values that are set.
/// </summary>
public sealed class DocumentProperties
{
    /// <summary>dc:title</summary>
    public string? Title { get; set; }

    /// <summary>dc:creator (the document's author).</summary>
    public string? Author { get; set; }

    /// <summary>dc:subject</summary>
    public string? Subject { get; set; }

    /// <summary>cp:keywords</summary>
    public string? Keywords { get; set; }

    /// <summary>dc:description (free-form comments).</summary>
    public string? Comments { get; set; }

    /// <summary>cp:lastModifiedBy</summary>
    public string? LastModifiedBy { get; set; }

    /// <summary>dcterms:created (W3CDTF).</summary>
    public DateTimeOffset? Created { get; set; }

    /// <summary>dcterms:modified (W3CDTF).</summary>
    public DateTimeOffset? Modified { get; set; }
}

/// <summary>
/// A page header or footer: an ordered list of paragraphs shown in the top (header) or bottom
/// (footer) margin of every page. Maps onto a WordprocessingML header/footer part (w:hdr / w:ftr).
/// A footer paragraph may contain a page-number field run (see <see cref="Run.PageNumberField"/>).
/// </summary>
public sealed class HeaderFooter
{
    public List<Paragraph> Paragraphs { get; } = [];

    public HeaderFooter() { }

    public HeaderFooter(string text) => Paragraphs.Add(new Paragraph(text));

    /// <summary>True when there is no visible content (no paragraphs, or only empty ones).</summary>
    public bool IsEmpty => Paragraphs.Count == 0 || Paragraphs.All(p => p.Runs.Count == 0);

    public string PlainText => string.Join("\n", Paragraphs.Select(p => p.PlainText));
}

/// <summary>Page geometry for a section (points; US Letter with 1in margins by default).</summary>
public sealed class PageSettings
{
    public double WidthPt { get; set; } = 612;
    public double HeightPt { get; set; } = 792;
    public double MarginLeftPt { get; set; } = 72;
    public double MarginRightPt { get; set; } = 72;
    public double MarginTopPt { get; set; } = 72;
    public double MarginBottomPt { get; set; } = 72;
    public bool Landscape { get; set; }
}

/// <summary>
/// The FreeW text document: ordered paragraphs, a style catalog, document-level defaults, and
/// page settings. Still intentionally lean, but now rich enough to carry real formatting and to
/// map onto WordprocessingML (document.xml / styles.xml) in a later milestone.
/// </summary>
public sealed class TextDocument
{
    /// <summary>The document body: an ordered sequence of blocks (paragraphs and tables).</summary>
    public List<Block> Blocks { get; } = [];
    public Dictionary<string, DocumentStyle> Styles { get; } = [];
    public RunFormatting DefaultRun { get; set; } = new() { FontFamily = "Calibri", FontSizePt = 11 };
    public ParagraphFormatting DefaultParagraph { get; set; } = ParagraphFormatting.Default;
    public PageSettings Page { get; } = new();

    /// <summary>
    /// The default page header (top margin), or null when the document has no header. Maps to a
    /// word/header1.xml part referenced from w:sectPr via w:headerReference w:type="default".
    /// </summary>
    public HeaderFooter? Header { get; set; }

    /// <summary>
    /// The default page footer (bottom margin), or null when the document has no footer. Maps to a
    /// word/footer1.xml part referenced from w:sectPr via w:footerReference w:type="default".
    /// </summary>
    public HeaderFooter? Footer { get; set; }

    /// <summary>Document-level metadata (maps to docProps/core.xml).</summary>
    public DocumentProperties Properties { get; } = new();

    /// <summary>
    /// The document's footnotes, keyed by footnote id (matching <see cref="Run.FootnoteId"/> on the
    /// body reference runs). Maps to word/footnotes.xml (w:footnotes / w:footnote w:id="N"). Empty
    /// when the document has no footnotes, in which case no footnotes part is emitted.
    /// </summary>
    public Dictionary<int, Footnote> Footnotes { get; } = [];

    /// <summary>The next unused footnote id (1-based; ignores the reserved separator ids -1 and 0).</summary>
    public int NextFootnoteId() => Footnotes.Count == 0 ? 1 : Math.Max(0, Footnotes.Keys.Max()) + 1;

    /// <summary>The body's paragraphs (top-level only; table cell paragraphs are not included).</summary>
    public IEnumerable<Paragraph> Paragraphs => Blocks.OfType<Paragraph>();

    public static TextDocument CreateEmpty()
    {
        var doc = new TextDocument();
        doc.AddBuiltInStyles();
        doc.Blocks.Add(new Paragraph());
        return doc;
    }

    public string PlainText => string.Join("\n", Blocks.Select(BlockPlainText));

    private static string BlockPlainText(Block block) => block switch
    {
        Paragraph p => p.PlainText,
        Table t => string.Join("\n", t.Rows.Select(r => string.Join("\t", r.Cells.Select(c => c.PlainText)))),
        _ => string.Empty
    };

    private void AddBuiltInStyles()
    {
        Styles["Normal"] = new DocumentStyle { Id = "Normal", Name = "Normal" };
        Styles["Heading1"] = new DocumentStyle
        {
            Id = "Heading1",
            Name = "Heading 1",
            BasedOnStyleId = "Normal",
            Run = new RunFormatting { Bold = true, FontSizePt = 16, ColorHex = "#2F5496" },
            Paragraph = new ParagraphFormatting { SpaceBeforePt = 12, SpaceAfterPt = 4 }
        };
        Styles["Heading2"] = new DocumentStyle
        {
            Id = "Heading2",
            Name = "Heading 2",
            BasedOnStyleId = "Normal",
            Run = new RunFormatting { Bold = true, FontSizePt = 13, ColorHex = "#2F5496" },
            Paragraph = new ParagraphFormatting { SpaceBeforePt = 10, SpaceAfterPt = 4 }
        };
        Styles["Heading3"] = new DocumentStyle
        {
            Id = "Heading3",
            Name = "Heading 3",
            BasedOnStyleId = "Normal",
            Run = new RunFormatting { Bold = true, FontSizePt = 12, ColorHex = "#1F3864" },
            Paragraph = new ParagraphFormatting { SpaceBeforePt = 8, SpaceAfterPt = 4 }
        };
        Styles["Title"] = new DocumentStyle
        {
            Id = "Title",
            Name = "Title",
            BasedOnStyleId = "Normal",
            Run = new RunFormatting { Bold = true, FontSizePt = 28 },
            Paragraph = new ParagraphFormatting { SpaceAfterPt = 8 }
        };
        Styles["Subtitle"] = new DocumentStyle
        {
            Id = "Subtitle",
            Name = "Subtitle",
            BasedOnStyleId = "Normal",
            Run = new RunFormatting { Italic = true, FontSizePt = 15, ColorHex = "#5A5A5A" },
            Paragraph = new ParagraphFormatting { SpaceAfterPt = 8 }
        };
        Styles["Quote"] = new DocumentStyle
        {
            Id = "Quote",
            Name = "Quote",
            BasedOnStyleId = "Normal",
            Run = new RunFormatting { Italic = true, ColorHex = "#404040" },
            Paragraph = new ParagraphFormatting
            {
                SpaceBeforePt = 10,
                SpaceAfterPt = 10,
                IndentLeftPt = 36,
                IndentRightPt = 36
            }
        };
    }
}

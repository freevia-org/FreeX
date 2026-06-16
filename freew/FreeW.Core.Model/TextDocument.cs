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
    /// Mutually exclusive with <see cref="HyperlinkAnchor"/>: a run links either externally or
    /// internally, never both.
    /// </summary>
    public string? HyperlinkUrl { get; set; }

    /// <summary>
    /// Optional internal hyperlink target: the name of a bookmark elsewhere in this document (see
    /// <see cref="Paragraph.BookmarkName"/>). When non-null the run is wrapped in a
    /// w:hyperlink w:anchor="…" on save (no relationship) and rendered as a link that jumps to the
    /// bookmark. Mutually exclusive with <see cref="HyperlinkUrl"/>.
    /// </summary>
    public string? HyperlinkAnchor { get; set; }

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

    /// <summary>
    /// When set, this run is covered by the review comment with this id in
    /// <see cref="TextDocument.Comments"/>. The covered span serialises with a w:commentRangeStart /
    /// w:commentRangeEnd pair bracketing the run(s), and a trailing reference run (see
    /// <see cref="IsCommentReference"/>) carries the w:commentReference. Consecutive runs sharing the
    /// same id form one comment range.
    /// </summary>
    public int? CommentId { get; set; }

    /// <summary>
    /// When true together with <see cref="CommentId"/>, this run is the comment's anchor marker — it
    /// carries no literal text and serialises as a run wrapping w:commentReference w:id="N". One such
    /// run is emitted immediately after the commented range's w:commentRangeEnd.
    /// </summary>
    public bool IsCommentReference { get; set; }

    /// <summary>
    /// Tracked-change (revision) mark on this run. <see cref="RevisionKind.None"/> is an ordinary run;
    /// <see cref="RevisionKind.Inserted"/> is a tracked insertion (serialises wrapped in w:ins, rendered
    /// underlined in the revision colour); <see cref="RevisionKind.Deleted"/> is a tracked deletion (the
    /// text is kept in the model but serialises wrapped in w:del with w:delText, rendered struck-through).
    /// Mirrors how <see cref="CommentId"/>/<see cref="FootnoteId"/> are modelled as optional run marks.
    /// </summary>
    public RevisionKind Revision { get; set; } = RevisionKind.None;

    /// <summary>The revision author (w:author on w:ins/w:del). Null when the run carries no revision.</summary>
    public string? RevisionAuthor { get; set; }

    /// <summary>
    /// The revision timestamp as a W3CDTF string (the w:date on w:ins/w:del), or null when unset. Kept
    /// as an explicit string (never auto-stamped) so the writer stays deterministic, matching how
    /// <see cref="Comment.DateXml"/> is modelled.
    /// </summary>
    public string? RevisionDateXml { get; set; }

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

    /// <summary>
    /// Creates the textless anchor run for the comment with id <paramref name="commentId"/>. It
    /// serialises as a run wrapping a w:commentReference and is emitted just after the commented
    /// range's w:commentRangeEnd. The matching content lives in <see cref="TextDocument.Comments"/>.
    /// </summary>
    public static Run CommentReference(int commentId) =>
        new(string.Empty) { CommentId = commentId, IsCommentReference = true };
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
/// A single review comment: an id (matching the body runs' <see cref="Run.CommentId"/>), an author
/// and initials, an optional explicit date, and the comment's block content as a list of paragraphs.
/// Maps onto a w:comment element inside word/comments.xml. The date is an explicit model value (never
/// auto-stamped) so the writer stays deterministic — it is only emitted when set.
/// </summary>
public sealed class Comment(int id)
{
    public int Id { get; } = id;

    /// <summary>The comment author's display name (w:author). Empty when unknown.</summary>
    public string Author { get; set; } = string.Empty;

    /// <summary>The author's initials (w:initials). Empty when unknown.</summary>
    public string Initials { get; set; } = string.Empty;

    /// <summary>
    /// The comment's timestamp as a W3CDTF string (w:date), or null when unset. Kept as a string so
    /// the writer never stamps a non-deterministic <c>DateTime.Now</c>; callers set it explicitly.
    /// </summary>
    public string? DateXml { get; set; }

    public List<Paragraph> Content { get; } = [];

    public Comment(int id, string text, string author = "", string initials = "") : this(id)
    {
        Author = author;
        Initials = initials;
        Content.Add(new Paragraph(text));
    }

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
/// The tracked-change state of a <see cref="Run"/>. <see cref="None"/> is an ordinary run;
/// <see cref="Inserted"/> is a tracked insertion (w:ins); <see cref="Deleted"/> is a tracked deletion
/// (w:del, whose text serialises as w:delText and is kept in the model until the change is accepted).
/// </summary>
public enum RevisionKind
{
    None,
    Inserted,
    Deleted
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

    /// <summary>
    /// Optional bookmark name marking this paragraph as a navigation target. When non-null the
    /// paragraph is bracketed by w:bookmarkStart/w:bookmarkEnd on save, and runs elsewhere can point
    /// to it via <see cref="Run.HyperlinkAnchor"/>. Bookmarks are invisible markers (no glyphs).
    /// </summary>
    public string? BookmarkName { get; set; }

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

    /// <summary>
    /// Cell background shading as an RRGGBB hex (e.g. <c>"#FFFF00"</c>). Null means no shading.
    /// Round-trips to docx as cell shading (<c>tc/tcPr/w:shd w:fill</c>), mirroring
    /// <see cref="ParagraphFormatting.ShadingColorHex"/> and <see cref="RunFormatting.HighlightColorHex"/>.
    /// </summary>
    public string? ShadingColorHex { get; set; }

    /// <summary>
    /// Preferred cell width in points (<c>tc/tcPr/w:tcW</c>), or null for automatic width. Optional so
    /// existing cells are unaffected.
    /// </summary>
    public double? WidthPt { get; set; }

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

    /// <summary>
    /// Per-column widths in points, one entry per column, matching the docx table grid
    /// (<c>w:tbl/w:tblGrid/w:gridCol</c>). Empty when no explicit grid is known (the default), so
    /// existing tables are unaffected.
    /// </summary>
    public List<double> ColumnWidthsPt { get; } = [];

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

    /// <summary>
    /// The number of equal-width text columns the page content flows into (w:sectPr/w:cols w:num).
    /// Defaults to 1 (single column) so existing documents are unaffected. Always at least 1.
    /// </summary>
    public int ColumnCount { get; set; } = 1;

    /// <summary>
    /// The gap between adjacent columns in points (w:sectPr/w:cols w:space). Defaults to 36 points
    /// (half an inch), Word's default column spacing. Only meaningful when <see cref="ColumnCount"/> &gt; 1.
    /// </summary>
    public double ColumnSpacingPt { get; set; } = 36;
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

    /// <summary>
    /// The document's review comments, keyed by comment id (matching the body runs' <see cref="Run.CommentId"/>).
    /// Maps to word/comments.xml (w:comments / w:comment w:id="N"). Empty when the document has no
    /// comments, in which case no comments part is emitted.
    /// </summary>
    public Dictionary<int, Comment> Comments { get; } = [];

    /// <summary>The next unused comment id (0-based, as Word numbers comments from 0).</summary>
    public int NextCommentId() => Comments.Count == 0 ? 0 : Comments.Keys.Max() + 1;

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

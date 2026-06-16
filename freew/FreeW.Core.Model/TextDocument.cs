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

    /// <summary>Creates a run that carries an inline image instead of text.</summary>
    public static Run FromImage(InlineImage image) => new(string.Empty) { Image = image };
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

    /// <summary>Document-level metadata (maps to docProps/core.xml).</summary>
    public DocumentProperties Properties { get; } = new();

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
        Styles["Title"] = new DocumentStyle
        {
            Id = "Title",
            Name = "Title",
            BasedOnStyleId = "Normal",
            Run = new RunFormatting { Bold = true, FontSizePt = 28 },
            Paragraph = new ParagraphFormatting { SpaceAfterPt = 8 }
        };
    }
}

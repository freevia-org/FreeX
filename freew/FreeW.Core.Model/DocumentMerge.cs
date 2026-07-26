namespace FreeW.Core.Model;

/// <summary>
/// Pure helpers for merging the body blocks of one document into another — the model side of
/// "Insert Text from File", which opens a second .docx and drops its content at the caret. The clone
/// is deep enough that the inserted blocks are fully independent of the source: paragraphs get fresh
/// <see cref="Run"/> copies (carrying their formatting and run marks), tables get fresh rows/cells, so
/// editing the merged target never mutates the source and vice versa. The small immutable formatting
/// records (<see cref="RunFormatting"/>, <see cref="ParagraphFormatting"/>, <see cref="TableFormatting"/>,
/// <see cref="ContentControl"/>, <see cref="InlineImage"/> byte arrays) are shared by reference, which is
/// safe precisely because they are immutable/never reassigned through the cloned graph.
/// </summary>
public static class DocumentMerge
{
    /// <summary>
    /// Deep-clone the body blocks of <paramref name="source"/> so they can be inserted into another
    /// document without aliasing the source. Paragraphs and tables are copied; the source is left
    /// untouched. Returns a fresh list of fresh block instances, in document order.
    /// </summary>
    public static IReadOnlyList<Block> CloneBlocks(TextDocument source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var clones = new List<Block>(source.Blocks.Count);
        foreach (var block in source.Blocks)
            clones.Add(CloneBlock(block));
        return clones;
    }

    /// <summary>
    /// Insert <paramref name="blocks"/> into <paramref name="target"/>'s body starting at
    /// <paramref name="index"/> (clamped to the body), preserving their order. The blocks are inserted
    /// as-is (callers that need independence from another document pass the result of
    /// <see cref="CloneBlocks"/>).
    /// </summary>
    public static void InsertBlocksAt(TextDocument target, int index, IEnumerable<Block> blocks)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(blocks);
        var at = Math.Clamp(index, 0, target.Blocks.Count);
        foreach (var block in blocks)
            target.Blocks.Insert(at++, block);
    }

    /// <summary>
    /// Deep-clone the body blocks of <paramref name="source"/> and insert them into
    /// <paramref name="target"/> at <paramref name="index"/> (clamped). The source is left untouched and
    /// the target receives independent copies. Returns the cloned blocks that were inserted.
    /// </summary>
    public static IReadOnlyList<Block> Merge(TextDocument target, int index, TextDocument source)
    {
        ArgumentNullException.ThrowIfNull(target);
        var clones = CloneBlocks(source);
        InsertBlocksAt(target, index, clones);
        return clones;
    }

    /// <summary>Deep-clone a single body block (paragraph or table). Unknown block kinds are passed through.</summary>
    public static Block CloneBlock(Block block) => block switch
    {
        Paragraph p => CloneParagraph(p),
        Table t => CloneTable(t),
        _ => block
    };

    private static Paragraph CloneParagraph(Paragraph source)
    {
        var clone = new Paragraph
        {
            BlockContentControl = source.BlockContentControl,
            Formatting = source.Formatting,
            StyleId = source.StyleId,
            DropCap = source.DropCap,
        };
        clone.BookmarkNames.AddRange(source.BookmarkNames);
        foreach (var run in source.Runs)
            clone.Runs.Add(CloneRun(run));
        return clone;
    }

    private static Run CloneRun(Run source) => new(source.Text, source.Formatting)
    {
        Image = source.Image?.Clone(),
        WordArt = source.WordArt?.Clone(),
        SmartArt = source.SmartArt is { } smartArt ? SmartArtCommandCopy.Clone(smartArt) : null,
        HyperlinkUrl = source.HyperlinkUrl,
        HyperlinkAnchor = source.HyperlinkAnchor,
        HyperlinkTooltip = source.HyperlinkTooltip,
        FieldKind = source.FieldKind,
        FootnoteId = source.FootnoteId,
        EndnoteId = source.EndnoteId,
        CommentId = source.CommentId,
        IsCommentReference = source.IsCommentReference,
        Revision = source.Revision,
        Control = source.Control, // immutable record — safe to share
        Citation = source.Citation, // immutable — safe to share
        CrossReference = source.CrossReference, // immutable record — safe to share
        ComplexField = source.ComplexField, // immutable record — safe to share
        RevisionAuthor = source.RevisionAuthor,
        RevisionDateXml = source.RevisionDateXml
    };

    private static Table CloneTable(Table source)
    {
        var clone = new Table
        {
            BlockContentControl = source.BlockContentControl,
            Formatting = source.Formatting,
            Borders = source.Borders
        };
        clone.ColumnWidthsPt.AddRange(source.ColumnWidthsPt);
        foreach (var row in source.Rows)
        {
            var rowClone = new TableRow();
            foreach (var cell in row.Cells)
                rowClone.Cells.Add(CloneCell(cell));
            clone.Rows.Add(rowClone);
        }
        return clone;
    }

    private static TableCell CloneCell(TableCell source)
    {
        var clone = new TableCell
        {
            ShadingColorHex = source.ShadingColorHex,
            WidthPt = source.WidthPt,
            GridSpan = source.GridSpan,
            VerticalMerge = source.VerticalMerge
        };
        foreach (var paragraph in source.Paragraphs)
            clone.Paragraphs.Add(CloneParagraph(paragraph));
        return clone;
    }
}

using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Editing;

/// <summary>
/// AV-TRACKEDIT: the table-cell sibling of <see cref="SetParagraphMarkRevisionCommand"/>. Sets (or clears)
/// the <see cref="Paragraph.MarkRevision"/> of one paragraph INSIDE a table cell, snapshotting the previous
/// mark state for undo. Used by a tracked Backspace at the start of a cell paragraph / Delete at the end of
/// one: rather than physically splicing the two cell paragraphs together immediately (which would silently
/// bypass Track Changes), the boundary's owning paragraph stays in place with its mark flagged
/// <see cref="RevisionKind.Deleted"/>, and the two only actually merge when
/// <see cref="TrackChanges.AcceptAll"/> resolves the mark (its cell walk,
/// <c>ResolveParagraphContainer</c>, already merges marked cell paragraphs).
///
/// Cell addressing uses the same (blockIndex, rowIndex, cellStartCol) tuple as
/// <see cref="SpliceCellParagraphsCommand"/> and <see cref="ReplaceCellParagraphRunsCommand"/>.
/// </summary>
internal sealed class SetCellParagraphMarkRevisionCommand(
    int blockIndex,
    int rowIndex,
    int cellStartCol,
    int paraIndex,
    RevisionKind kind,
    string? author,
    string? dateXml) : IDocumentCommand
{
    private RevisionKind _previousKind;
    private string? _previousAuthor;
    private string? _previousDateXml;
    private bool _applied;

    public string Label => kind == RevisionKind.Deleted ? "Delete Paragraph Mark" : "Insert Paragraph Mark";

    public void Apply(IDocumentCommandContext context)
    {
        if (!TryGetParagraph(context, out var paragraph))
            return;
        _previousKind = paragraph.MarkRevision;
        _previousAuthor = paragraph.MarkRevisionAuthor;
        _previousDateXml = paragraph.MarkRevisionDateXml;
        paragraph.MarkRevision = kind;
        paragraph.MarkRevisionAuthor = author;
        paragraph.MarkRevisionDateXml = dateXml;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || !TryGetParagraph(context, out var paragraph))
            return;
        paragraph.MarkRevision = _previousKind;
        paragraph.MarkRevisionAuthor = _previousAuthor;
        paragraph.MarkRevisionDateXml = _previousDateXml;
        _applied = false;
    }

    private bool TryGetParagraph(IDocumentCommandContext context, out Paragraph paragraph)
    {
        paragraph = null!;
        if (blockIndex < 0 || blockIndex >= context.Document.Blocks.Count)
            return false;
        if (context.Document.Blocks[blockIndex] is not Table table)
            return false;
        if (rowIndex < 0 || rowIndex >= table.Rows.Count)
            return false;
        var col = 0;
        foreach (var cell in table.Rows[rowIndex].Cells)
        {
            if (col == cellStartCol)
            {
                if (paraIndex < 0 || paraIndex >= cell.Paragraphs.Count)
                    return false;
                paragraph = cell.Paragraphs[paraIndex];
                return true;
            }
            col += Math.Max(1, cell.GridSpan);
        }
        return false;
    }
}

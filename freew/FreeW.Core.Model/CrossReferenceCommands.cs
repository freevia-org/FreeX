namespace FreeW.Core.Model;

/// <summary>Append a cross-reference field and its optional auto-bookmark as one undoable edit.</summary>
public sealed class InsertCrossReferenceCommand(
    int hostBlockIndex,
    Run fieldRun,
    int? targetBlockIndex,
    string? bookmarkName,
    int? targetRunIndex = null,
    int? targetNoteId = null,
    bool? targetIsFootnote = null) : IDocumentCommand
{
    private Run[]? _previousHostRuns;
    private string[]? _previousTargetBookmarks;
    private BookmarkBoundary[]? _previousTargetBookmarkBoundaries;

    public string Label => "Insert Cross-reference";

    public void Apply(IDocumentCommandContext context)
    {
        if (ParagraphAt(context, hostBlockIndex) is not { } host)
            return;

        _previousHostRuns = [.. host.Runs];
        if (!string.IsNullOrWhiteSpace(bookmarkName)
            && TargetParagraph(context) is { } target)
        {
            _previousTargetBookmarks = [.. target.BookmarkNames];
            _previousTargetBookmarkBoundaries = [.. target.BookmarkBoundaries];
            if (!target.BookmarkNames.Contains(bookmarkName, StringComparer.Ordinal))
                target.BookmarkNames.Add(bookmarkName);
            if (targetRunIndex is { } runIndex && runIndex >= 0 && runIndex < target.Runs.Count)
            {
                var pairKey = "auto:" + bookmarkName;
                target.BookmarkBoundaries.Add(new BookmarkBoundary(
                    pairKey,
                    BookmarkBoundaryKind.Start,
                    runIndex,
                    bookmarkName));
                target.BookmarkBoundaries.Add(new BookmarkBoundary(
                    pairKey,
                    BookmarkBoundaryKind.End,
                    runIndex + 1));
            }
        }

        host.Runs.Add(fieldRun);
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_previousHostRuns is null || ParagraphAt(context, hostBlockIndex) is not { } host)
            return;

        host.Runs.Clear();
        host.Runs.AddRange(_previousHostRuns);

        if (_previousTargetBookmarks is not null
            && TargetParagraph(context) is { } target)
        {
            target.BookmarkNames.Clear();
            target.BookmarkNames.AddRange(_previousTargetBookmarks);
            target.BookmarkBoundaries.Clear();
            target.BookmarkBoundaries.AddRange(_previousTargetBookmarkBoundaries ?? []);
        }

        _previousHostRuns = null;
        _previousTargetBookmarks = null;
        _previousTargetBookmarkBoundaries = null;
    }

    private static Paragraph? ParagraphAt(IDocumentCommandContext context, int blockIndex) =>
        blockIndex >= 0 && blockIndex < context.Document.Blocks.Count
            ? context.Document.Blocks[blockIndex] as Paragraph
            : null;

    private Paragraph? TargetParagraph(IDocumentCommandContext context)
    {
        if (targetNoteId is { } noteId && targetIsFootnote is { } footnote)
        {
            return EnumerateBodyParagraphs(context.Document.Blocks).FirstOrDefault(paragraph =>
                paragraph.Runs.Any(run => (footnote ? run.FootnoteId : run.EndnoteId) == noteId));
        }

        return targetBlockIndex is { } targetIndex
            ? ParagraphAt(context, targetIndex)
            : null;
    }

    private static IEnumerable<Paragraph> EnumerateBodyParagraphs(IEnumerable<Block> blocks)
    {
        foreach (var block in blocks)
        {
            if (block is Paragraph paragraph)
            {
                yield return paragraph;
                continue;
            }

            if (block is not Table table)
                continue;

            foreach (var cellParagraph in table.Rows
                         .SelectMany(row => row.Cells)
                         .SelectMany(cell => cell.Paragraphs))
            {
                yield return cellParagraph;
            }
        }
    }
}

namespace FreeW.Core.Model;

/// <summary>Append a cross-reference field and its optional auto-bookmark as one undoable edit.</summary>
public sealed class InsertCrossReferenceCommand(
    int hostBlockIndex,
    Run fieldRun,
    int? targetBlockIndex,
    string? bookmarkName,
    int? targetRunIndex = null,
    int? targetNoteId = null,
    bool? targetIsFootnote = null,
    int? targetTextStartOffset = null,
    int? targetTextEndOffset = null) : IDocumentCommand
{
    private Run[]? _previousHostRuns;
    private Run[]? _previousTargetRuns;
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
            _previousTargetRuns = [.. target.Runs];
            if (!target.BookmarkNames.Contains(bookmarkName, StringComparer.Ordinal))
                target.BookmarkNames.Add(bookmarkName);
            if (targetTextStartOffset is { } textStart
                && targetTextEndOffset is { } textEnd
                && textStart >= 0
                && textEnd >= textStart
                && textEnd <= target.PlainText.Length)
            {
                var previousBoundaryPositions = BookmarkBoundaryMapper.Capture(target);
                BookmarkBoundaryMapper.EnsureRunBoundaryAtTextOffset(target, textEnd);
                BookmarkBoundaryMapper.EnsureRunBoundaryAtTextOffset(target, textStart);
                BookmarkBoundaryMapper.Restore(target, previousBoundaryPositions);
                AddBookmarkBoundaries(
                    target,
                    bookmarkName,
                    BookmarkBoundaryMapper.EnsureRunBoundaryAtTextOffset(target, textStart),
                    BookmarkBoundaryMapper.EnsureRunBoundaryAtTextOffset(target, textEnd));
            }
            else if (targetRunIndex is { } runIndex && runIndex >= 0 && runIndex < target.Runs.Count)
            {
                AddBookmarkBoundaries(target, bookmarkName, runIndex, runIndex + 1);
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
            target.Runs.Clear();
            target.Runs.AddRange(_previousTargetRuns ?? []);
            target.BookmarkBoundaries.Clear();
            target.BookmarkBoundaries.AddRange(_previousTargetBookmarkBoundaries ?? []);
        }

        _previousHostRuns = null;
        _previousTargetRuns = null;
        _previousTargetBookmarks = null;
        _previousTargetBookmarkBoundaries = null;
    }

    private static void AddBookmarkBoundaries(
        Paragraph target,
        string bookmarkName,
        int startRunIndex,
        int endRunIndex)
    {
        var pairKey = "auto:" + bookmarkName;
        target.BookmarkBoundaries.Add(new BookmarkBoundary(
            pairKey,
            BookmarkBoundaryKind.Start,
            startRunIndex,
            bookmarkName));
        target.BookmarkBoundaries.Add(new BookmarkBoundary(
            pairKey,
            BookmarkBoundaryKind.End,
            endRunIndex));
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

            foreach (var row in table.Rows)
            {
                foreach (var cell in row.Cells)
                {
                    foreach (var cellParagraph in cell.Paragraphs)
                        yield return cellParagraph;
                    foreach (var nestedTable in cell.NestedTables)
                        foreach (var nestedParagraph in EnumerateBodyParagraphs([nestedTable]))
                            yield return nestedParagraph;
                }
            }
        }
    }
}

namespace FreeW.Core.Model;

/// <summary>Append a cross-reference field and its optional auto-bookmark as one undoable edit.</summary>
public sealed class InsertCrossReferenceCommand(
    int hostBlockIndex,
    Run fieldRun,
    int? targetBlockIndex,
    string? bookmarkName) : IDocumentCommand
{
    private Run[]? _previousHostRuns;
    private string[]? _previousTargetBookmarks;

    public string Label => "Insert Cross-reference";

    public void Apply(IDocumentCommandContext context)
    {
        if (ParagraphAt(context, hostBlockIndex) is not { } host)
            return;

        _previousHostRuns = [.. host.Runs];
        if (targetBlockIndex is { } targetIndex
            && !string.IsNullOrWhiteSpace(bookmarkName)
            && ParagraphAt(context, targetIndex) is { } target)
        {
            _previousTargetBookmarks = [.. target.BookmarkNames];
            if (!target.BookmarkNames.Contains(bookmarkName, StringComparer.Ordinal))
                target.BookmarkNames.Add(bookmarkName);
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
            && targetBlockIndex is { } targetIndex
            && ParagraphAt(context, targetIndex) is { } target)
        {
            target.BookmarkNames.Clear();
            target.BookmarkNames.AddRange(_previousTargetBookmarks);
        }

        _previousHostRuns = null;
        _previousTargetBookmarks = null;
    }

    private static Paragraph? ParagraphAt(IDocumentCommandContext context, int blockIndex) =>
        blockIndex >= 0 && blockIndex < context.Document.Blocks.Count
            ? context.Document.Blocks[blockIndex] as Paragraph
            : null;
}

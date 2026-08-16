namespace FreeW.Core.Model;

/// <summary>Set the primary bookmark name on a body paragraph while preserving undo state.</summary>
public sealed class SetParagraphBookmarkNameCommand(int blockIndex, string? name) : IDocumentCommand
{
    private string[]? _previous;

    public string Label => string.IsNullOrWhiteSpace(name) ? "Remove Bookmark" : "Add Bookmark";

    public void Apply(IDocumentCommandContext context)
    {
        if (ParagraphAt(context) is not { } paragraph)
            return;

        _previous = [.. paragraph.BookmarkNames];
        paragraph.BookmarkName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_previous is null || ParagraphAt(context) is not { } paragraph)
            return;

        paragraph.BookmarkNames.Clear();
        paragraph.BookmarkNames.AddRange(_previous);
        _previous = null;
    }

    private Paragraph? ParagraphAt(IDocumentCommandContext context) =>
        blockIndex >= 0 && blockIndex < context.Document.Blocks.Count
            ? context.Document.Blocks[blockIndex] as Paragraph
            : null;
}

/// <summary>Remove every occurrence of one bookmark name while preserving undo state.</summary>
public sealed class RemoveBookmarkCommand(string name) : IDocumentCommand
{
    private List<(Paragraph Paragraph, string[] Names)>? _previous;

    public string Label => "Delete Bookmark";

    public void Apply(IDocumentCommandContext context)
    {
        _previous = context.Document.Blocks
            .OfType<Paragraph>()
            .Where(paragraph => paragraph.BookmarkNames.Contains(name, StringComparer.Ordinal))
            .Select(paragraph => (paragraph, paragraph.BookmarkNames.ToArray()))
            .ToList();
        Bookmarks.RemoveBookmark(context.Document, name);
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_previous is null)
            return;

        foreach (var (paragraph, names) in _previous)
        {
            paragraph.BookmarkNames.Clear();
            paragraph.BookmarkNames.AddRange(names);
        }
        _previous = null;
    }
}

/// <summary>
/// Remove exactly one bookmark instance — the paragraph at <paramref name="location"/> — while preserving
/// undo state. Unlike <see cref="RemoveBookmarkCommand"/> (which clears every paragraph sharing the name),
/// this targets a single location, so deleting one duplicate-named bookmark in the Bookmark Manager never
/// silently removes a different one that happens to share its name.
/// </summary>
public sealed class RemoveBookmarkAtCommand(BookmarkLocation location) : IDocumentCommand
{
    private string[]? _previous;

    public string Label => "Delete Bookmark";

    public void Apply(IDocumentCommandContext context)
    {
        if (Bookmarks.ResolveLocation(context.Document, location) is not { } paragraph)
            return;

        _previous = [.. paragraph.BookmarkNames];
        paragraph.BookmarkNames.RemoveAll(n => string.Equals(n, location.Name, StringComparison.Ordinal));
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_previous is null || Bookmarks.ResolveLocation(context.Document, location) is not { } paragraph)
            return;

        paragraph.BookmarkNames.Clear();
        paragraph.BookmarkNames.AddRange(_previous);
        _previous = null;
    }
}

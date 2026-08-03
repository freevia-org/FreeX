using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Editing;

internal sealed class SetNoteNumberingOptionsCommand(
    NoteNumberFormat footnoteFormat,
    int footnoteStartAt,
    NoteNumberRestart footnoteRestart,
    NoteNumberFormat endnoteFormat,
    int endnoteStartAt,
    NoteNumberRestart endnoteRestart) : IDocumentCommand
{
    private (NoteNumberFormat Format, int StartAt, NoteNumberRestart Restart) _previousFootnote;
    private (NoteNumberFormat Format, int StartAt, NoteNumberRestart Restart) _previousEndnote;
    private bool _applied;

    public string Label => "Footnote and Endnote Options";

    public void Apply(IDocumentCommandContext context)
    {
        _previousFootnote = Snapshot(context.Document.FootnoteNumbering);
        _previousEndnote = Snapshot(context.Document.EndnoteNumbering);
        Apply(context.Document.FootnoteNumbering, footnoteFormat, footnoteStartAt, footnoteRestart);
        Apply(context.Document.EndnoteNumbering, endnoteFormat, endnoteStartAt, endnoteRestart);
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied)
            return;
        Apply(context.Document.FootnoteNumbering, _previousFootnote.Format, _previousFootnote.StartAt, _previousFootnote.Restart);
        Apply(context.Document.EndnoteNumbering, _previousEndnote.Format, _previousEndnote.StartAt, _previousEndnote.Restart);
        _applied = false;
    }

    private static (NoteNumberFormat, int, NoteNumberRestart) Snapshot(NoteNumberingOptions options) =>
        (options.NumberFormat, options.StartAt, options.NumberRestart);

    private static void Apply(
        NoteNumberingOptions options,
        NoteNumberFormat format,
        int startAt,
        NoteNumberRestart restart)
    {
        options.NumberFormat = format;
        options.StartAt = startAt;
        options.NumberRestart = restart;
    }
}

internal sealed class RemoveBookmarkCommand(string name) : IDocumentCommand
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

internal sealed class SetMultiLevelNumberFormatsCommand(IReadOnlyList<ListNumberFormat> formats) : IDocumentCommand
{
    private ListNumberFormat[]? _previous;

    public string Label => "Define Multilevel List";

    public void Apply(IDocumentCommandContext context)
    {
        _previous = [.. context.Document.MultiLevelList.NumberFormats];
        context.Document.MultiLevelList.SetNumberFormats(formats);
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_previous is null)
            return;
        context.Document.MultiLevelList.SetNumberFormats(_previous);
        _previous = null;
    }
}

/// <summary>
/// AV-REF: Assign a bookmark name to the body paragraph at <paramref name="paragraphIndex"/> (snapshotting
/// the prior <see cref="Paragraph.BookmarkNames"/> for undo). Used to auto-anchor a cross-reference target
/// paragraph that lacks a bookmark so the inserted REF/PAGEREF field can resolve, mirroring Word's hidden
/// <c>_Ref…</c> auto-bookmarks. No-ops when the target is not a paragraph.
/// </summary>
internal sealed class SetBookmarkNameCommand(int paragraphIndex, string name) : IDocumentCommand
{
    private List<string>? _previous;

    public string Label => "Add Bookmark";

    public void Apply(IDocumentCommandContext context)
    {
        if (ParagraphAt(context) is not { } paragraph)
            return;
        _previous = [.. paragraph.BookmarkNames];
        if (!paragraph.BookmarkNames.Contains(name))
            paragraph.BookmarkNames.Add(name);
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
        paragraphIndex >= 0 && paragraphIndex < context.Document.Blocks.Count
            ? context.Document.Blocks[paragraphIndex] as Paragraph
            : null;
}

internal sealed class AddIndexEntryCommand(string term) : IDocumentCommand
{
    private int _index = -1;

    public string Label => "Mark Index Entry";

    public void Apply(IDocumentCommandContext context)
    {
        var entry = new IndexEntry(term);
        if (entry.Term.Length == 0)
            return;

        _index = context.Document.IndexEntries.Count;
        context.Document.IndexEntries.Add(entry);
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_index < 0 || _index >= context.Document.IndexEntries.Count)
            return;

        context.Document.IndexEntries.RemoveAt(_index);
        _index = -1;
    }
}

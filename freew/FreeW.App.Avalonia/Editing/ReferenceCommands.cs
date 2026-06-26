using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Editing;

/// <summary>
/// AV-REF: Create a footnote or endnote in the document's note store with a single seed paragraph of
/// <paramref name="text"/> (an empty paragraph when blank, ready for the user to type). Undo removes the
/// note again. The matching superscript reference run is inserted separately (via
/// <see cref="InsertObjectRunCommand"/>) and the pair is wrapped in one undo group by the caller, so a
/// single Ctrl+Z reverts the whole "Insert Footnote/Endnote".
///
/// <para>
/// A note id is allocated by the caller from <see cref="TextDocument.NextFootnoteId"/> /
/// <see cref="TextDocument.NextEndnoteId"/>; this command just commits the note under that id. Revert
/// removes the keyed entry so re-applying (redo) re-adds it deterministically.
/// </para>
/// </summary>
internal sealed class AddNoteCommand(int id, string text, bool footnote) : IDocumentCommand
{
    public string Label => footnote ? "Insert Footnote" : "Insert Endnote";

    public void Apply(IDocumentCommandContext context)
    {
        var doc = context.Document;
        if (footnote)
            doc.Footnotes[id] = new Footnote(id, text);
        else
            doc.Endnotes[id] = new Endnote(id, text);
    }

    public void Revert(IDocumentCommandContext context)
    {
        var doc = context.Document;
        if (footnote)
            doc.Footnotes.Remove(id);
        else
            doc.Endnotes.Remove(id);
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

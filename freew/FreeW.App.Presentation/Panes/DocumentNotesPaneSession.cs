using FreeW.Core.Model;

namespace FreeW.App.Presentation.Panes;

public readonly record struct DocumentNoteKey(bool IsFootnote, int Id);

public sealed record DocumentNoteProjection(
    DocumentNoteKey Key,
    string Label,
    string Preview)
{
    public string DisplayText
    {
        get
        {
            var preview = Preview.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return preview.Length == 0
                ? Label
                : $"{Label}: {(preview.Length > 60 ? preview[..57] + "..." : preview)}";
        }
    }

    public override string ToString() => DisplayText;
}

public sealed record DocumentNotesPaneMutationActions(
    Func<int, bool, IReadOnlyList<Paragraph>, bool> ReplaceContent,
    Func<int, bool, bool> Delete);

public sealed record DocumentNotesPaneViewState(
    IReadOnlyList<DocumentNoteProjection> Items,
    int SelectedIndex,
    TextDocument? EditorDocument)
{
    public bool HasSelection => SelectedIndex >= 0 && SelectedIndex < Items.Count;
    public bool CanApply => HasSelection;
    public bool CanDelete => HasSelection;
    public DocumentNoteProjection? SelectedNote => HasSelection ? Items[SelectedIndex] : null;
}

public sealed record DocumentNotesPaneOutcome(
    DocumentNotesPaneViewState State,
    bool MutationApplied = false);

/// <summary>
/// Owns footnote/endnote projections, selection transitions, rich editor wrappers, and mutation targeting.
/// Renderers retain the native sub-editor, controls, focus, geometry, and invalidation.
/// </summary>
public sealed class DocumentNotesPaneSession
{
    private readonly Func<TextDocument> _document;
    private readonly DocumentNotesPaneMutationActions _mutations;

    public DocumentNotesPaneSession(
        Func<TextDocument> document,
        DocumentNotesPaneMutationActions mutations)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _mutations = mutations ?? throw new ArgumentNullException(nameof(mutations));
        State = new DocumentNotesPaneViewState([], -1, null);
    }

    public DocumentNotesPaneViewState State { get; private set; }

    public DocumentNotesPaneOutcome Refresh() => Refresh(State.SelectedNote?.Key);

    public DocumentNotesPaneOutcome ShowAndSelect(bool footnote, int id) =>
        Refresh(new DocumentNoteKey(footnote, id));

    public DocumentNotesPaneOutcome SelectIndex(int index)
    {
        var selectedIndex = index >= 0 && index < State.Items.Count ? index : -1;
        State = BuildState(_document(), State.Items, selectedIndex);
        return new DocumentNotesPaneOutcome(State);
    }

    public DocumentNotesPaneOutcome Apply(IReadOnlyList<Block> editedBlocks)
    {
        ArgumentNullException.ThrowIfNull(editedBlocks);
        if (State.SelectedNote is not { } selected)
            return new DocumentNotesPaneOutcome(State);

        var paragraphs = editedBlocks
            .OfType<Paragraph>()
            .Select(paragraph => (Paragraph)DocumentMerge.CloneBlock(paragraph))
            .ToArray();
        if (!_mutations.ReplaceContent(
                selected.Key.Id,
                selected.Key.IsFootnote,
                paragraphs))
        {
            return new DocumentNotesPaneOutcome(State);
        }

        Refresh(selected.Key);
        return new DocumentNotesPaneOutcome(State, MutationApplied: true);
    }

    public DocumentNotesPaneOutcome DeleteSelected()
    {
        if (State.SelectedNote is not { } selected
            || !_mutations.Delete(selected.Key.Id, selected.Key.IsFootnote))
        {
            return new DocumentNotesPaneOutcome(State);
        }

        State = State with { SelectedIndex = -1, EditorDocument = null };
        Refresh(requested: null);
        return new DocumentNotesPaneOutcome(State, MutationApplied: true);
    }

    private DocumentNotesPaneOutcome Refresh(DocumentNoteKey? requested)
    {
        var document = _document();
        var items = document.Footnotes.Values
            .OrderBy(note => note.Id)
            .Select(note => new DocumentNoteProjection(
                new DocumentNoteKey(IsFootnote: true, note.Id),
                $"Footnote {note.Id}",
                note.PlainText))
            .Concat(document.Endnotes.Values
                .OrderBy(note => note.Id)
                .Select(note => new DocumentNoteProjection(
                    new DocumentNoteKey(IsFootnote: false, note.Id),
                    $"Endnote {note.Id}",
                    note.PlainText)))
            .ToArray();
        var selectedIndex = requested is { } key
            ? Array.FindIndex(items, item => item.Key == key)
            : -1;
        if (selectedIndex < 0 && items.Length > 0)
            selectedIndex = 0;
        State = BuildState(document, items, selectedIndex);
        return new DocumentNotesPaneOutcome(State);
    }

    private static DocumentNotesPaneViewState BuildState(
        TextDocument document,
        IReadOnlyList<DocumentNoteProjection> items,
        int selectedIndex)
    {
        var editorDocument = selectedIndex >= 0 && selectedIndex < items.Count
            ? BuildEditorDocument(document, items[selectedIndex].Key)
            : null;
        return new DocumentNotesPaneViewState(items, selectedIndex, editorDocument);
    }

    private static TextDocument BuildEditorDocument(TextDocument document, DocumentNoteKey key)
    {
        var wrapper = TextDocument.CreateEmpty();
        wrapper.DefaultRun = document.DefaultRun;
        wrapper.DefaultParagraph = document.DefaultParagraph;
        wrapper.Blocks.Clear();

        var content = key.IsFootnote
            ? document.Footnotes.GetValueOrDefault(key.Id)?.Content
            : document.Endnotes.GetValueOrDefault(key.Id)?.Content;
        if (content is not null)
            wrapper.Blocks.AddRange(content.Select(DocumentMerge.CloneBlock));
        if (wrapper.Blocks.Count == 0)
            wrapper.Blocks.Add(new Paragraph());
        return wrapper;
    }
}

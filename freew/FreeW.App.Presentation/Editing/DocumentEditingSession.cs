using FreeW.Core.Model;

namespace FreeW.App.Presentation.Editing;

/// <summary>
/// Owns the active FreeW document and its portable command history. Renderers retain native caret,
/// selection, input, layout, and redraw behavior, and pass model-relative positions into this session.
/// </summary>
public sealed class DocumentEditingSession
{
    private readonly Func<string?> _revisionAuthor;
    private DocumentCommandBus _commands;

    public DocumentEditingSession(Func<string?>? revisionAuthor = null)
    {
        _revisionAuthor = revisionAuthor ?? (() => null);
        Document = TextDocument.CreateEmpty();
        _commands = CreateCommandBus(Document);
    }

    public event Action? Changed;

    public TextDocument Document { get; private set; }

    public DocumentCommandBus Commands => _commands;

    /// <summary>Replaces the active document and starts a fresh undo/redo history for it.</summary>
    public void LoadDocument(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        _commands.Changed -= OnCommandsChanged;
        Document = document;
        _commands = CreateCommandBus(document);
    }

    /// <summary>Inserts one model block immediately after the renderer's current top-level caret block.</summary>
    public int InsertBlockAfter(int caretBlockIndex, Block block)
    {
        ArgumentNullException.ThrowIfNull(block);

        var insertAt = ResolveInsertionIndexAfter(caretBlockIndex);
        _commands.Execute(new InsertBlockCommand(insertAt, block));
        return insertAt;
    }

    /// <summary>
    /// Inserts an ordered block range immediately after the renderer's current top-level caret block as
    /// one undoable edit. Returns the first inserted index, or -1 when there is nothing to insert.
    /// </summary>
    public int InsertBlocksAfter(
        int caretBlockIndex,
        IReadOnlyList<Block> blocks,
        string undoLabel)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentException.ThrowIfNullOrWhiteSpace(undoLabel);
        if (blocks.Count == 0)
            return -1;

        var insertAt = ResolveInsertionIndexAfter(caretBlockIndex);
        if (blocks.Count == 1)
        {
            _commands.Execute(new InsertBlockCommand(insertAt, blocks[0]));
            return insertAt;
        }

        _commands.BeginUndoGroup();
        try
        {
            for (var index = 0; index < blocks.Count; index++)
                _commands.Execute(new InsertBlockCommand(insertAt + index, blocks[index]));
            _commands.CommitUndoGroup(undoLabel);
        }
        catch
        {
            _commands.AbortUndoGroup();
            throw;
        }

        return insertAt;
    }

    /// <summary>Clones and inserts another document's body as one undoable caret-relative edit.</summary>
    public int InsertDocumentAfter(int caretBlockIndex, TextDocument? source)
    {
        if (source is null || source.Blocks.Count == 0)
            return -1;

        var clones = DocumentMerge.CloneBlocksForInsertion(Document, source);
        if (clones.Count == 0)
            return -1;

        foreach (var (id, style) in source.Styles)
            Document.Styles.TryAdd(id, style);

        return InsertBlocksAfter(caretBlockIndex, clones, "Insert Text from File");
    }

    /// <summary>Removes a named bookmark through the shared undo history.</summary>
    public bool RemoveBookmark(string? name)
    {
        var normalized = name?.Trim();
        if (string.IsNullOrEmpty(normalized)
            || !Document.Blocks
                .OfType<Paragraph>()
                .Any(paragraph => paragraph.BookmarkNames.Contains(normalized, StringComparer.Ordinal)))
        {
            return false;
        }

        _commands.Execute(new RemoveBookmarkCommand(normalized));
        return true;
    }

    private int ResolveInsertionIndexAfter(int caretBlockIndex) =>
        Math.Clamp(caretBlockIndex + 1, 0, Document.Blocks.Count);

    private DocumentCommandBus CreateCommandBus(TextDocument document)
    {
        var commands = new DocumentCommandBus(new SessionCommandContext(document, _revisionAuthor));
        commands.Changed += OnCommandsChanged;
        return commands;
    }

    private void OnCommandsChanged() => Changed?.Invoke();

    private sealed class SessionCommandContext(
        TextDocument document,
        Func<string?> revisionAuthor) : IDocumentCommandContext
    {
        public TextDocument Document => document;
        public string? RevisionAuthor => revisionAuthor();
    }
}

using System.Globalization;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Editing;

public readonly record struct DocumentTextPosition(int BlockIndex, int Offset);

public readonly record struct DocumentTextRange(
    DocumentTextPosition Anchor,
    DocumentTextPosition Active)
{
    public bool IsCollapsed => Anchor == Active;

    public DocumentTextRange Normalize() =>
        Anchor.BlockIndex < Active.BlockIndex
        || (Anchor.BlockIndex == Active.BlockIndex && Anchor.Offset <= Active.Offset)
            ? this
            : new DocumentTextRange(Active, Anchor);
}

public readonly record struct DocumentTextHyperlink(
    string? Url,
    string? Anchor,
    string? Tooltip);

public readonly record struct DocumentTextEditResult(
    DocumentTextPosition Caret,
    bool KeptDeletedText);

/// <summary>
/// Owns the active FreeW document and its portable command history. Renderers retain native caret,
/// selection, input, layout, and redraw behavior, and pass model-relative positions into this session.
/// </summary>
public sealed class DocumentEditingSession
{
    private readonly Func<string?> _revisionAuthor;
    private readonly Func<string?> _revisionDateXml;
    private DocumentCommandBus _commands;

    public DocumentEditingSession(
        Func<string?>? revisionAuthor = null,
        Func<string?>? revisionDateXml = null)
    {
        _revisionAuthor = revisionAuthor ?? (() => null);
        _revisionDateXml = revisionDateXml ?? CurrentRevisionDateXml;
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

    /// <summary>
    /// Inserts tracked text at a model-relative body caret. Formatting and hyperlink state inherit from
    /// the character immediately before the caret, matching the WPF renderer's established behavior.
    /// </summary>
    public bool TryInsertTrackedBodyText(
        DocumentTextPosition caret,
        string text,
        RunFormatting? formatting,
        out DocumentTextEditResult result) =>
        TryReplaceTrackedBodyTextCore(
            new DocumentTextRange(caret, caret),
            text,
            formatting,
            inheritHyperlink: true,
            explicitHyperlink: null,
            out result);

    /// <summary>
    /// Inserts tracked text with renderer-resolved hyperlink inheritance. A null hyperlink explicitly
    /// means ordinary text, which lets renderers retain their native caret-edge policy.
    /// </summary>
    public bool TryInsertTrackedBodyText(
        DocumentTextPosition caret,
        string text,
        RunFormatting? formatting,
        DocumentTextHyperlink? hyperlink,
        out DocumentTextEditResult result) =>
        TryReplaceTrackedBodyTextCore(
            new DocumentTextRange(caret, caret),
            text,
            formatting,
            inheritHyperlink: false,
            hyperlink,
            out result);

    /// <summary>
    /// Replaces a same-paragraph body selection as one tracked, undoable edit. Existing selected text is
    /// retained as a deletion revision and the replacement is inserted at the normalized range start.
    /// </summary>
    public bool TryReplaceTrackedBodyText(
        DocumentTextRange range,
        string text,
        RunFormatting? formatting,
        out DocumentTextEditResult result) =>
        TryReplaceTrackedBodyTextCore(
            range,
            text,
            formatting,
            inheritHyperlink: true,
            explicitHyperlink: null,
            out result);

    /// <summary>
    /// Marks a same-paragraph body range as deleted. Forward Delete can advance past retained struck text;
    /// Backspace and selection deletion collapse to the normalized range start.
    /// </summary>
    public bool TryDeleteTrackedBodyText(
        DocumentTextRange range,
        bool advancePastKeptText,
        out DocumentTextEditResult result)
    {
        result = default;
        if (!TryResolveBodyRange(range, out var blockIndex, out var startOffset, out var endOffset)
            || startOffset == endOffset)
        {
            return false;
        }

        var deletion = default(RevisionEditPlanner.DeleteResult);
        var author = ResolveRevisionAuthor();
        var dateXml = _revisionDateXml();
        _commands.Execute(new ReplaceParagraphRunsCommand(blockIndex, paragraph =>
        {
            deletion = RevisionEditPlanner.DeleteRangeAsRevision(
                paragraph,
                startOffset,
                endOffset,
                author,
                dateXml);
        }));

        var caretOffset = advancePastKeptText && deletion.KeptDeletedText
            ? endOffset
            : deletion.CaretOffset;
        result = new DocumentTextEditResult(
            new DocumentTextPosition(blockIndex, caretOffset),
            deletion.KeptDeletedText);
        return true;
    }

    private bool TryReplaceTrackedBodyTextCore(
        DocumentTextRange range,
        string text,
        RunFormatting? formatting,
        bool inheritHyperlink,
        DocumentTextHyperlink? explicitHyperlink,
        out DocumentTextEditResult result)
    {
        result = default;
        if (string.IsNullOrEmpty(text)
            || !TryResolveBodyRange(range, out var blockIndex, out var startOffset, out var endOffset))
        {
            return false;
        }

        var author = ResolveRevisionAuthor();
        var dateXml = _revisionDateXml();
        var keptDeletedText = false;
        var caretOffset = startOffset;
        _commands.Execute(new ReplaceParagraphRunsCommand(blockIndex, paragraph =>
        {
            if (startOffset != endOffset)
            {
                keptDeletedText = RevisionEditPlanner.DeleteRangeAsRevision(
                    paragraph,
                    startOffset,
                    endOffset,
                    author,
                    dateXml).KeptDeletedText;
            }

            var activeFormatting = formatting
                ?? RevisionEditPlanner.FormattingAtOffset(paragraph, startOffset);
            var hyperlink = inheritHyperlink
                ? RevisionEditPlanner.LinkAtOffset(paragraph, startOffset)
                : new RevisionEditPlanner.InsertOptions(
                    HyperlinkUrl: explicitHyperlink?.Url,
                    HyperlinkAnchor: explicitHyperlink?.Anchor,
                    HyperlinkTooltip: explicitHyperlink?.Tooltip);
            caretOffset = RevisionEditPlanner.InsertText(
                paragraph,
                startOffset,
                text,
                activeFormatting,
                new RevisionEditPlanner.InsertOptions(
                    RevisionKind.Inserted,
                    author,
                    dateXml,
                    hyperlink.HyperlinkUrl,
                    hyperlink.HyperlinkAnchor,
                    hyperlink.HyperlinkTooltip));
        }));

        result = new DocumentTextEditResult(
            new DocumentTextPosition(blockIndex, caretOffset),
            keptDeletedText);
        return true;
    }

    private bool TryResolveBodyRange(
        DocumentTextRange range,
        out int blockIndex,
        out int startOffset,
        out int endOffset)
    {
        var normalized = range.Normalize();
        blockIndex = normalized.Anchor.BlockIndex;
        startOffset = endOffset = 0;
        if (blockIndex != normalized.Active.BlockIndex
            || blockIndex < 0
            || blockIndex >= Document.Blocks.Count
            || Document.Blocks[blockIndex] is not Paragraph paragraph)
        {
            return false;
        }

        startOffset = Math.Clamp(normalized.Anchor.Offset, 0, paragraph.PlainText.Length);
        endOffset = Math.Clamp(normalized.Active.Offset, 0, paragraph.PlainText.Length);
        return true;
    }

    private string ResolveRevisionAuthor()
    {
        var author = _revisionAuthor()?.Trim();
        return string.IsNullOrWhiteSpace(author) ? "FreeW User" : author;
    }

    private static string CurrentRevisionDateXml() =>
        DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

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

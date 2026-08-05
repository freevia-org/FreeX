using System.Globalization;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Editing;

public readonly record struct DocumentTextPosition(int BlockIndex, int Offset);

public readonly record struct DocumentTextRange(
    DocumentTextPosition Anchor,
    DocumentTextPosition Active)
{
    public bool IsCollapsed => Anchor == Active;

    public DocumentTextPosition Start => Normalize().Anchor;

    public DocumentTextPosition End => Normalize().Active;

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

public readonly record struct DocumentParagraphEditResult(
    DocumentTextPosition Caret,
    int ReplacedBlockCount);

/// <summary>
/// Owns the active FreeW document, portable command history, and ordinary body-text mutations. Renderers
/// retain native input events, caret translation, layout, focus, redraw, and specialized editing surfaces.
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
        Review = new DocumentReviewEditingSession(this, _revisionDateXml);
    }

    public event Action? Changed;

    public TextDocument Document { get; private set; }

    public DocumentCommandBus Commands => _commands;

    public DocumentReviewEditingSession Review { get; }

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
    /// Replaces an ordinary body range with untracked text as one undoable edit. The range may span
    /// adjacent ordinary paragraphs; renderer-owned table, drawing, field, and anchored-content paths
    /// are rejected so their native editors remain authoritative.
    /// </summary>
    public bool TryReplaceBodyText(
        DocumentTextRange range,
        string text,
        RunFormatting? formatting,
        out DocumentTextEditResult result) =>
        TryReplaceBodyTextCore(
            range,
            text,
            formatting,
            inheritHyperlink: true,
            explicitHyperlink: null,
            out result);

    /// <summary>
    /// Replaces an ordinary body range using the renderer's resolved hyperlink-edge policy. A null
    /// hyperlink explicitly inserts ordinary text.
    /// </summary>
    public bool TryReplaceBodyText(
        DocumentTextRange range,
        string text,
        RunFormatting? formatting,
        DocumentTextHyperlink? hyperlink,
        out DocumentTextEditResult result) =>
        TryReplaceBodyTextCore(
            range,
            text,
            formatting,
            inheritHyperlink: false,
            hyperlink,
            out result);

    /// <summary>Deletes an ordinary same- or cross-paragraph body range as one undoable edit.</summary>
    public bool TryDeleteBodyText(
        DocumentTextRange range,
        out DocumentTextEditResult result)
    {
        result = default;
        if (!TryResolveBodySpan(range, requireStructuralEdit: false, out var span)
            || span.Start == span.End)
        {
            return false;
        }

        if (span.Start.BlockIndex == span.End.BlockIndex)
        {
            _commands.Execute(new ReplaceParagraphRunsCommand(span.Start.BlockIndex, paragraph =>
            {
                ReplaceTextRange(paragraph, span.Start.Offset, span.End.Offset, string.Empty, null, default);
            }));
        }
        else
        {
            if (!CanRestructure(span))
                return false;
            var merged = BuildRangeReplacement(span, string.Empty, null, default);
            _commands.Execute(new ReplaceBlocksCommand(
                span.Start.BlockIndex,
                span.End.BlockIndex - span.Start.BlockIndex + 1,
                [merged]));
        }

        result = new DocumentTextEditResult(span.Start, KeptDeletedText: false);
        return true;
    }

    /// <summary>Joins an ordinary body paragraph to its immediately preceding paragraph.</summary>
    public bool TryMergeBodyParagraphWithPrevious(
        int blockIndex,
        out DocumentParagraphEditResult result)
    {
        result = default;
        if (blockIndex <= 0
            || blockIndex >= Document.Blocks.Count
            || Document.Blocks[blockIndex - 1] is not Paragraph previous
            || Document.Blocks[blockIndex] is not Paragraph current
            || !CanRestructure(previous)
            || !CanRestructure(current))
        {
            return false;
        }

        var caretOffset = previous.PlainText.Length;
        var merged = CreateParagraph(previous, keepStyle: true);
        AppendClonedRuns(merged, previous, 0, previous.PlainText.Length);
        AppendClonedRuns(merged, current, 0, current.PlainText.Length);
        CoalesceEditableRuns(merged);
        _commands.Execute(new ReplaceBlocksCommand(blockIndex - 1, 2, [merged]));

        result = new DocumentParagraphEditResult(
            new DocumentTextPosition(blockIndex - 1, caretOffset),
            ReplacedBlockCount: 2);
        return true;
    }

    /// <summary>Joins an ordinary body paragraph to its immediately following paragraph.</summary>
    public bool TryMergeBodyParagraphWithNext(
        int blockIndex,
        out DocumentParagraphEditResult result)
    {
        result = default;
        if (blockIndex < 0
            || blockIndex + 1 >= Document.Blocks.Count
            || Document.Blocks[blockIndex] is not Paragraph current
            || Document.Blocks[blockIndex + 1] is not Paragraph next
            || !CanRestructure(current)
            || !CanRestructure(next))
        {
            return false;
        }

        var caretOffset = current.PlainText.Length;
        var merged = CreateParagraph(current, keepStyle: true);
        AppendClonedRuns(merged, current, 0, current.PlainText.Length);
        AppendClonedRuns(merged, next, 0, next.PlainText.Length);
        CoalesceEditableRuns(merged);
        _commands.Execute(new ReplaceBlocksCommand(blockIndex, 2, [merged]));

        result = new DocumentParagraphEditResult(
            new DocumentTextPosition(blockIndex, caretOffset),
            ReplacedBlockCount: 2);
        return true;
    }

    /// <summary>
    /// Replaces an ordinary body selection with a paragraph break as one undoable edit. Empty list items
    /// exit their list; non-empty list items continue with the same list formatting.
    /// </summary>
    public bool TryInsertBodyParagraphBreak(
        DocumentTextRange range,
        out DocumentParagraphEditResult result)
    {
        result = default;
        if (!TryResolveBodySpan(range, requireStructuralEdit: true, out var span))
            return false;

        var remaining = BuildRangeReplacement(span, string.Empty, null, default);
        var replaceCount = span.End.BlockIndex - span.Start.BlockIndex + 1;
        var splitOffset = Math.Clamp(span.Start.Offset, 0, remaining.PlainText.Length);
        if (remaining.Formatting.ListKind != ListKind.None && remaining.PlainText.Length == 0)
        {
            remaining.Formatting = remaining.Formatting with { ListKind = ListKind.None, ListLevel = 0 };
            _commands.Execute(new ReplaceBlocksCommand(span.Start.BlockIndex, replaceCount, [remaining]));
            result = new DocumentParagraphEditResult(span.Start, replaceCount);
            return true;
        }

        var first = CreateParagraph(remaining, keepStyle: true);
        AppendClonedRuns(first, remaining, 0, splitOffset);
        var second = CreateParagraph(remaining, keepStyle: false);
        AppendClonedRuns(second, remaining, splitOffset, remaining.PlainText.Length);
        _commands.Execute(new ReplaceBlocksCommand(span.Start.BlockIndex, replaceCount, [first, second]));

        result = new DocumentParagraphEditResult(
            new DocumentTextPosition(span.Start.BlockIndex + 1, 0),
            replaceCount);
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
    /// Replaces a tracked body selection using the renderer's resolved hyperlink-edge policy. A null
    /// hyperlink explicitly inserts ordinary tracked text.
    /// </summary>
    public bool TryReplaceTrackedBodyText(
        DocumentTextRange range,
        string text,
        RunFormatting? formatting,
        DocumentTextHyperlink? hyperlink,
        out DocumentTextEditResult result) =>
        TryReplaceTrackedBodyTextCore(
            range,
            text,
            formatting,
            inheritHyperlink: false,
            hyperlink,
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

    private bool TryReplaceBodyTextCore(
        DocumentTextRange range,
        string text,
        RunFormatting? formatting,
        bool inheritHyperlink,
        DocumentTextHyperlink? explicitHyperlink,
        out DocumentTextEditResult result)
    {
        result = default;
        if (string.IsNullOrEmpty(text)
            || !TryResolveBodySpan(range, requireStructuralEdit: false, out var span))
        {
            return false;
        }

        var startParagraph = (Paragraph)Document.Blocks[span.Start.BlockIndex];
        var activeFormatting = formatting
            ?? RevisionEditPlanner.FormattingAtOffset(startParagraph, span.Start.Offset);
        var hyperlink = inheritHyperlink
            ? RevisionEditPlanner.LinkAtOffset(startParagraph, span.Start.Offset)
            : new RevisionEditPlanner.InsertOptions(
                HyperlinkUrl: explicitHyperlink?.Url,
                HyperlinkAnchor: explicitHyperlink?.Anchor,
                HyperlinkTooltip: explicitHyperlink?.Tooltip);
        var options = new RevisionEditPlanner.InsertOptions(
            HyperlinkUrl: hyperlink.HyperlinkUrl,
            HyperlinkAnchor: hyperlink.HyperlinkAnchor,
            HyperlinkTooltip: hyperlink.HyperlinkTooltip);

        if (span.Start.BlockIndex == span.End.BlockIndex)
        {
            _commands.Execute(new ReplaceParagraphRunsCommand(span.Start.BlockIndex, paragraph =>
            {
                ReplaceTextRange(
                    paragraph,
                    span.Start.Offset,
                    span.End.Offset,
                    text,
                    activeFormatting,
                    options);
            }));
        }
        else
        {
            if (!CanRestructure(span))
                return false;
            var merged = BuildRangeReplacement(span, text, activeFormatting, options);
            _commands.Execute(new ReplaceBlocksCommand(
                span.Start.BlockIndex,
                span.End.BlockIndex - span.Start.BlockIndex + 1,
                [merged]));
        }

        result = new DocumentTextEditResult(
            new DocumentTextPosition(span.Start.BlockIndex, span.Start.Offset + text.Length),
            KeptDeletedText: false);
        return true;
    }

    private bool TryResolveBodySpan(
        DocumentTextRange range,
        bool requireStructuralEdit,
        out DocumentTextRange span)
    {
        span = range.Normalize();
        if (span.Anchor.BlockIndex < 0
            || span.Active.BlockIndex >= Document.Blocks.Count
            || span.Anchor.BlockIndex > span.Active.BlockIndex)
        {
            return false;
        }

        for (var blockIndex = span.Anchor.BlockIndex; blockIndex <= span.Active.BlockIndex; blockIndex++)
        {
            if (Document.Blocks[blockIndex] is not Paragraph paragraph
                || !IsPortableBodyTextParagraph(paragraph)
                || requireStructuralEdit && !CanRestructure(paragraph))
            {
                return false;
            }
        }

        var startParagraph = (Paragraph)Document.Blocks[span.Anchor.BlockIndex];
        var endParagraph = (Paragraph)Document.Blocks[span.Active.BlockIndex];
        span = new DocumentTextRange(
            new DocumentTextPosition(
                span.Anchor.BlockIndex,
                Math.Clamp(span.Anchor.Offset, 0, startParagraph.PlainText.Length)),
            new DocumentTextPosition(
                span.Active.BlockIndex,
                Math.Clamp(span.Active.Offset, 0, endParagraph.PlainText.Length)));
        return true;
    }

    private bool CanRestructure(DocumentTextRange span)
    {
        for (var blockIndex = span.Start.BlockIndex; blockIndex <= span.End.BlockIndex; blockIndex++)
        {
            if (Document.Blocks[blockIndex] is not Paragraph paragraph || !CanRestructure(paragraph))
                return false;
        }
        return true;
    }

    private Paragraph BuildRangeReplacement(
        DocumentTextRange span,
        string text,
        RunFormatting? formatting,
        RevisionEditPlanner.InsertOptions options)
    {
        var startParagraph = (Paragraph)Document.Blocks[span.Start.BlockIndex];
        var endParagraph = (Paragraph)Document.Blocks[span.End.BlockIndex];
        var replacement = CreateParagraph(startParagraph, keepStyle: true);
        AppendClonedRuns(replacement, startParagraph, 0, span.Start.Offset);
        if (!string.IsNullOrEmpty(text) && formatting is not null)
        {
            replacement.Runs.Add(new Run(text, formatting)
            {
                HyperlinkUrl = options.HyperlinkUrl,
                HyperlinkAnchor = options.HyperlinkAnchor,
                HyperlinkTooltip = options.HyperlinkTooltip,
            });
        }
        AppendClonedRuns(replacement, endParagraph, span.End.Offset, endParagraph.PlainText.Length);
        CoalesceEditableRuns(replacement);
        return replacement;
    }

    private static void ReplaceTextRange(
        Paragraph paragraph,
        int startOffset,
        int endOffset,
        string text,
        RunFormatting? formatting,
        RevisionEditPlanner.InsertOptions options)
    {
        var lo = Math.Clamp(Math.Min(startOffset, endOffset), 0, paragraph.PlainText.Length);
        var hi = Math.Clamp(Math.Max(startOffset, endOffset), 0, paragraph.PlainText.Length);
        var rebuilt = new List<Run>();
        AppendClonedRuns(rebuilt, paragraph, 0, lo);
        AppendClonedRuns(rebuilt, paragraph, hi, paragraph.PlainText.Length);
        paragraph.Runs.Clear();
        paragraph.Runs.AddRange(rebuilt);
        if (!string.IsNullOrEmpty(text) && formatting is not null)
            RevisionEditPlanner.InsertText(paragraph, lo, text, formatting, options);
        CoalesceEditableRuns(paragraph);
    }

    private static Paragraph CreateParagraph(Paragraph source, bool keepStyle) => new()
    {
        Formatting = source.Formatting,
        StyleId = keepStyle ? source.StyleId : null,
    };

    private static void AppendClonedRuns(
        Paragraph target,
        Paragraph source,
        int startOffset,
        int endOffset) =>
        AppendClonedRuns(target.Runs, source, startOffset, endOffset);

    private static void AppendClonedRuns(
        ICollection<Run> target,
        Paragraph source,
        int startOffset,
        int endOffset)
    {
        var lo = Math.Clamp(Math.Min(startOffset, endOffset), 0, source.PlainText.Length);
        var hi = Math.Clamp(Math.Max(startOffset, endOffset), 0, source.PlainText.Length);
        var position = 0;
        foreach (var run in source.Runs)
        {
            var runStart = position;
            var runEnd = runStart + run.Text.Length;
            position = runEnd;
            if (run.Text.Length == 0)
            {
                if (runStart >= lo && (runStart < hi || hi == source.PlainText.Length && runStart == hi))
                    target.Add(RevisionEditPlanner.CloneRunWithText(run, string.Empty));
                continue;
            }
            if (runEnd <= lo || runStart >= hi)
                continue;

            var localStart = Math.Max(lo, runStart) - runStart;
            var localEnd = Math.Min(hi, runEnd) - runStart;
            target.Add(RevisionEditPlanner.CloneRunWithText(run, run.Text[localStart..localEnd]));
        }
    }

    private static void CoalesceEditableRuns(Paragraph paragraph)
    {
        for (var index = 0; index < paragraph.Runs.Count - 1; index++)
        {
            var left = paragraph.Runs[index];
            var right = paragraph.Runs[index + 1];
            if (!CanCoalesce(left, right))
                continue;
            paragraph.Runs[index] = RevisionEditPlanner.CloneRunWithText(left, left.Text + right.Text);
            paragraph.Runs.RemoveAt(index + 1);
            index--;
        }
    }

    private static bool CanCoalesce(Run left, Run right) =>
        IsMergeableTextRun(left)
        && IsMergeableTextRun(right)
        && left.Formatting.Equals(right.Formatting)
        && string.Equals(left.HyperlinkUrl, right.HyperlinkUrl, StringComparison.Ordinal)
        && string.Equals(left.HyperlinkAnchor, right.HyperlinkAnchor, StringComparison.Ordinal)
        && string.Equals(left.HyperlinkTooltip, right.HyperlinkTooltip, StringComparison.Ordinal);

    private static bool IsMergeableTextRun(Run run) =>
        run.Text.Length > 0
        && IsPortableBodyTextRun(run)
        && run.CommentId is null
        && run.Revision == RevisionKind.None
        && run.FormatRevision is null;

    private static bool IsPortableBodyTextParagraph(Paragraph paragraph) =>
        paragraph.BookmarkBoundaries.Count == 0
        && paragraph.Runs.All(IsPortableBodyTextRun);

    private static bool CanRestructure(Paragraph paragraph) =>
        IsPortableBodyTextParagraph(paragraph)
        && paragraph.BookmarkNames.Count == 0
        && paragraph.DropCap is null
        && paragraph.SectionBreak is null
        && paragraph.PreservedNumbering is null
        && paragraph.ParagraphFormatRevision is null;

    private static bool IsPortableBodyTextRun(Run run) =>
        run.Image is null
        && run.Equation is null
        && run.Shape is null
        && run.WordArt is null
        && run.Ruby is null
        && run.Chart is null
        && run.EmbeddedObject is null
        && run.SmartArt is null
        && run.PreservedDrawing is null
        && run.DrawingGroup is null
        && run.SubDocument is null
        && run.FieldKind == RunFieldKind.None
        && run.TableFormula is null
        && run.Citation is null
        && run.CrossReference is null
        && run.ComplexField is null
        && run.FootnoteId is null
        && run.EndnoteId is null
        && !run.IsCommentReference
        && !run.IsPageBreak
        && !run.IsColumnBreak
        && run.Control is null
        && run.MoveRevisionId is null;

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

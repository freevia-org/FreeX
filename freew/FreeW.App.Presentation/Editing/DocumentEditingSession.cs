using System.Globalization;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Ribbon;
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
/// Renderer projection of the native selection needed to apply a named style. Text ranges contain only
/// editable body text; paragraph indices retain paragraph-style targets even when a range endpoint is empty.
/// </summary>
public sealed record NamedStyleApplicationTarget(
    IReadOnlyList<DocumentTextRange> TextRanges,
    IReadOnlyList<int> ParagraphIndices,
    bool HasTextSelection,
    bool CanApplyCharacterFormatting = true);

public sealed record NamedStyleApplicationResult(
    string RequestedStyleId,
    DocumentStyle EffectiveStyle,
    NamedStyleApplicationKind Kind,
    bool ModelChanged,
    bool RequiresRendererProjection)
{
    public RunFormatting ProjectCharacterFormatting(RunFormatting formatting)
    {
        if (Kind != NamedStyleApplicationKind.Character)
            throw new InvalidOperationException("Only character styles can be projected onto native text.");

        return NamedStyleApplicationPlanner.OverlayCharacterStyle(formatting, EffectiveStyle.Run);
    }
}

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
        Body = new DocumentBodyEditingCoordinator(this);
        Interaction = new DocumentEditorInteractionSession(this);
        Review = new DocumentReviewEditingSession(this, _revisionDateXml);
        Design = new DocumentDesignEditingCoordinator(this);
        ParagraphStylePreview = new DocumentParagraphStylePreviewSession(this);
        TableStylePreview = new DocumentTableStylePreviewSession(this);
        ChartDesignPreview = new DocumentChartDesignPreviewSession(this);
        SmartArtDesignPreview = new DocumentSmartArtDesignPreviewSession(this);
        Paragraphs = new DocumentParagraphFormattingCoordinator(this);
        Objects = new DocumentObjectEditingCoordinator(this);
        Tables = new DocumentTableEditingCoordinator(this);
        References = new DocumentReferenceEditingCoordinator(this);
    }

    public event Action? Changed;

    public TextDocument Document { get; private set; }

    public DocumentCommandBus Commands => _commands;

    public DocumentBodyEditingCoordinator Body { get; }

    public DocumentEditorInteractionSession Interaction { get; }

    public DocumentReviewEditingSession Review { get; }

    public DocumentDesignEditingCoordinator Design { get; }

    public DocumentParagraphStylePreviewSession ParagraphStylePreview { get; }

    public DocumentTableStylePreviewSession TableStylePreview { get; }

    public DocumentChartDesignPreviewSession ChartDesignPreview { get; }

    public DocumentSmartArtDesignPreviewSession SmartArtDesignPreview { get; }

    public DocumentParagraphFormattingCoordinator Paragraphs { get; }

    public DocumentObjectEditingCoordinator Objects { get; }

    public DocumentTableEditingCoordinator Tables { get; }

    public DocumentReferenceEditingCoordinator References { get; }

    /// <summary>Replaces the active document and starts a fresh undo/redo history for it.</summary>
    public void LoadDocument(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        Design.CancelPreview();
        ParagraphStylePreview.Cancel();
        TableStylePreview.Cancel();
        ChartDesignPreview.Cancel();
        SmartArtDesignPreview.Cancel();
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

        var clones = DocumentMerge.CloneBlocksForInsertion(Document, source).ToList();
        if (clones.Count == 0)
            return -1;

        foreach (var (id, style) in source.Styles)
            Document.Styles.TryAdd(id, style);

        var insertAt = ResolveInsertionIndexAfter(caretBlockIndex);
        RestartUnrelatedNumberListRuns(clones, PrecedingParagraphContinuesNumberList(insertAt - 1));

        return InsertBlocksAfter(caretBlockIndex, clones, "Insert Text from File");
    }

    /// <summary>Replaces an empty body paragraph with cloned source blocks as one undoable paste.</summary>
    public bool ReplaceEmptyParagraphWithDocument(int blockIndex, TextDocument? source)
    {
        if (source is null
            || source.Blocks.Count == 0
            || blockIndex < 0
            || blockIndex >= Document.Blocks.Count
            || Document.Blocks[blockIndex] is not Paragraph { PlainText.Length: 0 })
        {
            return false;
        }

        var clones = DocumentMerge.CloneBlocksForInsertion(Document, source).ToList();
        if (clones.Count == 0)
            return false;
        foreach (var (id, style) in source.Styles)
            Document.Styles.TryAdd(id, style);
        _commands.Execute(new ReplaceBlocksCommand(blockIndex, 1, clones));
        return true;
    }

    /// <summary>
    /// Inserts a clipboard document at a body caret, splicing its first and last paragraphs into the
    /// destination paragraph the way Word's paste does: the text before the caret keeps the destination's
    /// own paragraph properties and gains the source's first paragraph's runs, any middle blocks (further
    /// paragraphs, tables) land between, and the text after the caret follows the source's last
    /// paragraph. Runs are CLONED, not rebuilt from characters, so every mark the source carries — a
    /// content control, a tracked change's author, a comment id, a linked character style — survives the
    /// paste; the source's styles and package parts come across through <see cref="DocumentMerge"/>.
    /// Declines when the caret sits strictly inside a content control (splitting the destination run
    /// there would emit that one w:sdt twice) or when the destination paragraph cannot be restructured.
    /// </summary>
    public bool TryInsertDocumentAtBodyCaret(
        DocumentTextPosition caret,
        TextDocument? source,
        out DocumentParagraphEditResult result,
        bool trackChanges = false)
    {
        result = default;
        if (source is null
            || source.Blocks.Count == 0
            || caret.BlockIndex < 0
            || caret.BlockIndex >= Document.Blocks.Count
            || Document.Blocks[caret.BlockIndex] is not Paragraph destination
            || !CanReceivePaste(destination)
            || IsOffsetInsideContentControl(destination, caret.Offset))
        {
            return false;
        }

        var clones = DocumentMerge.CloneBlocksForInsertion(Document, source).ToList();
        if (clones.Count == 0)
            return false;
        foreach (var (id, style) in source.Styles)
            Document.Styles.TryAdd(id, style);

        // With Track Changes on, pasted content arrives as this author's insertion, exactly as typing
        // does. A run the SOURCE already marked keeps its own mark: that copied history is the source's
        // record, not something this paste performed.
        if (trackChanges)
        {
            var author = ResolveRevisionAuthor();
            var dateXml = _revisionDateXml();
            foreach (var run in clones.SelectMany(PastedRuns))
            {
                if (run.Revision != RevisionKind.None)
                    continue;
                run.Revision = RevisionKind.Inserted;
                run.RevisionAuthor = author;
                run.RevisionDateXml = dateXml;
            }
        }

        var offset = Math.Clamp(caret.Offset, 0, destination.PlainText.Length);
        var head = CreateParagraph(destination, keepStyle: true);
        AppendClonedRuns(head, destination, 0, offset);
        var tail = CreateParagraph(destination, keepStyle: true);
        AppendClonedRuns(tail, destination, offset, destination.PlainText.Length);

        var blocks = new List<Block>();
        var caretBlockOffset = 0;
        var caretBlock = 0;

        // The source's first paragraph continues the line the caret was on...
        if (clones[0] is Paragraph first)
        {
            foreach (var run in first.Runs)
                head.Runs.Add(run);
            CoalesceEditableRuns(head);
            clones.RemoveAt(0);
        }

        blocks.Add(head);
        caretBlockOffset = head.PlainText.Length;

        // ...the rest arrive as their own blocks...
        foreach (var block in clones)
        {
            caretBlock = blocks.Count;
            blocks.Add(block);
            caretBlockOffset = block is Paragraph paragraph ? paragraph.PlainText.Length : 0;
        }

        // ...and whatever followed the caret trails the last of them, on the same line — a paste ends
        // mid-paragraph unless the source's own last block was a table, which needs its own paragraph.
        if (blocks[^1] is Paragraph lastParagraph)
        {
            foreach (var run in tail.Runs)
                lastParagraph.Runs.Add(run);
            CoalesceEditableRuns(lastParagraph);
        }
        else if (tail.Runs.Count > 0)
        {
            caretBlock = blocks.Count;
            blocks.Add(tail);
            caretBlockOffset = 0;
        }

        RestartUnrelatedNumberListRuns(blocks, PrecedingParagraphContinuesNumberList(caret.BlockIndex - 1));

        _commands.Execute(new ReplaceBlocksCommand(caret.BlockIndex, 1, blocks));
        result = new DocumentParagraphEditResult(
            new DocumentTextPosition(caret.BlockIndex + caretBlock, caretBlockOffset),
            ReplacedBlockCount: 1);
        return true;
    }

    /// <summary>
    /// True when the paragraph at <paramref name="index"/> is itself a Number-kind list paragraph --
    /// i.e. content spliced in immediately after it would be continuing that same list rather than
    /// starting an unrelated new one. Mirrors
    /// <see cref="DocumentParagraphFormattingCoordinator.ToggleListKind"/>'s own adjacency check for the
    /// ribbon Numbering button.
    /// </summary>
    private bool PrecedingParagraphContinuesNumberList(int index) =>
        index >= 0
        && index < Document.Blocks.Count
        && Document.Blocks[index] is Paragraph paragraph
        && paragraph.Formatting.ListKind == ListKind.Number;

    /// <summary>
    /// Paste ("Insert Text from File", Ctrl+V, and the ribbon Paste button) clones every source
    /// paragraph's <see cref="ParagraphFormatting.ListStartOverride"/> verbatim (see
    /// <see cref="DocumentMerge.CloneBlocksForInsertion"/>) -- correct for a paragraph that continues its
    /// own source list, but wrong the moment a Number-kind run lands somewhere that isn't already
    /// rendering that same list: the shared per-document counter in
    /// <see cref="DocumentListMarkerSequencePlanner"/> would otherwise just keep counting up from
    /// whatever unrelated list last left off. This mirrors the exact restart detection
    /// <see cref="DocumentParagraphFormattingCoordinator.ToggleListKind"/> already performs for the
    /// ribbon Numbering button: only the first paragraph of each newly-unrelated Number run is forced to
    /// restart at 1 (a paragraph that already carries an explicit override -- the source's own deliberate
    /// restart/continue mark -- is left untouched); every following paragraph in that same run keeps
    /// counting from there. A run interrupted by a non-Number paragraph (or a table) partway through the
    /// pasted content is itself unrelated to whatever came before it, so it restarts too.
    /// </summary>
    private static void RestartUnrelatedNumberListRuns(IReadOnlyList<Block> blocks, bool precedingContinuesNumberList)
    {
        var previousWasNumberList = precedingContinuesNumberList;
        foreach (var block in blocks)
        {
            if (block is not Paragraph paragraph || paragraph.Formatting.ListKind != ListKind.Number)
            {
                previousWasNumberList = false;
                continue;
            }

            if (!previousWasNumberList && paragraph.Formatting.ListStartOverride is null)
                paragraph.Formatting = paragraph.Formatting with { ListStartOverride = 1 };

            previousWasNumberList = true;
        }
    }

    /// <summary>
    /// Whether a paragraph can receive a pasted document. This is <see cref="CanRestructure"/>'s test with
    /// one allowance: a paragraph holding a content control CAN receive one, because the paste clones runs
    /// rather than rebuilding them from characters, and the caret-inside-a-field case is refused
    /// separately. Without the allowance a paste anywhere in a form's paragraph fell back to plain text.
    /// </summary>
    private static bool CanReceivePaste(Paragraph paragraph) =>
        paragraph.BookmarkBoundaries.Count == 0
        && paragraph.BookmarkNames.Count == 0
        && paragraph.DropCap is null
        && paragraph.SectionBreak is null
        && paragraph.PreservedNumbering is null
        && paragraph.ParagraphFormatRevision is null
        && !ContentControlInteractionPlanner.IsBlockContentControlLocked(paragraph.BlockContentControl)
        && paragraph.Runs.All(run => run.Control is not null || IsPortableBodyTextRun(run));

    /// <summary>Every run a pasted block carries, including the ones inside a pasted table's cells.</summary>
    private static IEnumerable<Run> PastedRuns(Block block) => block switch
    {
        Paragraph paragraph => paragraph.Runs,
        Table table => table.Rows
            .SelectMany(row => row.Cells)
            .SelectMany(cell => cell.Paragraphs)
            .SelectMany(paragraph => paragraph.Runs),
        _ => [],
    };

    private static bool IsOffsetInsideContentControl(Paragraph paragraph, int offset)
    {
        var position = 0;
        foreach (var run in paragraph.Runs)
        {
            var end = position + run.Text.Length;
            if (run.Control is not null && offset > position && offset < end)
                return true;
            position = end;
        }

        return false;
    }

    /// <summary>Sorts paragraph slots in a body span while preserving interleaved non-paragraph blocks.</summary>
    public bool SortParagraphSpan(
        int firstBlockIndex,
        int lastBlockIndex,
        SortKind kind,
        bool ascending,
        bool caseSensitive,
        bool hasHeaderRow)
    {
        var first = Math.Min(firstBlockIndex, lastBlockIndex);
        var last = Math.Max(firstBlockIndex, lastBlockIndex);
        if (first < 0 || last >= Document.Blocks.Count)
            return false;
        var paragraphs = Document.Blocks
            .Skip(first)
            .Take(last - first + 1)
            .OfType<Paragraph>()
            .ToArray();
        if (paragraphs.Length < 2)
            return false;

        var sorted = ParagraphSort.Sort(
            paragraphs,
            kind,
            ascending,
            caseSensitive,
            hasHeaderRow);
        var replacement = new List<Block>(last - first + 1);
        var nextSorted = 0;
        for (var index = first; index <= last; index++)
        {
            replacement.Add(Document.Blocks[index] is Paragraph
                ? sorted[nextSorted++]
                : Document.Blocks[index]);
        }
        _commands.Execute(new ReplaceBlocksCommand(first, replacement.Count, replacement));
        return true;
    }

    /// <summary>Converts a paragraph span to a table and reports the replacement block index.</summary>
    public int ConvertParagraphsToTable(
        IReadOnlyList<int> blockIndices,
        char delimiter,
        bool showBorders)
    {
        ArgumentNullException.ThrowIfNull(blockIndices);
        var targets = ResolveParagraphIndices(blockIndices).Order().ToArray();
        var plan = DocumentTableConversionMutationPlanner.PlanTextToTable(Document, targets, delimiter);
        if (plan is null)
            return -1;
        if (showBorders && plan.Replacement.SingleOrDefault() is Table table)
            table.Formatting = table.Formatting with { Borders = true };
        _commands.Execute(new ReplaceBlocksCommand(plan.StartIndex, plan.RemoveCount, plan.Replacement));
        return plan.StartIndex;
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
    /// Removes exactly the bookmark instance at <paramref name="location"/> through the shared undo
    /// history — not every paragraph document-wide that happens to share its name. Used by the Bookmark
    /// Manager's Delete action so selecting one duplicate-named entry never destroys a different one (see
    /// <see cref="Bookmarks.RemoveBookmarkAt"/>).
    /// </summary>
    public bool RemoveBookmarkAt(BookmarkLocation location)
    {
        if (string.IsNullOrEmpty(location.Name)
            || Bookmarks.ResolveLocation(Document, location) is not { } paragraph
            || !paragraph.BookmarkNames.Contains(location.Name, StringComparer.Ordinal))
        {
            return false;
        }

        _commands.Execute(new RemoveBookmarkAtCommand(location));
        return true;
    }

    /// <summary>Applies one portable paragraph-format transform as a single undoable edit.</summary>
    public bool FormatParagraphs(
        IReadOnlyList<int> blockIndices,
        Func<ParagraphFormatting, ParagraphFormatting> transform,
        string undoLabel = "Paragraph Formatting")
    {
        ArgumentNullException.ThrowIfNull(blockIndices);
        ArgumentNullException.ThrowIfNull(transform);
        ArgumentException.ThrowIfNullOrWhiteSpace(undoLabel);
        var targets = ResolveParagraphIndices(blockIndices);
        if (targets.Count == 0)
            return false;

        ExecuteGroup(
            targets
                .Select(index => (IDocumentCommand)new SetParagraphFormattingCommand(
                    index,
                    transform(((Paragraph)Document.Blocks[index]).Formatting)))
                .ToArray(),
            undoLabel);
        return true;
    }

    /// <summary>Applies a character-format transform to complete paragraph runs as one undo step.</summary>
    public bool FormatParagraphRuns(
        IReadOnlyList<int> blockIndices,
        Func<RunFormatting, RunFormatting> transform,
        string undoLabel = "Character Formatting")
    {
        ArgumentNullException.ThrowIfNull(blockIndices);
        ArgumentNullException.ThrowIfNull(transform);
        ArgumentException.ThrowIfNullOrWhiteSpace(undoLabel);
        var targets = ResolveParagraphIndices(blockIndices);
        if (targets.Count == 0)
            return false;

        ExecuteGroup(
            targets
                .Select(index => (IDocumentCommand)new FormatParagraphRunsCommand(index, transform))
                .ToArray(),
            undoLabel);
        return true;
    }

    /// <summary>
    /// Applies one character-format transform to exact renderer-projected body-text ranges. A valid
    /// selection is considered handled even when every covered run already has the requested formatting.
    /// </summary>
    public bool TryApplyRunFormatting(
        IReadOnlyList<DocumentTextRange> ranges,
        Func<RunFormatting, RunFormatting> transform,
        string undoLabel = "Character Formatting")
    {
        ArgumentNullException.ThrowIfNull(ranges);
        ArgumentNullException.ThrowIfNull(transform);
        ArgumentException.ThrowIfNullOrWhiteSpace(undoLabel);
        var resolved = ResolveBodyTextRanges(ranges);
        if (resolved.Count == 0)
            return false;

        ExecuteRunFormatting(resolved, transform, undoLabel);
        return true;
    }

    /// <summary>
    /// Sets character formatting over exact body-text ranges, using <paramref name="isSet"/> to preserve
    /// semantic no-op behavior such as case-insensitive color equality.
    /// </summary>
    public bool TrySetRunFormatting(
        IReadOnlyList<DocumentTextRange> ranges,
        Func<RunFormatting, bool> isSet,
        Func<RunFormatting, RunFormatting> set,
        string undoLabel = "Character Formatting")
    {
        ArgumentNullException.ThrowIfNull(ranges);
        ArgumentNullException.ThrowIfNull(isSet);
        ArgumentNullException.ThrowIfNull(set);
        ArgumentException.ThrowIfNullOrWhiteSpace(undoLabel);
        var resolved = ResolveBodyTextRanges(ranges);
        if (resolved.Count == 0)
            return false;
        if (RunRangesAllMatch(resolved, isSet))
            return true;

        ExecuteRunFormatting(resolved, set, undoLabel);
        return true;
    }

    /// <summary>
    /// Toggles one character-format property consistently across all exact body-text ranges. The target
    /// value is cleared only when every covered run already matches <paramref name="isSet"/>.
    /// </summary>
    public bool TryToggleRunFormatting(
        IReadOnlyList<DocumentTextRange> ranges,
        Func<RunFormatting, bool> isSet,
        Func<RunFormatting, bool, RunFormatting> set,
        string undoLabel = "Character Formatting")
    {
        ArgumentNullException.ThrowIfNull(ranges);
        ArgumentNullException.ThrowIfNull(isSet);
        ArgumentNullException.ThrowIfNull(set);
        ArgumentException.ThrowIfNullOrWhiteSpace(undoLabel);
        var resolved = ResolveBodyTextRanges(ranges);
        if (resolved.Count == 0)
            return false;

        var target = !RunRangesAllMatch(resolved, isSet);
        ExecuteRunFormatting(
            resolved,
            formatting => set(formatting, target),
            undoLabel);
        return true;
    }

    /// <summary>Applies a proofing-language plan as one portable character-formatting edit.</summary>
    public bool TryApplyProofingLanguage(
        ProofingLanguageApplyPlan plan,
        Func<int, Paragraph, bool>? canEditParagraph = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var ranges = plan.Ranges
            .Where(range => range.BlockIndex >= 0
                && range.BlockIndex < Document.Blocks.Count
                && Document.Blocks[range.BlockIndex] is Paragraph paragraph
                && (canEditParagraph?.Invoke(range.BlockIndex, paragraph) ?? true))
            .Select(range => new DocumentTextRange(
                new DocumentTextPosition(range.BlockIndex, range.StartOffset),
                new DocumentTextPosition(range.BlockIndex, range.EndOffset)))
            .ToArray();

        return TrySetRunFormatting(
            ranges,
            formatting => string.Equals(
                formatting.LanguageTag,
                plan.LanguageTag,
                StringComparison.OrdinalIgnoreCase),
            formatting => formatting with { LanguageTag = plan.LanguageTag },
            "Proofing Language");
    }

    /// <summary>Applies confirmed soft-hyphen insertions through the shared undo history.</summary>
    public bool ApplyManualHyphenation(IReadOnlyList<ManualHyphenationEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        if (edits.Count == 0)
            return false;
        _commands.Execute(new ApplyManualHyphenationCommand(edits));
        return true;
    }

    /// <summary>Moves one outline subtree and returns the heading's resulting model index.</summary>
    public int MoveHeadingSubtree(int blockIndex, bool moveUp)
    {
        if (blockIndex < 0 || blockIndex >= Document.Blocks.Count)
            return blockIndex;
        var heading = Document.Blocks[blockIndex];
        var reordered = OutlineTools.MoveSubtree(Document.Blocks, blockIndex, moveUp);
        if (ReferenceEquals(reordered, Document.Blocks))
            return blockIndex;

        _commands.Execute(new ReorderBlocksCommand(reordered));
        for (var index = 0; index < reordered.Count; index++)
        {
            if (ReferenceEquals(reordered[index], heading))
                return index;
        }
        return blockIndex;
    }

    public bool ApplyDropCap(
        int blockIndex,
        DropCapPosition position,
        double sizePt,
        int lineSpan,
        double distanceFromTextPt)
    {
        if (blockIndex < 0
            || blockIndex >= Document.Blocks.Count
            || Document.Blocks[blockIndex] is not Paragraph)
        {
            return false;
        }

        _commands.Execute(new ReplaceParagraphRunsCommand(
            blockIndex,
            paragraph => DropCap.ApplyDropCap(
                paragraph,
                position,
                sizePt,
                lineSpan,
                distanceFromTextPt)));
        return true;
    }

    public bool ClearDropCap(int blockIndex)
    {
        if (blockIndex < 0
            || blockIndex >= Document.Blocks.Count
            || Document.Blocks[blockIndex] is not Paragraph)
        {
            return false;
        }

        _commands.Execute(new ReplaceParagraphRunsCommand(blockIndex, DropCap.ClearFormatting));
        return true;
    }

    /// <summary>Applies a paragraph style to all valid targets as a single undoable edit.</summary>
    public bool SetParagraphStyles(
        IReadOnlyList<int> blockIndices,
        string? styleId,
        string undoLabel = "Apply Style")
    {
        ArgumentNullException.ThrowIfNull(blockIndices);
        ArgumentException.ThrowIfNullOrWhiteSpace(undoLabel);
        var targets = ResolveParagraphIndices(blockIndices);
        if (targets.Count == 0)
            return false;

        ExecuteGroup(
            targets
                .Select(index => (IDocumentCommand)new SetParagraphStyleCommand(index, styleId))
                .ToArray(),
            undoLabel);
        return true;
    }

    /// <summary>
    /// Resolves and applies a named style from renderer-projected selection coordinates. Linked paragraph
    /// styles become character formatting only for a nonempty text selection. Exact character ranges and
    /// paragraph targets are mutated through the shared command history; collapsed-caret and native text
    /// surfaces receive a formatting projection in the result instead of an invented model mutation.
    /// </summary>
    public NamedStyleApplicationResult? ApplyNamedStyle(
        string styleId,
        NamedStyleApplicationTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (string.IsNullOrWhiteSpace(styleId))
            return null;

        BuiltInStyles.EnsureSeeded(Document, styleId);
        var plan = NamedStyleApplicationPlanner.Resolve(Document, styleId, target.HasTextSelection);
        if (plan is null)
            return null;

        if (plan.Kind == NamedStyleApplicationKind.Paragraph)
        {
            var changed = SetParagraphStyles(target.ParagraphIndices, plan.EffectiveStyle.Id);
            return new NamedStyleApplicationResult(
                plan.RequestedStyleId,
                plan.EffectiveStyle,
                plan.Kind,
                changed,
                RequiresRendererProjection: false);
        }

        if (!target.CanApplyCharacterFormatting)
        {
            return new NamedStyleApplicationResult(
                plan.RequestedStyleId,
                plan.EffectiveStyle,
                plan.Kind,
                ModelChanged: false,
                RequiresRendererProjection: false);
        }

        var ranges = ResolveBodyTextRanges(target.TextRanges);
        if (ranges.Count == 0)
        {
            return new NamedStyleApplicationResult(
                plan.RequestedStyleId,
                plan.EffectiveStyle,
                plan.Kind,
                ModelChanged: false,
                RequiresRendererProjection: true);
        }

        Func<RunFormatting, RunFormatting> transform = formatting =>
            NamedStyleApplicationPlanner.OverlayCharacterStyle(formatting, plan.EffectiveStyle.Run);
        var commands = CreateRunFormattingCommands(
            ranges,
            transform,
            "Apply Character Style",
            skipUnchangedRanges: true);
        if (commands.Count > 0)
            ExecuteGroup(commands, "Apply Character Style");

        return new NamedStyleApplicationResult(
            plan.RequestedStyleId,
            plan.EffectiveStyle,
            plan.Kind,
            ModelChanged: commands.Count > 0,
            RequiresRendererProjection: false);
    }

    /// <summary>Transforms one paragraph style after validating the portable model target.</summary>
    public bool ShiftParagraphStyle(int blockIndex, Func<string?, string?> shift)
    {
        ArgumentNullException.ThrowIfNull(shift);
        if (blockIndex < 0
            || blockIndex >= Document.Blocks.Count
            || Document.Blocks[blockIndex] is not Paragraph paragraph)
        {
            return false;
        }

        var next = shift(paragraph.StyleId);
        if (string.Equals(next, paragraph.StyleId, StringComparison.Ordinal))
            return false;
        _commands.Execute(new SetParagraphStyleCommand(blockIndex, next));
        return true;
    }

    /// <summary>
    /// Applies a multilevel-list definition, linked heading styles, and number formats as one undo step.
    /// </summary>
    public bool ApplyMultilevelListDefinition(
        IReadOnlyList<int> blockIndices,
        MultilevelListDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(blockIndices);
        ArgumentNullException.ThrowIfNull(definition);
        var targets = ResolveParagraphIndices(blockIndices);
        if (targets.Count == 0)
            return false;

        var commands = new List<IDocumentCommand>();
        foreach (var index in targets)
        {
            var paragraph = (Paragraph)Document.Blocks[index];
            var updated = MultilevelListDialogPlanner.ApplyDefinition(
                paragraph.Formatting,
                definition);
            commands.Add(new SetParagraphFormattingCommand(index, updated));
            var linkedStyleId = MultilevelListDialogPlanner.ResolveLinkedHeadingStyleId(
                updated.ListLevel,
                definition);
            if (linkedStyleId is not null && Document.Styles.ContainsKey(linkedStyleId))
                commands.Add(new SetParagraphStyleCommand(index, linkedStyleId));
        }
        commands.Add(new SetMultiLevelNumberFormatsCommand(definition.NumberFormats));
        ExecuteGroup(commands, "Define Multilevel List");
        return true;
    }

    /// <summary>Replaces the document's multilevel number formats as one reversible formatting edit.</summary>
    public bool SetMultiLevelNumberFormats(IReadOnlyList<ListNumberFormat> numberFormats)
    {
        ArgumentNullException.ThrowIfNull(numberFormats);
        var normalized = numberFormats
            .Take(MultiLevelListFormat.LevelCount)
            .Concat(Enumerable.Repeat(
                ListNumberFormat.Decimal,
                MultiLevelListFormat.LevelCount))
            .Take(MultiLevelListFormat.LevelCount)
            .ToArray();
        if (Document.MultiLevelList.NumberFormats.SequenceEqual(normalized))
            return false;

        _commands.Execute(new SetMultiLevelNumberFormatsCommand(normalized));
        return true;
    }

    /// <summary>Creates a catalog style and applies it to the requested paragraphs as one undo step.</summary>
    public DocumentStyle? CreateParagraphStyleAndApply(
        IReadOnlyList<int> blockIndices,
        string name,
        string? basedOnId,
        RunFormatting run,
        ParagraphFormatting paragraph,
        string? nextStyleId)
    {
        ArgumentNullException.ThrowIfNull(blockIndices);
        var targets = ResolveParagraphIndices(blockIndices);
        DocumentStyle? created = null;
        _commands.BeginUndoGroup();
        try
        {
            _commands.Execute(new StyleCatalogCommand("New Style", document =>
            {
                created = StyleManager.CreateStyle(
                    document,
                    name,
                    basedOnId,
                    run,
                    paragraph,
                    nextStyleId);
            }));
            if (created is not null)
            {
                foreach (var index in targets)
                    _commands.Execute(new SetParagraphStyleCommand(index, created.Id));
            }
            _commands.CommitUndoGroup("New Style");
        }
        catch
        {
            _commands.AbortUndoGroup();
            throw;
        }
        return created;
    }

    /// <summary>Updates a paragraph-style catalog entry through the shared undo history.</summary>
    public DocumentStyle? ModifyParagraphStyle(
        string styleId,
        RunFormatting run,
        ParagraphFormatting paragraph,
        string? basedOnId,
        string? nextStyleId)
    {
        if (string.IsNullOrWhiteSpace(styleId) || !Document.Styles.TryGetValue(styleId, out var existing))
            return null;

        // Dry-run the same StyleManager.ModifyStyle logic against a scratch catalog (a fresh dictionary
        // holding the same DocumentStyle references -- ModifyStyle only ever replaces the target entry
        // with a new instance, never mutates one in place, so sharing the other entries is safe) so a
        // Modify Style OK click that changes nothing (dialog closed without edits, or edits that
        // round-trip back to the original values) never reaches the command bus. Without this, every
        // such click pushed a no-op "Modify Style" undo entry -- see finding shared-undo-across-panes F2.
        var scratch = TextDocument.CreateEmpty();
        scratch.Styles.Clear();
        foreach (var (id, style) in Document.Styles)
            scratch.Styles[id] = style;

        var preview = StyleManager.ModifyStyle(
            scratch,
            styleId,
            run: run,
            para: paragraph,
            basedOnId: basedOnId,
            clearBasedOn: basedOnId is null,
            nextStyleId: nextStyleId,
            clearNext: nextStyleId is null);

        if (preview is not null
            && preview.Run == existing.Run
            && preview.Paragraph == existing.Paragraph
            && string.Equals(preview.BasedOnStyleId, existing.BasedOnStyleId, StringComparison.Ordinal)
            && string.Equals(preview.NextStyleId, existing.NextStyleId, StringComparison.Ordinal))
        {
            return existing;
        }

        DocumentStyle? updated = null;
        _commands.Execute(new StyleCatalogCommand("Modify Style", document =>
        {
            updated = StyleManager.ModifyStyle(
                document,
                styleId,
                run: run,
                para: paragraph,
                basedOnId: basedOnId,
                clearBasedOn: basedOnId is null,
                nextStyleId: nextStyleId,
                clearNext: nextStyleId is null);
        }));
        return updated;
    }

    /// <summary>Deletes a custom paragraph style through the shared catalog policy and undo history.</summary>
    public bool DeleteParagraphStyle(string styleId)
    {
        if (string.IsNullOrWhiteSpace(styleId)
            || StyleManager.IsBuiltIn(styleId)
            || !Document.Styles.ContainsKey(styleId))
        {
            return false;
        }

        var deleted = false;
        _commands.Execute(new StyleCatalogCommand(
            "Delete Style",
            document => deleted = StyleManager.DeleteStyle(document, styleId)));
        return deleted;
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
            || !CanRestructureAllowingSectionBreak(previous)
            || !CanRestructureAllowingSectionBreak(current))
        {
            return false;
        }

        // freew-cc-6: the merge removes `current`'s block, so a body-level region that wraps only it —
        // Word's sdtLocked w:sdt — would be deleted along with it.
        if (!CanMergeAcrossBlockRegions(previous, current))
            return false;

        var caretOffset = previous.PlainText.Length;
        var merged = CreateParagraph(previous, keepStyle: true);
        AppendClonedRuns(merged, previous, 0, previous.PlainText.Length);
        AppendClonedRuns(merged, current, 0, current.PlainText.Length);
        CoalesceEditableRuns(merged);
        // Deleting Backspace at the start of `current` removes `previous`'s own paragraph mark, so
        // `previous`'s section break (if any) is discarded and `current`'s (the surviving mark) carries
        // forward -- see SurvivingSectionBreak. The same surviving mark owns the paragraph's pPr, so the
        // merged paragraph's style/formatting must come from `current`, not from `previous` (whose mark
        // was deleted) -- CreateParagraph above only supplies the body-level region wrappers from
        // `previous`, the block that keeps this slot in Blocks.
        merged.StyleId = current.StyleId;
        merged.Formatting = current.Formatting;
        merged.SectionBreak = SurvivingSectionBreak(earlier: previous, later: current);
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
            || !CanRestructureAllowingSectionBreak(current)
            || !CanRestructureAllowingSectionBreak(next))
        {
            return false;
        }

        // freew-cc-6: the merge removes `next`'s block along with any body-level region wrapping it.
        if (!CanMergeAcrossBlockRegions(current, next))
            return false;

        var caretOffset = current.PlainText.Length;
        var merged = CreateParagraph(current, keepStyle: true);
        AppendClonedRuns(merged, current, 0, current.PlainText.Length);
        AppendClonedRuns(merged, next, 0, next.PlainText.Length);
        CoalesceEditableRuns(merged);
        // Deleting Delete-forward at the end of `current` removes `current`'s own paragraph mark, so
        // `current`'s section break (if any) is discarded and `next`'s (the surviving mark) carries
        // forward -- see SurvivingSectionBreak. The same surviving mark owns the paragraph's pPr, so the
        // merged paragraph's style/formatting must come from `next`, not from `current` (whose mark was
        // deleted) -- CreateParagraph above only supplies the body-level region wrappers from `current`,
        // the block that keeps this slot in Blocks.
        merged.StyleId = next.StyleId;
        merged.Formatting = next.Formatting;
        merged.SectionBreak = SurvivingSectionBreak(earlier: current, later: next);
        _commands.Execute(new ReplaceBlocksCommand(blockIndex, 2, [merged]));

        result = new DocumentParagraphEditResult(
            new DocumentTextPosition(blockIndex, caretOffset),
            ReplacedBlockCount: 2);
        return true;
    }

    /// <summary>
    /// Records deletion of the paragraph mark that owns a body-paragraph boundary, leaving both
    /// paragraphs intact until the revision is accepted.
    /// </summary>
    public bool TryDeleteBodyParagraphBoundaryAsRevision(
        int owningParagraphIndex,
        DocumentTextPosition caret,
        out DocumentParagraphEditResult result)
    {
        result = default;
        if (owningParagraphIndex < 0
            || owningParagraphIndex + 1 >= Document.Blocks.Count
            || Document.Blocks[owningParagraphIndex] is not Paragraph
            || Document.Blocks[owningParagraphIndex + 1] is not Paragraph)
        {
            return false;
        }

        _commands.Execute(new SetParagraphMarkRevisionCommand(
            owningParagraphIndex,
            RevisionKind.Deleted,
            ResolveRevisionAuthor(),
            _revisionDateXml()));
        result = new DocumentParagraphEditResult(caret, ReplacedBlockCount: 0);
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
        var second = CreateParagraph(remaining, keepStyle: true);
        second.StyleId = ResolveNextParagraphStyleId(remaining.StyleId);
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
    /// Replaces a body selection as one tracked, undoable edit. Existing selected text is retained as a
    /// deletion revision and the replacement is inserted at the normalized range start. Paragraph marks
    /// covered by a cross-paragraph selection are retained as deletion revisions until accepted.
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
    /// Marks a body range as deleted. Forward Delete can advance past retained struck text; Backspace and
    /// selection deletion collapse to the normalized range start. Cross-paragraph selections also mark each
    /// covered paragraph boundary as deleted without physically merging the paragraphs.
    /// </summary>
    public bool TryDeleteTrackedBodyText(
        DocumentTextRange range,
        bool advancePastKeptText,
        out DocumentTextEditResult result)
    {
        result = default;
        var normalized = range.Normalize();
        if (normalized.Start.BlockIndex != normalized.End.BlockIndex)
        {
            return TryResolveBodySpan(range, requireStructuralEdit: true, out var span)
                && TryReplaceTrackedBodySpanCore(
                    span,
                    replacementText: null,
                    formatting: null,
                    inheritHyperlink: true,
                    explicitHyperlink: null,
                    out result);
        }

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
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var normalized = range.Normalize();
        if (normalized.Start.BlockIndex != normalized.End.BlockIndex)
        {
            return TryResolveBodySpan(range, requireStructuralEdit: true, out var span)
                && TryReplaceTrackedBodySpanCore(
                    span,
                    text,
                    formatting,
                    inheritHyperlink,
                    explicitHyperlink,
                    out result);
        }

        if (!TryResolveBodyRange(range, out var blockIndex, out var startOffset, out var endOffset))
            return false;

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

    private bool TryReplaceTrackedBodySpanCore(
        DocumentTextRange span,
        string? replacementText,
        RunFormatting? formatting,
        bool inheritHyperlink,
        DocumentTextHyperlink? explicitHyperlink,
        out DocumentTextEditResult result)
    {
        result = default;
        if (span.Start.BlockIndex >= span.End.BlockIndex
            || Document.Blocks[span.Start.BlockIndex] is not Paragraph startParagraph
            || Document.Blocks[span.End.BlockIndex] is not Paragraph endParagraph)
        {
            return false;
        }

        var startOffset = Math.Clamp(span.Start.Offset, 0, startParagraph.PlainText.Length);
        var endOffset = Math.Clamp(span.End.Offset, 0, endParagraph.PlainText.Length);
        var author = ResolveRevisionAuthor();
        var dateXml = _revisionDateXml();
        var activeFormatting = formatting
            ?? RevisionEditPlanner.FormattingAtOffset(startParagraph, startOffset);
        var hyperlink = inheritHyperlink
            ? RevisionEditPlanner.LinkAtOffset(startParagraph, startOffset)
            : new RevisionEditPlanner.InsertOptions(
                HyperlinkUrl: explicitHyperlink?.Url,
                HyperlinkAnchor: explicitHyperlink?.Anchor,
                HyperlinkTooltip: explicitHyperlink?.Tooltip);
        var insertOptions = new RevisionEditPlanner.InsertOptions(
            RevisionKind.Inserted,
            author,
            dateXml,
            hyperlink.HyperlinkUrl,
            hyperlink.HyperlinkAnchor,
            hyperlink.HyperlinkTooltip);
        var keptDeletedText = false;
        var ownsUndoGroup = !_commands.IsUndoGroupOpen;
        if (ownsUndoGroup)
            _commands.BeginUndoGroup();

        try
        {
            _commands.Execute(new ReplaceParagraphRunsCommand(span.Start.BlockIndex, paragraph =>
            {
                keptDeletedText |= RevisionEditPlanner.DeleteRangeAsRevision(
                    paragraph,
                    startOffset,
                    paragraph.PlainText.Length,
                    author,
                    dateXml).KeptDeletedText;
                if (!string.IsNullOrEmpty(replacementText))
                {
                    RevisionEditPlanner.InsertText(
                        paragraph,
                        startOffset,
                        replacementText,
                        activeFormatting,
                        insertOptions);
                }
            }));

            for (var blockIndex = span.Start.BlockIndex + 1; blockIndex < span.End.BlockIndex; blockIndex++)
            {
                _commands.Execute(new ReplaceParagraphRunsCommand(blockIndex, paragraph =>
                {
                    keptDeletedText |= RevisionEditPlanner.DeleteRangeAsRevision(
                        paragraph,
                        0,
                        paragraph.PlainText.Length,
                        author,
                        dateXml).KeptDeletedText;
                }));
            }

            _commands.Execute(new ReplaceParagraphRunsCommand(span.End.BlockIndex, paragraph =>
            {
                keptDeletedText |= RevisionEditPlanner.DeleteRangeAsRevision(
                    paragraph,
                    0,
                    endOffset,
                    author,
                    dateXml).KeptDeletedText;
            }));

            for (var blockIndex = span.Start.BlockIndex; blockIndex < span.End.BlockIndex; blockIndex++)
            {
                _commands.Execute(new SetParagraphMarkRevisionCommand(
                    blockIndex,
                    RevisionKind.Deleted,
                    author,
                    dateXml));
            }
        }
        catch
        {
            if (ownsUndoGroup)
                _commands.RollbackUndoGroup();
            throw;
        }

        if (ownsUndoGroup)
        {
            _commands.CommitUndoGroup(
                string.IsNullOrEmpty(replacementText) ? "Delete Selection" : "Replace Selection");
        }

        result = new DocumentTextEditResult(
            new DocumentTextPosition(
                span.Start.BlockIndex,
                startOffset + (replacementText?.Length ?? 0)),
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

    /// <summary>
    /// Whether a (possibly cross-paragraph) delete/replace span can restructure every paragraph it
    /// touches. A paragraph owning a <see cref="Paragraph.SectionBreak"/> is allowed inside the span, not
    /// just at a caret boundary: per Word's rule, merging across it discards the section(s) that end
    /// strictly before <see cref="DocumentTextRange.End"/>'s paragraph and keeps that last paragraph's own
    /// section break -- exactly what <see cref="TryMergeBodyParagraphWithPrevious"/> and
    /// <see cref="TryMergeBodyParagraphWithNext"/> already do for a collapsed caret via
    /// <see cref="SurvivingSectionBreak"/>. <see cref="BuildRangeReplacement"/> is what actually carries
    /// that surviving break onto the merged paragraph.
    /// </summary>
    private bool CanRestructure(DocumentTextRange span)
    {
        var survivor = Document.Blocks[span.Start.BlockIndex] as Paragraph;
        for (var blockIndex = span.Start.BlockIndex; blockIndex <= span.End.BlockIndex; blockIndex++)
        {
            if (Document.Blocks[blockIndex] is not Paragraph paragraph || !CanRestructureAllowingSectionBreak(paragraph))
                return false;

            // freew-cc-6: everything past the first block is replaced by the merged paragraph, so a
            // body-level region wrapping one of them is deleted — which Word's sdtLocked forbids.
            if (blockIndex > span.Start.BlockIndex && (survivor is null || !CanMergeAcrossBlockRegions(survivor, paragraph)))
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
        // Any section break strictly before the end paragraph is being deleted along with that
        // paragraph's own block (see CanRestructure(DocumentTextRange)'s section-break allowance); per
        // SurvivingSectionBreak's rule the merged paragraph keeps only the END paragraph's own break (or
        // lack of one). When no section break is involved anywhere in the span this is always already
        // null, so it is a no-op for every other caller of this helper.
        replacement.SectionBreak = endParagraph.SectionBreak;
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

    /// <summary>
    /// The factory every restructuring path here builds its replacement paragraph with. Besides the
    /// formatting, it carries the BODY-LEVEL wrappers the source paragraph sits inside: the block content
    /// control (an outer w:sdt around whole paragraphs) and the custom-XML region. Both are grouped by
    /// instance when written, so a rebuilt paragraph that dropped them would silently fall out of its
    /// region — and on a split, both halves belong to the region the original was in.
    /// </summary>
    private static Paragraph CreateParagraph(Paragraph source, bool keepStyle) => new()
    {
        Formatting = source.Formatting,
        StyleId = keepStyle ? source.StyleId : null,
        BlockContentControl = source.BlockContentControl,
        BlockCustomXml = source.BlockCustomXml,
    };

    /// <summary>
    /// Resolves the style id for the paragraph created when the user presses Enter, per Word's
    /// "style for following paragraph" chain (<c>w:next</c>). Follows <paramref name="currentStyleId"/>'s
    /// <see cref="DocumentStyle.NextStyleId"/> when it names a style that exists in the document;
    /// otherwise keeps the current style, matching Word's behavior when no next style is set (or the
    /// referenced style is missing).
    /// </summary>
    private string? ResolveNextParagraphStyleId(string? currentStyleId)
    {
        if (currentStyleId is not null
            && Document.Styles.TryGetValue(currentStyleId, out var style)
            && style.NextStyleId is { } nextStyleId
            && Document.Styles.ContainsKey(nextStyleId))
        {
            return nextStyleId;
        }
        return currentStyleId;
    }

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

    // A locked BLOCK-level content control (a body w:sdt wrapping the whole paragraph -- e.g. Word's
    // "lock whole paragraph", see Paragraph.BlockContentControl) has no run-level marker at all, so it
    // must be checked separately from IsPortableBodyTextRun's run.Control-is-null check below: without
    // this, the portable body-edit session would happily mutate a locked whole-paragraph content control's
    // text directly (ReplaceParagraphRunsCommand et al.), bypassing every renderer-side lock choke point
    // that only runs once this session DECLINES (e.g. DocumentView.TryPrepareNativeFallback in both
    // shells). An UNLOCKED block-level control is left portable (unlike a run-level one, which is always
    // excluded regardless of lock) because there is no dedicated interactive UI for it the way there is for
    // a checkbox/date-picker/drop-down run -- ordinary typing inside an unlocked whole-paragraph control is
    // just ordinary typing.
    private static bool IsPortableBodyTextParagraph(Paragraph paragraph) =>
        paragraph.BookmarkBoundaries.Count == 0
        && !ContentControlInteractionPlanner.IsBlockContentControlLocked(paragraph.BlockContentControl)
        && paragraph.Runs.All(IsPortableBodyTextRun);

    /// <summary>
    /// Whether two adjacent paragraphs may be merged into one given the body-level regions they sit in.
    /// The merge keeps <paramref name="survivor"/>'s region and discards <paramref name="absorbed"/>'s
    /// block, so a differing region on the absorbed paragraph disappears — which Word's <c>sdtLocked</c>
    /// forbids. Paragraphs inside the SAME region merge freely; the region simply keeps the merged block.
    /// </summary>
    private static bool CanMergeAcrossBlockRegions(Paragraph survivor, Paragraph absorbed) =>
        ReferenceEquals(survivor.BlockContentControl, absorbed.BlockContentControl)
        || ContentControlInteractionPlanner.CanDeleteBlockContentControl(absorbed.BlockContentControl);

    private static bool CanRestructure(Paragraph paragraph) =>
        CanRestructureAllowingSectionBreak(paragraph) && paragraph.SectionBreak is null;

    /// <summary>
    /// Same eligibility test as <see cref="CanRestructure"/> but permits the paragraph to own a
    /// <see cref="Paragraph.SectionBreak"/> marker. Used only by the two section-break-aware merge
    /// helpers (<see cref="TryMergeBodyParagraphWithPrevious"/>/<see cref="TryMergeBodyParagraphWithNext"/>),
    /// which delete the section break themselves and must decide separately -- via
    /// <see cref="SurvivingSectionBreak"/> -- which of the two paragraphs' section properties survive the
    /// merge, matching Word's rule that deleting a section-break paragraph mark folds the deleted section
    /// into the one that follows it.
    /// </summary>
    private static bool CanRestructureAllowingSectionBreak(Paragraph paragraph) =>
        IsPortableBodyTextParagraph(paragraph)
        && paragraph.BookmarkNames.Count == 0
        && paragraph.DropCap is null
        && paragraph.PreservedNumbering is null
        && paragraph.ParagraphFormatRevision is null;

    /// <summary>
    /// Deleting the paragraph mark between <paramref name="earlier"/> and <paramref name="later"/> always
    /// deletes <paramref name="earlier"/>'s own mark (whether the user pressed Delete at the end of
    /// <paramref name="earlier"/> or Backspace at the start of <paramref name="later"/> -- both target the
    /// same boundary). Per Word's section-break rule, the section that mark used to end is discarded and
    /// the merged content is folded into the section that follows, so the merged paragraph must keep
    /// <paramref name="later"/>'s <see cref="Paragraph.SectionBreak"/> (or lack of one) and never
    /// <paramref name="earlier"/>'s.
    /// </summary>
    private static Section? SurvivingSectionBreak(Paragraph earlier, Paragraph later) => later.SectionBreak;

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

    internal IReadOnlyList<DocumentTextRange> ResolveBodyTextRanges(
        IReadOnlyList<DocumentTextRange> ranges)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        var resolved = new List<DocumentTextRange>();
        foreach (var requested in ranges)
        {
            var range = requested.Normalize();
            if (range.IsCollapsed
                || range.Start.BlockIndex < 0
                || range.End.BlockIndex >= Document.Blocks.Count)
            {
                continue;
            }

            var firstBlock = range.Start.BlockIndex;
            var lastBlock = range.End.BlockIndex;
            for (var blockIndex = firstBlock; blockIndex <= lastBlock; blockIndex++)
            {
                if (Document.Blocks[blockIndex] is not Paragraph paragraph)
                    continue;

                var startOffset = blockIndex == range.Start.BlockIndex ? range.Start.Offset : 0;
                var endOffset = blockIndex == range.End.BlockIndex
                    ? range.End.Offset
                    : paragraph.PlainText.Length;
                startOffset = Math.Clamp(startOffset, 0, paragraph.PlainText.Length);
                endOffset = Math.Clamp(endOffset, 0, paragraph.PlainText.Length);
                if (endOffset <= startOffset)
                    continue;

                resolved.Add(new DocumentTextRange(
                    new DocumentTextPosition(blockIndex, startOffset),
                    new DocumentTextPosition(blockIndex, endOffset)));
            }
        }

        return resolved
            .Distinct()
            .OrderBy(range => range.Start.BlockIndex)
            .ThenBy(range => range.Start.Offset)
            .ThenBy(range => range.End.Offset)
            .ToArray();
    }

    internal IReadOnlyList<IDocumentCommand> CreateRunFormattingCommands(
        IReadOnlyList<DocumentTextRange> ranges,
        Func<RunFormatting, RunFormatting> transform,
        string commandLabel,
        bool skipUnchangedRanges)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        ArgumentNullException.ThrowIfNull(transform);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandLabel);

        return ResolveBodyTextRanges(ranges)
            .Where(range => !skipUnchangedRanges || RunRangeWouldChange(range, transform))
            .Select(range => (IDocumentCommand)new RunFormattingRangeCommand(
                range.Start.BlockIndex,
                range.Start.Offset,
                range.End.Offset,
                transform,
                _revisionDateXml,
                commandLabel))
            .ToArray();
    }

    private bool RunRangeWouldChange(
        DocumentTextRange range,
        Func<RunFormatting, RunFormatting> transform)
    {
        var paragraph = (Paragraph)Document.Blocks[range.Start.BlockIndex];
        var position = 0;
        foreach (var run in paragraph.Runs)
        {
            var runStart = position;
            var runEnd = runStart + run.Text.Length;
            position = runEnd;
            if (runEnd <= range.Start.Offset || runStart >= range.End.Offset || run.Text.Length == 0)
                continue;
            if (transform(run.Formatting) != run.Formatting)
                return true;
        }
        return false;
    }

    private bool RunRangesAllMatch(
        IReadOnlyList<DocumentTextRange> ranges,
        Func<RunFormatting, bool> predicate) =>
        ranges.All(range => RunRangeAllMatches(range, predicate));

    private bool RunRangeAllMatches(
        DocumentTextRange range,
        Func<RunFormatting, bool> predicate)
    {
        var paragraph = (Paragraph)Document.Blocks[range.Start.BlockIndex];
        var position = 0;
        var sawText = false;
        foreach (var run in paragraph.Runs)
        {
            var runStart = position;
            var runEnd = runStart + run.Text.Length;
            position = runEnd;
            if (runEnd <= range.Start.Offset || runStart >= range.End.Offset || run.Text.Length == 0)
                continue;
            sawText = true;
            if (!predicate(run.Formatting))
                return false;
        }
        return sawText;
    }

    private void ExecuteRunFormatting(
        IReadOnlyList<DocumentTextRange> ranges,
        Func<RunFormatting, RunFormatting> transform,
        string undoLabel)
    {
        var commands = CreateRunFormattingCommands(
            ranges,
            transform,
            undoLabel,
            skipUnchangedRanges: true);
        if (commands.Count > 0)
            ExecuteGroup(commands, undoLabel);
    }

    private static void ApplyRunFormattingToTextRange(
        Paragraph paragraph,
        int startOffset,
        int endOffset,
        Func<RunFormatting, RunFormatting> transform,
        TextDocument document,
        string? revisionAuthor,
        string? revisionDateXml)
    {
        var rebuilt = new List<Run>();
        var position = 0;
        foreach (var source in paragraph.Runs)
        {
            var length = source.Text.Length;
            var runStart = position;
            var runEnd = position + length;
            position = runEnd;
            if (length == 0)
            {
                rebuilt.Add(RevisionEditPlanner.CloneRunWithText(source, source.Text));
                continue;
            }

            var coverStart = Math.Max(runStart, startOffset);
            var coverEnd = Math.Min(runEnd, endOffset);
            if (coverStart >= coverEnd)
            {
                rebuilt.Add(RevisionEditPlanner.CloneRunWithText(source, source.Text));
                continue;
            }

            var localStart = coverStart - runStart;
            var localEnd = coverEnd - runStart;
            if (localStart > 0)
                rebuilt.Add(RevisionEditPlanner.CloneRunWithText(source, source.Text[..localStart]));

            var covered = RevisionEditPlanner.CloneRunWithText(source, source.Text[localStart..localEnd]);
            var formatting = transform(source.Formatting);
            covered.Formatting = formatting;
            if (document is { TrackRevisions: true, DoNotTrackFormatting: false }
                && formatting != source.Formatting
                && covered.FormatRevision is null)
            {
                covered.FormatRevision = new FormatRevision(
                    source.Formatting,
                    string.IsNullOrWhiteSpace(revisionAuthor) ? "FreeW User" : revisionAuthor.Trim(),
                    revisionDateXml);
            }
            rebuilt.Add(covered);

            if (localEnd < length)
                rebuilt.Add(RevisionEditPlanner.CloneRunWithText(source, source.Text[localEnd..]));
        }

        paragraph.Runs.Clear();
        paragraph.Runs.AddRange(rebuilt);
    }

    private IReadOnlyList<int> ResolveParagraphIndices(IReadOnlyList<int> blockIndices) =>
        blockIndices
            .Distinct()
            .Where(index => index >= 0
                && index < Document.Blocks.Count
                && Document.Blocks[index] is Paragraph)
            .ToArray();

    private void ExecuteGroup(IReadOnlyList<IDocumentCommand> commands, string undoLabel)
    {
        if (commands.Count == 1)
        {
            _commands.Execute(commands[0]);
            return;
        }

        _commands.BeginUndoGroup();
        try
        {
            foreach (var command in commands)
                _commands.Execute(command);
            _commands.CommitUndoGroup(undoLabel);
        }
        catch
        {
            _commands.AbortUndoGroup();
            throw;
        }
    }

    internal void ExecuteCommands(IReadOnlyList<IDocumentCommand> commands, string undoLabel) =>
        ExecuteGroup(commands, undoLabel);

    internal void NotifyChanged() => Changed?.Invoke();

    public string? RevisionDateXmlForEdit() => _revisionDateXml();

    private DocumentCommandBus CreateCommandBus(TextDocument document)
    {
        var commands = new DocumentCommandBus(new SessionCommandContext(document, _revisionAuthor));
        commands.Changed += OnCommandsChanged;
        return commands;
    }

    private void OnCommandsChanged() => Changed?.Invoke();

    private sealed class RunFormattingRangeCommand(
        int blockIndex,
        int startOffset,
        int endOffset,
        Func<RunFormatting, RunFormatting> transform,
        Func<string?> revisionDateXml,
        string commandLabel) : IDocumentCommand
    {
        private List<Run>? _previous;
        private List<Run>? _replacement;

        public string Label => commandLabel;
        public DocumentCommandMutationKind MutationKind => DocumentCommandMutationKind.BodyFormatting;

        public void Apply(IDocumentCommandContext context)
        {
            var paragraph = (Paragraph)context.Document.Blocks[blockIndex];
            if (_replacement is not null)
            {
                paragraph.Runs.Clear();
                paragraph.Runs.AddRange(_replacement);
                return;
            }

            _previous = [.. paragraph.Runs];
            ApplyRunFormattingToTextRange(
                paragraph,
                startOffset,
                endOffset,
                transform,
                context.Document,
                context.RevisionAuthor,
                revisionDateXml());
            _replacement = [.. paragraph.Runs];
        }

        public void Revert(IDocumentCommandContext context)
        {
            if (_previous is null)
                return;

            var paragraph = (Paragraph)context.Document.Blocks[blockIndex];
            paragraph.Runs.Clear();
            paragraph.Runs.AddRange(_previous);
        }
    }

    private sealed class SessionCommandContext(
        TextDocument document,
        Func<string?> revisionAuthor) : IDocumentCommandContext
    {
        public TextDocument Document => document;
        public string? RevisionAuthor => revisionAuthor();
    }
}

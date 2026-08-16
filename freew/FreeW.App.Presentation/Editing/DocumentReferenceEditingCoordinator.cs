using System.Globalization;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Editing;

public readonly record struct DocumentReferenceEditResult(
    bool Applied,
    int HostBlockIndex,
    int InsertedBlockIndex);

public readonly record struct DocumentReferenceRegionEditResult(
    bool Applied,
    int InsertIndex,
    int DeletedCount,
    int InsertedCount);

public readonly record struct DocumentGeneratedReferenceEditResult(
    DocumentReferenceRegionEditResult Region,
    DocumentTextPosition Caret)
{
    public bool Applied => Region.Applied;
}

public readonly record struct DocumentReferenceTextEditResult(
    bool Applied,
    int HostBlockIndex,
    int TextOffset,
    int NoteId);

public sealed record DocumentReferenceBlockPageResolution(
    Func<int, int?>? PageNumberAtBlock,
    int? PageCount = null);

public readonly record struct DocumentFieldCodeToggleResult(
    bool Applied,
    bool ShowCodes,
    int FieldCount);

public readonly record struct DocumentFieldUpdateResult(
    int UpdatedFieldCount,
    int RefreshedGeneratedRegionCount);

public readonly record struct DocumentComplexFieldTarget(
    ComplexField Field,
    string? ResultText = null);

public readonly record struct DocumentComplexFieldEditResult(
    bool Applied,
    int TargetedFieldCount,
    int UpdatedFieldCount);

/// <summary>
/// Outcome of <see cref="DocumentReferenceEditingCoordinator.TrySetBookmark"/>. Word enforces unique
/// bookmark names within a document, so a name already used by a different paragraph is rejected rather
/// than silently creating a second bookmark instance sharing that name (which would make the Bookmark
/// Manager's Delete-by-selection ambiguous — see <see cref="Bookmarks.RemoveBookmarkAt"/>).
/// </summary>
public enum BookmarkInsertOutcome
{
    /// <summary>The bookmark was applied to the target paragraph.</summary>
    Applied,

    /// <summary>The name was empty/whitespace, or the target block was not an editable paragraph.</summary>
    Invalid,

    /// <summary>A different paragraph already carries this exact name; nothing was changed.</summary>
    DuplicateName,
}

/// <summary>
/// Owns portable field transitions, recomputation, generated-reference replacement, and insertion.
/// Renderers retain native target extraction, focus, and projection of the resulting model position.
/// </summary>
public sealed class DocumentReferenceEditingCoordinator
{
    private const string InsertBibliographyUndoLabel = "Insert Bibliography";
    private const string UpdateBibliographyUndoLabel = "Update Bibliography";
    private const string InsertIndexUndoLabel = "Insert Index";
    private const string UpdateIndexUndoLabel = "Update Index";
    private const string InsertTableOfFiguresUndoLabel = "Insert Table of Figures";
    private const string UpdateTableOfFiguresUndoLabel = "Update Table of Figures";
    private const string InsertTableOfAuthoritiesUndoLabel = "Insert Table of Authorities";
    private const string UpdateTableOfAuthoritiesUndoLabel = "Update Table of Authorities";

    private readonly DocumentEditingSession _session;

    internal DocumentReferenceEditingCoordinator(DocumentEditingSession session) => _session = session;

    public Run BuildComplexFieldInsertionRun(
        string instruction,
        string? cachedResult,
        Func<Run, string> liveDisplayResolver,
        TextDocument? evaluationDocument = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instruction);
        return BuildComplexFieldInsertionRun(
            new ComplexField($" {instruction.Trim()} "),
            cachedResult,
            liveDisplayResolver,
            evaluationDocument);
    }

    public Run BuildComplexFieldInsertionRun(
        ComplexField field,
        string? cachedResult,
        Func<Run, string> liveDisplayResolver,
        TextDocument? evaluationDocument = null)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(liveDisplayResolver);

        var run = new Run(cachedResult ?? string.Empty) { ComplexField = field };
        if (cachedResult is not null)
            return run;

        run.Text = ComplexFieldDisplayPlanner.IsPageSectionField(field.Keyword)
            ? ComplexFieldDisplayPlanner.ResolvePageSectionField(field, string.Empty, 1, 1)
            : ComplexFieldEngine.CanRecompute(field)
                ? ComplexFieldEngine.Recompute(evaluationDocument ?? _session.Document, 0, run)
                : liveDisplayResolver(run);
        return run;
    }

    public DocumentFieldCodeToggleResult ToggleFieldCodes()
    {
        var fields = DocumentFieldStories.Enumerate(_session.Document)
            .SelectMany(item => item.Paragraph.Runs)
            .Where(run => run.ComplexField is not null)
            .ToArray();
        if (fields.Length == 0)
            return new DocumentFieldCodeToggleResult(false, false, 0);

        var showCodes = fields.Count(run => run.ComplexField!.ShowCode) * 2 <= fields.Length;
        foreach (var run in fields)
            run.ComplexField = run.ComplexField! with { ShowCode = showCodes };

        return new DocumentFieldCodeToggleResult(true, showCodes, fields.Length);
    }

    public DocumentComplexFieldEditResult ToggleComplexFieldCodes(
        IReadOnlyCollection<ComplexField> fields) =>
        MutateComplexFields(fields, field => field with { ShowCode = !field.ShowCode });

    public DocumentComplexFieldEditResult SetComplexFieldsLocked(
        IReadOnlyCollection<ComplexField> fields,
        bool isLocked) =>
        MutateComplexFields(fields, field => field.WithLock(isLocked));

    public DocumentComplexFieldEditResult UnlinkComplexFields(
        IReadOnlyCollection<DocumentComplexFieldTarget> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var selected = new Dictionary<ComplexField, string?>(ReferenceEqualityComparer.Instance);
        foreach (var field in fields)
            selected[field.Field] = field.ResultText;
        if (selected.Count == 0)
            return new DocumentComplexFieldEditResult(false, 0, 0);

        var targets = ComplexFieldRuns(selected.Keys);
        foreach (var target in targets)
        {
            var field = target.ComplexField!;
            if (selected[field] is { } resultText)
                target.Text = resultText;
            target.ComplexField = null;
        }

        return new DocumentComplexFieldEditResult(
            targets.Count > 0,
            targets.Count,
            targets.Count);
    }

    public DocumentComplexFieldEditResult UpdateComplexFields(
        IReadOnlyCollection<ComplexField> fields,
        Func<DocumentReferenceBlockPageResolution>? blockPageResolutionFactory = null,
        string? fileName = null,
        DateTime? evaluatedAt = null,
        TextDocument? evaluationDocument = null)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var selected = new HashSet<ComplexField>(fields, ReferenceEqualityComparer.Instance);
        if (selected.Count == 0)
            return new DocumentComplexFieldEditResult(false, 0, 0);

        var fieldParagraphs = DocumentFieldStories.Enumerate(_session.Document).ToArray();
        var targetedFieldCount = fieldParagraphs
            .SelectMany(item => item.Paragraph.Runs)
            .Count(run => run.ComplexField is { } field && selected.Contains(field));
        if (targetedFieldCount == 0)
            return new DocumentComplexFieldEditResult(false, 0, 0);

        var updatedFieldCount = UpdateFieldRuns(
            fieldParagraphs,
            run => run.ComplexField is { } field && selected.Contains(field),
            blockPageResolutionFactory,
            fileName,
            evaluatedAt,
            evaluationDocument);
        return new DocumentComplexFieldEditResult(
            true,
            targetedFieldCount,
            updatedFieldCount);
    }

    public DocumentFieldUpdateResult UpdateFields(
        Func<DocumentReferenceBlockPageResolution>? blockPageResolutionFactory = null,
        Func<ToaCitationPageResolver?>? authorityPageResolverFactory = null,
        string? fileName = null,
        DateTime? evaluatedAt = null,
        TextDocument? evaluationDocument = null,
        Func<Func<int, TableParagraphAddress?, string?>?>? figurePageTextResolverFactory = null,
        Func<ToaCitationPageAddressResolver?>? authorityPageAddressResolverFactory = null)
    {
        var fieldParagraphs = DocumentFieldStories.Enumerate(_session.Document).ToArray();
        var updatedFieldCount = UpdateFieldRuns(
            fieldParagraphs,
            include: null,
            blockPageResolutionFactory,
            fileName,
            evaluatedAt,
            evaluationDocument);

        var refreshedGeneratedRegionCount = RefreshGeneratedReferenceRegions(
            blockPageResolutionFactory,
            authorityPageResolverFactory,
            figurePageTextResolverFactory,
            authorityPageAddressResolverFactory);
        return new DocumentFieldUpdateResult(updatedFieldCount, refreshedGeneratedRegionCount);
    }

    public DocumentReferenceEditResult InsertTableOfContents(
        int insertionIndex,
        Func<int, string?>? pageTextResolver) =>
        InsertTableOfContents(insertionIndex, () => pageTextResolver);

    public DocumentReferenceEditResult InsertTableOfContents(
        int insertionIndex,
        Func<Func<int, string?>?> pageTextResolverFactory,
        Action? refreshLayout = null,
        int maxStabilizationPasses = 8) =>
        ApplyStabilizedTableOfContentsRegion(
            insertionIndex,
            replaceExisting: false,
            "Insert Table of Contents",
            pageTextResolverFactory,
            refreshLayout,
            maxStabilizationPasses);

    public DocumentReferenceEditResult RefreshTableOfContents(
        Func<int, string?>? pageTextResolver) =>
        RefreshTableOfContents(() => pageTextResolver);

    public DocumentReferenceEditResult RefreshTableOfContents(
        Func<Func<int, string?>?> pageTextResolverFactory,
        Action? refreshLayout = null,
        int maxStabilizationPasses = 8) =>
        ApplyStabilizedTableOfContentsRegion(
            insertionIndex: 0,
            replaceExisting: true,
            "Update Table of Contents",
            pageTextResolverFactory,
            refreshLayout,
            maxStabilizationPasses);

    private DocumentReferenceEditResult ApplyStabilizedTableOfContentsRegion(
        int insertionIndex,
        bool replaceExisting,
        string undoLabel,
        Func<Func<int, string?>?> pageTextResolverFactory,
        Action? refreshLayout,
        int maxStabilizationPasses)
    {
        ArgumentNullException.ThrowIfNull(pageTextResolverFactory);
        ArgumentException.ThrowIfNullOrWhiteSpace(undoLabel);
        if (maxStabilizationPasses < 1)
            throw new ArgumentOutOfRangeException(nameof(maxStabilizationPasses));

        TableOfContents.EnsureStyles(_session.Document);
        var existing = _session.Document.Blocks
            .Select((block, index) => (block, index))
            .Where(item => TableOfContents.IsTocParagraph(item.block))
            .Select(item => item.index)
            .ToArray();
        var insertAt = replaceExisting && existing.Length > 0
            ? existing[0]
            : Math.Clamp(insertionIndex, 0, _session.Document.Blocks.Count);
        var paragraphs = TableOfContents.Build(_session.Document, pageTextResolverFactory());
        var commands = new List<IDocumentCommand>(existing.Length + paragraphs.Count);
        if (replaceExisting)
        {
            for (var index = existing.Length - 1; index >= 0; index--)
                commands.Add(new DeleteParagraphCommand(existing[index]));
        }
        commands.AddRange(paragraphs.Select((paragraph, offset) =>
            (IDocumentCommand)new InsertParagraphCommand(insertAt + offset, paragraph)));
        if (commands.Count == 0)
            return new DocumentReferenceEditResult(false, -1, insertAt);

        _session.Commands.BeginUndoGroup();
        try
        {
            ExecuteCommands(commands);
            var regionCount = paragraphs.Count;
            var isStable = false;
            for (var pass = 0; pass < maxStabilizationPasses; pass++)
            {
                refreshLayout?.Invoke();
                var stabilized = TableOfContents.Build(_session.Document, pageTextResolverFactory());
                if (TableOfContents.MatchesGeneratedRegionAt(_session.Document, insertAt, stabilized))
                {
                    isStable = true;
                    break;
                }

                ReplaceTableOfContentsRegion(insertAt, regionCount, stabilized);
                regionCount = stabilized.Count;
            }

            if (!isStable)
            {
                refreshLayout?.Invoke();
                var finalCheck = TableOfContents.Build(_session.Document, pageTextResolverFactory());
                if (!TableOfContents.MatchesGeneratedRegionAt(_session.Document, insertAt, finalCheck))
                    throw new InvalidOperationException("Table of Contents pagination did not stabilize.");
            }

            _session.Commands.CommitUndoGroup(undoLabel);
            return new DocumentReferenceEditResult(true, -1, insertAt);
        }
        catch
        {
            if (_session.Commands.IsUndoGroupOpen)
                _session.Commands.RollbackUndoGroup();
            throw;
        }
    }

    private void ReplaceTableOfContentsRegion(
        int insertAt,
        int currentCount,
        IReadOnlyList<Paragraph> paragraphs)
    {
        for (var offset = currentCount - 1; offset >= 0; offset--)
            _session.Commands.Execute(new DeleteParagraphCommand(insertAt + offset));
        for (var offset = 0; offset < paragraphs.Count; offset++)
            _session.Commands.Execute(new InsertParagraphCommand(insertAt + offset, paragraphs[offset]));
    }

    public DocumentReferenceEditResult InsertCaption(
        int caretBlockIndex,
        string labelText,
        string text)
    {
        var normalizedLabel = Captions.NormalizeLabelText(labelText);
        Captions.EnsureStyles(_session.Document);
        var number = Captions.NextCaptionNumber(_session.Document, normalizedLabel);
        var caption = Captions.BuildCaption(normalizedLabel, number, text);
        var insertAt = Math.Clamp(caretBlockIndex + 1, 0, _session.Document.Blocks.Count);
        _session.Commands.Execute(new InsertParagraphCommand(insertAt, caption));
        return new DocumentReferenceEditResult(true, -1, insertAt);
    }

    public DocumentReferenceEditResult InsertCrossReference(
        int sourceBlockIndex,
        int preferredHostBlockIndex,
        CrossRefType type,
        CrossRefTarget target,
        CrossRefInsertAs insertAs,
        bool hyperlink)
    {
        var plan = CrossReferences.PlanInsertion(
            _session.Document,
            type,
            target,
            insertAs,
            hyperlink,
            sourceBlockIndex);
        var hostBlockIndex = ResolveHostParagraph(preferredHostBlockIndex);
        var commands = new List<IDocumentCommand>();
        if (hostBlockIndex < 0)
        {
            hostBlockIndex = _session.Document.Blocks.Count;
            commands.Add(new InsertParagraphCommand(hostBlockIndex, new Paragraph()));
        }
        commands.Add(new InsertCrossReferenceCommand(
            hostBlockIndex,
            plan.FieldRun,
            plan.Target.BlockIndex,
            plan.BookmarkNameToAdd,
            plan.TargetRunIndex,
            plan.TargetNoteId,
            plan.TargetIsFootnote,
            plan.TargetTextStartOffset,
            plan.TargetTextEndOffset));
        ExecuteGroup(commands, "Insert Cross-reference");
        return new DocumentReferenceEditResult(true, hostBlockIndex, -1);
    }

    public DocumentGeneratedReferenceEditResult InsertBibliography(DocumentTextPosition caret)
    {
        var plan = BibliographyRegionPlanner.BuildInsertPlan(
            _session.Document,
            ResolveGeneratedReferenceInsertionIndex(caret.BlockIndex),
            _session.Document.BibliographyStyle);
        return ApplyGeneratedReferenceRegion(
            caret,
            plan.DeleteIndicesDescending,
            plan.InsertIndex,
            plan.Paragraphs,
            InsertBibliographyUndoLabel);
    }

    public DocumentGeneratedReferenceEditResult RefreshBibliography(DocumentTextPosition caret)
    {
        var plan = BibliographyRegionPlanner.BuildRefreshPlan(
            _session.Document,
            _session.Document.BibliographyStyle);
        return ApplyGeneratedReferenceRegion(
            caret,
            plan.DeleteIndicesDescending,
            plan.InsertIndex,
            plan.Paragraphs,
            UpdateBibliographyUndoLabel);
    }

    public DocumentGeneratedReferenceEditResult InsertIndex(
        DocumentTextPosition caret,
        string? identifier,
        Func<int, IndexPageReferenceAddress?>? pageReferenceOf)
    {
        DocumentIndex.EnsureStyles(_session.Document, identifier);
        var paragraphs = DocumentIndex.Build(
            _session.Document,
            identifier: identifier,
            pageReferenceOf: pageReferenceOf);
        return ApplyGeneratedReferenceRegion(
            caret,
            Array.Empty<int>(),
            ResolveGeneratedReferenceInsertionIndex(caret.BlockIndex),
            paragraphs,
            InsertIndexUndoLabel);
    }

    public DocumentGeneratedReferenceEditResult RefreshIndex(
        DocumentTextPosition caret,
        string? identifier,
        Func<int, IndexPageReferenceAddress?>? pageReferenceOf)
    {
        DocumentIndex.EnsureStyles(_session.Document, identifier);
        var deleteIndices = GeneratedRegionIndices(
            block => DocumentIndex.IsIndexParagraph(block, identifier));
        var insertIndex = deleteIndices.Count > 0
            ? deleteIndices[0]
            : _session.Document.Blocks.Count;
        var paragraphs = DocumentIndex.Build(
            _session.Document,
            identifier: identifier,
            pageReferenceOf: pageReferenceOf);
        return ApplyGeneratedReferenceRegion(
            caret,
            deleteIndices,
            insertIndex,
            paragraphs,
            UpdateIndexUndoLabel);
    }

    public DocumentGeneratedReferenceEditResult InsertTableOfFigures(
        DocumentTextPosition caret,
        string labelText,
        Func<int, TableParagraphAddress?, string?>? pageTextResolver)
    {
        var normalizedLabel = Captions.NormalizeLabelText(labelText);
        TableOfFigures.EnsureStyles(_session.Document);
        var paragraphs = TableOfFigures.BuildWithTableAddresses(
            _session.Document,
            normalizedLabel,
            pageTextResolver);
        return ApplyGeneratedReferenceRegion(
            caret,
            Array.Empty<int>(),
            ResolveGeneratedReferenceInsertionIndex(caret.BlockIndex),
            paragraphs,
            InsertTableOfFiguresUndoLabel);
    }

    public DocumentGeneratedReferenceEditResult RefreshTableOfFigures(
        DocumentTextPosition caret,
        string labelText,
        Func<int, TableParagraphAddress?, string?>? pageTextResolver)
    {
        var normalizedLabel = Captions.NormalizeLabelText(labelText);
        TableOfFigures.EnsureStyles(_session.Document);
        var deleteIndices = GeneratedRegionIndices(TableOfFigures.IsTableOfFiguresParagraph);
        var insertIndex = deleteIndices.Count > 0
            ? deleteIndices[0]
            : _session.Document.Blocks.Count;
        var paragraphs = TableOfFigures.BuildWithTableAddresses(
            _session.Document,
            normalizedLabel,
            pageTextResolver);
        return ApplyGeneratedReferenceRegion(
            caret,
            deleteIndices,
            insertIndex,
            paragraphs,
            UpdateTableOfFiguresUndoLabel);
    }

    public DocumentGeneratedReferenceEditResult InsertTableOfAuthorities(
        DocumentTextPosition caret,
        ToaOptions options,
        Func<ToaCitationPageAddressResolver?> pageResolverFactory,
        Action? refreshLayout = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(pageResolverFactory);
        var plan = TableOfAuthoritiesRegionPlanner.BuildInsertPlanWithTableAddresses(
            _session.Document,
            ResolveGeneratedReferenceInsertionIndex(caret.BlockIndex),
            options,
            pageResolverFactory());
        return ApplyStabilizedTableOfAuthoritiesRegion(
            caret,
            plan,
            pageResolverFactory,
            InsertTableOfAuthoritiesUndoLabel,
            refreshLayout);
    }

    public DocumentGeneratedReferenceEditResult RefreshTableOfAuthorities(
        DocumentTextPosition caret,
        ToaOptions? options,
        Func<ToaCitationPageAddressResolver?> pageResolverFactory,
        Action? refreshLayout = null)
    {
        ArgumentNullException.ThrowIfNull(pageResolverFactory);
        var plan = TableOfAuthoritiesRegionPlanner.BuildRefreshPlanWithTableAddresses(
            _session.Document,
            options,
            pageResolverFactory());
        return ApplyStabilizedTableOfAuthoritiesRegion(
            caret,
            plan,
            pageResolverFactory,
            UpdateTableOfAuthoritiesUndoLabel,
            refreshLayout);
    }

    public DocumentReferenceRegionEditResult InsertGeneratedRegion(
        int insertIndex,
        IReadOnlyList<Paragraph> paragraphs,
        string undoLabel) =>
        ApplyGeneratedRegion(Array.Empty<int>(), insertIndex, paragraphs, undoLabel);

    public bool SetBookmark(int blockIndex, string? name) =>
        TrySetBookmark(blockIndex, name) == BookmarkInsertOutcome.Applied;

    /// <summary>
    /// Names the paragraph at <paramref name="blockIndex"/> as a bookmark target, matching Word's
    /// unique-name rule: when a <em>different</em> paragraph already carries this exact name, the insert
    /// is rejected (<see cref="BookmarkInsertOutcome.DuplicateName"/>) rather than creating a second
    /// instance sharing the name — re-applying the same name to its own current paragraph is not a
    /// duplicate and still succeeds.
    /// </summary>
    public BookmarkInsertOutcome TrySetBookmark(int blockIndex, string? name)
    {
        var normalized = name?.Trim();
        if (string.IsNullOrEmpty(normalized)
            || blockIndex < 0
            || blockIndex >= _session.Document.Blocks.Count
            || _session.Document.Blocks[blockIndex] is not Paragraph)
        {
            return BookmarkInsertOutcome.Invalid;
        }

        var isDuplicate = Bookmarks.List(_session.Document).Any(location =>
            location.BlockIndex != blockIndex
            && string.Equals(location.Name, normalized, StringComparison.Ordinal));
        if (isDuplicate)
            return BookmarkInsertOutcome.DuplicateName;

        _session.Commands.Execute(new SetParagraphBookmarkNameCommand(blockIndex, normalized));
        return BookmarkInsertOutcome.Applied;
    }

    public DocumentReferenceTextEditResult InsertNote(
        int blockIndex,
        int textOffset,
        string? text,
        bool footnote)
    {
        var commands = new List<IDocumentCommand>();
        Paragraph paragraph;
        if (blockIndex < 0
            || blockIndex >= _session.Document.Blocks.Count
            || _session.Document.Blocks[blockIndex] is not Paragraph resolved)
        {
            blockIndex = _session.Document.Blocks.Count;
            paragraph = new Paragraph();
            commands.Add(new InsertParagraphCommand(blockIndex, paragraph));
        }
        else
        {
            paragraph = resolved;
        }

        var offset = Math.Clamp(textOffset, 0, paragraph.PlainText.Length);
        var id = footnote
            ? _session.Document.NextFootnoteId()
            : _session.Document.NextEndnoteId();
        commands.Add(new InsertNoteCommand(
            id,
            footnote,
            text ?? string.Empty,
            blockIndex,
            offset));
        ExecuteGroup(commands, footnote ? "Insert Footnote" : "Insert Endnote");
        var markerLength = id.ToString(System.Globalization.CultureInfo.InvariantCulture).Length;
        return new DocumentReferenceTextEditResult(true, blockIndex, offset + markerLength, id);
    }

    public void DeleteNote(int id, bool footnote) =>
        _session.Commands.Execute(new DeleteNoteCommand(id, footnote));

    public void ReplaceNoteContent(
        int id,
        bool footnote,
        IReadOnlyList<Paragraph> paragraphs)
    {
        ArgumentNullException.ThrowIfNull(paragraphs);
        _session.Commands.Execute(new ReplaceNoteContentCommand(id, footnote, paragraphs));
    }

    public void ApplyNoteNumberingOptions(FootnoteEndnoteOptionsDialogResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        _session.Commands.Execute(new SetNoteNumberingOptionsCommand(
            result.FootnoteFormat,
            result.FootnoteStartAt,
            result.FootnoteRestart,
            result.EndnoteFormat,
            result.EndnoteStartAt,
            result.EndnoteRestart));
    }

    public bool ApplyCitationStyle(CitationStyle style)
    {
        if (_session.Document.BibliographyStyle == style)
            return false;
        _session.Commands.Execute(new ApplyCitationStyleCommand(style));
        return true;
    }

    public void ReplaceSources(IReadOnlyList<Source> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        _session.Commands.Execute(new ReplaceSourcesCommand(sources));
    }

    public bool ReplaceContentControlRun(int blockIndex, int runIndex, Run updated)
    {
        ArgumentNullException.ThrowIfNull(updated);
        if (blockIndex < 0
            || blockIndex >= _session.Document.Blocks.Count
            || _session.Document.Blocks[blockIndex] is not Paragraph paragraph
            || runIndex < 0
            || runIndex >= paragraph.Runs.Count)
        {
            return false;
        }

        _session.Commands.Execute(new ReplaceContentControlRunCommand(blockIndex, runIndex, updated));
        return true;
    }

    public DocumentReferenceTextEditResult InsertIndexEntry(
        int blockIndex,
        int textOffset,
        IndexMark mark)
    {
        ArgumentNullException.ThrowIfNull(mark);
        var markRun = DocumentIndex.MarkRun(mark);
        if (DocumentIndex.MarkedEntry(markRun) is not { MainEntry.Length: > 0 } normalized
            || !TryGetParagraph(blockIndex, out var paragraph)
            || paragraph.Runs.Any(run =>
                DocumentIndex.MarksEquivalent(DocumentIndex.MarkedEntry(run), normalized)))
        {
            return new DocumentReferenceTextEditResult(false, blockIndex, textOffset, 0);
        }

        var offset = Math.Clamp(textOffset, 0, paragraph.PlainText.Length);
        _session.Commands.Execute(new ReplaceParagraphRunsCommand(blockIndex, target =>
            RevisionEditPlanner.InsertRunAtOffset(target, offset, markRun)));
        return new DocumentReferenceTextEditResult(true, blockIndex, offset, 0);
    }

    public int MarkAllIndexEntries(string sourceText, IndexMark mark)
    {
        ArgumentNullException.ThrowIfNull(mark);
        var markRun = DocumentIndex.MarkRun(mark);
        if (DocumentIndex.MarkedEntry(markRun) is not { MainEntry.Length: > 0 } normalized)
            return 0;
        var targets = DocumentIndex.MarkAllTargets(_session.Document, sourceText, normalized);
        if (targets.Count == 0)
            return 0;

        ExecuteGroup(
            targets.Select(target => target.TableParagraph is { } tableParagraph
                ? (IDocumentCommand)new ReplaceTableCellParagraphRunsCommand(
                    target.BlockIndex,
                    tableParagraph,
                    paragraph => RevisionEditPlanner.InsertRunAtOffset(
                        paragraph,
                        target.TextOffset,
                        DocumentIndex.MarkRun(normalized)))
                : new ReplaceParagraphRunsCommand(
                    target.BlockIndex,
                    paragraph => RevisionEditPlanner.InsertRunAtOffset(
                        paragraph,
                        target.TextOffset,
                        DocumentIndex.MarkRun(normalized)))).ToArray(),
            "Mark All Index Entries");
        return targets.Count;
    }

    public DocumentReferenceTextEditResult InsertAuthorityCitation(
        int blockIndex,
        int textOffset,
        Citation citation)
    {
        ArgumentNullException.ThrowIfNull(citation);
        if (citation.LongCitation.Length == 0 || !TryGetParagraph(blockIndex, out var paragraph))
            return new DocumentReferenceTextEditResult(false, blockIndex, textOffset, 0);

        var offset = Math.Clamp(textOffset, 0, paragraph.PlainText.Length);
        _session.Commands.Execute(new ReplaceParagraphRunsCommand(blockIndex, target =>
            RevisionEditPlanner.InsertRunAtOffset(target, offset, Run.CitationMark(citation))));
        return new DocumentReferenceTextEditResult(true, blockIndex, offset, 0);
    }

    public DocumentReferenceRegionEditResult RefreshGeneratedRegion(
        Func<Block, bool> isGeneratedBlock,
        int fallbackInsertIndex,
        IReadOnlyList<Paragraph> paragraphs,
        string undoLabel)
    {
        ArgumentNullException.ThrowIfNull(isGeneratedBlock);
        var deleteIndices = _session.Document.Blocks
            .Select((block, index) => (block, index))
            .Where(item => isGeneratedBlock(item.block))
            .Select(item => item.index)
            .ToArray();
        var insertIndex = deleteIndices.Length > 0
            ? deleteIndices[0]
            : fallbackInsertIndex;
        return ApplyGeneratedRegion(deleteIndices, insertIndex, paragraphs, undoLabel);
    }

    public DocumentReferenceRegionEditResult ApplyGeneratedRegion(
        IReadOnlyList<int> deleteIndicesDescending,
        int insertIndex,
        IReadOnlyList<Paragraph> paragraphs,
        string undoLabel)
    {
        ArgumentNullException.ThrowIfNull(deleteIndicesDescending);
        ArgumentNullException.ThrowIfNull(paragraphs);
        ArgumentException.ThrowIfNullOrWhiteSpace(undoLabel);
        var edit = BuildGeneratedRegionEdit(deleteIndicesDescending, insertIndex, paragraphs);
        if (edit.Commands.Count == 0)
            return edit.Result;

        ExecuteGroup(edit.Commands, undoLabel);
        return edit.Result;
    }

    public DocumentReferenceRegionEditResult ApplyStabilizedTableOfAuthoritiesRegion(
        TableOfAuthoritiesRegionPlan initialPlan,
        Func<ToaCitationPageAddressResolver?> pageResolverFactory,
        string undoLabel,
        Action? refreshLayout = null,
        int maxStabilizationPasses = 8)
    {
        ArgumentNullException.ThrowIfNull(initialPlan);
        ArgumentNullException.ThrowIfNull(pageResolverFactory);
        ArgumentException.ThrowIfNullOrWhiteSpace(undoLabel);
        if (maxStabilizationPasses < 1)
            throw new ArgumentOutOfRangeException(nameof(maxStabilizationPasses));

        var initialEdit = BuildGeneratedRegionEdit(
            initialPlan.DeleteIndicesDescending,
            initialPlan.InsertIndex,
            initialPlan.Paragraphs);
        if (initialEdit.Commands.Count == 0)
            return initialEdit.Result;

        _session.Commands.BeginUndoGroup();
        try
        {
            ExecuteCommands(initialEdit.Commands);
            var finalInsertedCount = initialEdit.Result.InsertedCount;
            var isStable = false;
            for (var pass = 0; pass < maxStabilizationPasses; pass++)
            {
                refreshLayout?.Invoke();
                var stabilized = TableOfAuthoritiesRegionPlanner.BuildRefreshPlanWithTableAddresses(
                    _session.Document,
                    pageResolver: pageResolverFactory());
                if (TableOfAuthoritiesRegionPlanner.MatchesGeneratedRegion(
                        _session.Document,
                        stabilized.Paragraphs))
                {
                    isStable = true;
                    break;
                }

                var stabilizedEdit = BuildGeneratedRegionEdit(
                    stabilized.DeleteIndicesDescending,
                    stabilized.InsertIndex,
                    stabilized.Paragraphs);
                ExecuteCommands(stabilizedEdit.Commands);
                finalInsertedCount = stabilizedEdit.Result.InsertedCount;
            }

            if (!isStable)
            {
                refreshLayout?.Invoke();
                var finalCheck = TableOfAuthoritiesRegionPlanner.BuildRefreshPlanWithTableAddresses(
                    _session.Document,
                    pageResolver: pageResolverFactory());
                if (!TableOfAuthoritiesRegionPlanner.MatchesGeneratedRegion(
                        _session.Document,
                        finalCheck.Paragraphs))
                {
                    throw new InvalidOperationException("Table of Authorities pagination did not stabilize.");
                }
            }

            _session.Commands.CommitUndoGroup(undoLabel);
            return initialEdit.Result with { InsertedCount = finalInsertedCount };
        }
        catch
        {
            if (_session.Commands.IsUndoGroupOpen)
                _session.Commands.RollbackUndoGroup();
            throw;
        }
    }

    private DocumentGeneratedReferenceEditResult ApplyGeneratedReferenceRegion(
        DocumentTextPosition caret,
        IReadOnlyList<int> deleteIndices,
        int insertIndex,
        IReadOnlyList<Paragraph> paragraphs,
        string undoLabel)
    {
        var originalBlockCount = _session.Document.Blocks.Count;
        var region = ApplyGeneratedRegion(deleteIndices, insertIndex, paragraphs, undoLabel);
        return CompleteGeneratedReferenceEdit(caret, originalBlockCount, deleteIndices, region);
    }

    private DocumentGeneratedReferenceEditResult ApplyStabilizedTableOfAuthoritiesRegion(
        DocumentTextPosition caret,
        TableOfAuthoritiesRegionPlan plan,
        Func<ToaCitationPageAddressResolver?> pageResolverFactory,
        string undoLabel,
        Action? refreshLayout)
    {
        var originalBlockCount = _session.Document.Blocks.Count;
        var region = ApplyStabilizedTableOfAuthoritiesRegion(
            plan,
            pageResolverFactory,
            undoLabel,
            refreshLayout);
        return CompleteGeneratedReferenceEdit(
            caret,
            originalBlockCount,
            plan.DeleteIndicesDescending,
            region);
    }

    private DocumentGeneratedReferenceEditResult CompleteGeneratedReferenceEdit(
        DocumentTextPosition caret,
        int originalBlockCount,
        IReadOnlyList<int> deleteIndices,
        DocumentReferenceRegionEditResult region)
    {
        if (!region.Applied)
            return new DocumentGeneratedReferenceEditResult(region, caret);

        var deletes = deleteIndices
            .Where(index => index >= 0 && index < originalBlockCount)
            .Distinct()
            .Order()
            .ToArray();
        var finalBlockCount = originalBlockCount - deletes.Length + region.InsertedCount;
        if (finalBlockCount == 0)
        {
            return new DocumentGeneratedReferenceEditResult(
                region,
                new DocumentTextPosition(-1, 0));
        }

        if (originalBlockCount == 0 || caret.BlockIndex < 0)
        {
            return new DocumentGeneratedReferenceEditResult(
                region,
                new DocumentTextPosition(Math.Clamp(region.InsertIndex, 0, finalBlockCount - 1), 0));
        }

        var originalCaretBlock = Math.Clamp(caret.BlockIndex, 0, originalBlockCount - 1);
        if (deletes.Contains(originalCaretBlock))
        {
            return new DocumentGeneratedReferenceEditResult(
                region,
                new DocumentTextPosition(Math.Clamp(region.InsertIndex, 0, finalBlockCount - 1), 0));
        }

        var targetBlock = originalCaretBlock - deletes.Count(index => index < originalCaretBlock);
        if (region.InsertedCount > 0 && region.InsertIndex <= targetBlock)
            targetBlock += region.InsertedCount;
        targetBlock = Math.Clamp(targetBlock, 0, finalBlockCount - 1);
        return new DocumentGeneratedReferenceEditResult(
            region,
            new DocumentTextPosition(targetBlock, Math.Max(0, caret.Offset)));
    }

    private IReadOnlyList<int> GeneratedRegionIndices(Func<Block, bool> isGeneratedBlock)
    {
        ArgumentNullException.ThrowIfNull(isGeneratedBlock);
        return _session.Document.Blocks
            .Select((block, index) => (block, index))
            .Where(item => isGeneratedBlock(item.block))
            .Select(item => item.index)
            .ToArray();
    }

    private int ResolveGeneratedReferenceInsertionIndex(int caretBlockIndex) =>
        caretBlockIndex < 0 || caretBlockIndex > _session.Document.Blocks.Count
            ? _session.Document.Blocks.Count
            : caretBlockIndex;

    private GeneratedRegionEdit BuildGeneratedRegionEdit(
        IReadOnlyList<int> deleteIndicesDescending,
        int insertIndex,
        IReadOnlyList<Paragraph> paragraphs)
    {
        var deletes = deleteIndicesDescending
            .Where(index => index >= 0 && index < _session.Document.Blocks.Count)
            .Distinct()
            .OrderDescending()
            .ToArray();
        var insertAt = Math.Clamp(
            insertIndex,
            0,
            Math.Max(0, _session.Document.Blocks.Count - deletes.Length));
        var commands = new List<IDocumentCommand>(deletes.Length + paragraphs.Count);
        commands.AddRange(deletes.Select(index => (IDocumentCommand)new DeleteParagraphCommand(index)));
        commands.AddRange(paragraphs.Select((paragraph, offset) =>
            (IDocumentCommand)new InsertParagraphCommand(insertAt + offset, paragraph)));
        return new GeneratedRegionEdit(
            new DocumentReferenceRegionEditResult(
                commands.Count > 0,
                insertAt,
                deletes.Length,
                paragraphs.Count),
            commands);
    }

    private void ExecuteCommands(IReadOnlyList<IDocumentCommand> commands)
    {
        foreach (var command in commands)
            _session.Commands.Execute(command);
    }

    private int ResolveHostParagraph(int preferredHostBlockIndex)
    {
        if (preferredHostBlockIndex >= 0
            && preferredHostBlockIndex < _session.Document.Blocks.Count
            && _session.Document.Blocks[preferredHostBlockIndex] is Paragraph)
        {
            return preferredHostBlockIndex;
        }

        for (var index = _session.Document.Blocks.Count - 1; index >= 0; index--)
        {
            if (_session.Document.Blocks[index] is Paragraph)
                return index;
        }
        return -1;
    }

    private bool TryGetParagraph(int blockIndex, out Paragraph paragraph)
    {
        paragraph = null!;
        if (blockIndex < 0
            || blockIndex >= _session.Document.Blocks.Count
            || _session.Document.Blocks[blockIndex] is not Paragraph resolved)
        {
            return false;
        }

        paragraph = resolved;
        return true;
    }

    private DocumentComplexFieldEditResult MutateComplexFields(
        IReadOnlyCollection<ComplexField> fields,
        Func<ComplexField, ComplexField> mutate)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(mutate);
        var targets = ComplexFieldRuns(fields);
        foreach (var target in targets)
            target.ComplexField = mutate(target.ComplexField!);

        return new DocumentComplexFieldEditResult(
            targets.Count > 0,
            targets.Count,
            targets.Count);
    }

    private List<Run> ComplexFieldRuns(IEnumerable<ComplexField> fields)
    {
        var selected = new HashSet<ComplexField>(fields, ReferenceEqualityComparer.Instance);
        return selected.Count == 0
            ? []
            : DocumentFieldStories.Enumerate(_session.Document)
                .SelectMany(item => item.Paragraph.Runs)
                .Where(run => run.ComplexField is { } field && selected.Contains(field))
                .ToList();
    }

    private int UpdateFieldRuns(
        IReadOnlyList<DocumentFieldStoryParagraph> fieldParagraphs,
        Func<Run, bool>? include,
        Func<DocumentReferenceBlockPageResolution>? blockPageResolutionFactory,
        string? fileName,
        DateTime? evaluatedAt,
        TextDocument? evaluationDocument)
    {
        var document = _session.Document;
        var fieldDocument = evaluationDocument ?? document;
        var fieldPages = RequiresBlockPageResolution(fieldParagraphs, include)
            ? ResolveBlockPages(blockPageResolutionFactory)
            : null;
        var fieldPageText = BuildPageTextResolver(fieldPages);
        var now = evaluatedAt ?? DateTime.Now;
        var updatedFieldCount = 0;

        foreach (var storyParagraph in fieldParagraphs)
        {
            var blockIndex = storyParagraph.BodyBlockIndex;
            var paragraph = storyParagraph.Paragraph;
            for (var runIndex = 0; runIndex < paragraph.Runs.Count; runIndex++)
            {
                var run = paragraph.Runs[runIndex];
                if (include is not null && !include(run))
                    continue;

                string resolved;
                var allowEmptyResult = false;
                if (run.CrossReference is { } crossReference)
                {
                    resolved = CrossReferences.ResolveField(
                        document,
                        crossReference,
                        run.Text,
                        blockIndex,
                        fieldPages?.PageNumberAtBlock,
                        fieldPageText,
                        sourceRunIndex: runIndex);
                }
                else if (run.ComplexField is { } complexField)
                {
                    if (complexField.IsLocked)
                        continue;

                    allowEmptyResult = DocumentFieldStories.CanRecomputeComplexField(
                        storyParagraph.StoryKind,
                        complexField);
                    resolved = allowEmptyResult
                        ? ComplexFieldEngine.Recompute(
                            fieldDocument,
                            blockIndex,
                            run,
                            fieldPages?.PageNumberAtBlock,
                            fieldPageText)
                        : ComplexFieldDisplayPlanner.ApplyTemporalPicture(
                            complexField,
                            now,
                            (run.Formatting ?? document.DefaultRun).LanguageTag,
                            CultureInfo.CurrentCulture,
                            ResolveLiveFieldResult(
                                fieldDocument,
                                ComplexFieldDisplayPlanner.ResolveLiveKind(complexField.Keyword),
                                run.Text,
                                blockIndex,
                                fieldPages,
                                fieldPageText,
                                fileName,
                                now));
                }
                else if (run.FieldKind != RunFieldKind.None)
                {
                    resolved = ResolveLiveFieldResult(
                        fieldDocument,
                        run.FieldKind,
                        run.Text,
                        blockIndex,
                        fieldPages,
                        fieldPageText,
                        fileName,
                        now);
                }
                else
                {
                    continue;
                }

                if ((!allowEmptyResult && resolved.Length == 0)
                    || string.Equals(resolved, run.Text, StringComparison.Ordinal))
                {
                    continue;
                }

                run.Text = resolved;
                updatedFieldCount++;
            }
        }

        return updatedFieldCount;
    }

    private int RefreshGeneratedReferenceRegions(
        Func<DocumentReferenceBlockPageResolution>? blockPageResolutionFactory,
        Func<ToaCitationPageResolver?>? authorityPageResolverFactory,
        Func<Func<int, TableParagraphAddress?, string?>?>? figurePageTextResolverFactory,
        Func<ToaCitationPageAddressResolver?>? authorityPageAddressResolverFactory)
    {
        var document = _session.Document;
        var refreshedCount = 0;

        if (document.Blocks.Any(TableOfContents.IsTocParagraph))
        {
            var pages = ResolveBlockPages(blockPageResolutionFactory);
            if (RefreshTableOfContents(BuildPageTextResolver(pages)).Applied)
                refreshedCount++;
        }

        if (document.Blocks.Any(Citations.IsBibliographyParagraph))
        {
            var plan = BibliographyRegionPlanner.BuildRefreshPlan(
                document,
                document.BibliographyStyle);
            if (ApplyGeneratedRegion(
                    plan.DeleteIndicesDescending,
                    plan.InsertIndex,
                    plan.Paragraphs,
                    "Update Bibliography").Applied)
            {
                refreshedCount++;
            }
        }

        if (document.Blocks.Any(TableOfFigures.IsTableOfFiguresParagraph))
        {
            var labelText = TableOfFigures.ExistingLabelText(document) ?? Captions.FigureLabelText;
            TableOfFigures.EnsureStyles(document);
            var paragraphs = figurePageTextResolverFactory is null
                ? TableOfFigures.Build(
                    document,
                    labelText,
                    BuildPageTextResolver(ResolveBlockPages(blockPageResolutionFactory)))
                : TableOfFigures.BuildWithTableAddresses(
                    document,
                    labelText,
                    figurePageTextResolverFactory());
            if (RefreshGeneratedRegion(
                    TableOfFigures.IsTableOfFiguresParagraph,
                    document.Blocks.Count,
                    paragraphs,
                    "Update Table of Figures").Applied)
            {
                refreshedCount++;
            }
        }

        if (TableOfAuthoritiesRegionPlanner.ContainsRegion(document))
        {
            var plan = authorityPageAddressResolverFactory is null
                ? TableOfAuthoritiesRegionPlanner.BuildRefreshPlan(
                    document,
                    pageResolver: authorityPageResolverFactory?.Invoke())
                : TableOfAuthoritiesRegionPlanner.BuildRefreshPlanWithTableAddresses(
                    document,
                    pageResolver: authorityPageAddressResolverFactory());
            if (ApplyGeneratedRegion(
                    plan.DeleteIndicesDescending,
                    plan.InsertIndex,
                    plan.Paragraphs,
                    "Update Table of Authorities").Applied)
            {
                refreshedCount++;
            }
        }

        return refreshedCount;
    }

    private Func<int, string?>? BuildPageTextResolver(DocumentReferenceBlockPageResolution? pages) =>
        pages?.PageNumberAtBlock is null
            ? null
            : PageNumberFormatDialogPlanner.BuildBlockPageReferenceResolver(
                _session.Document,
                pages.PageNumberAtBlock);

    private static DocumentReferenceBlockPageResolution? ResolveBlockPages(
        Func<DocumentReferenceBlockPageResolution>? factory) =>
        factory?.Invoke();

    private static bool RequiresBlockPageResolution(
        IReadOnlyList<DocumentFieldStoryParagraph> fieldParagraphs,
        Func<Run, bool>? include = null) =>
        fieldParagraphs
            .SelectMany(item => item.Paragraph.Runs)
            .Where(run => include is null || include(run))
            .Any(run =>
                run.CrossReference?.Kind == CrossRefFieldKind.PageRef
                || run.ComplexField?.Keyword is "PAGE" or "NUMPAGES" or "PAGEREF"
                || run.FieldKind is RunFieldKind.PageNumber or RunFieldKind.NumPages);

    private static string ResolveLiveFieldResult(
        TextDocument document,
        RunFieldKind kind,
        string cached,
        int blockIndex,
        DocumentReferenceBlockPageResolution? pages,
        Func<int, string?>? pageTextAtBlock,
        string? fileName,
        DateTime evaluatedAt)
    {
        return DocumentFieldDisplayPlanner.Resolve(
            kind,
            cached,
            document,
            new DocumentFieldDisplayContext(
                evaluatedAt,
                fileName,
                pageTextAtBlock?.Invoke(blockIndex),
                pages?.PageCount));
    }

    private void ExecuteGroup(IReadOnlyList<IDocumentCommand> commands, string undoLabel)
    {
        if (commands.Count == 1)
        {
            _session.Commands.Execute(commands[0]);
            return;
        }

        _session.Commands.BeginUndoGroup();
        try
        {
            foreach (var command in commands)
                _session.Commands.Execute(command);
            _session.Commands.CommitUndoGroup(undoLabel);
        }
        catch
        {
            _session.Commands.AbortUndoGroup();
            throw;
        }
    }

    private sealed record GeneratedRegionEdit(
        DocumentReferenceRegionEditResult Result,
        IReadOnlyList<IDocumentCommand> Commands);
}

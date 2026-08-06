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

/// <summary>
/// Owns portable generated-reference region replacement and field insertion. Renderers retain native
/// caret extraction, focus, and projection of the resulting model position.
/// </summary>
public sealed class DocumentReferenceEditingCoordinator
{
    private readonly DocumentEditingSession _session;

    internal DocumentReferenceEditingCoordinator(DocumentEditingSession session) => _session = session;

    public DocumentFieldCodeToggleResult ToggleFieldCodes()
    {
        var fields = EnumerateFieldParagraphs(_session.Document.Blocks)
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

    public DocumentFieldUpdateResult UpdateFields(
        Func<DocumentReferenceBlockPageResolution>? blockPageResolutionFactory = null,
        Func<ToaCitationPageResolver?>? authorityPageResolverFactory = null,
        string? fileName = null,
        DateTime? evaluatedAt = null)
    {
        var document = _session.Document;
        var fieldParagraphs = EnumerateFieldParagraphs(document.Blocks).ToArray();
        var fieldPages = RequiresBlockPageResolution(fieldParagraphs)
            ? ResolveBlockPages(blockPageResolutionFactory)
            : null;
        var fieldPageText = BuildPageTextResolver(fieldPages);
        var now = evaluatedAt ?? DateTime.Now;
        var updatedFieldCount = 0;

        foreach (var (blockIndex, paragraph) in fieldParagraphs)
        {
            for (var runIndex = 0; runIndex < paragraph.Runs.Count; runIndex++)
            {
                var run = paragraph.Runs[runIndex];
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
                    if (complexField.SimpleField?.IsLocked == true)
                        continue;

                    allowEmptyResult = ComplexFieldEngine.CanRecompute(complexField);
                    resolved = allowEmptyResult
                        ? ComplexFieldEngine.Recompute(
                            document,
                            blockIndex,
                            run,
                            fieldPages?.PageNumberAtBlock,
                            fieldPageText)
                        : ResolveLiveFieldResult(
                            document,
                            ComplexFieldDisplayPlanner.ResolveLiveKind(complexField.Keyword),
                            run.Text,
                            blockIndex,
                            fieldPages,
                            fieldPageText,
                            fileName,
                            now);
                }
                else if (run.FieldKind != RunFieldKind.None)
                {
                    resolved = ResolveLiveFieldResult(
                        document,
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
                    continue;

                run.Text = resolved;
                updatedFieldCount++;
            }
        }

        var refreshedGeneratedRegionCount = RefreshGeneratedReferenceRegions(
            blockPageResolutionFactory,
            authorityPageResolverFactory);
        return new DocumentFieldUpdateResult(updatedFieldCount, refreshedGeneratedRegionCount);
    }

    public DocumentReferenceEditResult InsertTableOfContents(
        int insertionIndex,
        Func<int, string?>? pageTextResolver)
    {
        TableOfContents.EnsureStyles(_session.Document);
        var paragraphs = TableOfContents.Build(_session.Document, pageTextResolver);
        var insertAt = Math.Clamp(insertionIndex, 0, _session.Document.Blocks.Count);
        if (paragraphs.Count == 0)
            return new DocumentReferenceEditResult(false, -1, insertAt);

        ExecuteGroup(
            paragraphs
                .Select((paragraph, offset) =>
                    (IDocumentCommand)new InsertParagraphCommand(insertAt + offset, paragraph))
                .ToArray(),
            "Insert Table of Contents");
        return new DocumentReferenceEditResult(true, -1, insertAt);
    }

    public DocumentReferenceEditResult RefreshTableOfContents(
        Func<int, string?>? pageTextResolver)
    {
        TableOfContents.EnsureStyles(_session.Document);
        var existing = _session.Document.Blocks
            .Select((block, index) => (block, index))
            .Where(item => TableOfContents.IsTocParagraph(item.block))
            .Select(item => item.index)
            .ToArray();
        var insertAt = existing.Length > 0 ? existing[0] : 0;
        var paragraphs = TableOfContents.Build(_session.Document, pageTextResolver);
        var commands = new List<IDocumentCommand>(existing.Length + paragraphs.Count);
        for (var index = existing.Length - 1; index >= 0; index--)
            commands.Add(new DeleteParagraphCommand(existing[index]));
        commands.AddRange(paragraphs.Select((paragraph, offset) =>
            (IDocumentCommand)new InsertParagraphCommand(insertAt + offset, paragraph)));
        if (commands.Count == 0)
            return new DocumentReferenceEditResult(false, -1, insertAt);

        ExecuteGroup(commands, "Update Table of Contents");
        return new DocumentReferenceEditResult(true, -1, insertAt);
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

    public DocumentReferenceRegionEditResult InsertGeneratedRegion(
        int insertIndex,
        IReadOnlyList<Paragraph> paragraphs,
        string undoLabel) =>
        ApplyGeneratedRegion(Array.Empty<int>(), insertIndex, paragraphs, undoLabel);

    public bool SetBookmark(int blockIndex, string? name)
    {
        var normalized = name?.Trim();
        if (string.IsNullOrEmpty(normalized)
            || blockIndex < 0
            || blockIndex >= _session.Document.Blocks.Count
            || _session.Document.Blocks[blockIndex] is not Paragraph)
        {
            return false;
        }

        _session.Commands.Execute(new SetParagraphBookmarkNameCommand(blockIndex, normalized));
        return true;
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
            targets.Select(target => (IDocumentCommand)new ReplaceParagraphRunsCommand(
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
        if (commands.Count == 0)
            return new DocumentReferenceRegionEditResult(false, insertAt, 0, 0);

        ExecuteGroup(commands, undoLabel);
        return new DocumentReferenceRegionEditResult(
            true,
            insertAt,
            deletes.Length,
            paragraphs.Count);
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

    private int RefreshGeneratedReferenceRegions(
        Func<DocumentReferenceBlockPageResolution>? blockPageResolutionFactory,
        Func<ToaCitationPageResolver?>? authorityPageResolverFactory)
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
            var pages = ResolveBlockPages(blockPageResolutionFactory);
            var paragraphs = TableOfFigures.Build(
                document,
                labelText,
                BuildPageTextResolver(pages));
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
            var plan = TableOfAuthoritiesRegionPlanner.BuildRefreshPlan(
                document,
                pageResolver: authorityPageResolverFactory?.Invoke());
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
        IReadOnlyList<(int BlockIndex, Paragraph Paragraph)> fieldParagraphs) =>
        fieldParagraphs
            .SelectMany(item => item.Paragraph.Runs)
            .Any(run =>
                run.CrossReference?.Kind == CrossRefFieldKind.PageRef
                || run.ComplexField?.Keyword is "PAGE" or "NUMPAGES" or "PAGEREF"
                || run.FieldKind is RunFieldKind.PageNumber or RunFieldKind.NumPages);

    private static IEnumerable<(int BlockIndex, Paragraph Paragraph)> EnumerateFieldParagraphs(
        IReadOnlyList<Block> blocks)
    {
        for (var blockIndex = 0; blockIndex < blocks.Count; blockIndex++)
        {
            if (blocks[blockIndex] is Paragraph paragraph)
            {
                yield return (blockIndex, paragraph);
                continue;
            }

            if (blocks[blockIndex] is not Table table)
                continue;

            foreach (var row in table.Rows)
            foreach (var cell in row.Cells)
            foreach (var cellParagraph in cell.Paragraphs)
                yield return (blockIndex, cellParagraph);
        }
    }

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
        var liveValue = kind switch
        {
            RunFieldKind.Date or RunFieldKind.Time =>
                ComplexFieldDisplayPlanner.FormatInvariantTemporalValue(kind, evaluatedAt),
            RunFieldKind.Author => document.Properties.Author,
            RunFieldKind.FileName => fileName,
            RunFieldKind.Title => document.Properties.Title,
            RunFieldKind.Subject => document.Properties.Subject,
            RunFieldKind.Keywords => document.Properties.Keywords,
            RunFieldKind.DocComments => document.Properties.Comments,
            RunFieldKind.PageNumber => pageTextAtBlock?.Invoke(blockIndex)
                ?? FirstPageNumberText(document),
            RunFieldKind.NumPages when pages?.PageCount is > 0 =>
                pages.PageCount.Value.ToString(CultureInfo.InvariantCulture),
            _ => null,
        };
        return string.IsNullOrEmpty(liveValue) ? cached : liveValue;
    }

    private static string FirstPageNumberText(TextDocument document)
    {
        var firstValue = Math.Max(1, document.Page.PageNumberStartAt ?? 1);
        return PageNumberFormatDialogPlanner.FormatPageNumber(
            firstValue,
            document.Page.PageNumberFormat);
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
}

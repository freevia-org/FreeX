using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Editing;

public sealed partial class DocumentView
{
    private sealed record DisplaySelectionTarget(KeyboardParagraph Address, int Anchor, int Active);

    /// <summary>Exposes exact run slices for clipboard consumers without converting display offsets to a single model range.</summary>
    public bool TryGetDisplaySelectionProjection(out Paragraph paragraph, out DocumentDisplaySelectionPlan plan)
    {
        paragraph = null!;
        plan = null!;
        if (!TryResolveDisplaySelection(out var target))
            return false;
        paragraph = target.Address.Model;
        plan = PlanDisplaySelection(target);
        return true;
    }

    /// <summary>Whether this selection must not reach legacy cached-text deletion or Cut paths.</summary>
    public bool IsDisplaySelectionDeletionBlocked()
    {
        if (!TryResolveDisplaySelection(out var target))
            return HasUnsupportedRichDisplaySelection();
        var plan = PlanDisplaySelection(target);
        return !plan.CanMutate || !CanDeleteDisplaySelection(target.Address.Model, plan)
            || TrackChangesEnabled && plan.WholeAtomicRuns.Count > 0;
    }

    /// <summary>Cheap guard before legacy form-control offsets or structural text replacement are used.</summary>
    public bool SelectionNeedsDisplayProjection()
    {
        if (!TryResolveDisplaySelection(out var target))
            return HasUnsupportedRichDisplaySelection();
        return target.Address.Model.Runs.Any(IsDisplaySelectionAtomicRun)
            || target.Address.RowIndex >= 0 && target.Address.Model.Runs.Any(run => run.Text.Contains("\r\n", StringComparison.Ordinal));
    }

    /// <summary>Whether paragraph splitting/rebuilding must preserve metadata beyond plain character cells.</summary>
    public bool SelectionNeedsStructuralPreservation()
    {
        if (!TryResolveDisplaySelection(out var target))
        {
            if (TryResolveCollapsedDisplayCaret(out var caretAddress, out _))
                return HasRichDisplaySelectionRuns(caretAddress.Model)
                    || caretAddress.RowIndex >= 0 && caretAddress.Model.Runs.Any(run => run.Text.Contains("\r\n", StringComparison.Ordinal));
            return HasUnsupportedRichDisplaySelection();
        }
        return HasRichDisplaySelectionRuns(target.Address.Model)
            || target.Address.RowIndex >= 0 && target.Address.Model.Runs.Any(run => run.Text.Contains("\r\n", StringComparison.Ordinal));
    }

    /// <summary>Checks collapsed display offsets before legacy cached control offsets can be used.</summary>
    public bool CaretNeedsDisplayProjection() =>
        TryResolveCollapsedDisplayCaret(out var address, out _)
        && (address.Model.Runs.Any(IsDisplaySelectionAtomicRun)
            || address.RowIndex >= 0 && address.Model.Runs.Any(run => run.Text.Contains("\r\n", StringComparison.Ordinal)));

    private bool TryDeleteDisplayTextElement(bool backward)
    {
        if (!TryResolveCollapsedDisplayCaret(out var address, out var offset)
            || !HasRichDisplaySelectionRuns(address.Model)
                && !(address.RowIndex >= 0 && address.Model.Runs.Any(run => run.Text.Contains("\r\n", StringComparison.Ordinal))))
            return false;
        var range = TextElementDeletionPlanner.Plan(KeyboardParagraphText(address), offset, backward);
        if (range is null)
            return true; // A rich paragraph boundary must not fall back to a flattening merge.
        TryEditDisplaySelection(null, out _, new(address, range.Value.Start, range.Value.End),
            advancePastRetainedDeletion: !backward);
        return true;
    }

    private bool TryResolveCollapsedDisplayCaret(out KeyboardParagraph address, out int offset)
    {
        address = null!;
        offset = 0;
        if (_shapeCaret is not null || _hfCaret is not null || SelectedCellRange is not null)
            return false;
        if (_cellCaret is { } cell)
        {
            if (HasCellTextSelection() || GetCellParagraph(cell.TableBlock, cell.Row, cell.Col, cell.ParaIdx) is not { } paragraph)
                return false;
            address = new(paragraph, cell.TableBlock, cell.Row, cell.Col, cell.ParaIdx);
            offset = cell.Offset;
            return true;
        }
        if (NormalizedSelection() is not null || _caret.Block < 0 || _caret.Block >= _doc.Blocks.Count
            || _doc.Blocks[_caret.Block] is not Paragraph body)
            return false;
        address = new(body, _caret.Block);
        offset = _caret.Offset;
        return true;
    }

    private bool TryInsertDisplayText(string text)
    {
        if (!TryResolveCollapsedDisplayCaret(out var address, out var offset)
            || !HasRichDisplaySelectionRuns(address.Model)
                && !(address.RowIndex >= 0 && address.Model.Runs.Any(run => run.Text.Contains("\r\n", StringComparison.Ordinal))))
            return false;
        if (text.Length == 0 || text.IndexOfAny(['\r', '\n']) >= 0 || IsEditingLocked
            || ContentControlInteractionPlanner.IsBlockContentControlLocked(address.Model.BlockContentControl))
            return true;

        var spans = BuildDisplaySelectionSpans(address);
        var displayText = string.Concat(spans.Select(span => span.DisplayText));
        offset = Math.Clamp(offset, 0, displayText.Length);
        if (SplitsDisplaySelectionSurrogate(displayText, offset))
            return true;
        DocumentRunDisplaySpan? literal = null;
        var modelOffset = 0;
        var position = 0;
        var runBoundary = address.Model.Runs.Count;
        foreach (var span in spans)
        {
            var end = position + span.DisplayText.Length;
            if (span.DisplayText.Length > 0 && position >= offset && runBoundary == address.Model.Runs.Count)
                runBoundary = span.SourceRunIndex;
            if (span.IsAtomic && offset > position && offset < end)
                return true;
            if (!span.IsAtomic && span.DisplayText.Length > 0 && offset >= position && offset <= end)
            {
                var local = offset - position;
                if (span.DisplayToModelBoundaries is null && span.DisplayText.Length != span.SourceRun.Text.Length)
                    return true;
                modelOffset = span.DisplayToModelBoundaries?[local] ?? local;
                if (modelOffset < 0 || SplitsDisplaySelectionSurrogate(span.SourceRun.Text, modelOffset))
                    return true;
                literal = span;
                break; // Adjacent literal text owns the insertion boundary, not a field cache.
            }
            position = end;
        }
        var source = literal?.SourceRun ?? address.Model.Runs.ElementAtOrDefault(
            Math.Clamp(runBoundary, 0, Math.Max(0, address.Model.Runs.Count - 1))) ?? new Run("");
        // Editable control insertion is owned by the earlier mapped FormField fastpath. Reaching
        // this path with a control means its policy/type did not permit that edit; never split it.
        if (source.Control is not null || source.MoveRevisionId is not null)
            return true;
        var tracked = TrackChangesEnabled;
        var insertion = DisplaySelectionInsertion(source, text, tracked, RevisionAuthor,
            tracked ? _editingSession.RevisionDateXmlForEdit() : null);
        insertion.Formatting = _pendingRunFmt ?? source.Formatting;
        if (literal is null || modelOffset == 0 || modelOffset == source.Text.Length)
        {
            insertion.HyperlinkUrl = null;
            insertion.HyperlinkAnchor = null;
            insertion.HyperlinkTooltip = null;
        }
        var editedOffset = offset + text.Length;
        void Rebuild(Paragraph paragraph) => ApplyDisplayInsertion(paragraph, literal, modelOffset, runBoundary, insertion);
        IDocumentCommand command = address.RowIndex < 0
            ? new ReplaceParagraphRunsCommand(address.BlockIndex, Rebuild)
            : new ReplaceCellParagraphRunsCommand(address.BlockIndex, address.RowIndex, address.GridColumnIndex,
                address.ParagraphIndex, Rebuild);
        ExecuteTextEditWithCaretHistory(command, () =>
        {
            _pendingRunFmt = null;
            PlaceDisplaySelectionCaret(address, editedOffset);
        });
        return true;
    }

    private static bool SplitsDisplaySelectionSurrogate(string text, int offset) =>
        offset > 0 && offset < text.Length && char.IsHighSurrogate(text[offset - 1]) && char.IsLowSurrogate(text[offset]);

    private static void ApplyDisplayInsertion(Paragraph paragraph, DocumentRunDisplaySpan? literal,
        int modelOffset, int runBoundary, Run insertion)
    {
        var rebuilt = new List<Run>();
        var boundaries = new int[paragraph.Runs.Count + 1];
        for (var index = 0; index <= paragraph.Runs.Count; index++)
        {
            boundaries[index] = rebuilt.Count;
            if (literal is null && index == runBoundary)
                rebuilt.Add(insertion);
            if (index == paragraph.Runs.Count)
                break;
            if (literal?.SourceRunIndex == index)
            {
                var single = new Paragraph();
                single.Runs.Add(paragraph.Runs[index]);
                RevisionEditPlanner.InsertRunAtOffset(single, modelOffset, insertion);
                rebuilt.AddRange(single.Runs);
            }
            else rebuilt.Add(paragraph.Runs[index]);
        }
        var bookmarks = paragraph.BookmarkBoundaries.Select(boundary => boundary with
        {
            RunIndex = boundaries[Math.Clamp(boundary.RunIndex, 0, paragraph.Runs.Count)]
        }).ToArray();
        paragraph.Runs.Clear();
        paragraph.Runs.AddRange(rebuilt);
        paragraph.BookmarkBoundaries.Clear();
        paragraph.BookmarkBoundaries.AddRange(bookmarks);
    }

    private void PlaceDisplaySelectionCaret(KeyboardParagraph address, int offset)
    {
        offset = Math.Clamp(offset, 0, KeyboardParagraphText(address).Length);
        if (address.RowIndex < 0)
        {
            _caret = new DocPosition(address.BlockIndex, offset);
            _selectionAnchor = _caret;
        }
        else
        {
            _cellCaret = (address.BlockIndex, address.RowIndex, address.GridColumnIndex, address.ParagraphIndex, offset);
            _cellAnchor = _cellCaret;
            _caret = new DocPosition(address.BlockIndex, FindCellGlyphOffset(address.BlockIndex,
                address.RowIndex, address.GridColumnIndex, address.ParagraphIndex, offset));
            _selectionAnchor = _caret;
        }
    }

    private bool TryGetDisplaySelectionText(out string text)
    {
        text = string.Empty;
        if (TryResolveDisplaySelection(out var target))
        {
            text = PlanDisplaySelection(target).SelectedText;
            return true;
        }
        if (_shapeCaret is not null || _hfCaret is not null || SelectedCellRange is not null)
            return false;
        var slices = new List<string>();
        if (_cellCaret is { } cell && _cellAnchor is { } anchor
            && cell.TableBlock == anchor.TableBlock && cell.Row == anchor.Row && cell.Col == anchor.Col
            && HasCellTextSelection() && GetCellModel(cell.TableBlock, cell.Row, cell.Col) is { } model)
        {
            var (start, end) = CompareCellEndpoints(anchor, cell) <= 0 ? (anchor, cell) : (cell, anchor);
            for (var index = Math.Max(0, start.ParaIdx); index <= end.ParaIdx && index < model.Paragraphs.Count; index++)
            {
                var display = KeyboardParagraphText(new(model.Paragraphs[index], cell.TableBlock, cell.Row, cell.Col, index));
                slices.Add(DisplaySelectionTextSlice(display, index == start.ParaIdx ? start.Offset : 0,
                    index == end.ParaIdx ? end.Offset : display.Length));
            }
        }
        else if (_cellCaret is null && NormalizedSelection() is { } body)
        {
            for (var index = body.Start.Block; index <= body.End.Block; index++)
            {
                if (index < 0 || index >= _doc.Blocks.Count || _doc.Blocks[index] is not Paragraph paragraph)
                    return false;
                var display = KeyboardParagraphText(new(paragraph, index));
                slices.Add(DisplaySelectionTextSlice(display, index == body.Start.Block ? body.Start.Offset : 0,
                    index == body.End.Block ? body.End.Offset : display.Length));
            }
        }
        if (slices.Count == 0)
            return false;
        text = string.Join('\n', slices);
        return true;
    }

    private static string DisplaySelectionTextSlice(string text, int start, int end) =>
        text[Math.Clamp(start, 0, text.Length)..Math.Clamp(end, Math.Clamp(start, 0, text.Length), text.Length)];

    // The return value means handled, including refusals. Callers must stop rather than falling
    // through to a cached-text or delete-one-character path. The out value reports an actual edit.
    private bool TryDeleteDisplaySelection(out bool deleted) => TryEditDisplaySelection(null, out deleted);

    private bool TryReplaceDisplaySelection(string text, out bool replaced)
    {
        replaced = false;
        if (text.Length == 0 || text.IndexOfAny(['\r', '\n']) >= 0)
            return SelectionNeedsStructuralPreservation();
        return TryEditDisplaySelection(text, out replaced);
    }

    private bool TryEditDisplaySelection(string? replacementText, out bool deleted,
        DisplaySelectionTarget? explicitTarget = null, bool advancePastRetainedDeletion = false)
    {
        deleted = false;
        if (_shapeCaret is not null || _hfCaret is not null || SelectedCellRange is not null)
            return false;
        DisplaySelectionTarget target;
        if (explicitTarget is { } plannedTarget)
            target = plannedTarget;
        else if (!TryResolveDisplaySelection(out target))
            return HasUnsupportedRichDisplaySelection();
        if (!NeedsDisplaySelectionEdit(target.Address))
            return false;

        var plan = PlanDisplaySelection(target);
        if (!plan.CanMutate || !CanDeleteDisplaySelection(target.Address.Model, plan))
            return true;
        // Tracking an object/field/note deletion also needs acceptance/rejection semantics for its
        // payload. Keep it intact until that separate structural-revision path can handle it.
        if (TrackChangesEnabled && plan.WholeAtomicRuns.Count > 0)
            return true;

        var trackChanges = TrackChangesEnabled;
        var author = RevisionAuthor;
        var date = trackChanges ? _editingSession.RevisionDateXmlForEdit() : null;
        var replacementFormatting = replacementText is null ? null : _pendingRunFmt;
        var preserveReplacementComment = string.IsNullOrEmpty(replacementText)
            || AnnotationCleanupDocumentCommand.ReplacementKeepsExistingCommentAnchor(_doc, target.Address.Model, plan, trackChanges, author);
        var affected = plan.LiteralRanges.Select(slice => slice.Span.SourceRun)
            .Concat(plan.WholeAtomicRuns.Select(span => span.SourceRun)).ToArray();
        var keptDeletedText = false;
        void Rebuild(Paragraph paragraph) => keptDeletedText = ApplyDisplaySelectionDeletion(
            paragraph, plan, trackChanges, author, date, replacementText, replacementFormatting, preserveReplacementComment);
        var address = target.Address;
        IDocumentCommand command = address.RowIndex < 0
            ? new ReplaceParagraphRunsCommand(address.BlockIndex, Rebuild)
            : new ReplaceCellParagraphRunsCommand(address.BlockIndex, address.RowIndex, address.GridColumnIndex,
                address.ParagraphIndex, Rebuild);
        if (affected.Any(run => run.FootnoteId is not null || run.EndnoteId is not null || run.CommentId is not null))
            command = new AnnotationCleanupDocumentCommand(command, affected,
                replacementText is null ? "Delete selection" : "Replace selection");
        ExecuteTextEditWithCaretHistory(command, () =>
        {
            if (!string.IsNullOrEmpty(replacementText))
                _pendingRunFmt = null;
            var desiredOffset = advancePastRetainedDeletion && keptDeletedText
                ? plan.DisplayEnd
                : plan.DisplayStart + (replacementText?.Length ?? 0);
            PlaceDisplaySelectionCaret(address, desiredOffset);
        });
        deleted = true;
        return true;
    }

    private bool TryResolveDisplaySelection(out DisplaySelectionTarget target)
    {
        target = null!;
        if (_shapeCaret is not null || _hfCaret is not null || SelectedCellRange is not null)
            return false;
        if (_cellCaret is { } cell)
        {
            if (_cellAnchor is not { } anchor || anchor.TableBlock != cell.TableBlock
                || anchor.Row != cell.Row || anchor.Col != cell.Col || anchor.ParaIdx != cell.ParaIdx
                || anchor.Offset == cell.Offset
                || GetCellParagraph(cell.TableBlock, cell.Row, cell.Col, cell.ParaIdx) is not { } paragraph)
                return false;
            target = new(new KeyboardParagraph(paragraph, cell.TableBlock, cell.Row, cell.Col, cell.ParaIdx),
                anchor.Offset, cell.Offset);
            return true;
        }
        if (NormalizedSelection() is not { } selection || selection.Start.Block != selection.End.Block
            || _doc.Blocks[selection.Start.Block] is not Paragraph body)
            return false;
        target = new(new KeyboardParagraph(body, selection.Start.Block), selection.Start.Offset, selection.End.Offset);
        return true;
    }

    private DocumentDisplaySelectionPlan PlanDisplaySelection(DisplaySelectionTarget target)
        => DocumentDisplaySelectionPlanner.Plan(BuildDisplaySelectionSpans(target.Address), target.Anchor, target.Active);

    private IReadOnlyList<DocumentRunDisplaySpan> BuildDisplaySelectionSpans(KeyboardParagraph address)
    {
        var spans = new List<DocumentRunDisplaySpan>();
        for (var index = 0; index < address.Model.Runs.Count; index++)
        {
            var run = address.Model.Runs[index];
            var single = new Paragraph();
            single.Runs.Add(run);
            var display = KeyboardParagraphText(address with { Model = single });
            var atomic = IsDisplaySelectionAtomicRun(run);
            IReadOnlyList<int>? boundaries = null;
            if (!atomic && address.RowIndex >= 0 && run.Text.Contains("\r\n", StringComparison.Ordinal))
            {
                var mapped = new List<int> { 0 };
                for (var offset = 0; offset < run.Text.Length; offset++)
                {
                    if (run.Text[offset] == '\r' && offset + 1 < run.Text.Length && run.Text[offset + 1] == '\n')
                        offset++;
                    mapped.Add(offset + 1);
                }
                if (mapped.Count == display.Length + 1)
                    boundaries = mapped;
            }
            spans.Add(new(index, run, display, atomic, boundaries));
        }
        return spans;
    }

    private static bool IsDisplaySelectionAtomicRun(Run run) =>
        !IsUnicodeLiteralRun(run) || run.Ruby is not null || run.PreservedDrawing is not null
        || run.SubDocument is not null || run.Citation is not null || run.TableFormula is not null
        || run.CrossReference is not null || run.IsPageBreak || run.IsColumnBreak;

    private static bool NeedsDisplaySelectionEdit(KeyboardParagraph address) =>
        address.RowIndex >= 0 || HasRichDisplaySelectionRuns(address.Model);

    private static bool HasRichDisplaySelectionRuns(Paragraph paragraph) =>
        paragraph.BookmarkNames.Count > 0 || paragraph.BookmarkBoundaries.Count > 0
        || paragraph.Runs.Any(run => IsDisplaySelectionAtomicRun(run) || run.Text.Length == 0
            || run.MoveRevisionId is not null || run.CommentId is not null);

    public bool HasUnsupportedRichDisplaySelection()
    {
        // Rectangles already expose complete model paragraphs to the existing clipboard/table path.
        if (SelectedCellRange is not null)
            return false;
        if (CanDeleteSameCellBookmarkSelection())
            return false;
        if (_cellCaret is { } cell && _cellAnchor is { } anchor && HasCellTextSelection()
            && cell.TableBlock >= 0 && cell.TableBlock < _doc.Blocks.Count
            && _doc.Blocks[cell.TableBlock] is Table table)
        {
            var (start, end) = CompareCellEndpoints(anchor, cell) <= 0 ? (anchor, cell) : (cell, anchor);
            var first = CanonicalCellEntry(table, cell.TableBlock, start.Row, start.Col);
            var last = CanonicalCellEntry(table, cell.TableBlock, end.Row, end.Col);
            if (first is null || last is null)
                return true;
            var entries = EnumerateLogicalCells(table, cell.TableBlock).ToList();
            var from = entries.FindIndex(entry => ReferenceEquals(entry.Cell, first.Value.Cell));
            var to = entries.FindIndex(entry => ReferenceEquals(entry.Cell, last.Value.Cell));
            if (from < 0 || to < from)
                return true;
            var sameCell = from == to;
            if (!sameCell && end.Offset == 0)
                to--;
            for (var index = from; index <= to; index++)
            {
                var paragraphs = entries[index].Cell.Paragraphs;
                var lo = sameCell ? start.ParaIdx : 0;
                var hi = sameCell ? end.ParaIdx : paragraphs.Count - 1;
                if (paragraphs.Skip(Math.Max(0, lo)).Take(hi - lo + 1).Any(HasRichDisplaySelectionRuns))
                    return true;
            }
            return false;
        }
        if (NormalizedSelection() is not { } selection)
            return false;
        return _doc.Blocks.Skip(selection.Start.Block).Take(selection.End.Block - selection.Start.Block + 1)
            .Any(block => block is not Paragraph paragraph || HasRichDisplaySelectionRuns(paragraph));
    }

    private bool CanDeleteDisplaySelection(Paragraph paragraph, DocumentDisplaySelectionPlan plan)
    {
        if (IsEditingLocked || ContentControlInteractionPlanner.IsBlockContentControlLocked(paragraph.BlockContentControl))
            return false;
        foreach (var run in plan.LiteralRanges.Select(slice => slice.Span.SourceRun)
                     .Concat(plan.WholeAtomicRuns.Select(span => span.SourceRun)))
        {
            if (run.MoveRevisionId is not null || run.Control is not null
                && !ContentControlInteractionPlanner.CanEditContentControlText(run, RestrictEditingPolicy))
                return false;
        }
        return plan.WholeAtomicRuns.All(span => ContentControlInteractionPlanner.CanDeleteContentControl(span.SourceRun.Control));
    }

    private bool ApplyDisplaySelectionDeletion(
        Paragraph paragraph,
        DocumentDisplaySelectionPlan plan,
        bool trackChanges,
        string author,
        string? date,
        string? replacementText,
        RunFormatting? replacementFormatting,
        bool preserveReplacementComment)
    {
        var literals = plan.LiteralRanges.ToDictionary(slice => slice.Span.SourceRunIndex);
        var atomicIndexes = plan.WholeAtomicRuns.Select(span => span.SourceRunIndex).ToHashSet();
        var rebuilt = new List<Run>();
        var keptDeletedText = false;
        var boundaryIndexes = new int[paragraph.Runs.Count + 1];
        var insertionIndex = literals.Keys.Concat(atomicIndexes).Min();
        for (var index = 0; index < paragraph.Runs.Count; index++)
        {
            boundaryIndexes[index] = rebuilt.Count;
            var source = paragraph.Runs[index];
            if (atomicIndexes.Contains(index))
            {
                if (index == insertionIndex && !string.IsNullOrEmpty(replacementText))
                    rebuilt.Add(DisplaySelectionInsertion(source, replacementText, trackChanges, author, date,
                        replacementFormatting, preserveReplacementComment));
                continue;
            }
            if (!literals.TryGetValue(index, out var slice))
            {
                rebuilt.Add(source);
                continue;
            }
            var single = new Paragraph();
            single.Runs.Add(source);
            keptDeletedText |= TextElementDeletionPlanner.Apply(single, new(slice.ModelStart, slice.ModelLength),
                trackChanges, author, date).KeptDeletedText;
            if (index == insertionIndex && !string.IsNullOrEmpty(replacementText))
                InsertDisplaySelectionText(single, slice.ModelStart,
                    DisplaySelectionInsertion(source, replacementText, trackChanges, author, date,
                        replacementFormatting, preserveReplacementComment));
            rebuilt.AddRange(single.Runs);
        }
        boundaryIndexes[^1] = rebuilt.Count;
        var bookmarks = paragraph.BookmarkBoundaries.Select(boundary => boundary with
        {
            RunIndex = boundaryIndexes[Math.Clamp(boundary.RunIndex, 0, paragraph.Runs.Count)]
        }).ToArray();
        paragraph.Runs.Clear();
        paragraph.Runs.AddRange(rebuilt);
        paragraph.BookmarkBoundaries.Clear();
        paragraph.BookmarkBoundaries.AddRange(bookmarks);
        return keptDeletedText;
    }

    private static Run DisplaySelectionInsertion(Run source, string text, bool tracked, string author, string? date,
        RunFormatting? formatting = null, bool preserveCommentAnchor = true) =>
        new(text, formatting ?? source.Formatting)
        {
            StyleId = source.StyleId,
            Control = source.Control,
            CommentId = preserveCommentAnchor ? source.CommentId : null,
            HyperlinkUrl = source.HyperlinkUrl,
            HyperlinkAnchor = source.HyperlinkAnchor,
            HyperlinkTooltip = source.HyperlinkTooltip,
            Revision = tracked ? RevisionKind.Inserted : RevisionKind.None,
            RevisionAuthor = tracked ? author : null,
            RevisionDateXml = tracked ? date : null
        };

    private static void InsertDisplaySelectionText(Paragraph paragraph, int offset, Run insertion)
    {
        var position = 0;
        for (var index = 0; index < paragraph.Runs.Count; index++)
        {
            var run = paragraph.Runs[index];
            var local = offset - position;
            if (local <= 0)
            {
                paragraph.Runs.Insert(index, insertion);
                return;
            }
            if (local < run.Text.Length)
            {
                paragraph.Runs[index] = RevisionEditPlanner.CloneRunWithText(run, run.Text[..local]);
                paragraph.Runs.Insert(index + 1, insertion);
                paragraph.Runs.Insert(index + 2, RevisionEditPlanner.CloneRunWithText(run, run.Text[local..]));
                return;
            }
            position += run.Text.Length;
        }
        paragraph.Runs.Add(insertion);
    }
}

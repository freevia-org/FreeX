using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Editing;

public sealed partial class DocumentView
{
    private bool HfApplyRunEdit(HfTarget target, StoryParagraphRunEditPlan? plan)
    {
        if (plan is null)
            return false;
        var ownsUndoGroup = IsEmptyHeaderFooterPlaceholder(target) && !_bus.IsUndoGroupOpen;
        if (ownsUndoGroup)
            _bus.BeginUndoGroup();
        try
        {
            MaterializeEmptyHeaderFooterStory(target);
            _bus.Execute(new FreeW.Core.Model.EditHeaderFooterParagraphCommand(
                target.SectionIndex, target.UseFinalSectionStore,
                HeaderFooterDialogPlanner.CommandSlotIndexFor(target.Slot), target.ParaIdx, plan.ApplyTo));
            if (ownsUndoGroup)
                _bus.CommitUndoGroup("Edit header/footer");
        }
        catch
        {
            if (ownsUndoGroup && _bus.IsUndoGroupOpen)
                _bus.RollbackUndoGroup();
            throw;
        }
        _hfCaret = (target, Math.Clamp(plan.CaretOffset, 0, HfParaLength(target)));
        _hfSelectionAnchor = null;
        Relayout(_laidOutWidth > 0 ? _laidOutWidth : FallbackWidth);
        InvalidateVisual();
        CaretMoved?.Invoke();
        return true;
    }

    private bool HfTryInsertSameParagraphText(string text)
    {
        if (_hfCaret is not { } caret || GetHfParagraph(caret.Target) is not { } paragraph)
            return false;
        var selection = NormalizedHfSelection();
        if (selection is { } range && range.Start.ParagraphIndex != range.End.ParagraphIndex)
            return false;
        var start = selection?.Start.Offset ?? caret.Offset;
        var end = selection?.End.Offset ?? caret.Offset;
        var target = selection is { } selected
            ? caret.Target with { ParaIdx = selected.Start.ParagraphIndex } : caret.Target;
        paragraph = GetHfParagraph(target)!;
        if (!HfTypingIsAllowed(target, start))
            return false;
        var insertion = StoryParagraphRunEditPlanner.TypingRun(paragraph, start, text);
        var previous = insertion.Control;
        if (previous?.WordMetadata is { ShowingPlaceholder: true } metadata)
            insertion.Control = previous with { WordMetadata = metadata with { ShowingPlaceholder = false } };
        var policy = selection is not null ? StoryParagraphAtomicEditPolicy.ExpandSelection
            : StoryParagraphAtomicEditPolicy.RejectPartial;
        var plan = StoryParagraphRunEditPlanner.PlanReplace(paragraph, start, end, [insertion], policy,
            previous, insertion.Control);
        if (plan is null || !HfEditIsAllowed(target, plan.StartOffset, plan.EndOffset))
            return false;
        return HfApplyRunEdit(target, plan);
    }

    private bool HfTryInsertSameParagraphRun(Run run)
    {
        if (_hfCaret is not { } caret)
            return false;
        var selection = NormalizedHfSelection();
        if (selection is { } range && range.Start.ParagraphIndex != range.End.ParagraphIndex)
            return false;
        var target = selection is { } selected
            ? caret.Target with { ParaIdx = selected.Start.ParagraphIndex } : caret.Target;
        if (GetHfParagraph(target) is not { } paragraph)
            return false;
        var plan = StoryParagraphRunEditPlanner.PlanReplace(paragraph,
            selection?.Start.Offset ?? caret.Offset, selection?.End.Offset ?? caret.Offset, [run],
            selection is not null ? StoryParagraphAtomicEditPolicy.ExpandSelection
                : StoryParagraphAtomicEditPolicy.RejectPartial);
        if (plan is null || !HfEditIsAllowed(target, plan.StartOffset, plan.EndOffset))
            return false;
        return HfApplyRunEdit(target, plan);
    }

    private bool HfTryDeleteSameParagraphSelection(HfTarget target, HeaderFooterTextRange selection)
    {
        target = target with { ParaIdx = selection.Start.ParagraphIndex };
        if (GetHfParagraph(target) is not { } paragraph)
            return false;
        var plan = StoryParagraphRunEditPlanner.PlanReplace(paragraph, selection.Start.Offset,
            selection.End.Offset, [], StoryParagraphAtomicEditPolicy.ExpandSelection);
        if (plan is null || !HfEditIsAllowed(target, plan.StartOffset, plan.EndOffset))
            return false;
        return HfApplyRunEdit(target, plan);
    }

    private void HfDeleteAdjacentRunText(HfTarget target, int offset, bool backward)
    {
        if (GetHfParagraph(target) is not { } paragraph)
            return;
        var atoms = HfAtoms(paragraph);
        var (index, insideField) = HfAtomIndexForOffset(atoms, offset);
        if (insideField)
            return;
        var removeIndex = backward ? index - 1 : index;
        if (removeIndex < 0 || removeIndex >= atoms.Count)
        {
            if (!backward)
                HfMergeWithNextParagraph(target);
            return;
        }
        var atom = atoms[removeIndex];
        StoryParagraphRunEditPlan? plan;
        if (atom.FieldRun is { } field)
        {
            var runIndex = paragraph.Runs.FindIndex(run => ReferenceEquals(run, field));
            plan = StoryParagraphRunEditPlanner.PlanReplaceRuns(paragraph, runIndex, 1, []);
        }
        else
        {
            var start = backward ? offset - atom.ModelLength : offset;
            plan = StoryParagraphRunEditPlanner.PlanReplace(paragraph, start, start + atom.ModelLength, []);
        }
        if (plan is not null && HfEditIsAllowed(target, plan.StartOffset, plan.EndOffset))
            HfApplyRunEdit(target, plan);
    }

    private void HfMergeWithNextParagraph(HfTarget target)
    {
        if (ResolveHfStore(target) is not { } store
            || HeaderFooterDialogPlanner.GetSlot(store, target.Slot) is not { } story
            || target.ParaIdx + 1 >= story.Paragraphs.Count
            || story.Table is not null && !HeaderFooterTableParagraphMap.AreInSameCell(
                story, target.ParaIdx, target.ParaIdx + 1))
            return;
        var previousAnchor = _hfSelectionAnchor;
        _hfSelectionAnchor = (target with { ParaIdx = target.ParaIdx + 1 }, 0);
        if (!TryDeleteHfSelection())
            _hfSelectionAnchor = previousAnchor;
    }

    private bool HfCanUseLegacyParagraphMerge()
    {
        if (_hfCaret is not { } caret || ResolveHfStore(caret.Target) is not { } store
            || HeaderFooterDialogPlanner.GetSlot(store, caret.Target.Slot) is not { } story)
            return false;
        var selection = NormalizedHfSelection();
        if (selection is null)
            return true;
        // The legacy cross-paragraph planner already preserves run payloads and atomic expansion.
        // It does not yet merge positional bookmarks or preserve empty source anchors.
        return story.Paragraphs.Skip(selection.Value.Start.ParagraphIndex)
            .Take(selection.Value.End.ParagraphIndex - selection.Value.Start.ParagraphIndex + 1)
            .All(paragraph => paragraph.BookmarkBoundaries.Count == 0 && paragraph.BookmarkNames.Count == 0
                && paragraph.Runs.All(run => run.Text.Length > 0
                    || !HfRunHasSourceMetadata(run) && run.Formatting == RunFormatting.Default));
    }

    private bool HfCanUseLegacyStructuralEdit()
    {
        if (_hfCaret is not { } caret || ResolveHfStore(caret.Target) is not { } store
            || HeaderFooterDialogPlanner.GetSlot(store, caret.Target.Slot) is not { } story)
            return false;
        var selection = NormalizedHfSelection();
        var first = selection?.Start.ParagraphIndex ?? caret.Target.ParaIdx;
        var last = selection?.End.ParagraphIndex ?? caret.Target.ParaIdx;
        return story.Paragraphs.Skip(first).Take(last - first + 1).All(paragraph =>
            paragraph.StyleId is null && paragraph.MarkFormatting is null
            && paragraph.BookmarkNames.Count == 0 && paragraph.BookmarkBoundaries.Count == 0
            && paragraph.BlockContentControl is null && paragraph.BlockCustomXml is null
            && paragraph.PreservedNumbering is null && paragraph.SectionBreak is null
            && paragraph.Runs.All(run => !HfRunHasSourceMetadata(run)));
    }

    private static bool HfRunHasSourceMetadata(Run run) =>
        StoryParagraphRunEditPlanner.IsAtomic(run) || run.Control is not null || run.StyleId is not null
        || run.CommentId is not null || run.HyperlinkUrl is not null || run.HyperlinkAnchor is not null
        || run.HyperlinkTooltip is not null || run.Revision != RevisionKind.None || run.RevisionAuthor is not null
        || run.RevisionDateXml is not null || run.NestedRevision is not null || run.FormatRevision is not null
        || run.MoveRevisionId is not null || run.FieldLocked || run.FieldCodeVisible;
}

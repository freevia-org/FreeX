using FreeW.Core.Model;

namespace FreeW.App.Presentation.Editing;

public enum DocumentBodyDeleteDirection
{
    Backward,
    Forward,
}

public enum DocumentBodyEditorTransition
{
    InsertText,
    ReplaceSelection,
    DeleteSelection,
    DeleteCharacterBackward,
    DeleteCharacterForward,
    MergeWithPreviousParagraph,
    MergeWithNextParagraph,
    OutdentListItem,
    InsertParagraphBreak,
    ExitEmptyList,
}

public readonly record struct DocumentBodyTextInput(
    string Text,
    bool TrackChanges,
    RunFormatting? Formatting = null,
    bool InheritHyperlink = true,
    DocumentTextHyperlink? Hyperlink = null);

public readonly record struct DocumentBodyEditorActionResult(
    DocumentTextPosition Caret,
    DocumentBodyEditorTransition Transition);

/// <summary>
/// Interprets portable body selections and orchestrates ordinary editor actions. Renderers translate
/// native caret/selection coordinates before calling this coordinator and realize the returned caret.
/// </summary>
public sealed class DocumentBodyEditingCoordinator
{
    private readonly DocumentEditingSession _session;

    internal DocumentBodyEditingCoordinator(DocumentEditingSession session)
    {
        _session = session;
    }

    public bool TryApplyTextInput(
        DocumentTextRange selection,
        DocumentBodyTextInput input,
        out DocumentBodyEditorActionResult result)
    {
        result = default;
        if (string.IsNullOrEmpty(input.Text))
            return false;

        var normalized = selection.Normalize();
        var applied = input.TrackChanges
            ? TryApplyTrackedTextInput(selection, input, out var editResult)
            : TryApplyUntrackedTextInput(selection, input, out editResult);
        if (!applied)
            return false;

        result = new DocumentBodyEditorActionResult(
            editResult.Caret,
            normalized.IsCollapsed
                ? DocumentBodyEditorTransition.InsertText
                : DocumentBodyEditorTransition.ReplaceSelection);
        return true;
    }

    public bool TryApplyDeletion(
        DocumentTextRange selection,
        DocumentBodyDeleteDirection direction,
        bool trackChanges,
        out DocumentBodyEditorActionResult result) =>
        TryApplyDeletion(
            selection,
            direction,
            trackChanges,
            mergeForwardBoundary: true,
            out result);

    public bool TryApplyDeletion(
        DocumentTextRange selection,
        DocumentBodyDeleteDirection direction,
        bool trackChanges,
        bool mergeForwardBoundary,
        out DocumentBodyEditorActionResult result)
    {
        result = default;
        var normalized = selection.Normalize();
        if (!normalized.IsCollapsed)
        {
            var applied = trackChanges
                ? _session.TryDeleteTrackedBodyText(
                    normalized,
                    advancePastKeptText: false,
                    out var editResult)
                : _session.TryDeleteBodyText(normalized, out editResult);
            if (!applied)
                return false;

            result = new DocumentBodyEditorActionResult(
                editResult.Caret,
                DocumentBodyEditorTransition.DeleteSelection);
            return true;
        }

        var caret = normalized.Start;
        if (!TryGetCaretParagraph(caret, out var paragraph, out var caretOffset))
            return false;

        var deleteRange = direction switch
        {
            DocumentBodyDeleteDirection.Backward when caretOffset > 0 => new DocumentTextRange(
                new DocumentTextPosition(caret.BlockIndex, caretOffset - 1),
                new DocumentTextPosition(caret.BlockIndex, caretOffset)),
            DocumentBodyDeleteDirection.Forward when caretOffset < paragraph.PlainText.Length =>
                new DocumentTextRange(
                    new DocumentTextPosition(caret.BlockIndex, caretOffset),
                    new DocumentTextPosition(caret.BlockIndex, caretOffset + 1)),
            _ => default,
        };
        if (!deleteRange.IsCollapsed)
        {
            var applied = trackChanges
                ? _session.TryDeleteTrackedBodyText(
                    deleteRange,
                    advancePastKeptText: direction == DocumentBodyDeleteDirection.Forward,
                    out var editResult)
                : _session.TryDeleteBodyText(deleteRange, out editResult);
            if (!applied)
                return false;

            result = new DocumentBodyEditorActionResult(
                editResult.Caret,
                direction == DocumentBodyDeleteDirection.Backward
                    ? DocumentBodyEditorTransition.DeleteCharacterBackward
                    : DocumentBodyEditorTransition.DeleteCharacterForward);
            return true;
        }

        if (direction == DocumentBodyDeleteDirection.Backward
            && paragraph.Formatting.ListKind != ListKind.None)
        {
            _session.FormatParagraphs(
                [caret.BlockIndex],
                formatting => formatting.ListLevel == 0
                    ? formatting with { ListKind = ListKind.None, ListLevel = 0 }
                    : formatting with { ListLevel = formatting.ListLevel - 1 },
                "Outdent List Item");
            result = new DocumentBodyEditorActionResult(
                new DocumentTextPosition(caret.BlockIndex, caretOffset),
                DocumentBodyEditorTransition.OutdentListItem);
            return true;
        }

        if (trackChanges)
        {
            var owningParagraphIndex = direction == DocumentBodyDeleteDirection.Backward
                ? caret.BlockIndex - 1
                : caret.BlockIndex;
            if (!_session.TryDeleteBodyParagraphBoundaryAsRevision(
                    owningParagraphIndex,
                    caret,
                    out var trackedResult))
            {
                return false;
            }

            result = new DocumentBodyEditorActionResult(
                trackedResult.Caret,
                direction == DocumentBodyDeleteDirection.Backward
                    ? DocumentBodyEditorTransition.MergeWithPreviousParagraph
                    : DocumentBodyEditorTransition.MergeWithNextParagraph);
            return true;
        }

        if (direction == DocumentBodyDeleteDirection.Forward && !mergeForwardBoundary)
            return false;

        var merged = direction == DocumentBodyDeleteDirection.Backward
            ? _session.TryMergeBodyParagraphWithPrevious(caret.BlockIndex, out var paragraphResult)
            : _session.TryMergeBodyParagraphWithNext(caret.BlockIndex, out paragraphResult);
        if (!merged)
            return false;

        result = new DocumentBodyEditorActionResult(
            paragraphResult.Caret,
            direction == DocumentBodyDeleteDirection.Backward
                ? DocumentBodyEditorTransition.MergeWithPreviousParagraph
                : DocumentBodyEditorTransition.MergeWithNextParagraph);
        return true;
    }

    public bool TryApplyParagraphBreak(
        DocumentTextRange selection,
        out DocumentBodyEditorActionResult result)
    {
        result = default;
        var startBlockIndex = selection.Normalize().Start.BlockIndex;
        if (!_session.TryInsertBodyParagraphBreak(selection, out var editResult))
            return false;

        result = new DocumentBodyEditorActionResult(
            editResult.Caret,
            editResult.Caret.BlockIndex == startBlockIndex
                ? DocumentBodyEditorTransition.ExitEmptyList
                : DocumentBodyEditorTransition.InsertParagraphBreak);
        return true;
    }

    private bool TryApplyTrackedTextInput(
        DocumentTextRange selection,
        DocumentBodyTextInput input,
        out DocumentTextEditResult result) =>
        input.InheritHyperlink
            ? _session.TryReplaceTrackedBodyText(
                selection,
                input.Text,
                input.Formatting,
                out result)
            : _session.TryReplaceTrackedBodyText(
                selection,
                input.Text,
                input.Formatting,
                input.Hyperlink,
                out result);

    private bool TryApplyUntrackedTextInput(
        DocumentTextRange selection,
        DocumentBodyTextInput input,
        out DocumentTextEditResult result) =>
        input.InheritHyperlink
            ? _session.TryReplaceBodyText(
                selection,
                input.Text,
                input.Formatting,
                out result)
            : _session.TryReplaceBodyText(
                selection,
                input.Text,
                input.Formatting,
                input.Hyperlink,
                out result);

    private bool TryGetCaretParagraph(
        DocumentTextPosition caret,
        out Paragraph paragraph,
        out int caretOffset)
    {
        paragraph = null!;
        caretOffset = 0;
        if (caret.BlockIndex < 0
            || caret.BlockIndex >= _session.Document.Blocks.Count
            || _session.Document.Blocks[caret.BlockIndex] is not Paragraph target)
        {
            return false;
        }

        paragraph = target;
        caretOffset = Math.Clamp(caret.Offset, 0, paragraph.PlainText.Length);
        return true;
    }
}

using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Editing;

public sealed partial class DocumentView
{
    // Incremented by document load, command changes and externally refreshed model mutations.
    private long _clipboardModelGeneration;

    private sealed record ClipboardSelectionStamp(
        DocumentView Owner,
        TextDocument Document,
        long ModelGeneration,
        bool UndoGroupWasOpen,
        TextCaretState Caret,
        (int TableBlock, int Row, int Col)? CellBlockAnchor,
        (int TableBlock, int Row, int Col)? CellBlockFocus,
        string SelectedText);

    internal object CaptureClipboardSelectionStamp()
    {
        var caret = CaptureTextCaretState();
        // Freeze the child path too: a stamp must not alias a later path update.
        caret = caret with
        {
            GroupChildSelection = caret.GroupChildSelection is { } child
                ? child with { ChildPath = child.ChildPath.ToArray() } : null,
        };
        return new ClipboardSelectionStamp(this, _doc, _clipboardModelGeneration, _bus.IsUndoGroupOpen, caret,
            _cellBlockAnchor, _cellBlockFocus, SelectedText);
    }

    internal bool MatchesClipboardSelectionStamp(object stamp) =>
        stamp is ClipboardSelectionStamp captured
        && ReferenceEquals(captured.Owner, this)
        && ReferenceEquals(captured.Document, _doc)
        && captured.ModelGeneration == _clipboardModelGeneration
        && !captured.UndoGroupWasOpen && !_bus.IsUndoGroupOpen
        && captured.CellBlockAnchor == _cellBlockAnchor
        && captured.CellBlockFocus == _cellBlockFocus
        && string.Equals(captured.SelectedText, SelectedText, StringComparison.Ordinal)
        && SameClipboardCaret(captured.Caret, CaptureTextCaretState());

    private static bool SameClipboardCaret(TextCaretState left, TextCaretState right)
    {
        // Record equality alone compares IReadOnlyList fields by reference. Normalize those
        // fields before comparing scalar addresses, then compare their frozen contents.
        static TextCaretState Scalars(TextCaretState state) => state with
        {
            ShapeChildPath = null,
            FloatingSelections = Array.Empty<(int BlockIndex, int RunIndex, string Kind)>(),
            GroupChildSelection = state.GroupChildSelection is { } child
                ? child with { ChildPath = Array.Empty<int>() } : null,
        };
        return Scalars(left) == Scalars(right)
            && SamePath(left.ShapeChildPath, right.ShapeChildPath)
            && SamePath(left.GroupChildSelection?.ChildPath, right.GroupChildSelection?.ChildPath)
            && left.FloatingSelections.SequenceEqual(right.FloatingSelections);
    }

    private static bool SamePath(IReadOnlyList<int>? left, IReadOnlyList<int>? right) =>
        left is null ? right is null : right is not null && left.SequenceEqual(right);
}

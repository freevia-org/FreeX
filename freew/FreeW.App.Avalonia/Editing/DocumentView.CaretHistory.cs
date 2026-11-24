using Avalonia;
using Avalonia.Input;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Editing;

public sealed partial class DocumentView
{
    private bool TextHistoryStateRestored { get; set; }
    private readonly HashSet<IPointer> _textHistoryPointersToRelease = [];

    private sealed record TextCaretState(
        DocPosition BodyCaret,
        DocPosition? BodyAnchor,
        (int TableBlock, int Row, int Col, int ParaIdx, int Offset)? CellCaret,
        (int TableBlock, int Row, int Col, int ParaIdx, int Offset)? CellAnchor,
        (HfTarget Target, int Offset)? HeaderFooterCaret,
        (HfTarget Target, int Offset)? HeaderFooterAnchor,
        (int BlockIndex, int RunIndex, int TextParagraphIndex, int TextRunIndex, int Offset)? ShapeCaret,
        (int BlockIndex, int RunIndex, int TextParagraphIndex, int TextRunIndex, int Offset)? ShapeAnchor,
        IReadOnlyList<int>? ShapeChildPath,
        (int BlockIndex, int RunIndex, string Kind, Rect Rect)? FloatingSelection,
        (int BlockIndex, int RunIndex, string Kind, Rect Rect)? InlineSelection,
        FloatingGroupChildSelection? GroupChildSelection,
        IReadOnlyList<(int BlockIndex, int RunIndex, string Kind)> FloatingSelections);

    private TextCaretState CaptureTextCaretState() => new(
        _caret, _selectionAnchor, _cellCaret, _cellAnchor, _hfCaret, _hfSelectionAnchor,
        _shapeCaret, _shapeSelectionAnchor, _activeShapeTextChildPath?.ToArray(),
        _selectedFloating, _selectedInlineObject, _selectedFloatingGroupChild, _selectedFloatingObjects.ToArray());

    private void RestoreTextCaretState(TextCaretState state)
    {
        // A user may select a table or drawing after the edit, then undo it. Those
        // transient modes must not continue to outrank the restored text story.
        _cellBlockAnchor = null;
        _cellBlockFocus = null;
        if (_bodyDragPointer is { } bodyPointer) _textHistoryPointersToRelease.Add(bodyPointer);
        if (_hfSelectionDragPointer is { } headerFooterPointer) _textHistoryPointersToRelease.Add(headerFooterPointer);
        if (_shapeTextSelectionDragState?.Pointer is { } shapePointer) _textHistoryPointersToRelease.Add(shapePointer);
        _bodyDragPending = false;
        _bodyDragActive = false;
        _bodyDragSourceSelection = null;
        _bodyDragPointer = null;
        _hfSelectionDragActive = false;
        _hfSelectionDragPointer = null;
        _shapeTextSelectionDragState = null;
        _floatingDrag.Reset();
        _shapeEditPointsTarget = null;
        _caret = state.BodyCaret;
        _selectionAnchor = state.BodyAnchor;
        _cellCaret = state.CellCaret;
        _cellAnchor = state.CellAnchor;
        _hfCaret = state.HeaderFooterCaret;
        _hfSelectionAnchor = state.HeaderFooterAnchor;
        _shapeCaret = state.ShapeCaret;
        _shapeSelectionAnchor = state.ShapeAnchor;
        _activeShapeTextChildPath = state.ShapeChildPath;
        _selectedFloating = state.FloatingSelection;
        _selectedInlineObject = state.InlineSelection;
        _selectedFloatingGroupChild = state.GroupChildSelection;
        _selectedFloatingObjects.Clear();
        _selectedFloatingObjects.AddRange(state.FloatingSelections);
        TextHistoryStateRestored = true;
        InvalidateLayoutAndVisual();
    }

    private void PublishTextHistorySelection()
    {
        // Capture-loss and selection observers can throw. Run them after the command
        // bus has completed its history transition, so an observer cannot make a
        // successful model revert appear to be a failed source command.
        var pointers = _textHistoryPointersToRelease.ToArray();
        _textHistoryPointersToRelease.Clear();
        foreach (var pointer in pointers)
            pointer.Capture(null);
        RaiseFloatingSelectionChangedIfIdentityChanged();
    }

    private void ExecuteTextEditWithCaretHistory(IDocumentCommand command, Action placeEditedCaret)
    {
        _bus.Execute(new CaretHistoryCommand(this, command, CaptureTextCaretState(), placeEditedCaret));
        CaretMoved?.Invoke();
    }

    // Restore the story and both selection endpoints before the bus publishes Changed.
    // Observers (including the shell and accessibility peer) must see one coherent edit.
    private sealed class CaretHistoryCommand(
        DocumentView owner,
        IDocumentCommand command,
        TextCaretState before,
        Action placeEditedCaret) : IDocumentCommand
    {
        private TextCaretState? _after;

        public string Label => command.Label;
        public int EstimatedBytes => command.EstimatedBytes + 256;
        public DocumentCommandMutationKind MutationKind => command.MutationKind;
        public bool HasEffect(IDocumentCommandContext context) => command.HasEffect(context);

        public void Apply(IDocumentCommandContext context)
        {
            command.Apply(context);
            if (_after is { } after)
            {
                owner.RestoreTextCaretState(after);
                return;
            }
            owner.InvalidateLayoutAndVisual();
            placeEditedCaret();
            _after = owner.CaptureTextCaretState();
        }

        public void Revert(IDocumentCommandContext context)
        {
            command.Revert(context);
            owner.RestoreTextCaretState(before);
        }
    }
}

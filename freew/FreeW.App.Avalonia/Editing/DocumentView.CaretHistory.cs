using Avalonia;
using Avalonia.Input;
using FreeW.Core.Model;
using FreeW.App.Presentation.Editing;

namespace FreeW.App.Avalonia.Editing;

public sealed partial class DocumentView
{
    private bool TextHistoryStateRestored { get; set; }
    private readonly HashSet<IPointer> _textHistoryPointersToRelease = [];
    private bool _textGestureActive;
    private double _textGestureLayoutWidth = FallbackWidth;

    private void RunTextGesture(string label, Action action) => RunTextGesture(label, () =>
    {
        action();
        return true;
    });

    private T RunTextGesture<T>(string label, Func<T> action)
    {
        if (_textGestureActive)
            return action();

        var before = CaptureTextCaretState();
        var pendingFormatting = _pendingRunFmt;
        _textGestureLayoutWidth = _laidOutWidth > 0 && double.IsFinite(_laidOutWidth)
            ? _laidOutWidth
            : Bounds.Width > 0 && double.IsFinite(Bounds.Width) ? Bounds.Width : FallbackWidth;
        using var scope = _bus.BeginCommandScope(() =>
        {
            // Legacy placement helpers may consult placed glyphs after each source edit.
            // Refresh model-derived caches now, while public notifications stay deferred.
            RebindHeaderFooterCaretOwner();
            ClearBitmapCache();
            InvalidateLayoutAndVisual();
        });
        _textGestureActive = true;
        try
        {
            var result = action();
            var after = CaptureTextCaretState();
            // Changed observers may initiate another gesture. The source scope closes
            // before it publishes, so reentrant edits must start their own scope too.
            _textGestureActive = false;
            scope.Commit(label, command => StateRestoringDocumentCommand<TextCaretState>.FromApplied(
                command, before, after, RestoreTextCaretState, estimatedStateBytes: 256));
            return result;
        }
        catch (Exception exception)
        {
            if (!scope.IsCommitted)
            {
                IReadOnlyList<Exception> failures = scope.IsCompleted ? [] : scope.Rollback();
                RestoreTextCaretState(before);
                _pendingRunFmt = pendingFormatting;
                try { PublishTextHistorySelection(); }
                finally { CaretMoved?.Invoke(); }
                if (failures.Count > 0)
                    throw new AggregateException(new[] { exception }.Concat(failures));
            }
            throw;
        }
        finally
        {
            _textGestureActive = false;
            if (scope.IsCommitted)
                CaretMoved?.Invoke();
        }
    }

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
        // Restore the story and both selection endpoints before the bus publishes Changed.
        // Observer notifications remain in the outer host history path.
        _bus.Execute(new StateRestoringDocumentCommand<TextCaretState>(
            command, CaptureTextCaretState(), CaptureTextCaretState, RestoreTextCaretState,
            () =>
            {
                InvalidateLayoutAndVisual();
                placeEditedCaret();
            }, estimatedStateBytes: 256));
        CaretMoved?.Invoke();
    }
}

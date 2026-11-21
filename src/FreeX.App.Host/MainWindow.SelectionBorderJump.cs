using FreeX.App.Presentation.GridInteraction;

namespace FreeX.App.Host;

// Excel: double-clicking an edge of the active cell's border jumps to the edge of the data in that
// direction, exactly like Ctrl+Arrow; Shift+double-click extends the selection there.
public partial class MainWindow
{
    internal void OnSelectionBorderDoubleClicked(GridSelectionBorderEdge edge, bool extend)
    {
        if (SheetGrid.SelectedRange is not { } range || edge == GridSelectionBorderEdge.None)
            return;

        var sheet = _workbook.GetSheet(_currentSheetId);
        var current = extend && _selectionCursor.HasValue
            ? _selectionCursor.Value
            : SheetGrid.ActiveCell ?? range.Start;
        var target = GridSelectionMovePlanner.FindBorderJumpTarget(sheet, current, edge);

        if (extend && _selectionAnchor.HasValue)
            ExtendSelection(_selectionAnchor.Value, target);
        else
            SetActiveCell(target);

        EnsureCellVisible(target);
    }
}

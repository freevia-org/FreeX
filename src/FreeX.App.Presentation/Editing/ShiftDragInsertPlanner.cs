using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Editing;

/// <summary>
/// Excel's Shift+drag of a selection border: instead of overwriting the destination, the moved
/// cells are inserted there and the existing cells shift out of the way (down when the drag is
/// mostly vertical, right when it is mostly horizontal).
/// </summary>
public static class ShiftDragInsertPlanner
{
    public static KeyboardInsertDeleteDialogChoice ChooseShift(GridRange source, GridRange target)
    {
        var rowDelta = Math.Abs((long)target.Start.Row - source.Start.Row);
        var colDelta = Math.Abs((long)target.Start.Col - source.Start.Col);
        return colDelta > rowDelta
            ? KeyboardInsertDeleteDialogChoice.ShiftRight
            : KeyboardInsertDeleteDialogChoice.ShiftDown;
    }

    public static IWorkbookCommand? CreateCommand(Workbook workbook, SheetId sheetId, GridRange source, GridRange target)
    {
        if (source == target || workbook.GetSheet(sheetId) is not { } sheet)
            return null;

        var cells = new List<(CellAddress Source, Cell Cell)>();
        for (var r = source.Start.Row; r <= source.End.Row; r++)
        {
            for (var c = source.Start.Col; c <= source.End.Col; c++)
            {
                var address = new CellAddress(sheetId, r, c);
                cells.Add((address, sheet.GetCell(address)?.Clone() ?? Cell.FromValue(BlankValue.Instance)));
            }
        }

        return InsertCopiedCellsPlanner.CreateCommand(
            workbook, sheetId, source, cells, target, ChooseShift(source, target), isCut: true);
    }
}

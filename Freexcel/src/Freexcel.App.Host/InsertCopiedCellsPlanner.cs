using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static class InsertCopiedCellsPlanner
{
    public static IWorkbookCommand CreateCommand(
        Workbook workbook,
        SheetId sheetId,
        GridRange sourceRange,
        IReadOnlyList<(CellAddress Source, Cell Cell)> cells,
        GridRange destinationRange,
        KeyboardInsertDeleteDialogChoice choice)
    {
        var insertRange = CreateInsertRange(sheetId, destinationRange.Start, sourceRange);
        IWorkbookCommand insertCommand = choice switch
        {
            KeyboardInsertDeleteDialogChoice.ShiftDown => new InsertCellsCommand(
                sheetId,
                insertRange,
                InsertCellsShiftDirection.Down),
            KeyboardInsertDeleteDialogChoice.EntireRow => new InsertRowsCommand(
                sheetId,
                destinationRange.Start.Row,
                sourceRange.RowCount),
            KeyboardInsertDeleteDialogChoice.EntireColumn => new InsertColumnsCommand(
                sheetId,
                destinationRange.Start.Col,
                sourceRange.ColCount),
            _ => new InsertCellsCommand(
                sheetId,
                insertRange,
                InsertCellsShiftDirection.Right)
        };

        var pasteCommand = PasteCommandFactory.CreateInternalPasteCommand(
            workbook,
            sheetId,
            sourceRange,
            cells,
            destinationRange.Start,
            PasteCellsMode.All,
            default);

        return new CompositeWorkbookCommand("Insert Copied Cells", [insertCommand, pasteCommand]);
    }

    private static GridRange CreateInsertRange(SheetId sheetId, CellAddress destination, GridRange sourceRange)
    {
        var end = new CellAddress(
            sheetId,
            destination.Row + sourceRange.RowCount - 1,
            destination.Col + sourceRange.ColCount - 1);
        return new GridRange(destination, end);
    }
}

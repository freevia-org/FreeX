using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Editing;

public static class InsertCopiedCellsPlanner
{
    /// <summary>
    /// Builds the composite command for the "Insert Copied Cells"/"Insert Cut Cells" context-menu
    /// action: shift the existing destination cells out of the way, then paste the captured
    /// clipboard cells into the freed space.
    /// </summary>
    /// <param name="isCut">
    /// <c>true</c> when the clipboard content was cut (not copied). Excel's "Insert Cut Cells" MOVES
    /// the data: after the shifted-in paste lands, the original source range must be cleared or the
    /// data is silently duplicated instead of moved (R29-undo-redo-remaining-deep-1). Defaults to
    /// <c>false</c> (plain copy semantics, matching the pre-existing behavior) for callers that don't
    /// yet distinguish cut from copy.
    /// </param>
    public static IWorkbookCommand CreateCommand(
        Workbook workbook,
        SheetId sheetId,
        GridRange sourceRange,
        IReadOnlyList<(CellAddress Source, Cell Cell)> cells,
        GridRange destinationRange,
        KeyboardInsertDeleteDialogChoice choice,
        bool isCut = false)
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

        // Mirror ClipboardPastePlanner.ShouldClearCutSourceAfterPaste's guard (used by the ordinary
        // Ctrl+V-after-Cut path): only clear when the source doesn't overlap where the cut cells just
        // landed, so a self-overlapping insert never wipes out the paste it just made.
        if (ClipboardPastePlanner.ShouldClearCutSourceAfterPaste(
                isCut, sourceRange, destinationRange, PasteMode.All, default, keepColumnWidths: false))
        {
            // The clear runs BEFORE the insert/paste (not after, unlike the ordinary paste-after-cut
            // composite) because an EntireRow/EntireColumn insert shifts every cell at/after the
            // insertion line -- including the source range itself when it sits at or past that line.
            // Clearing first always targets the pre-shift (original) coordinates; clearing last would
            // target stale coordinates once the shift has moved the real data elsewhere.
            return new CompositeWorkbookCommand(
                "Insert Cut Cells",
                [new ClearContentsCommand(sourceRange.Start.Sheet, sourceRange), insertCommand, pasteCommand]);
        }

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

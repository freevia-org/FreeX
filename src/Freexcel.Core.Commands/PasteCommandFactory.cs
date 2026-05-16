using Freexcel.Core.Formula;
using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public enum PasteCellsMode
{
    All,
    Values,
    Formulas,
    Formats
}

public static class PasteCommandFactory
{
    public static IWorkbookCommand CreateExternalTextPasteCommand(
        SheetId targetSheetId,
        CellAddress destination,
        IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var edits = new List<(CellAddress Address, Cell Cell)>();
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            for (var colIndex = 0; colIndex < rows[rowIndex].Count; colIndex++)
            {
                var address = new CellAddress(
                    targetSheetId,
                    destination.Row + (uint)rowIndex,
                    destination.Col + (uint)colIndex);
                edits.Add((address, Cell.FromValue(ParseClipboardValue(rows[rowIndex][colIndex]))));
            }
        }

        return new EditCellsCommand(targetSheetId, edits);
    }

    public static IWorkbookCommand CreateInternalPasteCommand(
        Workbook workbook,
        SheetId targetSheetId,
        GridRange sourceRange,
        IReadOnlyList<(CellAddress Source, Cell Cell)> sourceCells,
        CellAddress destination,
        PasteCellsMode mode,
        PasteSpecialOptions options)
    {
        if (options.Transpose || options.Operation != PasteSpecialOperation.None)
        {
            var specialCells = sourceCells
                .Select(c => (c.Source, mode == PasteCellsMode.Values || options.Operation != PasteSpecialOperation.None
                    ? Cell.FromValue(c.Cell.Value)
                    : c.Cell.Clone()))
                .ToList();

            return new PasteSpecialCellsCommand(
                targetSheetId,
                sourceRange,
                specialCells,
                destination,
                options);
        }

        var rowDelta = (int)destination.Row - (int)sourceRange.Start.Row;
        var colDelta = (int)destination.Col - (int)sourceRange.Start.Col;
        var pasteOp = new PasteOffsetOp(rowDelta, colDelta);
        var activeSheetName = workbook.GetSheet(targetSheetId)?.Name ?? "";

        if (mode == PasteCellsMode.Formats)
        {
            return new PasteFormatsCommand(
                targetSheetId,
                sourceCells.Select(c => (Shift(c.Source, targetSheetId, rowDelta, colDelta), c.Cell.StyleId)).ToList());
        }

        var edits = new List<(CellAddress Address, Cell Cell)>(sourceCells.Count);
        foreach (var (source, sourceCell) in sourceCells)
        {
            var destinationAddress = Shift(source, targetSheetId, rowDelta, colDelta);
            var pastedCell = BuildPastedCell(sourceCell, mode, pasteOp, activeSheetName, rowDelta, colDelta);
            edits.Add((destinationAddress, pastedCell));
        }

        return mode == PasteCellsMode.All
            ? new PasteCellsCommand(targetSheetId, edits)
            : new EditCellsCommand(targetSheetId, edits);
    }

    private static Cell BuildPastedCell(
        Cell sourceCell,
        PasteCellsMode mode,
        PasteOffsetOp pasteOp,
        string activeSheetName,
        int rowDelta,
        int colDelta)
    {
        if (mode == PasteCellsMode.Values)
            return Cell.FromValue(sourceCell.Value);

        var pastedCell = sourceCell.Clone();
        if (pastedCell.FormulaText is not null && (rowDelta != 0 || colDelta != 0))
        {
            pastedCell.FormulaText =
                FormulaRewriter.Rewrite(pastedCell.FormulaText, pasteOp, activeSheetName)
                ?? pastedCell.FormulaText;
        }

        if (mode == PasteCellsMode.Formulas && !pastedCell.HasFormula)
            return Cell.FromValue(sourceCell.Value);

        return pastedCell;
    }

    private static CellAddress Shift(CellAddress source, SheetId targetSheetId, int rowDelta, int colDelta)
    {
        int newRow = (int)source.Row + rowDelta;
        int newCol = (int)source.Col + colDelta;
        if (newRow < 1) newRow = 1;
        if (newCol < 1) newCol = 1;
        return new(targetSheetId, (uint)newRow, (uint)newCol);
    }

    private static ScalarValue ParseClipboardValue(string text) =>
        double.TryParse(
            text,
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.CurrentCulture,
            out var number)
            ? new NumberValue(number)
            : new TextValue(text);
}

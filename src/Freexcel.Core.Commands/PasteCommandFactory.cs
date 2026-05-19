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
        var targetSheet = workbook.GetSheet(targetSheetId);
        var activeSheetName = targetSheet?.Name ?? "";

        if (options.Transpose || options.Operation != PasteSpecialOperation.None)
        {
            var specialCells = new List<(CellAddress Source, Cell Cell)>(sourceCells.Count);
            foreach (var (source, sourceCell) in sourceCells)
            {
                Cell pastedCell;
                if (options.Operation != PasteSpecialOperation.None)
                {
                    pastedCell = Cell.FromValue(sourceCell.Value);
                }
                else if (options.Transpose && (mode == PasteCellsMode.All || mode == PasteCellsMode.Values || mode == PasteCellsMode.Formulas))
                {
                    var destinationAddress = TransposeDestination(sourceRange, source, targetSheetId, destination);
                    var destinationStyle = GetDestinationStyle(targetSheet, destinationAddress);
                    var transposedRowDelta = (int)destinationAddress.Row - (int)source.Row;
                    var transposedColDelta = (int)destinationAddress.Col - (int)source.Col;
                    var transposedPasteOp = new PasteOffsetOp(transposedRowDelta, transposedColDelta);
                    pastedCell = BuildPastedCell(
                        sourceCell,
                        mode,
                        transposedPasteOp,
                        activeSheetName,
                        transposedRowDelta,
                        transposedColDelta,
                        destinationStyle);
                }
                else
                {
                    pastedCell = mode == PasteCellsMode.Values
                        ? Cell.FromValue(sourceCell.Value)
                        : sourceCell.Clone();
                }

                specialCells.Add((source, pastedCell));
            }

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
            var destinationStyle = GetDestinationStyle(targetSheet, destinationAddress);
            var pastedCell = BuildPastedCell(
                sourceCell,
                mode,
                pasteOp,
                activeSheetName,
                rowDelta,
                colDelta,
                destinationStyle);
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
        int colDelta,
        StyleId destinationStyle)
    {
        if (mode == PasteCellsMode.Values)
        {
            var valueCell = Cell.FromValue(sourceCell.Value);
            valueCell.StyleId = destinationStyle;
            return valueCell;
        }

        var pastedCell = sourceCell.Clone();
        if (pastedCell.FormulaText is not null && (rowDelta != 0 || colDelta != 0))
        {
            pastedCell.FormulaText =
                FormulaRewriter.Rewrite(pastedCell.FormulaText, pasteOp, activeSheetName)
                ?? pastedCell.FormulaText;
        }

        if (mode == PasteCellsMode.Formulas && !pastedCell.HasFormula)
        {
            var valueCell = Cell.FromValue(sourceCell.Value);
            valueCell.StyleId = destinationStyle;
            return valueCell;
        }

        if (mode == PasteCellsMode.Formulas)
            pastedCell.StyleId = destinationStyle;

        return pastedCell;
    }

    private static CellAddress TransposeDestination(
        GridRange sourceRange,
        CellAddress source,
        SheetId targetSheetId,
        CellAddress destination)
    {
        var rowOffset = source.Row - sourceRange.Start.Row;
        var colOffset = source.Col - sourceRange.Start.Col;
        return new CellAddress(targetSheetId, destination.Row + colOffset, destination.Col + rowOffset);
    }

    private static StyleId GetDestinationStyle(Sheet? targetSheet, CellAddress destinationAddress) =>
        targetSheet?.GetCell(destinationAddress)?.StyleId
        ?? targetSheet?.GetStyleOnly(destinationAddress.Row, destinationAddress.Col)
        ?? StyleId.Default;

    private static CellAddress Shift(CellAddress source, SheetId targetSheetId, int rowDelta, int colDelta)
    {
        int newRow = (int)source.Row + rowDelta;
        int newCol = (int)source.Col + colDelta;
        if (newRow < 1) newRow = 1;
        if (newCol < 1) newCol = 1;
        if (newRow > (int)CellAddress.MaxRow) newRow = (int)CellAddress.MaxRow;
        if (newCol > (int)CellAddress.MaxCol) newCol = (int)CellAddress.MaxCol;
        return new(targetSheetId, (uint)newRow, (uint)newCol);
    }

    private static ScalarValue ParseClipboardValue(string text) =>
        double.TryParse(
            text,
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture,
            out var number)
            ? new NumberValue(number)
            : new TextValue(text);
}

using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal static class PasteCommandCellFactory
{
    public static Cell BuildPastedCell(
        Workbook workbook,
        Cell sourceCell,
        PasteCellsMode mode,
        PasteSpecialContentKind contentKind,
        PasteOffsetOp pasteOp,
        string activeSheetName,
        int rowDelta,
        int colDelta,
        StyleId destinationStyle)
    {
        if (contentKind == PasteSpecialContentKind.ValuesAndNumberFormats)
        {
            var valueCell = Cell.FromValue(sourceCell.Value);
            valueCell.StyleId = MergeNumberFormat(workbook, destinationStyle, sourceCell.StyleId);
            return valueCell;
        }

        if (contentKind == PasteSpecialContentKind.ValuesAndSourceFormatting)
        {
            var valueCell = Cell.FromValue(sourceCell.Value);
            valueCell.StyleId = sourceCell.StyleId;
            return valueCell;
        }

        if (contentKind == PasteSpecialContentKind.FormulasAndNumberFormats)
        {
            var formulaCell = BuildFormulaOrValueCell(
                sourceCell,
                pasteOp,
                activeSheetName,
                rowDelta,
                colDelta,
                destinationStyle);
            formulaCell.StyleId = MergeNumberFormat(workbook, destinationStyle, sourceCell.StyleId);
            return formulaCell;
        }

        if (contentKind == PasteSpecialContentKind.AllExceptBorders)
        {
            var pastedCell = BuildAllCell(sourceCell, pasteOp, activeSheetName, rowDelta, colDelta);
            pastedCell.StyleId = MergeAllExceptBorders(workbook, sourceCell.StyleId, destinationStyle);
            return pastedCell;
        }

        if (mode == PasteCellsMode.Values)
        {
            var valueCell = Cell.FromValue(sourceCell.Value);
            valueCell.StyleId = destinationStyle;
            return valueCell;
        }

        if (mode == PasteCellsMode.Formulas)
            return BuildFormulaOrValueCell(sourceCell, pasteOp, activeSheetName, rowDelta, colDelta, destinationStyle);

        return BuildAllCell(sourceCell, pasteOp, activeSheetName, rowDelta, colDelta);
    }

    public static CellAddress TransposeDestination(
        GridRange sourceRange,
        CellAddress source,
        SheetId targetSheetId,
        CellAddress destination)
    {
        var rowOffset = source.Row - sourceRange.Start.Row;
        var colOffset = source.Col - sourceRange.Start.Col;
        return new CellAddress(targetSheetId, destination.Row + colOffset, destination.Col + rowOffset);
    }

    public static StyleId GetDestinationStyle(Sheet? targetSheet, CellAddress destinationAddress) =>
        targetSheet?.GetCell(destinationAddress)?.StyleId
        ?? targetSheet?.GetStyleOnly(destinationAddress.Row, destinationAddress.Col)
        ?? StyleId.Default;

    public static CellAddress Shift(CellAddress source, SheetId targetSheetId, int rowDelta, int colDelta)
    {
        int newRow = (int)source.Row + rowDelta;
        int newCol = (int)source.Col + colDelta;
        if (newRow < 1) newRow = 1;
        if (newCol < 1) newCol = 1;
        if (newRow > (int)CellAddress.MaxRow) newRow = (int)CellAddress.MaxRow;
        if (newCol > (int)CellAddress.MaxCol) newCol = (int)CellAddress.MaxCol;
        return new(targetSheetId, (uint)newRow, (uint)newCol);
    }

    private static Cell BuildFormulaOrValueCell(
        Cell sourceCell,
        PasteOffsetOp pasteOp,
        string activeSheetName,
        int rowDelta,
        int colDelta,
        StyleId destinationStyle)
    {
        var pastedCell = sourceCell.Clone();
        if (pastedCell.FormulaText is not null && (rowDelta != 0 || colDelta != 0))
        {
            pastedCell.FormulaText =
                FormulaRewriter.Rewrite(pastedCell.FormulaText, pasteOp, activeSheetName)
                ?? pastedCell.FormulaText;
        }

        if (!pastedCell.HasFormula)
        {
            var valueCell = Cell.FromValue(sourceCell.Value);
            valueCell.StyleId = destinationStyle;
            return valueCell;
        }

        pastedCell.StyleId = destinationStyle;

        return pastedCell;
    }

    private static Cell BuildAllCell(
        Cell sourceCell,
        PasteOffsetOp pasteOp,
        string activeSheetName,
        int rowDelta,
        int colDelta)
    {
        var pastedCell = sourceCell.Clone();
        if (pastedCell.FormulaText is not null && (rowDelta != 0 || colDelta != 0))
        {
            pastedCell.FormulaText =
                FormulaRewriter.Rewrite(pastedCell.FormulaText, pasteOp, activeSheetName)
                ?? pastedCell.FormulaText;
        }

        return pastedCell;
    }

    private static StyleId MergeNumberFormat(Workbook workbook, StyleId destinationStyleId, StyleId sourceStyleId)
    {
        var style = workbook.GetStyle(destinationStyleId).Clone();
        style.NumberFormat = workbook.GetStyle(sourceStyleId).NumberFormat;
        return workbook.RegisterStyle(style);
    }

    private static StyleId MergeAllExceptBorders(Workbook workbook, StyleId sourceStyleId, StyleId destinationStyleId)
    {
        var style = workbook.GetStyle(sourceStyleId).Clone();
        var destinationStyle = workbook.GetStyle(destinationStyleId);
        style.BorderTop = destinationStyle.BorderTop;
        style.BorderRight = destinationStyle.BorderRight;
        style.BorderBottom = destinationStyle.BorderBottom;
        style.BorderLeft = destinationStyle.BorderLeft;
        return workbook.RegisterStyle(style);
    }
}

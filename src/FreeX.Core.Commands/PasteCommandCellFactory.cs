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
        RewriteOperation pasteOp,
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
        if (!WorksheetBounds.TryOffset(destination, targetSheetId, colOffset, rowOffset, out var address))
            throw new ArgumentOutOfRangeException(nameof(destination), "Paste destination is outside the worksheet bounds.");

        return address;
    }

    public static StyleId GetDestinationStyle(Sheet? targetSheet, CellAddress destinationAddress) =>
        targetSheet?.GetCell(destinationAddress)?.StyleId
        ?? targetSheet?.GetStyleOnly(destinationAddress.Row, destinationAddress.Col)
        ?? StyleId.Default;

    public static CellAddress Shift(CellAddress source, SheetId targetSheetId, int rowDelta, int colDelta)
    {
        if (!WorksheetBounds.TryShift(source, targetSheetId, rowDelta, colDelta, out var address))
            throw new ArgumentOutOfRangeException(nameof(source), "Paste destination is outside the worksheet bounds.");

        return address;
    }

    private static Cell BuildFormulaOrValueCell(
        Cell sourceCell,
        RewriteOperation pasteOp,
        string activeSheetName,
        int rowDelta,
        int colDelta,
        StyleId destinationStyle)
    {
        var pastedCell = sourceCell.Clone();
        // A transpose op can move a formula's OTHER references even when the host cell's own
        // (rowDelta,colDelta) happens to be zero -- transposing swaps the block's shape, so a cell
        // that lands back on its own address can still hold sibling references that must be
        // axis-swapped. The rowDelta/colDelta shortcut below is only valid for a uniform-translation
        // PasteOffsetOp, where a zero delta means every reference in the formula is untouched too.
        if (pastedCell.FormulaText is not null && (pasteOp is PasteTransposeOp || rowDelta != 0 || colDelta != 0))
        {
            RowColumnShiftHelpers.SetFormulaTextPreservingArrayIdentity(
                pastedCell,
                FormulaRewriter.Rewrite(pastedCell.FormulaText, pasteOp, activeSheetName)
                    ?? pastedCell.FormulaText);
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
        RewriteOperation pasteOp,
        string activeSheetName,
        int rowDelta,
        int colDelta)
    {
        var pastedCell = sourceCell.Clone();
        // See BuildFormulaOrValueCell above: a transpose op must always attempt the rewrite, even
        // when this particular host cell's own delta is zero.
        if (pastedCell.FormulaText is not null && (pasteOp is PasteTransposeOp || rowDelta != 0 || colDelta != 0))
        {
            RowColumnShiftHelpers.SetFormulaTextPreservingArrayIdentity(
                pastedCell,
                FormulaRewriter.Rewrite(pastedCell.FormulaText, pasteOp, activeSheetName)
                    ?? pastedCell.FormulaText);
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
        style.BorderDiagonalDown = destinationStyle.BorderDiagonalDown;
        style.BorderDiagonalUp = destinationStyle.BorderDiagonalUp;
        return workbook.RegisterStyle(style);
    }
}

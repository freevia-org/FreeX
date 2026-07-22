using FreeX.Core.Model;

namespace FreeX.App.Services;

public static class AutoSumFormulaPlanner
{
    public static bool TryCreatePlan(Sheet? sheet, string functionName, GridRange selection, out AutoSumFormulaPlan plan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(functionName);

        if (TryGetSelectionTarget(sheet, selection, out var target, out var sumRange))
        {
            var formula = sumRange is { } explicitSumRange
                ? BuildFunctionFormula(functionName, explicitSumRange.Start.Col, explicitSumRange.Start.Row, explicitSumRange.End.Col, explicitSumRange.End.Row)
                : selection.CellCount > 1
                    ? BuildFunctionFormula(functionName, selection.Start.Col, selection.Start.Row, selection.End.Col, selection.End.Row)
                    : BuildFormula(sheet, functionName, target);
            plan = new AutoSumFormulaPlan(target, formula);
            return true;
        }

        plan = default;
        return false;
    }

    public static string BuildFormula(Sheet? sheet, string functionName, CellAddress address)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(functionName);

        if (sheet is null)
            return BuildFallbackFormula(functionName, address);

        var topRow = FindTopNumericRow(sheet, address);
        if (topRow == address.Row)
        {
            var leftCol = FindLeftNumericColumn(sheet, address);
            if (leftCol < address.Col)
                return BuildFunctionFormula(functionName, leftCol, address.Row, address.Col - 1, address.Row);
        }

        return topRow < address.Row
            ? BuildFunctionFormula(functionName, address.Col, topRow, address.Col, address.Row - 1)
            : BuildFallbackFormula(functionName, address);
    }

    private static string BuildFallbackFormula(string functionName, CellAddress address) =>
        BuildFunctionFormula(functionName, address.Col, Math.Max(1, address.Row - 1), address.Col, address.Row);

    private static uint FindTopNumericRow(Sheet sheet, CellAddress address)
    {
        var topRow = address.Row;
        while (topRow > 1)
        {
            var candidateRow = topRow - 1;
            if (sheet.GetValue(candidateRow, address.Col) is not NumberValue)
                break;

            // Excel stops AutoSum's upward scan at a pre-existing subtotal row instead of walking
            // through it and re-summing the same data twice: a cell whose own formula is itself an
            // aggregate (SUM/SUBTOTAL) becomes the boundary, matching Excel's "use the total above as
            // the edge" behavior rather than folding that total's source range into the new formula.
            if (IsAggregateFormulaCell(sheet.GetCell(candidateRow, address.Col)))
                break;

            topRow--;
        }

        return topRow;
    }

    private static bool IsAggregateFormulaCell(Cell? cell)
    {
        if (cell?.FormulaText is not { } formulaText)
            return false;

        var trimmed = formulaText.TrimStart();
        return trimmed.StartsWith("SUM(", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("SUBTOTAL(", StringComparison.OrdinalIgnoreCase);
    }

    private static uint FindLeftNumericColumn(Sheet sheet, CellAddress address)
    {
        var leftCol = address.Col;
        while (leftCol > 1)
        {
            var candidateCol = leftCol - 1;
            if (sheet.GetValue(address.Row, candidateCol) is not NumberValue)
                break;

            // Mirror FindTopNumericRow: stop the leftward scan at a pre-existing aggregate
            // formula cell (SUM/SUBTOTAL) instead of walking through it and re-summing the same
            // data it already totals, matching Excel's "use the total as the edge" behavior on
            // the horizontal axis too.
            if (IsAggregateFormulaCell(sheet.GetCell(address.Row, candidateCol)))
                break;

            leftCol--;
        }

        return leftCol;
    }

    private static string BuildFunctionFormula(string functionName, uint startCol, uint startRow, uint endCol, uint endRow) =>
        $"{functionName}({FormatRange(startCol, startRow, endCol, endRow)})";

    private static string FormatRange(uint startCol, uint startRow, uint endCol, uint endRow) =>
        $"{CellAddress.NumberToColumnName(startCol)}{startRow}:{CellAddress.NumberToColumnName(endCol)}{endRow}";

    private static bool TryGetSelectionTarget(Sheet? sheet, GridRange selection, out CellAddress target, out GridRange? sumRange)
    {
        sumRange = null;

        if (selection.CellCount == 1)
        {
            target = selection.Start;
            return true;
        }

        if (selection.RowCount == 1)
        {
            // Excel's classic AutoSum workflow: select the numbers plus a trailing blank cell
            // (e.g. A1:D1 with D1 blank) and Alt+= fills the SUM directly into that blank cell
            // instead of appending a new formula past the selection's edge.
            if (sheet is not null && TryGetBlankTrailingSumRange(
                    sheet,
                    selection,
                    new CellAddress(selection.Start.Sheet, selection.Start.Row, selection.End.Col - 1),
                    out var horizontalSumRange))
            {
                target = selection.End;
                sumRange = horizontalSumRange;
                return true;
            }

            if (selection.End.Col >= CellAddress.MaxCol)
            {
                target = default;
                return false;
            }

            target = new CellAddress(selection.Start.Sheet, selection.Start.Row, selection.End.Col + 1);
            return true;
        }

        if (selection.ColCount == 1 && sheet is not null && TryGetBlankTrailingSumRange(
                sheet,
                selection,
                new CellAddress(selection.Start.Sheet, selection.End.Row - 1, selection.Start.Col),
                out var verticalSumRange))
        {
            target = selection.End;
            sumRange = verticalSumRange;
            return true;
        }

        if (selection.End.Row >= CellAddress.MaxRow)
        {
            target = default;
            return false;
        }

        target = new CellAddress(selection.Start.Sheet, selection.End.Row + 1, selection.Start.Col);
        return true;
    }

    /// <summary>
    /// When the selection's own trailing cell (its last cell in the direction of the selection)
    /// is blank and the rest of the selection actually contains numbers, Excel treats that blank
    /// cell as the AutoSum destination rather than appending a formula outside the selection. A
    /// wholly-blank selection (no numbers anywhere) does not qualify -- that keeps falling
    /// through to the append-past-the-selection behavior, matching Excel's own AutoSum, which
    /// only substitutes into the trailing blank when there is something to sum.
    /// </summary>
    private static bool TryGetBlankTrailingSumRange(Sheet sheet, GridRange selection, CellAddress candidateSumEnd, out GridRange sumRange)
    {
        sumRange = default;

        if (sheet.GetValue(selection.End) is not BlankValue)
            return false;

        var candidateRange = new GridRange(selection.Start, candidateSumEnd);
        var containsNumber = false;
        foreach (var cell in candidateRange.AllCells())
        {
            if (sheet.GetValue(cell) is NumberValue)
            {
                containsNumber = true;
                break;
            }
        }

        if (!containsNumber)
            return false;

        sumRange = candidateRange;
        return true;
    }
}

public readonly record struct AutoSumFormulaPlan(CellAddress Target, string Formula);

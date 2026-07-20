using FreeX.Core.Model;

namespace FreeX.App.Services;

public static class AutoSumFormulaPlanner
{
    public static bool TryCreatePlan(Sheet? sheet, string functionName, GridRange selection, out AutoSumFormulaPlan plan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(functionName);

        if (TryGetSelectionTarget(selection, out var target))
        {
            var formula = selection.CellCount > 1
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
        while (leftCol > 1 && sheet.GetValue(address.Row, leftCol - 1) is NumberValue)
            leftCol--;

        return leftCol;
    }

    private static string BuildFunctionFormula(string functionName, uint startCol, uint startRow, uint endCol, uint endRow) =>
        $"{functionName}({FormatRange(startCol, startRow, endCol, endRow)})";

    private static string FormatRange(uint startCol, uint startRow, uint endCol, uint endRow) =>
        $"{CellAddress.NumberToColumnName(startCol)}{startRow}:{CellAddress.NumberToColumnName(endCol)}{endRow}";

    private static bool TryGetSelectionTarget(GridRange selection, out CellAddress target)
    {
        if (selection.CellCount == 1)
        {
            target = selection.Start;
            return true;
        }

        if (selection.RowCount == 1)
        {
            if (selection.End.Col >= CellAddress.MaxCol)
            {
                target = default;
                return false;
            }

            target = new CellAddress(selection.Start.Sheet, selection.Start.Row, selection.End.Col + 1);
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
}

public readonly record struct AutoSumFormulaPlan(CellAddress Target, string Formula);

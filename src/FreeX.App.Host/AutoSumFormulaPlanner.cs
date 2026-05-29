using FreeX.Core.Model;

namespace FreeX.App.Host;

public static class AutoSumFormulaPlanner
{
    public static string BuildFormula(Sheet? sheet, string functionName, CellAddress address)
    {
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
        while (topRow > 1 && sheet.GetValue(topRow - 1, address.Col) is NumberValue)
            topRow--;

        return topRow;
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
}

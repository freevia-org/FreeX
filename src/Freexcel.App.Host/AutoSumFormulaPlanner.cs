using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static class AutoSumFormulaPlanner
{
    public static string BuildFormula(Sheet? sheet, string functionName, CellAddress address)
    {
        if (sheet is null)
            return BuildFallbackFormula(functionName, address);

        var topRow = address.Row;
        while (topRow > 1 && sheet.GetValue(topRow - 1, address.Col) is NumberValue)
            topRow--;

        if (topRow == address.Row)
        {
            var leftCol = address.Col;
            while (leftCol > 1 && sheet.GetValue(address.Row, leftCol - 1) is NumberValue)
                leftCol--;

            if (leftCol < address.Col)
            {
                var leftRangeRef = $"{CellAddress.NumberToColumnName(leftCol)}{address.Row}:{CellAddress.NumberToColumnName(address.Col - 1)}{address.Row}";
                return $"{functionName}({leftRangeRef})";
            }
        }

        var range = topRow < address.Row
            ? $"{CellAddress.NumberToColumnName(address.Col)}{topRow}:{CellAddress.NumberToColumnName(address.Col)}{address.Row - 1}"
            : $"{CellAddress.NumberToColumnName(address.Col)}{Math.Max(1, address.Row - 1)}:{CellAddress.NumberToColumnName(address.Col)}{address.Row}";
        return $"{functionName}({range})";
    }

    private static string BuildFallbackFormula(string functionName, CellAddress address) =>
        $"{functionName}({CellAddress.NumberToColumnName(address.Col)}{Math.Max(1, address.Row - 1)}:{CellAddress.NumberToColumnName(address.Col)}{address.Row})";
}

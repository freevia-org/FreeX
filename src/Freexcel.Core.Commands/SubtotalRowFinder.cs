using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

internal static class SubtotalRowFinder
{
    public static List<uint> Find(Sheet sheet, SheetId sheetId, GridRange range)
    {
        var rows = new List<uint>();
        for (uint row = range.Start.Row; row <= range.End.Row; row++)
        {
            for (uint col = range.Start.Col; col <= range.End.Col; col++)
            {
                var formula = sheet.GetCell(new CellAddress(sheetId, row, col))?.FormulaText;
                if (formula is not null &&
                    formula.TrimStart().StartsWith("SUBTOTAL(", StringComparison.OrdinalIgnoreCase))
                {
                    rows.Add(row);
                    break;
                }
            }
        }

        return rows;
    }
}

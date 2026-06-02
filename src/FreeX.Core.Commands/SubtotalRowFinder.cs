using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal static class SubtotalRowFinder
{
    private const string SubtotalPrefix = "SUBTOTAL(";

    public static List<uint> Find(Sheet sheet, SheetId sheetId, GridRange range)
    {
        if (!sheet.HasFormulas)
            return [];

        return range.CellCount <= sheet.FormulaCellCount
            ? FindByRangeScan(sheet, sheetId, range)
            : FindByFormulaIndex(sheet, range);
    }

    private static List<uint> FindByRangeScan(Sheet sheet, SheetId sheetId, GridRange range)
    {
        var rows = new List<uint>();
        for (uint row = range.Start.Row; row <= range.End.Row; row++)
        {
            for (uint col = range.Start.Col; col <= range.End.Col; col++)
            {
                var formula = sheet.GetCell(new CellAddress(sheetId, row, col))?.FormulaText;
                if (formula is not null && IsSubtotalFormula(formula))
                {
                    rows.Add(row);
                    break;
                }
            }
        }

        return rows;
    }

    private static List<uint> FindByFormulaIndex(Sheet sheet, GridRange range)
    {
        List<uint>? rows = null;
        foreach (var address in sheet.EnumerateFormulaCells())
        {
            if (address.Row < range.Start.Row ||
                address.Row > range.End.Row ||
                address.Col < range.Start.Col ||
                address.Col > range.End.Col)
            {
                continue;
            }

            var formula = sheet.GetCell(address.Row, address.Col)?.FormulaText;
            if (formula is not null && IsSubtotalFormula(formula))
            {
                rows ??= [];
                rows.Add(address.Row);
            }
        }

        if (rows is null)
            return [];

        rows.Sort();
        var writeIndex = 1;
        for (var readIndex = 1; readIndex < rows.Count; readIndex++)
        {
            if (rows[readIndex] == rows[writeIndex - 1])
                continue;

            rows[writeIndex++] = rows[readIndex];
        }

        if (writeIndex < rows.Count)
            rows.RemoveRange(writeIndex, rows.Count - writeIndex);

        return rows;
    }

    private static bool IsSubtotalFormula(string formula) =>
        formula.AsSpan().TrimStart().StartsWith(SubtotalPrefix, StringComparison.OrdinalIgnoreCase);
}

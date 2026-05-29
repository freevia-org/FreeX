using FreeX.Core.Model;
using FreeX.Core.Commands;

namespace FreeX.App.Host;

public static class DataTableInputParser
{
    public static bool IsTwoVariableMode(string input)
    {
        var normalized = input.Trim();
        return normalized.Equals("two", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("2", StringComparison.OrdinalIgnoreCase);
    }

    public static CellAddress GetDefaultFormulaCell(GridRange range, bool twoVariable) =>
        GetDefaultFormulaCell(range, DataTableInputOrientation.Column, twoVariable);

    public static CellAddress GetDefaultFormulaCell(
        GridRange range,
        DataTableInputOrientation orientation,
        bool twoVariable = false) =>
        new(
            range.Start.Sheet,
            twoVariable || orientation == DataTableInputOrientation.Column ? range.Start.Row : range.Start.Row + 1,
            twoVariable || orientation == DataTableInputOrientation.Row ? range.Start.Col : range.Start.Col + 1);

    public static bool TryParseCell(string input, SheetId sheetId, out CellAddress address) =>
        CellReferenceInputParser.TryParseCell(input, sheetId, out address);
}

using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static class DataTableInputParser
{
    public static bool IsTwoVariableMode(string input) =>
        input.Trim().Equals("two", StringComparison.OrdinalIgnoreCase) ||
        input.Trim().Equals("2", StringComparison.OrdinalIgnoreCase);

    public static CellAddress GetDefaultFormulaCell(GridRange range, bool twoVariable) =>
        new(range.Start.Sheet, range.Start.Row, twoVariable ? range.Start.Col : range.Start.Col + 1);

    public static bool TryParseCell(string input, SheetId sheetId, out CellAddress address) =>
        CellAddress.TryParse(input.Trim(), sheetId, out address);
}

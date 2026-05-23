using Freexcel.Core.Model;
using Freexcel.Core.Commands;

namespace Freexcel.App.Host;

public static class DataTableInputParser
{
    public static bool IsTwoVariableMode(string input) =>
        input.Trim().Equals("two", StringComparison.OrdinalIgnoreCase) ||
        input.Trim().Equals("2", StringComparison.OrdinalIgnoreCase);

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
        CellAddress.TryParse(input.Trim(), sheetId, out address);
}

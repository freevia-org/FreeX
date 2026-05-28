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

    public static bool TryParseCell(string input, SheetId sheetId, out CellAddress address)
    {
        var normalized = NormalizeAbsoluteCellReference(input);
        return normalized is not null && CellAddress.TryParse(normalized, sheetId, out address) ||
               PageLayoutInputParser.TryParseAbsoluteR1C1CellReference(input, sheetId, out address);
    }

    private static string? NormalizeAbsoluteCellReference(string input)
    {
        var value = input.AsSpan().Trim();
        if (value.IsEmpty)
            return null;

        Span<char> buffer = stackalloc char[value.Length];
        var index = 0;
        var write = 0;

        if (value[index] == '$')
            index++;

        var columnStart = index;
        while (index < value.Length && char.IsLetter(value[index]))
            buffer[write++] = value[index++];

        if (index == columnStart)
            return null;

        if (index < value.Length && value[index] == '$')
            index++;

        var rowStart = index;
        while (index < value.Length && char.IsDigit(value[index]))
            buffer[write++] = value[index++];

        if (index == rowStart || index != value.Length)
            return null;

        return new string(buffer[..write]);
    }
}

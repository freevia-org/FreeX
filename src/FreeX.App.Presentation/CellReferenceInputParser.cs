using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Presentation;

public static class CellReferenceInputParser
{
    public static bool TryParseCell(string input, SheetId sheetId, out CellAddress address)
    {
        var normalized = AbsoluteCellReferenceNormalizer.Normalize(input);
        return normalized is not null && CellAddress.TryParse(normalized, sheetId, out address) ||
               TryParseAbsoluteR1C1Cell(input, sheetId, out address);
    }

    public static bool TryParseAbsoluteR1C1Cell(string input, SheetId sheetId, out CellAddress address)
    {
        address = default;
        var value = input.AsSpan().Trim();
        var index = 0;
        if (!TryReadPrefixedNumber(value, ref index, 'R', CellAddress.MaxRow, out var row) ||
            !TryReadPrefixedNumber(value, ref index, 'C', CellAddress.MaxCol, out var column) ||
            index != value.Length)
        {
            return false;
        }

        address = new CellAddress(sheetId, row, column);
        return true;
    }

    public static bool TryParseAbsoluteR1C1Row(string input, out uint row) =>
        TryParseSinglePrefixedNumber(input, 'R', CellAddress.MaxRow, out row);

    public static bool TryParseAbsoluteR1C1Column(string input, out uint column) =>
        TryParseSinglePrefixedNumber(input, 'C', CellAddress.MaxCol, out column);

    private static bool TryParseSinglePrefixedNumber(string input, char prefix, uint max, out uint number)
    {
        var value = input.AsSpan().Trim();
        var index = 0;
        return TryReadPrefixedNumber(value, ref index, prefix, max, out number) && index == value.Length;
    }

    private static bool TryReadPrefixedNumber(
        ReadOnlySpan<char> value,
        ref int index,
        char prefix,
        uint max,
        out uint number)
    {
        number = 0;
        if (index >= value.Length || char.ToUpperInvariant(value[index]) != prefix)
            return false;

        index++;
        var start = index;
        while (index < value.Length && char.IsDigit(value[index]))
        {
            number = number * 10 + (uint)(value[index] - '0');
            if (number > max)
                return false;
            index++;
        }

        return index > start && number > 0;
    }
}

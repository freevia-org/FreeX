using Freexcel.Core.Model;

namespace Freexcel.App.Host;

internal static class CellReferenceInputParser
{
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

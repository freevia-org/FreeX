using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxSqrefParser
{
    public static bool TryParseRangeToken(string token, SheetId sheet, out GridRange range)
    {
        range = default;
        var parts = token.Split(':');
        if (parts.Length == 1)
        {
            if (!CellAddress.TryParse(parts[0], sheet, out var address))
                return false;

            range = new GridRange(address, address);
            return true;
        }

        if (parts.Length != 2)
            return false;

        if (CellAddress.TryParse(parts[0], sheet, out var start) &&
            CellAddress.TryParse(parts[1], sheet, out var end))
        {
            range = new GridRange(start, end);
            return true;
        }

        return TryParseWholeColumnOrRowRange(parts[0], parts[1], sheet, out range);
    }

    private static bool TryParseWholeColumnOrRowRange(
        string startToken,
        string endToken,
        SheetId sheet,
        out GridRange range)
    {
        range = default;

        var startCol = CellAddress.ColumnNameToNumber(startToken);
        var endCol = CellAddress.ColumnNameToNumber(endToken);
        if (startCol is > 0 and <= CellAddress.MaxCol && endCol is > 0 and <= CellAddress.MaxCol)
        {
            range = new GridRange(
                new CellAddress(sheet, 1, startCol),
                new CellAddress(sheet, CellAddress.MaxRow, endCol));
            return true;
        }

        if (IsAsciiDigitsOnly(startToken) && IsAsciiDigitsOnly(endToken) &&
            uint.TryParse(startToken, out var startRow) && uint.TryParse(endToken, out var endRow) &&
            startRow is > 0 and <= CellAddress.MaxRow && endRow is > 0 and <= CellAddress.MaxRow)
        {
            range = new GridRange(
                new CellAddress(sheet, startRow, 1),
                new CellAddress(sheet, endRow, CellAddress.MaxCol));
            return true;
        }

        return false;
    }

    private static bool IsAsciiDigitsOnly(string value)
    {
        if (value.Length == 0)
            return false;

        foreach (var c in value)
        {
            if (c is < '0' or > '9')
                return false;
        }

        return true;
    }
}

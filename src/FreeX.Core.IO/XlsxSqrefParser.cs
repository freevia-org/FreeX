using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxSqrefParser
{
    public static string? NormalizeWhitespaceSeparatedTokens(string? value)
    {
        var tokens = value?
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens is { Length: > 0 }
            ? string.Join(' ', tokens)
            : null;
    }

    public static string? NormalizeCellRangeList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var seenTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalizedTokens = new List<string>();
        foreach (var token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var normalized = NormalizeCellRangeToken(token);
            if (normalized is null || !seenTokens.Add(normalized))
                continue;

            normalizedTokens.Add(normalized);
        }

        return normalizedTokens.Count == 0
            ? null
            : string.Join(' ', normalizedTokens);
    }

    public static string? NormalizeSelectionReferenceList(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return null;

        var tokens = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens.Length > 0 && tokens.All(IsCellOrRangeReference)
            ? string.Join(' ', tokens)
            : null;
    }

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

    private static string? NormalizeCellRangeToken(string token)
    {
        var parts = token.Split(':');
        var sheet = SheetId.New();
        if (parts.Length == 1)
        {
            return CellAddress.TryParse(parts[0], sheet, out var address)
                ? address.ToA1()
                : null;
        }

        if (parts.Length == 2 &&
            CellAddress.TryParse(parts[0], sheet, out var start) &&
            CellAddress.TryParse(parts[1], sheet, out var end))
        {
            var range = new GridRange(start, end);
            return range.Start == range.End
                ? range.Start.ToA1()
                : $"{range.Start.ToA1()}:{range.End.ToA1()}";
        }

        return null;
    }

    private static bool IsCellOrRangeReference(string token)
    {
        var parts = token.Split(':');
        if (parts.Length == 1)
            return CellAddress.TryParse(parts[0], SheetId.New(), out _);

        if (parts.Length != 2)
            return false;

        if (CellAddress.TryParse(parts[0], SheetId.New(), out _) &&
            CellAddress.TryParse(parts[1], SheetId.New(), out _))
        {
            return true;
        }

        return IsColumnOnlyReference(parts[0]) && IsColumnOnlyReference(parts[1]) ||
               IsRowOnlyReference(parts[0]) && IsRowOnlyReference(parts[1]);
    }

    private static bool IsColumnOnlyReference(string value)
    {
        if (value.Length is 0 or > 3)
            return false;

        foreach (var c in value)
        {
            if (c is (< 'A' or > 'Z') and (< 'a' or > 'z'))
                return false;
        }

        var column = CellAddress.ColumnNameToNumber(value);
        return column is > 0 and <= CellAddress.MaxCol;
    }

    private static bool IsRowOnlyReference(string value)
    {
        if (value.Length is 0 or > 7)
            return false;

        uint row = 0;
        foreach (var c in value)
        {
            if (c is < '0' or > '9')
                return false;

            row = row * 10 + (uint)(c - '0');
            if (row > CellAddress.MaxRow)
                return false;
        }

        return row > 0;
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

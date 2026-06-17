using System.Globalization;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Dialogs;

/// <summary>
/// Small portable parser for the free-text range fields of the Avalonia Page Setup dialog: the print
/// area (cell range), the rows-to-repeat-at-top (row range), and the columns-to-repeat-at-left (column
/// range). This is a trimmed Avalonia-local copy of the validation the WPF host's PageLayoutInputParser
/// performs, since that parser lives in the Windows-only host project. Empty / "none" inputs clear the
/// corresponding setting. No UI dependency, so it is unit-tested directly.
/// </summary>
public static class PageSetupRangeParser
{
    /// <summary>
    /// Parses a print-area cell range ("A1:D20", "A1", "$A$1:$D$20"). Empty input yields a null range
    /// (clear the print area). Returns false on a malformed reference.
    /// </summary>
    public static bool TryParsePrintArea(string input, SheetId sheetId, out GridRange? printArea)
    {
        printArea = null;
        var normalized = (input ?? string.Empty).Trim();
        if (normalized.Length == 0)
            return true;

        var parts = normalized.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 1 or > 2)
            return false;

        if (!TryParseCell(parts[0], sheetId, out var start))
            return false;

        if (parts.Length == 1)
        {
            printArea = new GridRange(start, start);
            return true;
        }

        if (!TryParseCell(parts[1], sheetId, out var end))
            return false;

        printArea = new GridRange(start, end);
        return true;
    }

    /// <summary>
    /// Parses a rows-to-repeat range ("1", "1:2"). Empty / "none" yields a null range (clear). Returns
    /// false when a token is not a 1-based row index in range.
    /// </summary>
    public static bool TryParseRepeatRows(string input, out WorksheetRepeatRange? range)
    {
        range = null;
        var normalized = (input ?? string.Empty).Trim();
        if (IsClear(normalized))
            return true;

        var parts = normalized.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return TryParseRange(parts, TryParseRowToken, IsValidRow, out range);
    }

    /// <summary>
    /// Parses a columns-to-repeat range ("A", "A:B", "1:2"). Empty / "none" yields a null range
    /// (clear). Returns false when a token is not a valid 1-based column index or name in range.
    /// </summary>
    public static bool TryParseRepeatColumns(string input, out WorksheetRepeatRange? range)
    {
        range = null;
        var normalized = (input ?? string.Empty).Trim();
        if (IsClear(normalized))
            return true;

        var parts = normalized.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return TryParseRange(parts, TryParseColumnToken, IsValidColumn, out range);
    }

    private static bool TryParseRange(
        string[] parts,
        TryParseToken tryParseToken,
        Func<uint, bool> isValid,
        out WorksheetRepeatRange? range)
    {
        range = null;
        if (parts.Length is not 1 and not 2)
            return false;

        if (!tryParseToken(parts[0], out var start) || !isValid(start))
            return false;

        var end = start;
        if (parts.Length == 2 && (!tryParseToken(parts[1], out end) || !isValid(end)))
            return false;

        range = new WorksheetRepeatRange(Math.Min(start, end), Math.Max(start, end));
        return true;
    }

    private static bool TryParseCell(string token, SheetId sheetId, out CellAddress address) =>
        CellAddress.TryParse(token.Trim().Replace("$", string.Empty), sheetId, out address);

    private static bool TryParseRowToken(string token, out uint row) =>
        uint.TryParse(token.Trim().TrimStart('$'), NumberStyles.Integer, CultureInfo.InvariantCulture, out row);

    private static bool TryParseColumnToken(string token, out uint column)
    {
        var trimmed = token.Trim().TrimStart('$');
        if (uint.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out column))
            return column > 0;

        if (trimmed.Length == 0 || !trimmed.All(char.IsLetter))
        {
            column = 0;
            return false;
        }

        column = CellAddress.ColumnNameToNumber(trimmed);
        return column > 0;
    }

    private static bool IsClear(string normalized) =>
        normalized.Length == 0 ||
        normalized.Equals("none", StringComparison.OrdinalIgnoreCase) ||
        normalized.Equals("clear", StringComparison.OrdinalIgnoreCase);

    private static bool IsValidRow(uint row) => row is > 0 and <= CellAddress.MaxRow;

    private static bool IsValidColumn(uint column) => column is > 0 and <= CellAddress.MaxCol;

    private delegate bool TryParseToken(string token, out uint value);
}

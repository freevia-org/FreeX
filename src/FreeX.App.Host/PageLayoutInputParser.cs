using System.Globalization;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public enum PageBreakInputKind
{
    Clear,
    Row,
    Column
}

public sealed record PageBreakInput(PageBreakInputKind Kind, uint? Row = null, uint? Column = null);

public static class PageLayoutInputParser
{
    public static bool TryParseBreakInput(string input, string keyword, out uint value)
    {
        value = 0;
        if (!input.StartsWith(keyword, StringComparison.OrdinalIgnoreCase))
            return false;

        var numberText = input[keyword.Length..].Trim();
        return uint.TryParse(numberText, out value);
    }

    public static bool TryParseColumnBreakInput(string input, string keyword, out uint value)
    {
        value = 0;
        if (!keyword.Equals("col", StringComparison.OrdinalIgnoreCase) &&
            !keyword.Equals("column", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!input.StartsWith(keyword, StringComparison.OrdinalIgnoreCase))
            return false;

        var columnText = input[keyword.Length..].Trim();
        return TryParseColumnBreakValue(columnText, out value);
    }

    public static bool TryParseColumnBreakValue(string input, out uint value)
    {
        value = 0;
        var trimmed = input.Trim();
        if (uint.TryParse(trimmed, out value))
            return IsValidColumnBreak(value);

        if (!IsColumnName(trimmed))
            return false;

        try
        {
            value = CellAddress.ColumnNameToNumber(trimmed);
            return IsValidColumnBreak(value);
        }
        catch (FormatException)
        {
            value = 0;
            return false;
        }
    }

    public static bool TryParsePageBreakInput(string input, out PageBreakInput pageBreak)
    {
        var trimmed = input.Trim();
        if (trimmed.Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            pageBreak = new PageBreakInput(PageBreakInputKind.Clear);
            return true;
        }

        if (TryParseBreakInput(trimmed, "row", out var rowBreak) && IsValidRowBreak(rowBreak))
        {
            pageBreak = new PageBreakInput(PageBreakInputKind.Row, Row: rowBreak);
            return true;
        }

        if (TryParseColumnBreakInput(trimmed, "col", out var columnBreak) ||
            TryParseColumnBreakInput(trimmed, "column", out columnBreak))
        {
            pageBreak = new PageBreakInput(PageBreakInputKind.Column, Column: columnBreak);
            return true;
        }

        pageBreak = new PageBreakInput(PageBreakInputKind.Clear);
        return false;
    }

    public static bool TryParseRepeatRows(string input, out WorksheetRepeatRange? range)
    {
        var normalized = input.Trim();
        if (IsClearInput(normalized))
        {
            range = null;
            return true;
        }

        var parts = normalized.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return TryParseRepeatRange(parts, TryParseRepeatRowToken, IsValidRepeatRow, out range);
    }

    public static bool TryParseRepeatColumns(string input, out WorksheetRepeatRange? range)
    {
        var normalized = input.Trim();
        if (IsClearInput(normalized))
        {
            range = null;
            return true;
        }

        var parts = normalized.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return TryParseRepeatRange(parts, TryParseNormalizedRepeatColumnToken, IsValidRepeatColumn, out range);
    }

    public static bool TryParseOptionalPrintArea(string input, SheetId sheetId, out GridRange? printArea)
    {
        printArea = null;
        var normalized = input.Trim();
        if (normalized.Length == 0)
            return true;

        var parts = normalized.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 1 or > 2)
            return false;

        if (!TryParseAbsoluteCellReference(parts[0], sheetId, out var start))
            return false;

        if (parts.Length == 1)
        {
            printArea = new GridRange(start, start);
            return true;
        }

        if (!TryParseAbsoluteCellReference(parts[1], sheetId, out var end))
            return false;

        printArea = new GridRange(start, end);
        return true;
    }

    public static bool TryParseAbsoluteR1C1CellReference(string input, SheetId sheetId, out CellAddress address) =>
        TryParseR1C1CellReference(input, sheetId, out address);

    public static string FormatScaleToFit(WorksheetScaleToFit scaleToFit) =>
        scaleToFit.ScalePercent.HasValue
            ? scaleToFit.ScalePercent.Value.ToString(CultureInfo.InvariantCulture)
            : $"{scaleToFit.FitToPagesWide ?? 1}x{scaleToFit.FitToPagesTall ?? 1}";

    public static bool TryParseScaleToFit(string input, out WorksheetScaleToFit scaleToFit)
    {
        var trimmed = input.Trim();
        if (trimmed.Contains('x', StringComparison.OrdinalIgnoreCase))
        {
            var parts = trimmed.Split('x', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 &&
                int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var wide) &&
                int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var tall) &&
                wide > 0 &&
                tall > 0)
            {
                scaleToFit = new WorksheetScaleToFit(null, wide, tall);
                return true;
            }
        }
        else if (int.TryParse(TrimPercentSuffix(trimmed), NumberStyles.Integer, CultureInfo.InvariantCulture, out var percent) &&
                 percent is >= 10 and <= 400)
        {
            scaleToFit = new WorksheetScaleToFit(percent, null, null);
            return true;
        }

        scaleToFit = WorksheetScaleToFit.Default;
        return false;
    }

    public static bool TryParseOptionalFirstPageNumber(string input, out int? firstPageNumber)
    {
        firstPageNumber = null;
        var trimmed = input.Trim();
        if (IsAutoInput(trimmed))
            return true;

        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value != 0)
        {
            firstPageNumber = value;
            return true;
        }

        return false;
    }

    public static bool TryParseMarginDistance(string input, out double value)
    {
        value = 0;
        return double.TryParse(input.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
               double.IsFinite(value) &&
               value >= 0;
    }

    public static bool TryParseOptionalPrintQuality(string input, out int? printQualityDpi)
    {
        printQualityDpi = null;
        var trimmed = input.Trim();
        if (IsAutoInput(trimmed))
            return true;

        var dpiText = TrimDpiSuffix(trimmed);
        if (int.TryParse(dpiText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0)
        {
            printQualityDpi = value;
            return true;
        }

        return false;
    }

    private static string TrimDpiSuffix(string value) =>
        value.EndsWith("dpi", StringComparison.OrdinalIgnoreCase)
            ? value[..^3].TrimEnd()
            : value;

    private static string TrimPercentSuffix(string value) =>
        value.EndsWith('%')
            ? value[..^1].TrimEnd()
            : value;

    private static bool IsClearInput(string normalized) =>
        normalized.Equals("none", StringComparison.OrdinalIgnoreCase) ||
        normalized.Equals("clear", StringComparison.OrdinalIgnoreCase) ||
        normalized.Length == 0;

    private static bool IsAutoInput(string normalized) =>
        normalized.Length == 0 ||
        normalized.Equals("auto", StringComparison.OrdinalIgnoreCase);

    public static bool IsValidRowBreak(uint row) =>
        row is > 0 and <= CellAddress.MaxRow;

    public static bool IsValidColumnBreak(uint column) =>
        column is > 0 and <= CellAddress.MaxCol;

    private static bool IsValidRepeatRow(uint row) =>
        row is > 0 and <= CellAddress.MaxRow;

    private static bool IsValidRepeatColumn(uint column) =>
        column is > 0 and <= CellAddress.MaxCol;

    private static bool TryParseRepeatRange(
        string[] parts,
        TryParseRepeatToken tryParseToken,
        Func<uint, bool> isValid,
        out WorksheetRepeatRange? range)
    {
        range = null;
        if (parts.Length is not 1 and not 2)
            return false;

        try
        {
            if (!tryParseToken(parts[0], out var start) || !isValid(start))
                return false;

            var end = start;
            if (parts.Length == 2 && (!tryParseToken(parts[1], out end) || !isValid(end)))
                return false;

            range = new WorksheetRepeatRange(Math.Min(start, end), Math.Max(start, end));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private delegate bool TryParseRepeatToken(string token, out uint value);

    private static bool TryParseRepeatRowToken(string token, out uint row) =>
        TryParseR1C1RowReference(token, out row) ||
        uint.TryParse(NormalizeAbsoluteReferenceToken(token), out row);

    private static bool TryParseRepeatColumnToken(string token, out uint column)
    {
        if (TryParseR1C1ColumnReference(token, out column))
            return true;

        if (!IsColumnName(token))
        {
            column = 0;
            return false;
        }

        column = CellAddress.ColumnNameToNumber(token);
        return true;
    }

    private static bool TryParseNormalizedRepeatColumnToken(string token, out uint column) =>
        TryParseRepeatColumnToken(NormalizeAbsoluteReferenceToken(token), out column);

    private static string NormalizeAbsoluteReferenceToken(string token) =>
        token.Trim().TrimStart('$');

    private static bool IsColumnName(string text) =>
        text.Length > 0 && text.All(char.IsLetter);

    private static bool TryParseAbsoluteCellReference(string token, SheetId sheetId, out CellAddress address)
    {
        address = default;
        var normalized = AbsoluteCellReferenceNormalizer.Normalize(token);
        return normalized is not null && CellAddress.TryParse(normalized, sheetId, out address) ||
               TryParseR1C1CellReference(token, sheetId, out address);
    }

    private static bool TryParseR1C1CellReference(string token, SheetId sheetId, out CellAddress address)
    {
        address = default;
        var value = token.AsSpan().Trim();
        if (value.Length < 4 || !IsR1C1Prefix(value[0], 'R'))
            return false;

        var index = 1;
        if (!TryReadR1C1Number(value, ref index, CellAddress.MaxRow, out var row))
            return false;

        if (index >= value.Length || !IsR1C1Prefix(value[index], 'C'))
            return false;

        index++;
        if (!TryReadR1C1Number(value, ref index, CellAddress.MaxCol, out var column) || index != value.Length)
            return false;

        address = new CellAddress(sheetId, row, column);
        return true;
    }

    private static bool TryParseR1C1RowReference(string token, out uint row)
    {
        row = 0;
        var value = token.AsSpan().Trim();
        if (value.Length < 2 || !IsR1C1Prefix(value[0], 'R'))
            return false;

        var index = 1;
        return TryReadR1C1Number(value, ref index, CellAddress.MaxRow, out row) && index == value.Length;
    }

    private static bool TryParseR1C1ColumnReference(string token, out uint column)
    {
        column = 0;
        var value = token.AsSpan().Trim();
        if (value.Length < 2 || !IsR1C1Prefix(value[0], 'C'))
            return false;

        var index = 1;
        return TryReadR1C1Number(value, ref index, CellAddress.MaxCol, out column) && index == value.Length;
    }

    private static bool TryReadR1C1Number(ReadOnlySpan<char> value, ref int index, uint max, out uint number)
    {
        number = 0;
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

    private static bool IsR1C1Prefix(char actual, char expected) =>
        char.ToUpperInvariant(actual) == expected;
}

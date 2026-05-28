using System.Globalization;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

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
        range = null;
        var normalized = input.Trim();
        if (IsClearInput(normalized))
            return true;

        var parts = normalized.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1 && TryParseRepeatRowToken(parts[0], out var single))
        {
            if (!IsValidRepeatRow(single))
                return false;

            range = new WorksheetRepeatRange(single, single);
            return true;
        }

        if (parts.Length == 2 &&
            TryParseRepeatRowToken(parts[0], out var start) &&
            TryParseRepeatRowToken(parts[1], out var end))
        {
            if (!IsValidRepeatRow(start) || !IsValidRepeatRow(end))
                return false;

            range = new WorksheetRepeatRange(Math.Min(start, end), Math.Max(start, end));
            return true;
        }

        return false;
    }

    public static bool TryParseRepeatColumns(string input, out WorksheetRepeatRange? range)
    {
        range = null;
        var normalized = input.Trim();
        if (IsClearInput(normalized))
            return true;

        var parts = normalized.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
        {
            try
            {
                var token = NormalizeAbsoluteReferenceToken(parts[0]);
                if (!IsColumnName(token))
                    return false;

                var single = CellAddress.ColumnNameToNumber(token);
                if (!IsValidRepeatColumn(single))
                    return false;

                range = new WorksheetRepeatRange(single, single);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        if (parts.Length == 2)
        {
            try
            {
                var startToken = NormalizeAbsoluteReferenceToken(parts[0]);
                var endToken = NormalizeAbsoluteReferenceToken(parts[1]);
                if (!IsColumnName(startToken) || !IsColumnName(endToken))
                    return false;

                var start = CellAddress.ColumnNameToNumber(startToken);
                var end = CellAddress.ColumnNameToNumber(endToken);
                if (!IsValidRepeatColumn(start) || !IsValidRepeatColumn(end))
                    return false;

                range = new WorksheetRepeatRange(Math.Min(start, end), Math.Max(start, end));
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        return false;
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
        else if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var percent) &&
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

        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0)
        {
            printQualityDpi = value;
            return true;
        }

        return false;
    }

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

    private static bool TryParseRepeatRowToken(string token, out uint row) =>
        uint.TryParse(NormalizeAbsoluteReferenceToken(token), out row);

    private static string NormalizeAbsoluteReferenceToken(string token) =>
        token.Trim().TrimStart('$');

    private static bool IsColumnName(string text) =>
        text.Length > 0 && text.All(char.IsLetter);

    private static bool TryParseAbsoluteCellReference(string token, SheetId sheetId, out CellAddress address)
    {
        address = default;
        var normalized = NormalizeAbsoluteCellReferenceToken(token);
        return normalized is not null && CellAddress.TryParse(normalized, sheetId, out address);
    }

    private static string? NormalizeAbsoluteCellReferenceToken(string token)
    {
        var value = token.AsSpan().Trim();
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

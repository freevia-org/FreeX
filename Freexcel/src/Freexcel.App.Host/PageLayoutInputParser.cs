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

    public static bool TryParsePageBreakInput(string input, out PageBreakInput pageBreak)
    {
        var trimmed = input.Trim();
        if (trimmed.Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            pageBreak = new PageBreakInput(PageBreakInputKind.Clear);
            return true;
        }

        if (TryParseBreakInput(trimmed, "row", out var rowBreak))
        {
            pageBreak = new PageBreakInput(PageBreakInputKind.Row, Row: rowBreak);
            return true;
        }

        if (TryParseBreakInput(trimmed, "col", out var columnBreak) ||
            TryParseBreakInput(trimmed, "column", out columnBreak))
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
        if (parts.Length == 1 && uint.TryParse(parts[0], out var single))
        {
            if (single == 0)
                return false;

            range = new WorksheetRepeatRange(single, single);
            return true;
        }

        if (parts.Length == 2 && uint.TryParse(parts[0], out var start) && uint.TryParse(parts[1], out var end))
        {
            if (start == 0 || end == 0)
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
                if (!IsColumnName(parts[0]))
                    return false;

                var single = CellAddress.ColumnNameToNumber(parts[0]);
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
                if (!IsColumnName(parts[0]) || !IsColumnName(parts[1]))
                    return false;

                var start = CellAddress.ColumnNameToNumber(parts[0]);
                var end = CellAddress.ColumnNameToNumber(parts[1]);
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

    private static bool IsClearInput(string normalized) =>
        normalized.Equals("none", StringComparison.OrdinalIgnoreCase) ||
        normalized.Equals("clear", StringComparison.OrdinalIgnoreCase) ||
        normalized.Length == 0;

    private static bool IsColumnName(string text) =>
        text.Length > 0 && text.All(char.IsLetter);
}

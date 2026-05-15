namespace Freexcel.Core.Commands;

public sealed record SubtotalInputOptions(
    uint GroupColumnOffset,
    IReadOnlyList<uint> SubtotalColumnOffsets,
    int FunctionNumber,
    bool ReplaceExisting,
    bool PageBreakBetweenGroups,
    bool SummaryBelowData);

public static class SubtotalInputParser
{
    public static bool TryParse(string input, out SubtotalInputOptions options, out string? error)
    {
        options = new SubtotalInputOptions(0, [], 9, false, false, true);
        error = null;

        var parts = input.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length is < 2 or > 4)
        {
            error = "Enter group column, subtotal column(s), and optional function.";
            return false;
        }

        if (!uint.TryParse(parts[0], out var groupColumn) || groupColumn == 0)
        {
            error = "Enter a valid group column.";
            return false;
        }

        var subtotalColumns = ParseSubtotalColumns(parts[1]);
        if (subtotalColumns.Count == 0)
        {
            error = "Enter one or more valid subtotal columns.";
            return false;
        }

        var functionText = parts.Length >= 3 ? parts[2] : "sum";
        if (!SubtotalFunctionService.TryParse(functionText, out var functionNumber))
        {
            error = "Enter a supported SUBTOTAL function: average, count, counta, max, min, product, stdev, stdevp, sum, var, varp, or 1-11.";
            return false;
        }

        var optionText = parts.Length == 4 ? parts[3] : "";
        options = new SubtotalInputOptions(
            groupColumn - 1,
            subtotalColumns.Select(column => column - 1).ToList(),
            functionNumber,
            ReplaceExisting: optionText.Contains("replace", StringComparison.OrdinalIgnoreCase),
            PageBreakBetweenGroups: optionText.Contains("pagebreak", StringComparison.OrdinalIgnoreCase) ||
                                    optionText.Contains("page break", StringComparison.OrdinalIgnoreCase),
            SummaryBelowData: !optionText.Contains("above", StringComparison.OrdinalIgnoreCase));
        return true;
    }

    private static List<uint> ParseSubtotalColumns(string text)
    {
        var columns = new List<uint>();
        var parts = text.Split(['+', '|', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (!uint.TryParse(part, out var column) || column == 0)
                return [];
            if (!columns.Contains(column))
                columns.Add(column);
        }

        return columns;
    }
}

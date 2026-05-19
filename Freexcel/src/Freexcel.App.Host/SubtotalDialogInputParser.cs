using Freexcel.Core.Commands;

namespace Freexcel.App.Host;

public static class SubtotalDialogInputParser
{
    public static bool TryParse(
        string groupColumnText,
        string subtotalColumnsText,
        string functionText,
        bool replaceCurrentSubtotals,
        bool pageBreakBetweenGroups,
        bool summaryBelowData,
        out SubtotalDialogResult result,
        out string? error)
    {
        result = default!;
        error = null;

        if (!uint.TryParse(groupColumnText.Trim(), out var groupColumnOffset))
        {
            error = "Enter a valid group column offset.";
            return false;
        }

        var subtotalColumnOffsets = ParseColumnOffsets(subtotalColumnsText);
        if (subtotalColumnOffsets.Count == 0)
        {
            error = "Enter one or more valid subtotal column offsets.";
            return false;
        }

        if (!SubtotalFunctionService.TryParse(functionText, out var functionNumber))
        {
            error = "Unsupported SUBTOTAL function.";
            return false;
        }

        result = new SubtotalDialogResult(
            groupColumnOffset,
            subtotalColumnOffsets,
            functionNumber,
            replaceCurrentSubtotals,
            pageBreakBetweenGroups,
            summaryBelowData);
        return true;
    }

    private static IReadOnlyList<uint> ParseColumnOffsets(string input)
    {
        var offsets = new List<uint>();
        foreach (var part in input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!uint.TryParse(part, out var offset))
                return [];
            if (!offsets.Contains(offset))
                offsets.Add(offset);
        }

        return offsets;
    }
}

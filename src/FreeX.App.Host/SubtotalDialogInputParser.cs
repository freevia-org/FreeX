using FreeX.Core.Commands;

namespace FreeX.App.Host;

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
            error = UiText.Get("Subtotal_EnterValidGroupColumnOffset");
            return false;
        }

        var subtotalColumnOffsets = ParseColumnOffsets(subtotalColumnsText);
        if (subtotalColumnOffsets.Count == 0)
        {
            error = UiText.Get("Subtotal_EnterValidSubtotalColumnOffsets");
            return false;
        }

        if (!SubtotalFunctionService.TryParse(functionText, out var functionNumber))
        {
            error = UiText.Get("Subtotal_UnsupportedSubtotalFunction");
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
        foreach (var part in SplitColumnOffsetParts(input))
        {
            if (!TryAddColumnOffset(offsets, part))
                return [];
        }

        return offsets;
    }

    private static string[] SplitColumnOffsetParts(string input) =>
        input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool TryAddColumnOffset(List<uint> offsets, string part)
    {
        if (!uint.TryParse(part, out var offset))
            return false;

        if (!offsets.Contains(offset))
            offsets.Add(offset);

        return true;
    }
}

using FreeX.Core.Commands;
using FreeX.App.Presentation.Localization;

namespace FreeX.App.Presentation.DataTools;

public enum SubtotalDialogInputParseIssue
{
    None,
    InvalidGroupColumnOffset,
    InvalidSubtotalColumnOffsets,
    UnsupportedSubtotalFunction
}

public sealed record SubtotalDialogInputParseResult(
    uint GroupColumnOffset,
    IReadOnlyList<uint> SubtotalColumnOffsets,
    int FunctionNumber,
    bool ReplaceCurrentSubtotals,
    bool PageBreakBetweenGroups,
    bool SummaryBelowData);

public enum SubtotalDialogInputFocusTarget
{
    GroupColumn,
    SubtotalColumns,
    Function
}

public static class SubtotalDialogInputParser
{
    public static ValidationPresentationDescriptor<SubtotalDialogInputFocusTarget> DescribeIssue(
        SubtotalDialogInputParseIssue issue) =>
        issue switch
        {
            SubtotalDialogInputParseIssue.InvalidSubtotalColumnOffsets => new(
                LocalizedTextDescriptor.Resource("Subtotal_AtLeastOneSubtotalColumnIsRequired"),
                SubtotalDialogInputFocusTarget.SubtotalColumns),
            SubtotalDialogInputParseIssue.UnsupportedSubtotalFunction => new(
                LocalizedTextDescriptor.Resource("Subtotal_UnsupportedSubtotalFunction"),
                SubtotalDialogInputFocusTarget.Function),
            _ => new(
                LocalizedTextDescriptor.Resource("Subtotal_UnsupportedSubtotalFunction"),
                SubtotalDialogInputFocusTarget.GroupColumn)
        };

    public static bool TryParse(
        string? groupColumnText,
        string? subtotalColumnsText,
        string? functionText,
        bool replaceCurrentSubtotals,
        bool pageBreakBetweenGroups,
        bool summaryBelowData,
        out SubtotalDialogInputParseResult result,
        out SubtotalDialogInputParseIssue issue)
    {
        result = default!;
        issue = SubtotalDialogInputParseIssue.None;

        if (!uint.TryParse((groupColumnText ?? "").Trim(), out var groupColumnOffset))
        {
            issue = SubtotalDialogInputParseIssue.InvalidGroupColumnOffset;
            return false;
        }

        var subtotalColumnOffsets = ParseColumnOffsets(subtotalColumnsText ?? "");
        if (subtotalColumnOffsets.Count == 0)
        {
            issue = SubtotalDialogInputParseIssue.InvalidSubtotalColumnOffsets;
            return false;
        }

        if (!SubtotalFunctionService.TryParse(functionText ?? "", out var functionNumber))
        {
            issue = SubtotalDialogInputParseIssue.UnsupportedSubtotalFunction;
            return false;
        }

        result = new SubtotalDialogInputParseResult(
            groupColumnOffset,
            subtotalColumnOffsets,
            functionNumber,
            replaceCurrentSubtotals,
            pageBreakBetweenGroups,
            summaryBelowData);
        return true;
    }

    public static bool TryCreateResult(
        uint groupColumnOffset,
        IEnumerable<uint> subtotalColumnOffsets,
        string? functionText,
        bool replaceCurrentSubtotals,
        bool pageBreakBetweenGroups,
        bool summaryBelowData,
        out SubtotalDialogInputParseResult result,
        out SubtotalDialogInputParseIssue issue)
    {
        ArgumentNullException.ThrowIfNull(subtotalColumnOffsets);

        result = default!;
        issue = SubtotalDialogInputParseIssue.None;

        if (!SubtotalFunctionService.TryParse(functionText ?? "", out var functionNumber))
        {
            issue = SubtotalDialogInputParseIssue.UnsupportedSubtotalFunction;
            return false;
        }

        var offsets = subtotalColumnOffsets.Distinct().ToList();
        if (offsets.Count == 0)
        {
            issue = SubtotalDialogInputParseIssue.InvalidSubtotalColumnOffsets;
            return false;
        }

        result = new SubtotalDialogInputParseResult(
            groupColumnOffset,
            offsets,
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

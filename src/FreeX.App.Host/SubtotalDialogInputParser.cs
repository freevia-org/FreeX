using SharedSubtotalDialogInputParseIssue = FreeX.App.Presentation.DataTools.SubtotalDialogInputParseIssue;
using SharedSubtotalDialogInputParseResult = FreeX.App.Presentation.DataTools.SubtotalDialogInputParseResult;
using SharedSubtotalDialogInputParser = FreeX.App.Presentation.DataTools.SubtotalDialogInputParser;

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
        if (SharedSubtotalDialogInputParser.TryParse(
                groupColumnText,
                subtotalColumnsText,
                functionText,
                replaceCurrentSubtotals,
                pageBreakBetweenGroups,
                summaryBelowData,
                out var parsed,
                out var issue))
        {
            result = Project(parsed);
            error = null;
            return true;
        }

        result = default!;
        error = DescribeTextInputIssue(issue);
        return false;
    }

    public static bool TryCreateResult(
        uint groupColumnOffset,
        IEnumerable<uint> subtotalColumnOffsets,
        string functionText,
        bool replaceCurrentSubtotals,
        bool pageBreakBetweenGroups,
        bool summaryBelowData,
        out SubtotalDialogResult result,
        out string? error)
    {
        if (SharedSubtotalDialogInputParser.TryCreateResult(
                groupColumnOffset,
                subtotalColumnOffsets,
                functionText,
                replaceCurrentSubtotals,
                pageBreakBetweenGroups,
                summaryBelowData,
                out var parsed,
                out var issue))
        {
            result = Project(parsed);
            error = null;
            return true;
        }

        result = default!;
        error = DescribeCreateResultIssue(issue);
        return false;
    }

    private static SubtotalDialogResult Project(SharedSubtotalDialogInputParseResult parsed) =>
        new(
            parsed.GroupColumnOffset,
            parsed.SubtotalColumnOffsets,
            parsed.FunctionNumber,
            parsed.ReplaceCurrentSubtotals,
            parsed.PageBreakBetweenGroups,
            parsed.SummaryBelowData);

    private static string? DescribeTextInputIssue(SharedSubtotalDialogInputParseIssue issue) =>
        issue switch
        {
            SharedSubtotalDialogInputParseIssue.InvalidGroupColumnOffset =>
                UiText.Get("Subtotal_EnterValidGroupColumnOffset"),
            SharedSubtotalDialogInputParseIssue.InvalidSubtotalColumnOffsets =>
                UiText.Get("Subtotal_EnterValidSubtotalColumnOffsets"),
            SharedSubtotalDialogInputParseIssue.UnsupportedSubtotalFunction =>
                UiText.Get("Subtotal_UnsupportedSubtotalFunction"),
            _ => null
        };

    private static string? DescribeCreateResultIssue(SharedSubtotalDialogInputParseIssue issue) =>
        issue switch
        {
            SharedSubtotalDialogInputParseIssue.InvalidSubtotalColumnOffsets =>
                UiText.Get("Subtotal_AtLeastOneSubtotalColumnIsRequired"),
            SharedSubtotalDialogInputParseIssue.UnsupportedSubtotalFunction =>
                UiText.Get("Subtotal_UnsupportedSubtotalFunction"),
            _ => null
        };
}

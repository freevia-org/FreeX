using FreeX.Core.Model;

namespace FreeX.App.Host;

internal sealed record AllowEditRangeButtonState(bool CanDeleteSelectedRange, bool CanClearRanges);

internal static class AllowEditRangeDialogPlanner
{
    public static AllowEditRangeSelectionRequest CreateRangeSelectionRequest(string currentText) =>
        new(currentText.Trim(), CollapseDialog: true);

    public static AllowEditRangeDialogResult CreateAddResult(GridRange range) =>
        new(AllowEditRangeDialogAction.Add, range);

    public static AllowEditRangeDialogResult CreateRemoveResult(GridRange range) =>
        new(AllowEditRangeDialogAction.Remove, range);

    public static AllowEditRangeDialogResult CreateClearResult() =>
        new(AllowEditRangeDialogAction.Clear, null);

    public static IReadOnlyList<string> BuildExistingRangeItems(IReadOnlyList<GridRange>? existingRanges) =>
        existingRanges?.Select(range => range.ToString()).ToList() ?? [];

    public static AllowEditRangeButtonState BuildButtonState(int rangeCount, bool hasSelectedRange)
    {
        var hasRanges = rangeCount > 0;
        return new AllowEditRangeButtonState(
            CanDeleteSelectedRange: hasRanges && hasSelectedRange,
            CanClearRanges: hasRanges);
    }
}

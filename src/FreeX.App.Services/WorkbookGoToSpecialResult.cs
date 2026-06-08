using FreeX.Core.Model;

namespace FreeX.App.Services;

public sealed record WorkbookGoToSpecialResult(
    bool Success,
    string? ErrorMessage,
    GridRange? SelectedRange,
    IReadOnlyList<GridRange> SelectedRanges,
    int MatchCount)
{
    public static WorkbookGoToSpecialResult Failed(string errorMessage) =>
        new(false, errorMessage, null, [], 0);

    public static WorkbookGoToSpecialResult Selected(
        GridRange selectedRange,
        IReadOnlyList<GridRange> selectedRanges,
        int matchCount) =>
        new(true, null, selectedRange, selectedRanges, matchCount);
}

using FreeX.Core.Model;

namespace FreeX.App.Services;

public sealed record WorkbookNavigationResult(
    bool Success,
    string? ErrorMessage,
    GridRange? SelectedRange,
    string? MatchedText = null,
    int MatchIndex = 0,
    int MatchCount = 0)
{
    public static WorkbookNavigationResult Failed(string errorMessage) =>
        new(false, errorMessage, null);

    public static WorkbookNavigationResult Selected(GridRange range) =>
        new(true, null, range);

    public static WorkbookNavigationResult Found(
        GridRange range,
        string matchedText,
        int matchIndex,
        int matchCount) =>
        new(true, null, range, matchedText, matchIndex, matchCount);
}

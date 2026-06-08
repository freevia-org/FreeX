using FreeX.Core.Model;

namespace FreeX.App.Services;

public sealed record WorkbookReplaceResult(
    bool Success,
    string? ErrorMessage,
    int ReplacedCount,
    GridRange? ReplacedRange = null,
    int MatchIndex = 0,
    int MatchCount = 0)
{
    public static WorkbookReplaceResult Failed(string errorMessage) =>
        new(false, errorMessage, 0);

    public static WorkbookReplaceResult Replaced(
        int replacedCount,
        GridRange? replacedRange = null,
        int matchIndex = 0,
        int matchCount = 0) =>
        new(true, null, replacedCount, replacedRange, matchIndex, matchCount);
}

using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum FindReplaceStatusKind
{
    None,
    NoMatches,
    Match,
    NoReplacements,
    Replacements
}

public sealed record FindReplaceNavigationPlan(
    bool HasMatch,
    int MatchIndex,
    string StatusText,
    FindReplaceStatusKind StatusKind);

public sealed record FindReplaceReplacementStatus(
    string StatusText,
    FindReplaceStatusKind StatusKind);

public static class FindReplaceDialogPlanner
{
    public const string FindTitle = "Find";
    public const string FindAndReplaceTitle = "Find and Replace";
    public const string NoMatchesStatus = "No matches found.";
    public const string NoReplacementsStatus = "No replacements made.";

    public static string TitleForMode(bool showReplace) =>
        showReplace ? FindAndReplaceTitle : FindTitle;

    public static TextSearchOptions BuildOptions(bool matchCase, bool wholeWord) => new()
    {
        MatchCase = matchCase,
        WholeWord = wholeWord,
    };

    public static bool CanReplaceAll(string? query) =>
        !string.IsNullOrEmpty(query);

    public static int ReplacementTargetIndex(int currentMatchIndex, int matchCount)
    {
        if (matchCount <= 0)
            return -1;

        return currentMatchIndex >= 0 && currentMatchIndex < matchCount
            ? currentMatchIndex
            : 0;
    }

    public static FindReplaceNavigationPlan Navigate(
        int currentMatchIndex,
        int matchCount,
        int direction)
    {
        if (matchCount <= 0)
            return new FindReplaceNavigationPlan(false, -1, NoMatchesStatus, FindReplaceStatusKind.NoMatches);

        var nextIndex = (currentMatchIndex + direction + matchCount) % matchCount;
        return new FindReplaceNavigationPlan(
            true,
            nextIndex,
            $"Match {nextIndex + 1} of {matchCount}",
            FindReplaceStatusKind.Match);
    }

    public static FindReplaceReplacementStatus ReplacementStatus(int replacementCount) =>
        replacementCount == 0
            ? new FindReplaceReplacementStatus(NoReplacementsStatus, FindReplaceStatusKind.NoReplacements)
            : new FindReplaceReplacementStatus(
                $"{replacementCount} replacement(s) made.",
                FindReplaceStatusKind.Replacements);
}

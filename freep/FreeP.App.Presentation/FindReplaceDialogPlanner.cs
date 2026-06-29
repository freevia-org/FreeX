using Free.Shared.AppServices;
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
    public const string NoMatchesStatus = FindReplaceDialogPolicy.NoMatchesStatus;
    public const string NoReplacementsStatus = FindReplaceDialogPolicy.NoReplacementsStatus;

    public static string TitleForMode(bool showReplace) =>
        showReplace ? FindAndReplaceTitle : FindTitle;

    public static TextSearchOptions BuildOptions(bool matchCase, bool wholeWord) => new()
    {
        MatchCase = matchCase,
        WholeWord = wholeWord,
    };

    public static bool CanReplaceAll(string? query) =>
        FindReplaceDialogPolicy.CanRunWithQuery(query);

    public static int ReplacementTargetIndex(int currentMatchIndex, int matchCount) =>
        FindReplaceDialogPolicy.ReplacementTargetIndex(currentMatchIndex, matchCount);

    public static FindReplaceNavigationPlan Navigate(
        int currentMatchIndex,
        int matchCount,
        int direction)
    {
        var plan = FindReplaceDialogPolicy.Navigate(currentMatchIndex, matchCount, direction);

        return new FindReplaceNavigationPlan(
            plan.HasMatch,
            plan.MatchIndex,
            plan.StatusText,
            ToLocalStatusKind(plan.StatusKind));
    }

    public static FindReplaceReplacementStatus ReplacementStatus(int replacementCount)
    {
        var status = FindReplaceDialogPolicy.BuildReplacementStatus(replacementCount);
        return new FindReplaceReplacementStatus(status.StatusText, ToLocalStatusKind(status.StatusKind));
    }

    private static FindReplaceStatusKind ToLocalStatusKind(FindReplacePolicyStatusKind statusKind) =>
        statusKind switch
        {
            FindReplacePolicyStatusKind.NoMatches => FindReplaceStatusKind.NoMatches,
            FindReplacePolicyStatusKind.Match => FindReplaceStatusKind.Match,
            FindReplacePolicyStatusKind.NoReplacements => FindReplaceStatusKind.NoReplacements,
            FindReplacePolicyStatusKind.Replacements => FindReplaceStatusKind.Replacements,
            _ => FindReplaceStatusKind.None
        };
}

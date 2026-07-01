using Free.Shared.AppServices;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public static class FindReplaceDialogPlanner
{
    public const string FindTitle = "Find";
    public const string FindAndReplaceTitle = "Find and Replace";

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

    public static FindReplaceNavigationPolicyPlan Navigate(
        int currentMatchIndex,
        int matchCount,
        int direction) =>
        FindReplaceDialogPolicy.Navigate(currentMatchIndex, matchCount, direction);

    public static FindReplaceReplacementPolicyStatus ReplacementStatus(int replacementCount) =>
        FindReplaceDialogPolicy.BuildReplacementStatus(replacementCount);
}

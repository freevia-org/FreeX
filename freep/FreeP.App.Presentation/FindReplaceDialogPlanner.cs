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

    public static FindReplaceWorkflowPlan BuildWorkflowPlan(
        bool showReplace,
        string? query,
        string? replacement,
        bool matchCase,
        bool wholeWord,
        IReadOnlyList<TextSearchMatch> matches,
        int currentMatchIndex,
        string? statusText = null,
        FindReplacePolicyStatusKind statusKind = FindReplacePolicyStatusKind.None)
    {
        ArgumentNullException.ThrowIfNull(matches);

        var normalizedQuery = query ?? string.Empty;
        var normalizedReplacement = replacement ?? string.Empty;
        var hasQuery = FindReplaceDialogPolicy.CanRunWithQuery(normalizedQuery);
        var matchCount = matches.Count;
        var targetIndex = ReplacementTargetIndex(currentMatchIndex, matchCount);

        return new FindReplaceWorkflowPlan(
            TitleForMode(showReplace),
            showReplace,
            normalizedQuery,
            normalizedReplacement,
            matchCase,
            wholeWord,
            matchCount,
            currentMatchIndex >= 0 && currentMatchIndex < matchCount ? currentMatchIndex : -1,
            statusText ?? string.Empty,
            statusKind,
            hasQuery,
            matchCount > 0,
            showReplace && targetIndex >= 0,
            showReplace && hasQuery);
    }
}

public sealed record FindReplaceWorkflowPlan(
    string Title,
    bool ShowReplace,
    string Query,
    string Replacement,
    bool MatchCase,
    bool WholeWord,
    int MatchCount,
    int CurrentMatchIndex,
    string StatusText,
    FindReplacePolicyStatusKind StatusKind,
    bool CanSearch,
    bool CanNavigate,
    bool CanReplace,
    bool CanReplaceAll);

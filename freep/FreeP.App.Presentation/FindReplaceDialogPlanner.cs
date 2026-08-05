using Free.Shared.AppServices;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum FindReplaceDialogOptionKind
{
    MatchCase,
    WholeWord,
}

public enum FindReplaceDialogAction
{
    FindNext,
    FindPrevious,
    ReplaceCurrent,
    ReplaceAll,
}

public sealed record FindReplaceDialogOption(
    FindReplaceDialogOptionKind Kind,
    string DisplayText);

public sealed record FindReplaceDialogActionOption(
    FindReplaceDialogAction Action,
    string DisplayText);

public sealed record FindReplaceDialogSurfacePlan(
    string FindLabel,
    string ReplaceLabel,
    IReadOnlyList<FindReplaceDialogOption> Options,
    IReadOnlyList<FindReplaceDialogActionOption> Actions,
    string CloseLabel)
{
    public string OptionLabel(FindReplaceDialogOptionKind kind) =>
        Options.First(option => option.Kind == kind).DisplayText;

    public string ActionLabel(FindReplaceDialogAction action) =>
        Actions.First(option => option.Action == action).DisplayText;
}

public sealed record FindReplaceDialogInitialState(
    bool ShowReplace,
    string Query,
    string Replacement,
    bool MatchCase,
    bool WholeWord);

public static class FindReplaceDialogPlanner
{
    public const string FindTitle = "Find";
    public const string FindAndReplaceTitle = "Find and Replace";
    public const string FindLabel = "Find what:";
    public const string ReplaceLabel = "Replace with:";
    public const string MatchCaseLabel = "Match case";
    public const string WholeWordLabel = "Whole word";
    public const string FindNextLabel = "Find Next";
    public const string FindPreviousLabel = "Find Previous";
    public const string ReplaceActionLabel = "Replace";
    public const string ReplaceAllLabel = "Replace All";
    public const string CloseLabel = "Close";

    public static IReadOnlyList<FindReplaceDialogOption> Options { get; } =
    [
        new(FindReplaceDialogOptionKind.MatchCase, MatchCaseLabel),
        new(FindReplaceDialogOptionKind.WholeWord, WholeWordLabel),
    ];

    public static IReadOnlyList<FindReplaceDialogActionOption> Actions { get; } =
    [
        new(FindReplaceDialogAction.FindNext, FindNextLabel),
        new(FindReplaceDialogAction.FindPrevious, FindPreviousLabel),
        new(FindReplaceDialogAction.ReplaceCurrent, ReplaceActionLabel),
        new(FindReplaceDialogAction.ReplaceAll, ReplaceAllLabel),
    ];

    public static FindReplaceDialogSurfacePlan BuildSurfacePlan() => new(
        FindLabel,
        ReplaceLabel,
        Options,
        Actions,
        CloseLabel);

    public static FindReplaceDialogInitialState BuildInitialState(bool showReplace) => new(
        showReplace,
        string.Empty,
        string.Empty,
        MatchCase: false,
        WholeWord: false);

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

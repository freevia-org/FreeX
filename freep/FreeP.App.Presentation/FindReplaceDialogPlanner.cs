using Free.Shared.AppServices;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum FindReplaceDialogOptionKind
{
    MatchCase,
    WholeWord,
}

public enum FindReplaceDialogField
{
    Query,
    Replacement,
    MatchCase,
    WholeWord,
    Status,
}

public enum FindReplaceDialogAction
{
    FindNext,
    FindPrevious,
    ReplaceCurrent,
    ReplaceAll,
    Close,
}

public sealed record FindReplaceDialogOption(
    FindReplaceDialogOptionKind Kind,
    string DisplayText);

public sealed record FindReplaceDialogActionOption(
    FindReplaceDialogAction Action,
    string DisplayText);

public sealed record FindReplaceDialogSurfacePlan(
    PresentationDialogSurfacePlan<FindReplaceDialogField, FindReplaceDialogAction> Schema,
    string FindOnlyTitle)
{
    public string FindLabel => Field(FindReplaceDialogField.Query).Label;

    public string ReplaceLabel => Field(FindReplaceDialogField.Replacement).Label;

    public IReadOnlyList<FindReplaceDialogOption> Options { get; } =
    [
        new(FindReplaceDialogOptionKind.MatchCase,
            Schema.Field(FindReplaceDialogField.MatchCase).Label),
        new(FindReplaceDialogOptionKind.WholeWord,
            Schema.Field(FindReplaceDialogField.WholeWord).Label),
    ];

    public IReadOnlyList<FindReplaceDialogActionOption> Actions { get; } = Schema.Actions
        .Where(action => action.Id != FindReplaceDialogAction.Close)
        .Select(action => new FindReplaceDialogActionOption(action.Id, action.Label))
        .ToArray();

    public string CloseLabel => Action(FindReplaceDialogAction.Close).Label;

    public PresentationDialogFieldPlan<FindReplaceDialogField> Field(
        FindReplaceDialogField field) => Schema.Field(field);

    public PresentationDialogActionPlan<FindReplaceDialogAction> Action(
        FindReplaceDialogAction action) => Schema.Action(action);

    public string OptionLabel(FindReplaceDialogOptionKind kind) =>
        Options.First(option => option.Kind == kind).DisplayText;

    public string ActionLabel(FindReplaceDialogAction action) =>
        Actions.First(option => option.Action == action).DisplayText;

    public string TitleForMode(bool showReplace) =>
        TitleForMode(FindReplaceDialogPolicy.OpenModeFor(showReplace));

    public string TitleForMode(FindReplaceOpenMode mode) =>
        FindReplaceDialogPolicy.ShowsReplaceSurface(mode) ? Schema.Title : FindOnlyTitle;
}

public static class FindReplaceDialogSurfaceCatalog
{
    public static FindReplaceDialogSurfacePlan Surface { get; } = new(
        new PresentationDialogSurfacePlan<FindReplaceDialogField, FindReplaceDialogAction>(
            FindReplaceDialogPlanner.FindAndReplaceTitle,
            "Find and Replace dialog",
            "FreeP.FindReplace.Window",
            [
                Field(FindReplaceDialogField.Query, PresentationDialogControlKind.Text,
                    FindReplaceDialogPlanner.FindLabel, "Find text", "Enter text to find."),
                Field(FindReplaceDialogField.Replacement, PresentationDialogControlKind.Text,
                    FindReplaceDialogPlanner.ReplaceLabel, "Replacement text"),
                Field(FindReplaceDialogField.MatchCase, PresentationDialogControlKind.Toggle,
                    FindReplaceDialogPlanner.MatchCaseLabel, "Match case"),
                Field(FindReplaceDialogField.WholeWord, PresentationDialogControlKind.Toggle,
                    FindReplaceDialogPlanner.WholeWordLabel, "Match whole word"),
                Field(FindReplaceDialogField.Status, PresentationDialogControlKind.Status,
                    string.Empty, "Find and replace status"),
            ],
            [
                Action(FindReplaceDialogAction.FindNext,
                    FindReplaceDialogPlanner.FindNextLabel, "Find next match", isDefault: true),
                Action(FindReplaceDialogAction.FindPrevious,
                    FindReplaceDialogPlanner.FindPreviousLabel, "Find previous match"),
                Action(FindReplaceDialogAction.ReplaceCurrent,
                    FindReplaceDialogPlanner.ReplaceActionLabel, "Replace current match"),
                Action(FindReplaceDialogAction.ReplaceAll,
                    FindReplaceDialogPlanner.ReplaceAllLabel, "Replace all matches"),
                Action(FindReplaceDialogAction.Close,
                    FindReplaceDialogPlanner.CloseLabel, "Close find and replace", isCancel: true),
            ]),
        FindReplaceDialogPlanner.FindTitle);

    private static PresentationDialogFieldPlan<FindReplaceDialogField> Field(
        FindReplaceDialogField id,
        PresentationDialogControlKind kind,
        string label,
        string accessibleName,
        string? helpText = null) =>
        new(id, kind, label, accessibleName, $"FreeP.FindReplace.{id}", helpText);

    private static PresentationDialogActionPlan<FindReplaceDialogAction> Action(
        FindReplaceDialogAction id,
        string label,
        string accessibleName,
        bool isDefault = false,
        bool isCancel = false) =>
        new(id, label, accessibleName, $"FreeP.FindReplace.{id}",
            IsDefault: isDefault, IsCancel: isCancel);
}

public sealed record FindReplaceDialogInitialState(
    bool ShowReplace,
    string Query,
    string Replacement,
    bool MatchCase,
    bool WholeWord);

/// <summary>
/// Deliberately NOT shared with FreeX/FreeW (recorded here rather than forced into
/// <c>Free.Shared.AppServices.FindReplaceDialogPolicy</c>):
/// <list type="bullet">
/// <item>The <c>MatchCase</c>/<c>WholeWord</c> pair. FreeX's option set is Within/SearchOrder/LookIn
/// plus match-entire-CELL, and FreeW adds wildcards; only MatchCase is truly common, and each app
/// lowers it into a different core search-options type.</item>
/// <item>The Replace/ReplaceAll enablement composition. Only FreeP computes enablement -- FreeX
/// collapses the buttons instead and FreeW validates on click -- so only its <c>showReplace</c>
/// gate is shared (<see cref="FindReplaceDialogPolicy.ShowsReplaceSurface"/>).</item>
/// <item>Session commit semantics. All three surfaces are modeless with no dirty/OK/Cancel state
/// machine, so there is nothing to unify.</item>
/// </list>
/// </summary>
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

    public static IReadOnlyList<FindReplaceDialogOption> Options =>
        FindReplaceDialogSurfaceCatalog.Surface.Options;

    public static IReadOnlyList<FindReplaceDialogActionOption> Actions =>
        FindReplaceDialogSurfaceCatalog.Surface.Actions;

    public static FindReplaceDialogSurfacePlan BuildSurfacePlan() =>
        FindReplaceDialogSurfaceCatalog.Surface;

    public static FindReplaceDialogInitialState BuildInitialState(bool showReplace) => new(
        showReplace,
        string.Empty,
        string.Empty,
        MatchCase: false,
        WholeWord: false);

    /// <summary>
    /// Projects the host's <c>showReplace</c> flag onto the cross-app open mode owned by
    /// <see cref="FindReplaceDialogPolicy"/>. FreeP renders the mode as a title swap plus a hidden
    /// replacement row; FreeX renders it as a selected TabItem.
    /// </summary>
    public static FindReplaceOpenMode OpenModeFor(bool showReplace) =>
        FindReplaceDialogPolicy.OpenModeFor(showReplace);

    public static bool ShowsReplaceSurface(FindReplaceOpenMode mode) =>
        FindReplaceDialogPolicy.ShowsReplaceSurface(mode);

    public static string TitleForMode(bool showReplace) =>
        FindReplaceDialogSurfaceCatalog.Surface.TitleForMode(OpenModeFor(showReplace));

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
        var openMode = OpenModeFor(showReplace);
        var showsReplaceSurface = ShowsReplaceSurface(openMode);
        var hasQuery = FindReplaceDialogPolicy.CanRunWithQuery(normalizedQuery);
        var matchCount = matches.Count;
        var targetIndex = ReplacementTargetIndex(currentMatchIndex, matchCount);

        return new FindReplaceWorkflowPlan(
            FindReplaceDialogSurfaceCatalog.Surface.TitleForMode(openMode),
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
            showsReplaceSurface && targetIndex >= 0,
            showsReplaceSurface && hasQuery);
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

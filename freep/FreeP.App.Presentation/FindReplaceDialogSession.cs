using Free.Shared.AppServices;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Renderer-neutral state and orchestration for the FreeP find/replace dialog.
/// Hosts retain native controls, focus, events, and workflow-plan rendering.
/// </summary>
public sealed class FindReplaceDialogSession
{
    private readonly EditingSession _editor;
    private readonly Action? _onNavigationOrMutation;
    private readonly FindReplacePolicyTextSpec _policyText;
    private readonly List<TextSearchMatch> _matches = [];
    private int _currentMatchIndex = -1;

    public FindReplaceDialogSession(
        EditingSession editor,
        bool showReplace = false,
        Action? onNavigationOrMutation = null,
        FindReplacePolicyTextSpec? policyText = null)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _onNavigationOrMutation = onNavigationOrMutation;
        _policyText = policyText ?? FreePFindReplacePolicyTextCatalog.BuildTextSpec();
        InitialState = FindReplaceDialogPlanner.BuildInitialState(showReplace);
        Query = InitialState.Query;
        Replacement = InitialState.Replacement;
        MatchCase = InitialState.MatchCase;
        WholeWord = InitialState.WholeWord;
        ShowReplace = InitialState.ShowReplace;
        LastWorkflowPlan = RefreshWorkflowPlan();
    }

    public FindReplaceDialogInitialState InitialState { get; }

    public FindReplaceDialogSurfacePlan Surface =>
        FindReplaceDialogSurfaceCatalog.Surface;

    public string Query { get; private set; } = string.Empty;

    public string Replacement { get; private set; } = string.Empty;

    public bool MatchCase { get; private set; }

    public bool WholeWord { get; private set; }

    public bool ShowReplace { get; private set; }

    public FindReplaceWorkflowPlan LastWorkflowPlan { get; private set; }

    public FindReplaceWorkflowPlan SetShowReplace(bool showReplace)
    {
        ShowReplace = showReplace;
        return RefreshWorkflowPlan();
    }

    public FindReplaceWorkflowPlan SetQuery(string? query)
    {
        Query = query ?? string.Empty;
        return InvalidateSearch();
    }

    public FindReplaceWorkflowPlan SetReplacement(string? replacement)
    {
        Replacement = replacement ?? string.Empty;
        return RefreshWorkflowPlan();
    }

    public FindReplaceWorkflowPlan SetMatchCase(bool matchCase)
    {
        MatchCase = matchCase;
        return InvalidateSearch();
    }

    public FindReplaceWorkflowPlan SetWholeWord(bool wholeWord)
    {
        WholeWord = wholeWord;
        return InvalidateSearch();
    }

    public FindReplaceWorkflowPlan SetInput(
        string? query,
        string? replacement = null,
        bool matchCase = false,
        bool wholeWord = false)
    {
        Query = query ?? string.Empty;
        Replacement = replacement ?? string.Empty;
        MatchCase = matchCase;
        WholeWord = wholeWord;
        return InvalidateSearch();
    }

    public FindReplaceWorkflowPlan Dispatch(FindReplaceDialogAction action) => action switch
    {
        FindReplaceDialogAction.FindNext => Navigate(+1),
        FindReplaceDialogAction.FindPrevious => Navigate(-1),
        FindReplaceDialogAction.ReplaceCurrent => ReplaceCurrent(),
        FindReplaceDialogAction.ReplaceAll => ReplaceAll(),
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };

    public FindReplaceWorkflowPlan Navigate(int direction)
    {
        EnsureMatches();
        var navigation = FindReplaceDialogPlanner.Navigate(
            _currentMatchIndex,
            _matches.Count,
            direction,
            _policyText);
        if (navigation.HasMatch)
        {
            _currentMatchIndex = navigation.MatchIndex;
            _editor.NavigateTo(_matches[_currentMatchIndex]);
            _onNavigationOrMutation?.Invoke();
        }

        return RefreshWorkflowPlan(navigation.StatusText, navigation.StatusKind);
    }

    public FindReplaceWorkflowPlan ReplaceCurrent()
    {
        EnsureMatches();
        var targetIndex = FindReplaceDialogPlanner.ReplacementTargetIndex(
            _currentMatchIndex,
            _matches.Count);
        if (targetIndex < 0)
        {
            return RefreshWorkflowPlan(
                _policyText.NoMatches,
                FindReplacePolicyStatusKind.NoMatches);
        }

        _editor.ReplaceOne(_matches[targetIndex], Replacement);
        _onNavigationOrMutation?.Invoke();
        InvalidateSearch();
        return Navigate(+1);
    }

    public FindReplaceWorkflowPlan ReplaceAll()
    {
        if (!FindReplaceDialogPlanner.CanReplaceAll(Query))
        {
            return RefreshWorkflowPlan(
                FindReplaceDialogPolicy.ValidationMessageFor(
                    FindReplaceValidationErrorKind.SearchTermRequired,
                    _policyText),
                FindReplacePolicyStatusKind.None);
        }

        var replacementCount = _editor.ReplaceAll(Query, Replacement, BuildOptions());
        _onNavigationOrMutation?.Invoke();
        InvalidateSearch();
        var status = FindReplaceDialogPlanner.ReplacementStatus(replacementCount, _policyText);
        return RefreshWorkflowPlan(status.StatusText, status.StatusKind);
    }

    private void EnsureMatches()
    {
        if (_matches.Count == 0)
            _matches.AddRange(_editor.FindAll(Query, BuildOptions()));
    }

    private FindReplaceWorkflowPlan InvalidateSearch()
    {
        _matches.Clear();
        _currentMatchIndex = -1;
        return RefreshWorkflowPlan();
    }

    private TextSearchOptions BuildOptions() =>
        FindReplaceDialogPlanner.BuildOptions(MatchCase, WholeWord);

    private FindReplaceWorkflowPlan RefreshWorkflowPlan(
        string? statusText = null,
        FindReplacePolicyStatusKind statusKind = FindReplacePolicyStatusKind.None)
    {
        LastWorkflowPlan = FindReplaceDialogPlanner.BuildWorkflowPlan(
            ShowReplace,
            Query,
            Replacement,
            MatchCase,
            WholeWord,
            _matches,
            _currentMatchIndex,
            statusText,
            statusKind);
        return LastWorkflowPlan;
    }
}

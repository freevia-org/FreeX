using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public sealed record FindReplaceWorkflowSearchResult(
    bool Success,
    string? ErrorMessage,
    IReadOnlyList<FindResult> Matches,
    int SelectedIndex = -1,
    GridRange? SelectedRange = null)
{
    public FindResult? SelectedMatch =>
        SelectedIndex >= 0 && SelectedIndex < Matches.Count
            ? Matches[SelectedIndex]
            : null;

    public static FindReplaceWorkflowSearchResult Failed(string errorMessage) =>
        new(false, errorMessage, []);

    public static FindReplaceWorkflowSearchResult FoundAll(IReadOnlyList<FindResult> matches) =>
        new(true, null, matches);

    public static FindReplaceWorkflowSearchResult FoundNext(
        IReadOnlyList<FindResult> matches,
        int selectedIndex,
        GridRange selectedRange) =>
        new(true, null, matches, selectedIndex, selectedRange);
}

/// <summary>
/// Controls the two intentional single-replace presentation variants. A submitted dialog can
/// advance across non-replaceable matches in one action and select the following match after a
/// replacement; command-style callers retain the selected match and advance on the next action.
/// </summary>
public sealed record FindReplaceNextBehavior(
    bool SkipNonReplaceableMatches = false,
    bool AdvanceToNextMatchAfterReplacement = false,
    bool ReuseCurrentMatchWithoutNavigation = false)
{
    public static FindReplaceNextBehavior CommandStyle { get; } = new();

    public static FindReplaceNextBehavior SubmittedDialogStyle { get; } = new(
        SkipNonReplaceableMatches: true,
        AdvanceToNextMatchAfterReplacement: true,
        ReuseCurrentMatchWithoutNavigation: true);
}

public sealed record FindReplaceWorkflowReplaceResult(
    bool Success,
    string? ErrorMessage,
    int ReplacedCount,
    int MatchIndex,
    int MatchCount,
    IReadOnlyList<FindResult> CurrentMatches,
    int CurrentIndex = -1,
    GridRange? SelectedRange = null,
    FindResult? ReplacedMatch = null,
    WorkbookCellEditResult? EditResult = null)
{
    public static FindReplaceWorkflowReplaceResult Failed(string errorMessage) =>
        new(false, errorMessage, 0, 0, 0, []);
}

/// <summary>
/// Portable state owner for the Find / Replace workflow shared by native renderers. Search
/// continuity, ordering, replacement command planning, and post-replace advancement live here;
/// hosts provide only workbook access plus native navigation and edit-execution adapters.
/// </summary>
public sealed class FindReplaceWorkflowSession
{
    private static readonly FindReplacePolicyTextSpec DefaultPolicyText =
        FindReplacePolicyTextSpec.NeutralEnglish with
        {
            SearchTermRequired = "Find text is required.",
            NotFoundFormat = "No matches found for \"{0}\"."
        };

    private readonly Func<Workbook> _getWorkbook;
    private readonly Func<CellAddress?> _getActiveCell;
    private readonly Func<CellAddress, WorkbookNavigationResult> _navigateTo;
    private readonly Func<IWorkbookCommand, WorkbookCellEditResult> _executeEdit;
    private readonly FindReplacePolicyTextSpec _policyText;
    private string? _lastFindText;
    private FindOptions? _lastFindOptions;
    private bool _lastFindMatchCase;
    private bool _lastFindMatchEntireCell;
    private FindResult? _lastFindResult;
    private FindResult? _lastReplaceResult;

    public FindReplaceWorkflowSession(
        Func<Workbook> getWorkbook,
        Func<CellAddress?> getActiveCell,
        Func<CellAddress, WorkbookNavigationResult> navigateTo,
        Func<IWorkbookCommand, WorkbookCellEditResult> executeEdit,
        FindReplacePolicyTextSpec? policyText = null)
    {
        ArgumentNullException.ThrowIfNull(getWorkbook);
        ArgumentNullException.ThrowIfNull(getActiveCell);
        ArgumentNullException.ThrowIfNull(navigateTo);
        ArgumentNullException.ThrowIfNull(executeEdit);

        _getWorkbook = getWorkbook;
        _getActiveCell = getActiveCell;
        _navigateTo = navigateTo;
        _executeEdit = executeEdit;
        _policyText = policyText ?? DefaultPolicyText;
    }

    public string LastFindText => _lastFindText ?? "";

    public FindReplaceWorkflowSearchResult FindNext(
        string? searchText = null,
        FindOptions? options = null,
        bool matchCase = false,
        bool matchEntireCell = false)
    {
        var text = searchText ?? _lastFindText ?? string.Empty;
        if (string.IsNullOrEmpty(text) && options?.RequiredFormat is null)
            return FindReplaceWorkflowSearchResult.Failed(
                FindReplaceDialogPolicy.ValidationMessageFor(
                    FindReplaceValidationErrorKind.SearchTermRequired,
                    _policyText));

        if (searchText is null && options is null)
        {
            options = _lastFindOptions;
            matchCase = _lastFindMatchCase;
            matchEntireCell = _lastFindMatchEntireCell;
        }

        var workbook = _getWorkbook();
        var effectiveOptions = ResolveFindOptions(workbook, options, FindLookIn.Formulas);
        var sameSearch =
            string.Equals(_lastFindText, text, StringComparison.Ordinal) &&
            _lastFindOptions == effectiveOptions &&
            _lastFindMatchCase == matchCase &&
            _lastFindMatchEntireCell == matchEntireCell;

        var results = FindReplaceService.Find(workbook, text, effectiveOptions, matchCase, matchEntireCell);
        RememberFindSearch(text, effectiveOptions, matchCase, matchEntireCell);

        if (results.Count == 0)
        {
            ClearLastFindTargets();
            return FindReplaceWorkflowSearchResult.Failed(
                FindReplaceDialogPolicy.BuildNotFoundStatus(text, _policyText));
        }

        var index = GetNextFindResultIndex(workbook, results, effectiveOptions.SearchOrder, sameSearch);
        var result = results[index];
        var navigation = _navigateTo(result.Address);
        if (!navigation.Success || navigation.SelectedRange is null)
            return FindReplaceWorkflowSearchResult.Failed(navigation.ErrorMessage ?? "Find failed.");

        _lastFindResult = result;
        _lastReplaceResult = null;
        return FindReplaceWorkflowSearchResult.FoundNext(results, index, navigation.SelectedRange.Value);
    }

    public FindReplaceWorkflowSearchResult FindAll(
        string searchText,
        FindOptions? options = null,
        bool matchCase = false,
        bool matchEntireCell = false)
    {
        ArgumentNullException.ThrowIfNull(searchText);
        if (string.IsNullOrEmpty(searchText) && options?.RequiredFormat is null)
            return FindReplaceWorkflowSearchResult.Failed(
                FindReplaceDialogPolicy.ValidationMessageFor(
                    FindReplaceValidationErrorKind.SearchTermRequired,
                    _policyText));

        var workbook = _getWorkbook();
        var effectiveOptions = ResolveFindOptions(workbook, options, FindLookIn.Formulas);
        var results = FindReplaceService.Find(workbook, searchText, effectiveOptions, matchCase, matchEntireCell);
        RememberFindSearch(searchText, effectiveOptions, matchCase, matchEntireCell);
        ClearLastFindTargets();
        return FindReplaceWorkflowSearchResult.FoundAll(results);
    }

    public FindReplaceWorkflowReplaceResult ReplaceAll(
        string searchText,
        string replaceText,
        FindOptions? options = null,
        bool matchCase = false,
        bool matchEntireCell = false,
        StyleDiff? replacementFormat = null)
    {
        ArgumentNullException.ThrowIfNull(searchText);
        ArgumentNullException.ThrowIfNull(replaceText);
        if (string.IsNullOrEmpty(searchText) && options?.RequiredFormat is null)
            return FindReplaceWorkflowReplaceResult.Failed(
                FindReplaceDialogPolicy.ValidationMessageFor(
                    FindReplaceValidationErrorKind.SearchTermRequired,
                    _policyText));

        var workbook = _getWorkbook();
        var effectiveOptions = ResolveFindOptions(workbook, options, FindLookIn.Values);
        RememberFindSearch(searchText, effectiveOptions, matchCase, matchEntireCell);
        ClearLastFindTargets();

        var matches = FindReplaceService.Find(workbook, searchText, effectiveOptions, matchCase, matchEntireCell);
        if (matches.Count == 0)
            return new FindReplaceWorkflowReplaceResult(true, null, 0, 0, 0, matches);

        var commands = BuildReplacementCommands(
            workbook,
            matches,
            searchText,
            replaceText,
            effectiveOptions.LookIn,
            matchCase,
            matchEntireCell,
            replacementFormat);
        if (commands.Count == 0)
        {
            return new FindReplaceWorkflowReplaceResult(
                true,
                null,
                0,
                0,
                matches.Count,
                matches);
        }

        var editResult = _executeEdit(ToCommand("Replace All", commands));
        if (!editResult.Success)
            return FindReplaceWorkflowReplaceResult.Failed(editResult.ErrorMessage ?? "Replace All failed.");

        var remaining = FindReplaceService.Find(workbook, searchText, effectiveOptions, matchCase, matchEntireCell);
        return new FindReplaceWorkflowReplaceResult(
            true,
            null,
            commands.Count,
            0,
            matches.Count,
            remaining,
            EditResult: editResult);
    }

    public FindReplaceWorkflowReplaceResult ReplaceNext(
        string searchText,
        string replaceText,
        FindOptions? options = null,
        bool matchCase = false,
        bool matchEntireCell = false,
        StyleDiff? replacementFormat = null,
        FindReplaceNextBehavior? behavior = null)
    {
        ArgumentNullException.ThrowIfNull(searchText);
        ArgumentNullException.ThrowIfNull(replaceText);
        if (string.IsNullOrEmpty(searchText) && options?.RequiredFormat is null)
            return FindReplaceWorkflowReplaceResult.Failed(
                FindReplaceDialogPolicy.ValidationMessageFor(
                    FindReplaceValidationErrorKind.SearchTermRequired,
                    _policyText));

        behavior ??= FindReplaceNextBehavior.CommandStyle;
        var workbook = _getWorkbook();
        var effectiveOptions = ResolveFindOptions(workbook, options, FindLookIn.Values);
        var sameSearchText =
            string.Equals(_lastFindText, searchText, StringComparison.Ordinal) &&
            _lastFindMatchCase == matchCase &&
            _lastFindMatchEntireCell == matchEntireCell;
        var sameSearch =
            sameSearchText &&
            (_lastFindOptions == effectiveOptions || HasLastFindTargetAtActiveCell());

        var matches = FindReplaceService.Find(workbook, searchText, effectiveOptions, matchCase, matchEntireCell);
        RememberFindSearch(searchText, effectiveOptions, matchCase, matchEntireCell);
        if (matches.Count == 0)
        {
            ClearLastFindTargets();
            return new FindReplaceWorkflowReplaceResult(true, null, 0, 0, 0, matches);
        }

        var index = GetReplaceTargetIndex(workbook, matches, effectiveOptions.SearchOrder, sameSearch);
        var canReuseCurrentMatch =
            behavior.ReuseCurrentMatchWithoutNavigation &&
            sameSearch &&
            _lastFindResult is { } currentResult &&
            IsSameFindTarget(matches[index], currentResult);
        var navigation = canReuseCurrentMatch
            ? WorkbookNavigationResult.Selected(new GridRange(matches[index].Address, matches[index].Address))
            : _navigateTo(matches[index].Address);
        if (!navigation.Success)
        {
            ClearLastFindTargets();
            return FindReplaceWorkflowReplaceResult.Failed(navigation.ErrorMessage ?? "Replace failed.");
        }

        for (var attempt = 0; attempt < matches.Count; attempt++)
        {
            var match = matches[index];
            var sheet = workbook.GetSheet(match.Address.Sheet);
            if (sheet is not null &&
                FindReplaceService.TryCreateReplacementCommand(
                    sheet,
                    match,
                    searchText,
                    replaceText,
                    matchCase,
                    matchEntireCell,
                    effectiveOptions.LookIn,
                    replacementFormat,
                    out var command,
                    workbook))
            {
                var editResult = _executeEdit(command);
                if (!editResult.Success)
                {
                    ClearLastFindTargets();
                    return FindReplaceWorkflowReplaceResult.Failed(editResult.ErrorMessage ?? "Replace failed.");
                }

                _lastFindResult = null;
                _lastReplaceResult = match;
                var remaining = FindReplaceService.Find(
                    workbook,
                    searchText,
                    effectiveOptions,
                    matchCase,
                    matchEntireCell);
                var currentIndex = -1;
                var selectedRange = navigation.SelectedRange;
                if (behavior.AdvanceToNextMatchAfterReplacement && remaining.Count > 0)
                {
                    currentIndex = FindFirstResultIndexAfterAddress(
                        workbook,
                        remaining,
                        match.Address,
                        effectiveOptions.SearchOrder);
                    var next = remaining[currentIndex];
                    var nextNavigation = _navigateTo(next.Address);
                    if (!nextNavigation.Success)
                    {
                        ClearLastFindTargets();
                        return FindReplaceWorkflowReplaceResult.Failed(
                            nextNavigation.ErrorMessage ?? "Find after replace failed.");
                    }

                    selectedRange = nextNavigation.SelectedRange;
                    _lastFindResult = next;
                    _lastReplaceResult = null;
                }

                return new FindReplaceWorkflowReplaceResult(
                    true,
                    null,
                    1,
                    index + 1,
                    matches.Count,
                    remaining,
                    currentIndex,
                    selectedRange,
                    match,
                    editResult);
            }

            if (!behavior.SkipNonReplaceableMatches)
            {
                ClearLastFindTargets();
                return new FindReplaceWorkflowReplaceResult(
                    true,
                    null,
                    0,
                    index + 1,
                    matches.Count,
                    matches,
                    index,
                    navigation.SelectedRange);
            }

            index = (index + 1) % matches.Count;
            navigation = _navigateTo(matches[index].Address);
            if (!navigation.Success)
            {
                ClearLastFindTargets();
                return FindReplaceWorkflowReplaceResult.Failed(navigation.ErrorMessage ?? "Replace failed.");
            }
        }

        _lastFindResult = matches[index];
        _lastReplaceResult = null;
        return new FindReplaceWorkflowReplaceResult(
            true,
            null,
            0,
            index + 1,
            matches.Count,
            matches,
            index,
            navigation.SelectedRange);
    }

    private FindOptions ResolveFindOptions(Workbook workbook, FindOptions? options, FindLookIn defaultLookIn)
    {
        var activeSheetId = _getActiveCell()?.Sheet ?? workbook.Sheets.FirstOrDefault()?.Id;
        var effectiveOptions = options ?? new FindOptions(
            Within: FindWithin.Sheet,
            CurrentSheetId: activeSheetId,
            SearchOrder: FindSearchOrder.ByRows,
            LookIn: defaultLookIn);
        if (effectiveOptions.Within == FindWithin.Sheet && effectiveOptions.CurrentSheetId is null)
            effectiveOptions = effectiveOptions with { CurrentSheetId = activeSheetId };
        return effectiveOptions;
    }

    private static IReadOnlyList<IWorkbookCommand> BuildReplacementCommands(
        Workbook workbook,
        IReadOnlyList<FindResult> matches,
        string searchText,
        string replaceText,
        FindLookIn lookIn,
        bool matchCase,
        bool matchEntireCell,
        StyleDiff? replacementFormat)
    {
        var commands = new List<IWorkbookCommand>();
        foreach (var match in matches)
        {
            var sheet = workbook.GetSheet(match.Address.Sheet);
            if (sheet is not null &&
                FindReplaceService.TryCreateReplacementCommand(
                    sheet,
                    match,
                    searchText,
                    replaceText,
                    matchCase,
                    matchEntireCell,
                    lookIn,
                    replacementFormat,
                    out var command,
                    workbook))
            {
                commands.Add(command);
            }
        }

        return commands;
    }

    private static IWorkbookCommand ToCommand(string title, IReadOnlyList<IWorkbookCommand> commands) =>
        commands.Count == 1
            ? commands[0]
            : new CompositeWorkbookCommand(title, commands);

    private int GetNextFindResultIndex(
        Workbook workbook,
        IReadOnlyList<FindResult> results,
        FindSearchOrder searchOrder,
        bool sameSearch)
    {
        if (sameSearch && _lastFindResult is { } lastResult)
        {
            var lastIndex = FindResultIndex(results, lastResult);
            if (lastIndex >= 0)
                return (lastIndex + 1) % results.Count;
        }

        return FindFirstResultAfterActiveCell(workbook, results, searchOrder);
    }

    private int GetReplaceTargetIndex(
        Workbook workbook,
        IReadOnlyList<FindResult> results,
        FindSearchOrder searchOrder,
        bool sameSearch)
    {
        var activeCell = _getActiveCell();
        if (sameSearch &&
            _lastFindResult is { } lastFindResult &&
            (activeCell is null || activeCell.Value.Equals(lastFindResult.Address)))
        {
            var lastIndex = FindResultIndex(results, lastFindResult);
            if (lastIndex >= 0)
                return lastIndex;
        }

        if (sameSearch &&
            activeCell is { } current &&
            _lastReplaceResult is { } lastReplaceResult &&
            current.Equals(lastReplaceResult.Address))
        {
            var nextSameCellIndex = FindNextResultIndexAtSameAddress(results, lastReplaceResult);
            if (nextSameCellIndex >= 0)
                return nextSameCellIndex;
        }

        return FindFirstResultAfterActiveCell(workbook, results, searchOrder);
    }

    private bool HasLastFindTargetAtActiveCell()
    {
        var activeCell = _getActiveCell();
        return activeCell is { } active &&
            ((_lastFindResult is { } lastFindResult && active.Equals(lastFindResult.Address)) ||
             (_lastReplaceResult is { } lastReplaceResult && active.Equals(lastReplaceResult.Address)));
    }

    private void RememberFindSearch(
        string searchText,
        FindOptions options,
        bool matchCase,
        bool matchEntireCell)
    {
        _lastFindText = searchText;
        _lastFindOptions = options;
        _lastFindMatchCase = matchCase;
        _lastFindMatchEntireCell = matchEntireCell;
    }

    private int FindFirstResultAfterActiveCell(
        Workbook workbook,
        IReadOnlyList<FindResult> results,
        FindSearchOrder searchOrder)
    {
        var activeCell = _getActiveCell();
        return activeCell is null
            ? 0
            : FindFirstResultIndexAfterAddress(workbook, results, activeCell.Value, searchOrder);
    }

    private static int FindFirstResultIndexAfterAddress(
        Workbook workbook,
        IReadOnlyList<FindResult> results,
        CellAddress address,
        FindSearchOrder searchOrder)
    {
        for (var index = 0; index < results.Count; index++)
        {
            if (CompareFindOrder(workbook, results[index].Address, address, searchOrder) > 0)
                return index;
        }

        return 0;
    }

    private static int FindResultIndex(IReadOnlyList<FindResult> results, FindResult result)
    {
        for (var index = 0; index < results.Count; index++)
        {
            if (IsSameFindTarget(results[index], result))
                return index;
        }

        return -1;
    }

    private static int FindNextResultIndexAtSameAddress(IReadOnlyList<FindResult> results, FindResult previous)
    {
        var previousIndex = FindResultIndex(results, previous);
        if (previousIndex >= 0)
            return (previousIndex + 1) % results.Count;

        for (var index = 0; index < results.Count; index++)
        {
            if (results[index].Address.Equals(previous.Address) &&
                CompareFindTargetOrder(results[index], previous) > 0)
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsSameFindTarget(FindResult left, FindResult right) =>
        left.Address.Equals(right.Address) &&
        left.Target == right.Target &&
        left.ReplyIndex == right.ReplyIndex;

    private static int CompareFindTargetOrder(FindResult left, FindResult right)
    {
        var targetComparison = GetFindTargetOrder(left).CompareTo(GetFindTargetOrder(right));
        return targetComparison != 0
            ? targetComparison
            : Nullable.Compare(left.ReplyIndex, right.ReplyIndex);
    }

    private static int GetFindTargetOrder(FindResult result) =>
        result.Target switch
        {
            FindResultTarget.ThreadedComment => 0,
            FindResultTarget.ThreadedCommentReply => 1 + Math.Max(0, result.ReplyIndex ?? 0),
            _ => 0
        };

    private static int CompareFindOrder(
        Workbook workbook,
        CellAddress left,
        CellAddress right,
        FindSearchOrder searchOrder)
    {
        var leftSheetIndex = workbook.IndexOfSheet(left.Sheet);
        var rightSheetIndex = workbook.IndexOfSheet(right.Sheet);
        leftSheetIndex = leftSheetIndex < 0 ? int.MaxValue : leftSheetIndex;
        rightSheetIndex = rightSheetIndex < 0 ? int.MaxValue : rightSheetIndex;
        var sheetComparison = leftSheetIndex.CompareTo(rightSheetIndex);
        if (sheetComparison != 0)
            return sheetComparison;

        if (searchOrder == FindSearchOrder.ByColumns)
        {
            var columnComparison = left.Col.CompareTo(right.Col);
            return columnComparison != 0 ? columnComparison : left.Row.CompareTo(right.Row);
        }

        var rowComparison = left.Row.CompareTo(right.Row);
        return rowComparison != 0 ? rowComparison : left.Col.CompareTo(right.Col);
    }

    private void ClearLastFindTargets()
    {
        _lastFindResult = null;
        _lastReplaceResult = null;
    }
}

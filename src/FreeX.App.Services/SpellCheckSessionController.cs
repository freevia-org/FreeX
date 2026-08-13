using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public enum SpellCheckSessionAction
{
    Stop,
    IgnoreOnce,
    IgnoreAll,
    Change,
    ChangeAll,
    AddToDictionary
}

public enum SpellCheckSessionStatus
{
    Reviewing,
    Complete,
    Stopped,
    Failed
}

public sealed record SpellCheckSessionDecision(
    SpellCheckSessionAction Action,
    string? Replacement = null);

public sealed record SpellCheckIssueDisplayModel(
    SpellingIssue Issue,
    string SheetName,
    string CellReference,
    string ContextText)
{
    public CellAddress Address => Issue.Address;
    public string Word => Issue.Word;
    public string Suggestion => Issue.Suggestion;
    public SpellingIssueSource Source => Issue.Source;
}

public sealed record SpellCheckCommandExecutionResult(
    bool Success,
    string? ErrorMessage = null,
    bool IsNoOp = false);

public sealed record SpellCheckSessionTransition(
    SpellCheckSessionStatus Status,
    SpellCheckIssueDisplayModel? Issue,
    int CorrectionsApplied,
    string? ErrorMessage = null,
    bool CustomDictionaryChanged = false)
{
    public bool RequiresReview => Status == SpellCheckSessionStatus.Reviewing && Issue is not null;
}

public interface ISpellCheckSessionAdapter
{
    Workbook Workbook { get; }
    SheetId ActiveSheetId { get; }
    IList<string> CustomDictionaryWords { get; }

    SpellCheckCommandExecutionResult ExecuteCommand(IWorkbookCommand command);

    void PersistCustomDictionary();
}

public sealed class SpellCheckSessionAdapter : ISpellCheckSessionAdapter
{
    private readonly Func<Workbook> _workbook;
    private readonly Func<SheetId> _activeSheetId;
    private readonly Func<IList<string>> _customDictionaryWords;
    private readonly Func<IWorkbookCommand, SpellCheckCommandExecutionResult> _executeCommand;
    private readonly Action _persistCustomDictionary;

    public SpellCheckSessionAdapter(
        Func<Workbook> workbook,
        Func<SheetId> activeSheetId,
        Func<IList<string>> customDictionaryWords,
        Func<IWorkbookCommand, SpellCheckCommandExecutionResult> executeCommand,
        Action persistCustomDictionary)
    {
        _workbook = workbook ?? throw new ArgumentNullException(nameof(workbook));
        _activeSheetId = activeSheetId ?? throw new ArgumentNullException(nameof(activeSheetId));
        _customDictionaryWords = customDictionaryWords ?? throw new ArgumentNullException(nameof(customDictionaryWords));
        _executeCommand = executeCommand ?? throw new ArgumentNullException(nameof(executeCommand));
        _persistCustomDictionary = persistCustomDictionary ?? throw new ArgumentNullException(nameof(persistCustomDictionary));
    }

    public Workbook Workbook => _workbook();
    public SheetId ActiveSheetId => _activeSheetId();
    public IList<string> CustomDictionaryWords => _customDictionaryWords();

    public SpellCheckCommandExecutionResult ExecuteCommand(IWorkbookCommand command) =>
        _executeCommand(command);

    public void PersistCustomDictionary() => _persistCustomDictionary();
}

public sealed class SpellCheckSessionController
{
    private readonly ISpellCheckSessionAdapter _adapter;
    private readonly HashSet<string> _ignoredWords = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<SpellingIssueKey> _ignoredIssues = [];
    private HashSet<string> _customDictionary = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<SpellingIssue> _issues = [];
    private SheetId _sheetId;
    private SpellingIssue? _currentIssue;
    private int _correctionsApplied;
    private bool _started;

    public SpellCheckSessionController(ISpellCheckSessionAdapter adapter)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
    }

    public SpellCheckSessionTransition Start()
    {
        _sheetId = _adapter.ActiveSheetId;
        _customDictionary = SpellCheckWorkflowPlanner.CreateCustomDictionary(_adapter.CustomDictionaryWords);
        _ignoredWords.Clear();
        _ignoredIssues.Clear();
        _issues = [];
        _currentIssue = null;
        _correctionsApplied = 0;
        _started = true;
        return ScanNextIssue();
    }

    public SpellCheckSessionTransition Apply(SpellCheckSessionDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);

        if (!_started || _currentIssue is null)
            return Failed("No spelling issue is active.");

        var issue = _currentIssue;
        switch (decision.Action)
        {
            case SpellCheckSessionAction.Stop:
                _currentIssue = null;
                return new(SpellCheckSessionStatus.Stopped, null, _correctionsApplied);

            case SpellCheckSessionAction.IgnoreOnce:
                _ignoredIssues.Add(SpellCheckWorkflowPlanner.CreateIssueKey(issue));
                return ScanNextIssue();

            case SpellCheckSessionAction.IgnoreAll:
                _ignoredWords.Add(issue.Word);
                return ScanNextIssue();

            case SpellCheckSessionAction.AddToDictionary:
                var dictionaryChanged = SpellCheckWorkflowPlanner.AddCustomDictionaryWord(
                    _adapter.CustomDictionaryWords,
                    _customDictionary,
                    issue.Word);
                if (dictionaryChanged)
                    _adapter.PersistCustomDictionary();
                return ScanNextIssue(dictionaryChanged);

            case SpellCheckSessionAction.Change:
                return ExecuteReplacement(
                    SpellCheckWorkflowPlanner.BuildReplacementCommand(
                        issue,
                        NormalizeReplacement(decision.Replacement, issue.Suggestion)),
                    correctionCount: 1);

            case SpellCheckSessionAction.ChangeAll:
                var replacement = NormalizeReplacement(decision.Replacement, issue.Suggestion);
                var command = SpellCheckWorkflowPlanner.BuildReplaceAllCommand(_issues, issue.Word, replacement);
                if (command is null)
                    return ScanNextIssue();

                var correctionCount = _issues.Count(candidate =>
                    string.Equals(candidate.Word, issue.Word, StringComparison.OrdinalIgnoreCase));
                return ExecuteReplacement(command, correctionCount);

            default:
                throw new ArgumentOutOfRangeException(nameof(decision), decision.Action, "Unknown spell-check action.");
        }
    }

    private SpellCheckSessionTransition ExecuteReplacement(
        IWorkbookCommand command,
        int correctionCount)
    {
        var execution = _adapter.ExecuteCommand(command);
        if (!execution.Success)
            return Failed(execution.ErrorMessage);

        if (!execution.IsNoOp)
            _correctionsApplied += correctionCount;
        return ScanNextIssue();
    }

    private SpellCheckSessionTransition ScanNextIssue(bool customDictionaryChanged = false)
    {
        var scan = SpellCheckWorkflowPlanner.ScanWorksheet(
            _adapter.Workbook,
            _sheetId,
            _customDictionary,
            _ignoredWords,
            _ignoredIssues);
        _issues = scan.Issues;
        if (scan.IsComplete)
        {
            _currentIssue = null;
            return new(
                SpellCheckSessionStatus.Complete,
                null,
                _correctionsApplied,
                CustomDictionaryChanged: customDictionaryChanged);
        }

        _currentIssue = _issues[0];
        return new(
            SpellCheckSessionStatus.Reviewing,
            CreateDisplayModel(_adapter.Workbook, _currentIssue),
            _correctionsApplied,
            CustomDictionaryChanged: customDictionaryChanged);
    }

    private SpellCheckSessionTransition Failed(string? errorMessage) =>
        new(
            SpellCheckSessionStatus.Failed,
            _currentIssue is null ? null : CreateDisplayModel(_adapter.Workbook, _currentIssue),
            _correctionsApplied,
            errorMessage);

    private static SpellCheckIssueDisplayModel CreateDisplayModel(
        Workbook workbook,
        SpellingIssue issue)
    {
        var sheetName = workbook.GetSheet(issue.Address.Sheet)?.Name ?? string.Empty;
        return new(
            issue,
            sheetName,
            issue.Address.ToA1(),
            BuildContextText(issue));
    }

    private static string BuildContextText(SpellingIssue issue)
    {
        if (issue.StartIndex < 0 ||
            issue.Length <= 0 ||
            issue.StartIndex > issue.CellText.Length - issue.Length)
        {
            return issue.CellText;
        }

        return issue.CellText[..issue.StartIndex] +
               "[" + issue.CellText.Substring(issue.StartIndex, issue.Length) + "]" +
               issue.CellText[(issue.StartIndex + issue.Length)..];
    }

    private static string NormalizeReplacement(string? replacement, string fallback) =>
        string.IsNullOrWhiteSpace(replacement) ? fallback : replacement.Trim();
}

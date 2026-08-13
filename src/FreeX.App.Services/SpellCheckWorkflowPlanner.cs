using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public readonly record struct SpellingIssueKey(
    CellAddress Address,
    string Word,
    SpellingIssueSource Source,
    int ReplyIndex,
    int StartIndex);

public sealed record SpellCheckScanResult(IReadOnlyList<SpellingIssue> Issues)
{
    public bool IsComplete => Issues.Count == 0;
}

public static class SpellCheckWorkflowPlanner
{
    public static HashSet<string> CreateCustomDictionary(IEnumerable<string>? persistedWords) =>
        new(
            AppOptions.NormalizeSpellCheckCustomDictionaryWords(persistedWords),
            StringComparer.OrdinalIgnoreCase);

    public static bool AddCustomDictionaryWord(
        IList<string> persistedWords,
        ISet<string> customDictionary,
        string word)
    {
        ArgumentNullException.ThrowIfNull(persistedWords);

        var normalizedWord = AppOptions.NormalizeSpellCheckCustomDictionaryWord(word);
        if (normalizedWord is null)
            return false;

        customDictionary.Add(normalizedWord);

        var normalizedPersistedWords = AppOptions.NormalizeSpellCheckCustomDictionaryWords(persistedWords);
        if (normalizedPersistedWords.Contains(normalizedWord, StringComparer.OrdinalIgnoreCase))
        {
            ReplacePersistedWords(persistedWords, normalizedPersistedWords);
            return false;
        }

        normalizedPersistedWords.Add(normalizedWord);
        normalizedPersistedWords.Sort(StringComparer.OrdinalIgnoreCase);
        ReplacePersistedWords(persistedWords, normalizedPersistedWords);
        return true;
    }

    public static bool RemoveCustomDictionaryWord(IList<string> persistedWords, string word)
    {
        ArgumentNullException.ThrowIfNull(persistedWords);

        var normalizedWord = AppOptions.NormalizeSpellCheckCustomDictionaryWord(word);
        if (normalizedWord is null)
            return false;

        var normalizedPersistedWords = AppOptions.NormalizeSpellCheckCustomDictionaryWords(persistedWords);
        var removed = normalizedPersistedWords.RemoveAll(
            candidate => string.Equals(candidate, normalizedWord, StringComparison.OrdinalIgnoreCase)) > 0;
        ReplacePersistedWords(persistedWords, normalizedPersistedWords);
        return removed;
    }

    public static string? RemoveCustomDictionaryWordAndSelectNext(
        IList<string> persistedWords,
        string word)
    {
        ArgumentNullException.ThrowIfNull(persistedWords);

        var normalizedWord = AppOptions.NormalizeSpellCheckCustomDictionaryWord(word);
        if (normalizedWord is null)
            return null;

        var normalizedPersistedWords = AppOptions.NormalizeSpellCheckCustomDictionaryWords(persistedWords);
        var removedIndex = normalizedPersistedWords.FindIndex(
            candidate => string.Equals(candidate, normalizedWord, StringComparison.OrdinalIgnoreCase));
        if (removedIndex < 0)
            return null;

        normalizedPersistedWords.RemoveAt(removedIndex);
        ReplacePersistedWords(persistedWords, normalizedPersistedWords);
        return normalizedPersistedWords.Count == 0
            ? null
            : normalizedPersistedWords[Math.Clamp(removedIndex, 0, normalizedPersistedWords.Count - 1)];
    }

    public static void ClearCustomDictionaryWords(IList<string> persistedWords)
    {
        ArgumentNullException.ThrowIfNull(persistedWords);
        persistedWords.Clear();
    }

    private static void ReplacePersistedWords(
        IList<string> persistedWords,
        IReadOnlyList<string> normalizedPersistedWords)
    {
        persistedWords.Clear();
        foreach (var persistedWord in normalizedPersistedWords)
            persistedWords.Add(persistedWord);
    }

    public static IReadOnlyList<SpellingIssue> FilterIssues(
        IEnumerable<SpellingIssue> issues,
        ISet<string>? ignoredWords,
        ISet<SpellingIssueKey>? ignoredIssues)
    {
        var filtered = new List<SpellingIssue>();
        foreach (var issue in issues)
        {
            if (ContainsIgnoredWord(ignoredWords, issue.Word) ||
                ignoredIssues?.Contains(CreateIssueKey(issue)) == true)
            {
                continue;
            }

            filtered.Add(issue);
        }

        return filtered;
    }

    public static SpellCheckScanResult ScanWorksheet(
        Workbook workbook,
        SheetId sheetId,
        IReadOnlySet<string>? customDictionary,
        ISet<string> ignoredWords,
        ISet<SpellingIssueKey> ignoredIssues) =>
        new(FilterIssues(
            SpellCheckService.FindIssues(workbook, sheetId, customDictionary),
            ignoredWords,
            ignoredIssues));

    public static SpellingIssueKey CreateIssueKey(SpellingIssue issue) =>
        new(issue.Address, issue.Word, issue.Source, issue.ReplyIndex, issue.StartIndex);

    private static bool ContainsIgnoredWord(IEnumerable<string>? ignoredWords, string word)
    {
        if (ignoredWords is null)
            return false;

        foreach (var ignoredWord in ignoredWords)
        {
            if (string.Equals(ignoredWord, word, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static (CellAddress Address, Cell NewCell) BuildReplacementEdit(
        SpellingIssue issue,
        string replacement) =>
        (issue.Address, Cell.FromValue(new TextValue(SpellCheckService.ApplyCorrection(issue, replacement))));

    public static IWorkbookCommand BuildReplacementCommand(SpellingIssue issue, string replacement) =>
        BuildCommandForIssueText(issue, SpellCheckService.ApplyCorrection(issue, replacement));

    public static IReadOnlyList<(CellAddress Address, Cell NewCell)> BuildReplaceAllEdits(
        IReadOnlyList<SpellingIssue> issues,
        string word,
        string replacement)
    {
        var edits = new List<(CellAddress Address, Cell NewCell)>();
        var editedAddresses = new HashSet<CellAddress>();
        foreach (var issue in issues)
        {
            if (!string.Equals(issue.Word, word, StringComparison.OrdinalIgnoreCase) ||
                !editedAddresses.Add(issue.Address))
            {
                continue;
            }

            var correctedText = SpellCheckService.ApplyCorrectionToAllOccurrences(issue, replacement);
            edits.Add((issue.Address, Cell.FromValue(new TextValue(correctedText))));
        }

        return edits;
    }

    public static IWorkbookCommand? BuildReplaceAllCommand(
        IReadOnlyList<SpellingIssue> issues,
        string word,
        string replacement)
    {
        var commands = new List<IWorkbookCommand>();
        var cellEditsBySheet = new Dictionary<SheetId, List<(CellAddress Address, Cell NewCell)>>();
        var editedTargets = new HashSet<SpellingIssueTargetKey>();

        foreach (var issue in issues)
        {
            if (!string.Equals(issue.Word, word, StringComparison.OrdinalIgnoreCase) ||
                !editedTargets.Add(CreateTargetKey(issue)))
            {
                continue;
            }

            var correctedText = SpellCheckService.ApplyCorrectionToAllOccurrences(issue, replacement);
            if (issue.Source == SpellingIssueSource.CellText)
            {
                if (!cellEditsBySheet.TryGetValue(issue.Address.Sheet, out var edits))
                {
                    edits = [];
                    cellEditsBySheet[issue.Address.Sheet] = edits;
                }

                edits.Add((issue.Address, Cell.FromValue(new TextValue(correctedText))));
                continue;
            }

            commands.Add(BuildCommandForIssueText(issue, correctedText));
        }

        foreach (var (sheetId, edits) in cellEditsBySheet)
            commands.Add(new EditCellsCommand(sheetId, edits));

        return commands.Count switch
        {
            0 => null,
            1 => commands[0],
            _ => new CompositeWorkbookCommand("Spell Check", commands)
        };
    }

    private static IWorkbookCommand BuildCommandForIssueText(SpellingIssue issue, string correctedText) =>
        issue.Source switch
        {
            SpellingIssueSource.CellText => new EditCellsCommand(
                issue.Address.Sheet,
                [(issue.Address, Cell.FromValue(new TextValue(correctedText)))]),
            SpellingIssueSource.Note => new SetCommentCommand(
                issue.Address.Sheet,
                issue.Address,
                correctedText),
            SpellingIssueSource.ThreadedComment => new UpdateThreadedCommentTextCommand(
                issue.Address.Sheet,
                issue.Address,
                correctedText),
            SpellingIssueSource.ThreadedCommentReply => new UpdateThreadedCommentReplyCommand(
                issue.Address.Sheet,
                issue.Address,
                issue.ReplyIndex,
                correctedText),
            _ => throw new ArgumentOutOfRangeException(nameof(issue), issue.Source, "Unknown spelling issue source.")
        };

    private static SpellingIssueTargetKey CreateTargetKey(SpellingIssue issue) =>
        new(issue.Address, issue.Source, issue.ReplyIndex);

    private readonly record struct SpellingIssueTargetKey(
        CellAddress Address,
        SpellingIssueSource Source,
        int ReplyIndex);
}

using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public static class SpellCheckWorkflowPlanner
{
    public static HashSet<string> CreateCustomDictionary(FreeXOptions options) =>
        new(
            FreeXOptions.NormalizeSpellCheckCustomDictionaryWords(options.SpellCheckCustomDictionaryWords),
            StringComparer.OrdinalIgnoreCase);

    public static bool AddCustomDictionaryWord(
        FreeXOptions options,
        ISet<string> customDictionary,
        string word)
    {
        var normalizedWord = FreeXOptions.NormalizeSpellCheckCustomDictionaryWord(word);
        if (normalizedWord is null)
            return false;

        customDictionary.Add(normalizedWord);

        var persistedWords = FreeXOptions.NormalizeSpellCheckCustomDictionaryWords(options.SpellCheckCustomDictionaryWords);
        if (persistedWords.Contains(normalizedWord, StringComparer.OrdinalIgnoreCase))
        {
            options.SpellCheckCustomDictionaryWords = persistedWords;
            return false;
        }

        persistedWords.Add(normalizedWord);
        persistedWords.Sort(StringComparer.OrdinalIgnoreCase);
        options.SpellCheckCustomDictionaryWords = persistedWords;
        return true;
    }

    public static IReadOnlyList<SpellingIssue> FilterIssues(
        IEnumerable<SpellingIssue> issues,
        ISet<string> ignoredWords,
        ISet<(CellAddress Address, string Word)> ignoredIssues)
    {
        var filtered = new List<SpellingIssue>();
        foreach (var issue in issues)
        {
            if (ContainsIgnoredWord(ignoredWords, issue.Word) ||
                ignoredIssues.Contains((issue.Address, issue.Word)))
            {
                continue;
            }

            filtered.Add(issue);
        }

        return filtered;
    }

    private static bool ContainsIgnoredWord(IEnumerable<string> ignoredWords, string word)
    {
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
}

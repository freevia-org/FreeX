using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public static class SpellCheckWorkflowPlanner
{
    public static IReadOnlyList<SpellingIssue> FilterIssues(
        IEnumerable<SpellingIssue> issues,
        ISet<string> ignoredWords,
        ISet<(CellAddress Address, string Word)> ignoredIssues)
    {
        var filtered = new List<SpellingIssue>();
        foreach (var issue in issues)
        {
            if (ignoredWords.Contains(issue.Word) ||
                ignoredIssues.Contains((issue.Address, issue.Word)))
            {
                continue;
            }

            filtered.Add(issue);
        }

        return filtered;
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

            edits.Add(BuildReplacementEdit(issue, replacement));
        }

        return edits;
    }
}

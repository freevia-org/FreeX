using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static class SpellCheckWorkflowPlanner
{
    public static IReadOnlyList<SpellingIssue> FilterIssues(
        IEnumerable<SpellingIssue> issues,
        ISet<string> ignoredWords,
        ISet<(CellAddress Address, string Word)> ignoredIssues) =>
        issues
            .Where(issue => !ignoredWords.Contains(issue.Word))
            .Where(issue => !ignoredIssues.Contains((issue.Address, issue.Word)))
            .ToList();

    public static (CellAddress Address, Cell NewCell) BuildReplacementEdit(
        SpellingIssue issue,
        string replacement) =>
        (issue.Address, Cell.FromValue(new TextValue(SpellCheckService.ApplyCorrection(issue, replacement))));

    public static IReadOnlyList<(CellAddress Address, Cell NewCell)> BuildReplaceAllEdits(
        IReadOnlyList<SpellingIssue> issues,
        string word,
        string replacement) =>
        issues
            .Where(issue => string.Equals(issue.Word, word, StringComparison.OrdinalIgnoreCase))
            .GroupBy(issue => issue.Address)
            .Select(group => BuildReplacementEdit(group.First(), replacement))
            .ToList();
}

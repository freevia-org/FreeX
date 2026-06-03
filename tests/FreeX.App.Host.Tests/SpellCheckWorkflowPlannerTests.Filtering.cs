using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class SpellCheckWorkflowPlannerTests
{
    [Fact]
    public void FilterIssues_RemovesIgnoredWordsAndSpecificIgnoredIssues()
    {
        var sheet = SheetId.New();
        var ignoredAddress = new CellAddress(sheet, 2, 1);
        var ignoredIssue = Issue(ignoredAddress, "teh", "teh value");
        var kept = Issue(new CellAddress(sheet, 3, 1), "teh", "teh item");

        var filtered = SpellCheckWorkflowPlanner.FilterIssues(
            [
                Issue(new CellAddress(sheet, 1, 1), "adn", "adn value"),
                ignoredIssue,
                kept
            ],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "adn" },
            new HashSet<SpellingIssueKey> { SpellCheckWorkflowPlanner.CreateIssueKey(ignoredIssue) });

        filtered.Should().ContainSingle().Which.Should().Be(kept);
    }

    [Fact]
    public void FilterIssues_RemovesIgnoredWordsCaseInsensitively()
    {
        var sheet = SheetId.New();
        var kept = Issue(new CellAddress(sheet, 2, 1), "adn", "adn value");

        var filtered = SpellCheckWorkflowPlanner.FilterIssues(
            [
                Issue(new CellAddress(sheet, 1, 1), "TEH", "TEH value"),
                kept
            ],
            new HashSet<string> { "teh" },
            new HashSet<SpellingIssueKey>());

        filtered.Should().ContainSingle().Which.Should().Be(kept);
    }

    [Fact]
    public void FilterIssues_IgnoreOnceKeepsOtherSourcesAtSameAddress()
    {
        var sheet = SheetId.New();
        var address = new CellAddress(sheet, 1, 1);
        var cellIssue = Issue(address, "teh", "teh cell", SpellingIssueSource.CellText, startIndex: 0);
        var noteIssue = Issue(address, "teh", "teh note", SpellingIssueSource.Note, startIndex: 0);

        var filtered = SpellCheckWorkflowPlanner.FilterIssues(
            [cellIssue, noteIssue],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<SpellingIssueKey> { SpellCheckWorkflowPlanner.CreateIssueKey(cellIssue) });

        filtered.Should().ContainSingle().Which.Should().Be(noteIssue);
    }

    [Fact]
    public void FilterIssues_ScansLargeIssueListsInOnePass()
    {
        var sheet = SheetId.New();
        var issues = Enumerable.Range(0, 5_000)
            .Select(index => Issue(
                new CellAddress(sheet, (uint)(index + 1), 1),
                index % 5 == 0 ? "adn" : "teh",
                $"{index} issue"))
            .ToArray();
        var ignoredIssues = issues
            .Where((_, index) => index % 7 == 0)
            .Select(SpellCheckWorkflowPlanner.CreateIssueKey)
            .ToHashSet();

        var filtered = SpellCheckWorkflowPlanner.FilterIssues(
            issues,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "adn" },
            ignoredIssues);

        filtered.Should().HaveCount(3_428);
        foreach (var issue in filtered)
        {
            issue.Word.Equals("adn", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
            ignoredIssues.Contains(SpellCheckWorkflowPlanner.CreateIssueKey(issue)).Should().BeFalse();
        }
    }
}

using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;
using System.IO;

namespace Freexcel.App.Host.Tests;

public sealed class SpellCheckWorkflowPlannerTests
{
    [Fact]
    public void FilterIssues_RemovesIgnoredWordsAndSpecificIgnoredIssues()
    {
        var sheet = SheetId.New();
        var ignoredAddress = new CellAddress(sheet, 2, 1);
        var kept = Issue(new CellAddress(sheet, 3, 1), "teh", "teh item");

        var filtered = SpellCheckWorkflowPlanner.FilterIssues(
            [
                Issue(new CellAddress(sheet, 1, 1), "adn", "adn value"),
                Issue(ignoredAddress, "teh", "teh value"),
                kept
            ],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "adn" },
            new HashSet<(CellAddress Address, string Word)> { (ignoredAddress, "teh") });

        filtered.Should().ContainSingle().Which.Should().Be(kept);
    }

    [Fact]
    public void BuildReplacementEdit_AppliesCorrectionAsTextCellEdit()
    {
        var address = new CellAddress(SheetId.New(), 4, 2);

        var edit = SpellCheckWorkflowPlanner.BuildReplacementEdit(
            Issue(address, "Teh", "Teh value"),
            "the");

        edit.Address.Should().Be(address);
        edit.NewCell.Value.Should().Be(new TextValue("The value"));
    }

    [Fact]
    public void BuildReplaceAllEdits_GroupsDuplicateIssuesByCell()
    {
        var sheet = SheetId.New();
        var firstAddress = new CellAddress(sheet, 1, 1);
        var secondAddress = new CellAddress(sheet, 2, 1);

        var edits = SpellCheckWorkflowPlanner.BuildReplaceAllEdits(
            [
                Issue(firstAddress, "teh", "teh and teh"),
                Issue(firstAddress, "teh", "teh and teh"),
                Issue(secondAddress, "TEH", "TEH value"),
                Issue(new CellAddress(sheet, 3, 1), "adn", "adn value")
            ],
            "teh",
            "the");

        edits.Should().HaveCount(2);
        edits.Select(edit => edit.Address).Should().Equal(firstAddress, secondAddress);
        edits[0].NewCell.Value.Should().Be(new TextValue("the and the"));
        edits[1].NewCell.Value.Should().Be(new TextValue("THE value"));
    }

    [Fact]
    public void BuildReplaceAllEdits_ScansLargeIssueListsWithoutGroupingAllocation()
    {
        var sheet = SheetId.New();
        var issues = Enumerable.Range(0, 5_000)
            .Select(index =>
            {
                var address = new CellAddress(sheet, (uint)(index / 2 + 1), 1);
                return Issue(address, index % 3 == 0 ? "TEH" : "adn", $"{index} TEH value");
            })
            .ToArray();

        var edits = SpellCheckWorkflowPlanner.BuildReplaceAllEdits(issues, "teh", "the");

        edits.Should().HaveCount(1_667);
        edits.Select(edit => edit.Address).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void BuildReplaceAllEdits_UsesSinglePassAddressDeduplication()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find(
            "src",
            "Freexcel.App.Host",
            "SpellCheckWorkflowPlanner.cs"));

        source.Should().Contain("var editedAddresses = new HashSet<CellAddress>();");
        source.Should().NotContain(".GroupBy(");
    }

    private static SpellingIssue Issue(CellAddress address, string word, string cellText) =>
        new(address, word, word.Equals("adn", StringComparison.OrdinalIgnoreCase) ? "and" : "the", cellText);
}

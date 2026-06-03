using System.IO;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class SpellCheckWorkflowPlannerTests
{
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
    public void BuildReplaceAllEdits_PreservesPerOccurrenceCasingWithinCells()
    {
        var sheet = SheetId.New();
        var address = new CellAddress(sheet, 1, 1);
        var issues = SpellCheckService.FindIssuesInCell(address, "teh TEH Teh");

        var edits = SpellCheckWorkflowPlanner.BuildReplaceAllEdits(issues, "teh", "the");

        edits.Should().ContainSingle();
        edits[0].Address.Should().Be(address);
        edits[0].NewCell.Value.Should().Be(new TextValue("the THE The"));
    }

    [Fact]
    public void BuildReplaceAllEdits_CollapsesRepeatedWordRunsWithinCells()
    {
        var sheet = SheetId.New();
        var address = new CellAddress(sheet, 1, 1);
        var issues = SpellCheckService.FindIssuesInCell(address, "the the the and The The file");

        var edits = SpellCheckWorkflowPlanner.BuildReplaceAllEdits(issues, "the the", "the");

        edits.Should().ContainSingle();
        edits[0].Address.Should().Be(address);
        edits[0].NewCell.Value.Should().Be(new TextValue("the and The file"));
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
            "FreeX.App.Host",
            "SpellCheckWorkflowPlanner.cs"));

        source.Should().Contain("var filtered = new List<SpellingIssue>();");
        source.Should().Contain("var editedAddresses = new HashSet<CellAddress>();");
        source.Should().Contain("var editedTargets = new HashSet<SpellingIssueTargetKey>();");
        source.Should().NotContain(".Where(");
        source.Should().NotContain(".GroupBy(");
    }
}

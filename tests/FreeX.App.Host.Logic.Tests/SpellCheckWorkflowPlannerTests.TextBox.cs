using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class SpellCheckWorkflowPlannerTests
{
    // shared-proofing-F1: exercises BOTH the detector (SpellCheckService.FindIssues) and the
    // correction planner (SpellCheckWorkflowPlanner.BuildReplacementCommand) for a text-box
    // misspelling, and asserts they agree end to end -- not just that the raw output contains a
    // literal. Before the fix, FindIssues never produced a TextBox-sourced issue at all (this
    // test's first assertion), and separately BuildCommandForIssueText had no case for
    // SpellingIssueSource.TextBox and would have thrown ArgumentOutOfRangeException had one been
    // constructed by hand.
    [Fact]
    public void FindIssues_And_BuildReplacementCommand_AgreeOnTextBoxMisspelling()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var anchor = new CellAddress(sheet.Id, 2, 2);
        var textBox = new TextBoxModel
        {
            Anchor = anchor,
            Text = "Please recieve teh shipment"
        };
        sheet.TextBoxes.Add(textBox);
        var context = new TestCommandContext(workbook);

        var issue = SpellCheckService.FindIssues(workbook, sheet.Id)
            .Single(candidate => candidate.Word == "recieve");
        issue.Source.Should().Be(SpellingIssueSource.TextBox);
        issue.TextBoxId.Should().Be(textBox.Id);

        var command = SpellCheckWorkflowPlanner.BuildReplacementCommand(issue, "receive");
        var outcome = command.Apply(context);

        outcome.Success.Should().BeTrue();
        textBox.Text.Should().Be("Please receive teh shipment");

        command.Revert(context);

        textBox.Text.Should().Be("Please recieve teh shipment");
    }

    [Fact]
    public void BuildReplaceAllCommand_UpdatesEveryTextBoxWithTheSameMisspelling()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var firstAnchor = new CellAddress(sheet.Id, 1, 1);
        var secondAnchor = new CellAddress(sheet.Id, 3, 3);
        var firstBox = new TextBoxModel { Anchor = firstAnchor, Text = "teh first box" };
        var secondBox = new TextBoxModel { Anchor = secondAnchor, Text = "teh second box" };
        sheet.TextBoxes.Add(firstBox);
        sheet.TextBoxes.Add(secondBox);
        var context = new TestCommandContext(workbook);

        var issues = SpellCheckService.FindIssues(workbook, sheet.Id);
        var command = SpellCheckWorkflowPlanner.BuildReplaceAllCommand(issues, "teh", "the");

        var outcome = command!.Apply(context);

        outcome.Success.Should().BeTrue();
        firstBox.Text.Should().Be("the first box");
        secondBox.Text.Should().Be("the second box");

        command.Revert(context);

        firstBox.Text.Should().Be("teh first box");
        secondBox.Text.Should().Be("teh second box");
    }

    // Sibling no-regression: two text boxes anchored to the SAME cell must be corrected/ignored
    // independently -- the SpellingIssueKey/SpellingIssueTargetKey dedup keys now include
    // TextBoxId precisely so this doesn't collapse into a single edit.
    [Fact]
    public void BuildReplaceAllCommand_TreatsTwoTextBoxesAtTheSameAnchorAsDistinctTargets()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var sharedAnchor = new CellAddress(sheet.Id, 1, 1);
        var firstBox = new TextBoxModel { Anchor = sharedAnchor, Text = "teh first" };
        var secondBox = new TextBoxModel { Anchor = sharedAnchor, Text = "teh second" };
        sheet.TextBoxes.Add(firstBox);
        sheet.TextBoxes.Add(secondBox);
        var context = new TestCommandContext(workbook);

        var issues = SpellCheckService.FindIssues(workbook, sheet.Id);
        issues.Should().HaveCount(2);

        var command = SpellCheckWorkflowPlanner.BuildReplaceAllCommand(issues, "teh", "the");
        command!.Apply(context).Success.Should().BeTrue();

        firstBox.Text.Should().Be("the first");
        secondBox.Text.Should().Be("the second");
    }
}

using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class SpellCheckWorkflowPlannerTests
{
    [Fact]
    public void BuildReplaceAllCommand_UpdatesCellsNotesAndThreadedCommentText()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new TextValue("teh cell and teh total"));
        sheet.Comments[a1] = "teh note and teh note";
        sheet.ThreadedComments[b1] = new ThreadedComment("teh root")
        {
            Replies =
            [
                new CommentReply("teh reply and teh reply"),
                new CommentReply("adn other reply")
            ]
        };
        var issues = SpellCheckService.FindIssues(workbook, sheet.Id);
        var context = new SimpleCtx(workbook);

        var command = SpellCheckWorkflowPlanner.BuildReplaceAllCommand(issues, "teh", "the");
        var outcome = command!.Apply(context);

        outcome.Success.Should().BeTrue();
        sheet.GetCell(a1)!.Value.Should().Be(new TextValue("the cell and the total"));
        sheet.Comments[a1].Should().Be("the note and the note");
        sheet.ThreadedComments[b1].Text.Should().Be("the root");
        sheet.ThreadedComments[b1].Replies[0].Text.Should().Be("the reply and the reply");
        sheet.ThreadedComments[b1].Replies[1].Text.Should().Be("adn other reply");

        command.Revert(context);

        sheet.GetCell(a1)!.Value.Should().Be(new TextValue("teh cell and teh total"));
        sheet.Comments[a1].Should().Be("teh note and teh note");
        sheet.ThreadedComments[b1].Text.Should().Be("teh root");
        sheet.ThreadedComments[b1].Replies[0].Text.Should().Be("teh reply and teh reply");
        sheet.ThreadedComments[b1].Replies[1].Text.Should().Be("adn other reply");
    }

    [Fact]
    public void BuildReplacementCommand_UpdatesThreadedCommentReplyText()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.ThreadedComments[address] = new ThreadedComment("clean root")
        {
            Replies = [new CommentReply("Fix teh reply")]
        };
        var issue = SpellCheckService.FindIssues(workbook, sheet.Id).Single();
        var context = new SimpleCtx(workbook);

        var command = SpellCheckWorkflowPlanner.BuildReplacementCommand(issue, "the");
        var outcome = command.Apply(context);

        outcome.Success.Should().BeTrue();
        sheet.ThreadedComments[address].Replies[0].Text.Should().Be("Fix the reply");

        command.Revert(context);

        sheet.ThreadedComments[address].Replies[0].Text.Should().Be("Fix teh reply");
    }
}

using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class CommentDialogPresentationPlannerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ReplyAcceptanceRejectsEmptyInput(string? input)
    {
        var acceptance = CommentDialogPresentationPlanner.PlanReplyAcceptance(input);

        acceptance.IsAccepted.Should().BeFalse();
        acceptance.Text.Should().BeEmpty();
        acceptance.ValidationMessage.Should().Be(
            CommentDialogPresentationPlanner.Text.ReplyRequiredMessage);
    }

    [Fact]
    public void ReplyAcceptanceTrimsTheCommittedText()
    {
        CommentDialogPresentationPlanner.PlanReplyAcceptance("  Agreed.  ")
            .Should().Be(new CommentTextAcceptance(true, "Agreed."));
    }

    [Fact]
    public void NewCommentEntryOwnsPromptTextAndSharedAcceptance()
    {
        CommentDialogPresentationPlanner.BuildTextEntry(CommentTextEntryKind.NewComment)
            .Should().Be(new CommentTextEntryPresentation(
                "New Comment",
                "Comment:",
                "Comment",
                "Enter comment text."));
        CommentDialogPresentationPlanner.PlanTextAcceptance(
                CommentTextEntryKind.NewComment,
                "  Review this.  ")
            .Should().Be(new CommentTextAcceptance(true, "Review this."));
        CommentDialogPresentationPlanner.PlanTextAcceptance(
                CommentTextEntryKind.NewComment,
                " ")
            .ValidationMessage.Should().Be("Enter comment text.");
    }

    [Fact]
    public void EmptyListOwnsTheDialogTitleSummaryAndEmptyState()
    {
        var presentation = CommentDialogPresentationPlanner.BuildList([]);

        presentation.Title.Should().Be("Comments");
        presentation.SummaryText.Should().Be("0 comment threads");
        presentation.EmptyMessage.Should().Be("No comments in this document.");
        presentation.Rows.Should().BeEmpty();
    }

    [Fact]
    public void RowsOwnThreadNumberingStateReplyGrammarAndRendererProjections()
    {
        var items = new[]
        {
            new CommentListItem(
                0,
                new CommentAnchorPosition(0, 0),
                " Ada ",
                " First line\r\nSecond line ",
                1,
                Resolved: true),
            new CommentListItem(
                4,
                new CommentAnchorPosition(1, 0),
                " ",
                new string('x', 190),
                2,
                Resolved: false),
        };

        var presentation = CommentDialogPresentationPlanner.BuildList(items);

        presentation.SummaryText.Should().Be("2 comment threads");
        presentation.Rows[0].Should().Be(new CommentListRowPresentation(
            DisplayNumber: 1,
            StateLabel: "Resolved",
            ReplyCountLabel: "1 reply",
            Author: "Ada",
            Body: "First line  Second line",
            HeadingText: "#1  Ada  Resolved - 1 reply",
            CompactText: "#1 Resolved - Ada - First line  Second line (1 reply)"));

        presentation.Rows[1].DisplayNumber.Should().Be(5);
        presentation.Rows[1].StateLabel.Should().Be("Open");
        presentation.Rows[1].ReplyCountLabel.Should().Be("2 replies");
        presentation.Rows[1].Author.Should().Be("Unknown");
        presentation.Rows[1].Body.Should().HaveLength(
            CommentDialogPresentationPlanner.MaximumBodyLength);
        presentation.Rows[1].Body.Should().EndWith("...");
    }

    [Fact]
    public void BlankBodyAndSingularThreadCountHaveOnePortableDefinition()
    {
        CommentDialogPresentationPlanner.NormalizeBody(" \r\n ")
            .Should().Be("(blank)");
        CommentDialogPresentationPlanner.FormatThreadCount(1)
            .Should().Be("1 comment thread");
        CommentDialogPresentationPlanner.FormatReplyCount(0)
            .Should().Be("0 replies");
    }
}

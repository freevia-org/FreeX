using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class CommentNavigationPlannerDedupSourceTests
{
    [Fact]
    public void HostCommentNavigationPlannerFacade_IsRemovedAndConsumersUsePresentationPlannerDirectly()
    {
        var hostSourceDirectory = DialogSourceTestSupport.FindHostSourceDirectory("MainWindow.ReviewCommands.cs");
        File.Exists(Path.Combine(hostSourceDirectory, "CommentNavigationPlanner.cs")).Should().BeFalse();

        var reviewCommands = DialogSourceTestSupport.ReadHostSources("MainWindow.ReviewCommands.cs");
        reviewCommands.Should().Contain("using FreeX.App.Presentation.Comments;");
        reviewCommands.Should().Contain("ReviewSessionController.GetSelectedNoteTarget()");
        reviewCommands.Should().Contain("ReviewSessionController.NavigateThreadedComment(previous)");
        reviewCommands.Should().Contain("ReviewSessionController.NavigateNote(previous)");

        var controller = DialogSourceTestSupport.ReadPresentationSources(
            "Comments", "PresentationReviewSessionController.cs");
        controller.Should().Contain("CommentNavigationPlanner.OrderedThreadedCommentAddresses(sheet.ThreadedComments)");
        controller.Should().Contain("CommentNavigationPlanner.OrderedNoteAddresses(sheet.Comments)");

        var commentListWindow = DialogSourceTestSupport.ReadHostSources("CommentListWindow.cs");
        commentListWindow.Should().Contain("using FreeX.App.Presentation.Comments;");
        commentListWindow.Should().Contain("CommentNavigationPlanner.CreateThreadedCommentRows(threadedComments)");
        commentListWindow.Should().Contain("CommentNavigationPlanner.CreateNoteRows(notes)");
        commentListWindow.Should().NotContain("CommentNavigationPlanner.OrderedThreadedCommentAddresses(threadedComments)");
        commentListWindow.Should().NotContain("CommentNavigationPlanner.FormatThreadedComment(threadedComments[address])");
        commentListWindow.Should().NotContain("CommentNavigationPlanner.OrderedNoteAddresses(notes)");

        var printRenderer = DialogSourceTestSupport.ReadHostSources("PrintRenderer.cs");
        printRenderer.Should().Contain("PrintCommentSummaryPlanner.BuildPages(");

        var printRendererComments = DialogSourceTestSupport.ReadHostSources("PrintRenderer.Comments.cs");
        printRendererComments.Should().Contain("using FreeX.App.Presentation.PageLayout;");
        printRendererComments.Should().Contain("PrintCommentSummaryPlanner.WrapOverlayText(");
        printRendererComments.Should().NotContain("CommentNavigationPlanner.FormatThreadedComment(");
        printRendererComments.Should().NotContain("SharedCommentNavigationPlanner");
    }
}

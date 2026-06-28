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
        reviewCommands.Should().Contain("CommentNavigationPlanner.GetDefaultCommentText(sheet.Comments, addr)");
        reviewCommands.Should().Contain("CommentNavigationPlanner.OrderedThreadedCommentAddresses(sheet.ThreadedComments)");
        reviewCommands.Should().Contain("CommentNavigationPlanner.OrderedNoteAddresses(sheet.Comments)");

        var commentListWindow = DialogSourceTestSupport.ReadHostSources("CommentListWindow.cs");
        commentListWindow.Should().Contain("using FreeX.App.Presentation.Comments;");
        commentListWindow.Should().Contain("CommentNavigationPlanner.OrderedThreadedCommentAddresses(threadedComments)");
        commentListWindow.Should().Contain("CommentNavigationPlanner.FormatThreadedComment(threadedComments[address])");
        commentListWindow.Should().Contain("CommentNavigationPlanner.OrderedNoteAddresses(notes)");

        var printRendererComments = DialogSourceTestSupport.ReadHostSources("PrintRenderer.Comments.cs");
        printRendererComments.Should().Contain("using FreeX.App.Presentation.Comments;");
        printRendererComments.Should().Contain("CommentNavigationPlanner.FormatThreadedComment(pair.Value)");
        printRendererComments.Should().NotContain("SharedCommentNavigationPlanner");
    }
}

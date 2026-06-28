using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class CommentNavigationPlannerFacadeTests
{
    [Fact]
    public void HostCommentNavigationPlanner_RemainsThinPresentationFacade()
    {
        var source = DialogSourceTestSupport.ReadHostSources("CommentNavigationPlanner.cs");

        source.Should().Contain(
            "using SharedCommentNavigationPlanner = FreeX.App.Presentation.Comments.CommentNavigationPlanner;");
        source.Should().Contain("SharedCommentNavigationPlanner.OrderedThreadedCommentAddresses(threadedComments)");
        source.Should().Contain("SharedCommentNavigationPlanner.FormatThreadedComment(thread)");
        source.Should().Contain("SharedCommentNavigationPlanner.FormatCellCommentPreview(comments, threadedComments, address)");
        source.Should().NotContain("OrderBy(address => address.Row)");
        source.Should().NotContain("StringBuilder");
        source.Should().NotContain("FormatCommentPart");
        source.Should().NotContain("FindFirstAfter");
    }
}

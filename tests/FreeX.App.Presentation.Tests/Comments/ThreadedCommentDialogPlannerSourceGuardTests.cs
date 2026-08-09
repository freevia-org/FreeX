using FluentAssertions;

namespace FreeX.App.Presentation.Tests.Comments;

public sealed class ThreadedCommentDialogPlannerSourceGuardTests
{
    [Fact]
    public void ThreadedCommentDialogPlanner_IsPortableAndRendererReady()
    {
        var commentsDirectory = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation", "Comments");
        var source = File.ReadAllText(Path.Combine(commentsDirectory, "ThreadedCommentDialogPlanner.cs"));

        source.Should().Contain("public enum ThreadedCommentDialogAction");
        source.Should().Contain("public enum ThreadedCommentDialogValidationError");
        source.Should().Contain("public sealed record ThreadedCommentDialogResult");
        source.Should().Contain("public static bool TryCreateResult");
        source.Should().Contain("public static string FormatReplyChoice");
        source.Should().NotContain("System.Windows");
        source.Should().NotContain("Avalonia");
        source.Should().NotContain("FreeX.App.Host");
    }

    [Fact]
    public void HostThreadedCommentDialog_UsesSharedPresentationPlanner()
    {
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var source = File.ReadAllText(Path.Combine(repoRoot, "src", "FreeX.App.Host", "ThreadedCommentDialog.cs"));

        source.Should().Contain("using FreeX.App.Presentation.Comments;");
        source.Should().Contain("ThreadedCommentDialogPlanner.TryCreateResult");
        source.Should().Contain("ThreadedCommentDialogPlanner.TryCreateReplyEditResult");
        source.Should().Contain("ThreadedCommentDialogPlanner.TryCreateReplyDeleteResult");
        source.Should().Contain("ThreadedCommentDialogPlanner.CreateResult");
        source.Should().NotContain("public enum ThreadedCommentDialogAction");
        source.Should().NotContain("public sealed record ThreadedCommentDialogResult");
    }
}

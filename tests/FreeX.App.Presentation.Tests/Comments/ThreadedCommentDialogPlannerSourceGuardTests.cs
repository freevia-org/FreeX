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
        source.Should().Contain("public sealed record ThreadedCommentReplyPresentationDescriptor");
        source.Should().Contain("public static bool TryCreateResult");
        source.Should().Contain("public static ThreadedCommentReplyPresentationDescriptor DescribeReply");
        source.Should().Contain("public static string FormatReplyChoice");
        source.Should().Contain("public const string ReplySelectorAutomationId");
        source.Should().Contain("public const string SelectedReplyEditorAutomationId");
        source.Should().Contain("public const string UpdateReplyAutomationId");
        source.Should().Contain("public const string DeleteReplyAutomationId");
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

    [Fact]
    public void RendererReplyEditors_UseSharedDescriptorsAndStableSemanticIds()
    {
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var sources = new[]
        {
            File.ReadAllText(Path.Combine(repoRoot, "src", "FreeX.App.Host", "ThreadedCommentDialog.cs")),
            File.ReadAllText(Path.Combine(repoRoot, "src", "FreeX.App.Avalonia", "MainWindow.Comments.cs"))
        };

        foreach (var source in sources)
        {
            source.Should().Contain("ThreadedCommentDialogPlanner.DescribeReply");
            source.Should().Contain("descriptor.AutomationName.Resolve(UiText.Get, UiText.Format)");
            source.Should().Contain("ThreadedCommentDialogPlanner.ReplySelectorAutomationId");
            source.Should().Contain("ThreadedCommentDialogPlanner.SelectedReplyEditorAutomationId");
            source.Should().Contain("ThreadedCommentDialogPlanner.UpdateReplyAutomationId");
            source.Should().Contain("ThreadedCommentDialogPlanner.DeleteReplyAutomationId");
            source.Should().NotContain("\"ThreadedComment_ReplyAutomationNameFormat\"");
            source.Should().NotContain("\"ThreadedCommentReplySelector\"");
            source.Should().NotContain("\"ThreadedCommentSelectedReplyBox\"");
            source.Should().NotContain("\"ThreadedCommentUpdateReplyButton\"");
            source.Should().NotContain("\"ThreadedCommentDeleteReplyButton\"");
        }
    }
}

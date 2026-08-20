using System.IO;

namespace FreeP.App.Avalonia.Tests;

public sealed class ReviewCommentPaneVisualParitySourceTests
{
    [Fact]
    public void WpfAndAvaloniaCommentPanes_ConsumeTheSharedCompactVisualContract()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var sharedMetrics = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Presentation",
            "PresentationCommentPaneVisualMetrics.cs"));
        var avalonia = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Avalonia",
            "MainWindow.cs"));
        var wpf = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Host",
            "MainWindow.cs"));

        sharedMetrics.Should().Contain("public const double CompactControlHeight = 22;");
        sharedMetrics.Should().Contain("public const double CardBottomMargin = 6;");
        sharedMetrics.Should().Contain("ToolbarActionWidth(string commandId)");
        avalonia.Should().Contain("PresentationCommentPaneVisualMetrics.CompactControlHeight");
        avalonia.Should().Contain("PresentationCommentPaneVisualMetrics.CardBottomMargin");
        avalonia.Should().Contain("PresentationCommentPaneVisualMetrics.ToolbarActionWidth(action.CommandId)");
        avalonia.Should().NotContain("PlaceholderText = \"Comment\"");
        wpf.Should().Contain("PresentationCommentPaneVisualMetrics.CompactControlHeight");
        wpf.Should().Contain("PresentationCommentPaneVisualMetrics.CardBottomMargin");
        wpf.Should().Contain("PresentationCommentPaneVisualMetrics.AddCommentInputMinimumWidth");
        wpf.Should().Contain("PresentationCommentPaneVisualMetrics.ToolbarActionWidth(action.CommandId)");
        wpf.Should().Contain(
            "if (action.CommandId == PresentationReviewWorkflowPlanner.ReplyCommentCommandId)");
    }

    [Fact]
    public void WpfAndAvaloniaReviewCommentInteractions_KeepResolveReplyAndSelectionOnTheSharedSession()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var avalonia = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Avalonia",
            "MainWindow.cs"));
        var wpf = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Host",
            "MainWindow.cs"));

        AssertSharedCommentWorkflowForwarders(avalonia);
        AssertSharedCommentWorkflowForwarders(wpf);

        avalonia.Should().Contain(
            "border.PointerPressed += (_, _) => SelectReviewComment(comment.CommentIndex);");
        avalonia.Should().Contain("if (comment.IsSelected && comment.CanReply)");

        wpf.Should().Contain(
            "cardHost.MouseLeftButtonDown += (_, _) => SelectReviewComment(cm.CommentIndex);");
        wpf.Should().Contain("if (!cm.IsSelected || !cm.CanReply)");
    }

    private static void AssertSharedCommentWorkflowForwarders(string source)
    {
        source.Should().Contain("_reviewWorkflowSession.SelectReviewComment(");
        source.Should().Contain("=> _reviewWorkflowSession.ResolveSelectedComment");
        source.Should().Contain("=> _reviewWorkflowSession.ReopenSelectedComment()");
        source.Should().Contain("=> _reviewWorkflowSession.ReplyToSelectedComment");
    }
}

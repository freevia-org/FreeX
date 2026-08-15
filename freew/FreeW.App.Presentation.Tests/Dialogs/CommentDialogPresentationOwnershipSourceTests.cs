namespace FreeW.App.Presentation.Tests;

public sealed class CommentDialogPresentationOwnershipSourceTests
{
    [Fact]
    public void ReplyValidationAndCommentListTextBelongToPresentation()
    {
        var planner = ReadSource(
            "freew", "FreeW.App.Presentation", "Dialogs", "CommentDialogPresentationPlanner.cs");
        var wpf = ReadSource(
            "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs");
        var wpfDialog = ReadSource(
            "freew", "FreeW.App.Host", "CommentReplyDialog.cs");
        var wpfListDialog = ReadSource(
            "freew", "FreeW.App.Host", "CommentListDialog.cs");
        var avaloniaDialog = ReadSource(
            "freew", "FreeW.App.Avalonia", "CommentDialogs.cs");
        var avaloniaShell = ReadSource(
            "freew", "FreeW.App.Avalonia", "MainWindow.cs");

        planner.Should().Contain("public static class CommentDialogPresentationPlanner");
        planner.Should().Contain("PlanTextAcceptance");
        planner.Should().Contain("BuildList");

        wpf.Should().Contain("CommentReplyDialog.Ask(");
        wpfListDialog.Should().Contain("CommentDialogPresentationPlanner.BuildList(items)");
        wpfDialog.Should().Contain(": Free.Shared.Ribbon.Wpf.DialogWindow");
        wpfDialog.Should().Contain("CommentDialogPresentationPlanner.BuildTextEntry(");
        wpfDialog.Should().Contain("CommentDialogPresentationPlanner.PlanTextAcceptance(");
        avaloniaDialog.Should().Contain("CommentDialogPresentationPlanner.PlanTextAcceptance(");
        avaloniaDialog.Should().Contain("CommentDialogPresentationPlanner.BuildList(items)");
        avaloniaShell.Should().Contain(
            "CommentDialogPresentationPlanner.Text.MissingReplyTargetMessage");

        wpf.Should().NotContain("item.ReplyCount == 1");
        wpf.Should().NotContain("comment thread{(items.Count == 1");
        wpf.Should().NotContain("Place the cursor inside a comment");
        wpf.Should().NotContain("This document does not contain any comments.");
        wpfDialog.Should().NotContain("Enter reply text.");
        avaloniaDialog.Should().NotContain("private static string StateText");
        avaloniaDialog.Should().NotContain("private static string TrimForDisplay");
        avaloniaDialog.Should().NotContain("Enter reply text.");
        avaloniaDialog.Should().NotContain("No comments in this document.");
        avaloniaShell.Should().NotContain("Place the caret in a comment to reply.");
    }

    private static string ReadSource(params string[] parts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine([root, .. parts]));
    }
}

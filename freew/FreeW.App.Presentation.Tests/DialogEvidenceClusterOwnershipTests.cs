namespace FreeW.App.Presentation.Tests;

public sealed class DialogEvidenceClusterOwnershipTests
{
    [Fact]
    public void Comment_field_and_manage_sources_use_shared_chrome_and_paired_evidence()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var commandRegistry = Read(root, "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs");
        var wpfComment = Read(root, "freew", "FreeW.App.Host", "CommentListDialog.cs");
        var wpfReply = Read(root, "freew", "FreeW.App.Host", "CommentReplyDialog.cs");
        var wpfField = Read(root, "freew", "FreeW.App.Host", "FieldPickerDialog.cs");
        var avaloniaComment = Read(root, "freew", "FreeW.App.Avalonia", "CommentDialogs.cs");
        var avaloniaField = Read(root, "freew", "FreeW.App.Avalonia", "FinalCommandParityDialogs.cs");
        var avaloniaSources = Read(root, "freew", "FreeW.App.Avalonia", "ReferencesDialogs.cs");
        var catalog = Read(root, "freew", "tools", "FreeW.DialogVisualHarness", "FreeWDialogEvidenceCatalog.cs");
        var wpfFactory = Read(root, "freew", "tools", "FreeW.DialogVisualHarness.Wpf", "WpfDialogRouteFactory.cs");

        wpfComment.Should().Contain(": Free.Shared.Ribbon.Wpf.DialogWindow");
        wpfComment.Should().Contain("CommentDialogPresentationPlanner.BuildList(items)");
        wpfReply.Should().Contain(": Free.Shared.Ribbon.Wpf.DialogWindow");
        wpfReply.Should().Contain("CommentDialogPresentationPlanner.PlanTextAcceptance(");
        wpfField.Should().Contain(": Free.Shared.Ribbon.Wpf.DialogWindow");
        wpfField.Should().Contain("FieldPickerDialogPlanner.TryGetInstruction(");
        commandRegistry.Should().Contain("class ManageSourcesDialogWindow : Free.Shared.Ribbon.Wpf.DialogWindow");
        commandRegistry.Should().Contain("AskManageSourcesForVisualHarness(Window? owner)");
        commandRegistry.Should().NotContain("private static class CommentListDialog");
        commandRegistry.Should().NotContain("private static class FieldPickerDialog");

        avaloniaComment.Should().Contain("class CommentListDialog : FreeWDialogWindow");
        avaloniaField.Should().Contain("class FieldPickerDialog : FreeWDialogWindow");
        avaloniaSources.Should().Contain("class ManageSourcesDialog : FreeWDialogWindow");

        catalog.Should().Contain("Pair(\"comment-list\", \"CommentListDialog\")");
        catalog.Should().Contain("Pair(\"comment-reply\", \"CommentReplyDialog\")");
        catalog.Should().Contain("Pair(\"field-picker\", \"FieldPickerDialog\")");
        catalog.Should().Contain("Pair(\"manage-sources\", \"ManageSourcesDialogWindow\", \"ManageSourcesDialog\"");
        catalog.Should().NotContain("AvaloniaOnly(\"comment-list\"");
        catalog.Should().NotContain("AvaloniaOnly(\"comment-reply\"");
        catalog.Should().NotContain("AvaloniaOnly(\"field-picker\"");
        catalog.Should().NotContain("AvaloniaOnly(\"manage-sources\"");
        wpfFactory.Should().Contain("FreeWRibbonCommands.AskManageSourcesForVisualHarness(owner)");
    }

    private static string Read(string root, params string[] relativeParts) =>
        File.ReadAllText(Path.Combine([root, .. relativeParts]));
}

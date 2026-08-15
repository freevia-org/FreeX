using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Presentation.Tests.Dialogs;

public sealed class LinkBookmarkDialogPlannerTests
{
    [Fact]
    public void Build_normalizes_choices_and_selects_the_first_bookmark()
    {
        var presentation = LinkBookmarkDialogPlanner.Build(
            ["  Introduction  ", "Details", "Introduction", " "]);

        presentation.BookmarkNames.Should().Equal("Introduction", "Details");
        presentation.SelectedIndex.Should().Be(0);
        presentation.IsEmpty.Should().BeFalse();
        presentation.Title.Should().Be("Link to Bookmark");
        presentation.AcceptLabel.Should().Be("OK");
        presentation.CancelLabel.Should().Be("Cancel");
    }

    [Fact]
    public void Build_empty_exposes_the_same_user_feedback_for_both_hosts()
    {
        var presentation = LinkBookmarkDialogPlanner.Build([]);

        presentation.IsEmpty.Should().BeTrue();
        presentation.SelectedIndex.Should().Be(-1);
        presentation.EmptyMessage.Should().NotBeNullOrWhiteSpace();
        presentation.EmptyTitle.Should().Be("FreeW");
    }

    [Theory]
    [InlineData(0, "One")]
    [InlineData(1, "Two")]
    [InlineData(-1, null)]
    [InlineData(2, null)]
    public void PlanAcceptance_requires_a_valid_choice(int selectedIndex, string? expected)
    {
        var presentation = LinkBookmarkDialogPlanner.Build(["One", "Two"]);

        LinkBookmarkDialogPlanner.PlanAcceptance(presentation, selectedIndex).Should().Be(expected);
    }

    [Fact]
    public void Both_renderers_project_the_shared_contract_and_the_route_is_paired()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpfDialog = Read(root, "freew", "FreeW.App.Host", "LinkBookmarkDialog.cs");
        var wpfCommands = Read(root, "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs");
        var avaloniaDialog = Read(root, "freew", "FreeW.App.Avalonia", "InsertDialogs.cs");
        var avaloniaLinkDialog = Slice(
            avaloniaDialog,
            "public sealed class LinkBookmarkDialog",
            "public sealed class QuickPartDialog");
        var avaloniaHost = Read(root, "freew", "FreeW.App.Avalonia", "MainWindow.cs");
        var catalog = Read(root, "freew", "tools", "FreeW.DialogVisualHarness", "FreeWDialogEvidenceCatalog.cs");

        wpfDialog.Should().Contain(": Free.Shared.Ribbon.Wpf.DialogWindow")
            .And.Contain("LinkBookmarkDialogPlanner.PlanAcceptance(")
            .And.Contain("private readonly ListBox _bookmarks")
            .And.Contain("DialogButtonRowFactory.Create(");
        wpfCommands.Should().Contain("LinkBookmarkDialogPlanner.Build(editor.BookmarkNames())")
            .And.Contain("LinkBookmarkDialog.Ask(Window.GetWindow(editor), presentation)")
            .And.NotContain("private static class BookmarkPicker");
        avaloniaLinkDialog.Should().Contain("class LinkBookmarkDialog : FreeWDialogWindow")
            .And.Contain("LinkBookmarkDialogPlanner.PlanAcceptance(")
            .And.Contain("private readonly ListBox _existing")
            .And.NotContain("private readonly ComboBox _existing");
        avaloniaHost.Should().Contain("LinkBookmarkDialogPlanner.Build(_editor.BookmarkNames())")
            .And.Contain("FreeWInfoDialog.ShowAsync(this, presentation.EmptyMessage, presentation.EmptyTitle)");
        catalog.Should().Contain("Pair(\"link-bookmark\", \"LinkBookmarkDialog\")")
            .And.NotContain("AvaloniaOnly(\"link-bookmark\"");
    }

    private static string Read(string root, params string[] relativeParts) =>
        File.ReadAllText(Path.Combine([root, .. relativeParts]));

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        end.Should().BeGreaterThan(start);
        return source[start..end];
    }
}

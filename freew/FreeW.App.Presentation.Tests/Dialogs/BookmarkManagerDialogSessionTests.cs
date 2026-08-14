using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Presentation.Tests.Dialogs;

public sealed class BookmarkManagerDialogSessionTests
{
    [Fact]
    public void Surface_OwnsTextGeometryActionsAndAccessibility()
    {
        var surface = BookmarkManagerDialogPlanner.Surface;

        surface.Title.Should().Be("Bookmark Manager");
        surface.Heading.Should().Be("Bookmarks:");
        surface.DialogWidth.Should().Be(380);
        surface.ListMinWidth.Should().Be(300);
        surface.ListMinHeight.Should().Be(180);
        surface.Actions.Select(action => action.Kind).Should().Equal(Enum.GetValues<BookmarkManagerActionKind>());
        surface.Actions.Select(action => action.AutomationId).Should().OnlyHaveUniqueItems();
        surface.Action(BookmarkManagerActionKind.Close).IsCancel.Should().BeTrue();
        BookmarkManagerDialogPlanner.RemovedStatusText("Here").Should().Be("Removed bookmark \"Here\".");
    }

    [Fact]
    public void Refresh_ProjectsBookmarksInOrderAndSelectsTheFirstItem()
    {
        var session = new BookmarkManagerDialogSession();

        var state = session.Refresh([
            new BookmarkLocation("First", 2),
            new BookmarkLocation("Second", 5),
        ]);

        state.Items.Should().Equal(
            new BookmarkManagerItem("First", 2),
            new BookmarkManagerItem("Second", 5));
        state.SelectedIndex.Should().Be(0);
        state.SelectedName.Should().Be("First");
        state.StatusText.Should().BeEmpty();
        state.CanGoTo.Should().BeTrue();
        state.CanDelete.Should().BeTrue();
    }

    [Fact]
    public void Refresh_RestoresTheSelectedNameOrFallsBackToTheFirstItem()
    {
        var session = new BookmarkManagerDialogSession();
        session.Refresh([
            new BookmarkLocation("First", 1),
            new BookmarkLocation("Second", 2),
        ]);
        session.SelectIndex(1);

        var restored = session.Refresh([
            new BookmarkLocation("Added", 0),
            new BookmarkLocation("Second", 4),
        ]);
        restored.SelectedIndex.Should().Be(1);
        restored.SelectedName.Should().Be("Second");

        var fallback = session.Refresh([
            new BookmarkLocation("Fallback", 8),
            new BookmarkLocation("Other", 9),
        ]);
        fallback.SelectedIndex.Should().Be(0);
        fallback.SelectedName.Should().Be("Fallback");
    }

    [Fact]
    public void EmptyProjectionAndClearedSelectionDisableBothActions()
    {
        var session = new BookmarkManagerDialogSession();

        var empty = session.Refresh([]);

        empty.Items.Should().BeEmpty();
        empty.SelectedIndex.Should().Be(-1);
        empty.SelectedName.Should().BeNull();
        empty.StatusText.Should().Be(BookmarkManagerDialogPlanner.EmptyStatusText);
        empty.CanGoTo.Should().BeFalse();
        empty.CanDelete.Should().BeFalse();
        empty.IsEnabled(BookmarkManagerActionKind.GoTo).Should().BeFalse();
        empty.IsEnabled(BookmarkManagerActionKind.Close).Should().BeTrue();

        session.Refresh([new BookmarkLocation("Target", 3)]);
        var cleared = session.SelectIndex(-1);
        cleared.CanGoTo.Should().BeFalse();
        cleared.CanDelete.Should().BeFalse();
        session.PlanGoTo().Should().BeNull();
        session.PlanDelete().Should().BeNull();
    }

    [Fact]
    public void DeletePlanRefreshesWithFallbackAndRemovedStatus()
    {
        var session = new BookmarkManagerDialogSession();
        session.Refresh([
            new BookmarkLocation("First", 1),
            new BookmarkLocation("Removed", 2),
            new BookmarkLocation("Last", 3),
        ]);
        session.SelectIndex(1);

        var plan = session.PlanDelete();
        var state = session.CompleteDelete(plan!, [
            new BookmarkLocation("First", 1),
            new BookmarkLocation("Last", 3),
        ]);

        plan.Should().Be(new BookmarkManagerDeleteRefreshPlan("Removed"));
        state.SelectedIndex.Should().Be(0);
        state.SelectedName.Should().Be("First");
        state.StatusText.Should().Be("Removed bookmark \"Removed\".");
        state.CanGoTo.Should().BeTrue();
        state.CanDelete.Should().BeTrue();
    }

    [Fact]
    public void DeletePlanKeepsRemovedStatusWhenTheListBecomesEmpty()
    {
        var session = new BookmarkManagerDialogSession();
        session.Refresh([new BookmarkLocation("Only", 7)]);

        var plan = session.PlanDelete();
        var state = session.CompleteDelete(plan!, []);

        state.StatusText.Should().Be("Removed bookmark \"Only\".");
        state.SelectedIndex.Should().Be(-1);
        state.CanGoTo.Should().BeFalse();
        state.CanDelete.Should().BeFalse();
    }

    [Fact]
    public void GoToIntentCarriesBothRendererNavigationKeys()
    {
        var session = new BookmarkManagerDialogSession();
        session.Refresh([
            new BookmarkLocation("First", 1),
            new BookmarkLocation("Target", 12),
        ]);
        session.SelectIndex(1);

        session.PlanGoTo().Should().Be(new BookmarkManagerGoToIntent("Target", 12));
    }

    [Fact]
    public void GoToIntentPreservesExactTableCellAddress()
    {
        var location = new BookmarkLocation(
            "CellTarget",
            3,
            TableRowIndex: 2,
            TableGridColumnIndex: 4,
            TableParagraphIndex: 1);
        var session = new BookmarkManagerDialogSession();
        session.Refresh([location]);

        session.PlanGoTo()!.Location.Should().Be(location);
    }
}

public sealed class BookmarkManagerDialogSourceOwnershipTests
{
    [Fact]
    public void RenderersDelegateSharedWorkflowAndKeepNativeDocumentActions()
    {
        var wpf = ReadSource("freew", "FreeW.App.Host", "BookmarkManagerDialog.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "BookmarkManagerDialog.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("BookmarkManagerDialogSession _session");
            source.Should().Contain("BookmarkManagerDialogPlanner.Surface");
            source.Should().Contain("Surface.Action(BookmarkManagerActionKind.GoTo)");
            source.Should().Contain("state.IsEnabled(BookmarkManagerActionKind.GoTo)");
            source.Should().Contain("_session.Refresh(locations)");
            source.Should().Contain("_session.SelectIndex(_list.SelectedIndex)");
            source.Should().Contain("_session.PlanGoTo()");
            source.Should().Contain("_session.PlanDelete()");
            source.Should().Contain("_session.CompleteDelete(deletePlan, locations)");
            source.Should().NotContain("This document has no bookmarks.");
            source.Should().NotContain("Removed bookmark \\\"");
            source.Should().NotContain("record Item(");
            source.Should().NotContain("Title = \"Bookmark Manager\"");
            source.Should().NotContain("Button(\"Go To\"");
        }

        wpf.Should().Contain("_editor.RemoveBookmark(plan.Name)");
        wpf.Should().Contain("_editor.GoToBookmark(intent.Location)");
        avalonia.Should().Contain("_editor.DeleteBookmark(plan.Name)");
        avalonia.Should().Contain("_editor.GoToBookmark(intent.Location)");

        var wpfEditor = ReadSource("freew", "FreeW.App.Host", "Editing", "DocumentView.cs");
        var avaloniaEditor = ReadSource("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");
        wpfEditor.Should().Contain("TableGridProjection.At(");
        wpfEditor.Should().Contain("PlaceCaretAtTableCellTextOffset(");
        avaloniaEditor.Should().Contain("location.TableGridColumnIndex.Value");
        avaloniaEditor.Should().NotContain("FindBookmarkCell(");
    }

    [Fact]
    public void WpfCommitsBeforeEnumeratingWhileAvaloniaUsesItsNativeDocumentProjection()
    {
        var wpf = ReadSource("freew", "FreeW.App.Host", "BookmarkManagerDialog.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "BookmarkManagerDialog.cs");

        var adapter = wpf.IndexOf("private IReadOnlyList<BookmarkLocation> EnumerateBookmarks()", StringComparison.Ordinal);
        var commit = wpf.IndexOf("_editor.CommitToModel();", adapter, StringComparison.Ordinal);
        var enumerate = wpf.IndexOf("Bookmarks.List(_editor.Model)", adapter, StringComparison.Ordinal);

        adapter.Should().BeGreaterThanOrEqualTo(0);
        commit.Should().BeGreaterThan(adapter);
        enumerate.Should().BeGreaterThan(commit);
        avalonia.Should().Contain("Bookmarks.List(_editor.Document)");
        avalonia.Should().NotContain("CommitToModel");
    }

    [Fact]
    public void PortableSessionHasNoRendererDependencies()
    {
        var source = ReadSource(
            "freew", "FreeW.App.Presentation", "Dialogs", "BookmarkManagerDialogSession.cs");

        source.Should().NotContain("using Avalonia");
        source.Should().NotContain("using System.Windows");
        source.Should().NotContain("DocumentView");
    }

    private static string ReadSource(params string[] parts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }
}

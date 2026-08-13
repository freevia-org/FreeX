using Free.Shared.Ribbon;
using FreeW.App.Presentation.ContextMenus;
using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Panes;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class NavigationPaneSessionTests
{
    [Fact]
    public void Query_projects_matching_subtrees_and_wraps_search_hits()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(Heading("Alpha"));
        var table = new Table();
        var row = new TableRow();
        var cell = new TableCell();
        cell.Paragraphs.Add(new Paragraph("needle in a cell"));
        row.Cells.Add(cell);
        table.Rows.Add(row);
        document.Blocks.Add(table);
        document.Blocks.Add(Heading("Other"));
        document.Blocks.Add(new Paragraph("plain body"));

        var session = new NavigationPaneSession(
            () => document,
            NoOpNavigationMutations());

        var queried = session.SetQuery("NEEDLE");

        queried.State.SearchHits.Should().Equal(1);
        queried.State.Headings.Select(heading => heading.Text).Should().Equal("Alpha");
        queried.State.SearchStatusText.Should().Be("1 of 1");
        queried.NavigateToBlockIndex.Should().Be(1);
        session.StepSearch(-1).NavigateToBlockIndex.Should().Be(1);
    }

    [Fact]
    public void Outline_command_uses_shared_selection_and_mutation_target()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(Heading("First"));
        document.Blocks.Add(Heading("Second"));
        (int Index, bool Up)? moved = null;
        var session = new NavigationPaneSession(
            () => document,
            new NavigationPaneMutationActions(
                (index, up) =>
                {
                    moved = (index, up);
                    return 1;
                },
                _ => { },
                _ => { },
                _ => { },
                _ => { },
                _ => false));
        session.Refresh();
        session.SelectHeading(0);

        var outcome = session.ExecuteOutlineCommand(
            new RibbonCommandId(FreeWContextMenuPlanner.OutlineMoveDown));

        moved.Should().Be((0, false));
        outcome.MutationApplied.Should().BeTrue();
        outcome.State.SelectedHeadingBlockIndex.Should().Be(1);
        outcome.NavigateToBlockIndex.Should().Be(1);
    }

    private static Paragraph Heading(string text) =>
        new(text) { StyleId = "Heading1" };

    private static NavigationPaneMutationActions NoOpNavigationMutations() =>
        new((index, _) => index, _ => { }, _ => { }, _ => { }, _ => { }, _ => false);
}

public sealed class ReviewingPaneSessionTests
{
    [Fact]
    public void Sort_selection_and_single_resolution_share_one_transition_model()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var first = new Paragraph();
        first.Runs.Add(new Run("insert")
        {
            Revision = RevisionKind.Inserted,
            RevisionAuthor = "Bob",
        });
        var second = new Paragraph();
        second.Runs.Add(new Run("delete")
        {
            Revision = RevisionKind.Deleted,
            RevisionAuthor = "Alice",
        });
        document.Blocks.Add(first);
        document.Blocks.Add(second);
        var session = SessionFor(document);

        session.Refresh();
        var sorted = session.SetSortOrder(ReviewRevisionSortOrder.Author);
        sorted.State.Entries.Select(entry => entry.Author).Should().Equal("Alice", "Bob");

        var selected = session.SelectIndex(0);
        selected.NavigateToRevision!.Author.Should().Be("Alice");
        var rejected = session.RejectSelected();

        rejected.MutationApplied.Should().BeTrue();
        rejected.State.Entries.Should().ContainSingle();
        rejected.State.SelectedRevision!.Author.Should().Be("Bob");
        rejected.State.CanResolveSelected.Should().BeTrue();
    }

    [Fact]
    public void Step_wraps_and_bulk_resolution_updates_enablement()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("one") { Revision = RevisionKind.Inserted });
        paragraph.Runs.Add(new Run("two") { Revision = RevisionKind.Deleted });
        document.Blocks.Add(paragraph);
        var session = SessionFor(document);

        session.Refresh();
        session.Step(-1).State.SelectedIndex.Should().Be(1);
        var resolved = session.AcceptAll();

        resolved.MutationApplied.Should().BeTrue();
        resolved.State.HasRevisions.Should().BeFalse();
        resolved.State.SelectedIndex.Should().Be(-1);
    }

    private static ReviewingPaneSession SessionFor(TextDocument document) =>
        new(
            () => ReviewingPaneSession.Enumerate(document),
            new ReviewingPaneMutationActions(
                entry => ReviewingPaneSession.Accept(document, entry),
                entry => ReviewingPaneSession.Reject(document, entry),
                () =>
                {
                    if (!TrackChanges.HasRevisions(document))
                        return false;
                    TrackChanges.AcceptAll(document);
                    return true;
                },
                () =>
                {
                    if (!TrackChanges.HasRevisions(document))
                        return false;
                    TrackChanges.RejectAll(document);
                    return true;
                }));
}

public sealed class DocumentNotesPaneSessionTests
{
    [Fact]
    public void Projection_selection_apply_and_delete_preserve_rich_wrapper_ownership()
    {
        var document = TextDocument.CreateEmpty();
        document.Footnotes[2] = new Footnote(2, "foot body");
        document.Endnotes[1] = new Endnote(1, "end body");
        var session = new DocumentNotesPaneSession(
            () => document,
            new DocumentNotesPaneMutationActions(
                (id, footnote, paragraphs) => Replace(document, id, footnote, paragraphs),
                (id, footnote) => footnote
                    ? document.Footnotes.Remove(id)
                    : document.Endnotes.Remove(id)));

        var shown = session.ShowAndSelect(footnote: false, id: 1);
        shown.State.Items.Select(item => item.Label).Should().Equal("Footnote 2", "Endnote 1");
        shown.State.SelectedNote!.Key.Should().Be(new DocumentNoteKey(false, 1));
        shown.State.EditorDocument!.Blocks.Clear();
        shown.State.EditorDocument.Blocks.Add(new Paragraph("edited end body"));
        document.Endnotes[1].PlainText.Should().Be("end body");

        var applied = session.Apply(shown.State.EditorDocument.Blocks);
        applied.MutationApplied.Should().BeTrue();
        document.Endnotes[1].PlainText.Should().Be("edited end body");

        var deleted = session.DeleteSelected();
        deleted.MutationApplied.Should().BeTrue();
        document.Endnotes.Should().BeEmpty();
        deleted.State.SelectedNote!.Key.Should().Be(new DocumentNoteKey(true, 2));
        deleted.State.CanApply.Should().BeTrue();
    }

    private static bool Replace(
        TextDocument document,
        int id,
        bool footnote,
        IReadOnlyList<Paragraph> paragraphs)
    {
        var content = footnote
            ? document.Footnotes.GetValueOrDefault(id)?.Content
            : document.Endnotes.GetValueOrDefault(id)?.Content;
        if (content is null)
            return false;
        content.Clear();
        foreach (var paragraph in paragraphs)
            content.Add((Paragraph)DocumentMerge.CloneBlock(paragraph));
        return true;
    }
}

public sealed class PaneSessionOwnershipSourceTests
{
    [Fact]
    public void Navigation_renderers_delegate_query_selection_and_outline_commands()
    {
        var (wpf, avaloniaNavigation, _, _) = Sources();

        wpf.Should().Contain("new NavigationPaneSession(");
        avaloniaNavigation.Should().Contain("new NavigationPaneSession(");
        wpf.Should().NotContain("_navSearchHits");
        wpf.Should().NotContain("FilterOutlineToMatches(");
        avaloniaNavigation.Should().NotContain("_searchHits");
        avaloniaNavigation.Should().NotContain("FilterToMatches(");
        avaloniaNavigation.Should().NotContain("switch (commandId.Value)");
    }

    [Fact]
    public void Reviewing_renderers_delegate_order_selection_and_mutation_targeting()
    {
        var (wpf, _, avaloniaReviewing, _) = Sources();

        wpf.Should().Contain("new ReviewingPaneSession(");
        avaloniaReviewing.Should().Contain("new ReviewingPaneSession(");
        wpf.Should().NotContain("RevisionSortComparer.Sort(");
        wpf.Should().NotContain("_reviewEntries");
        avaloniaReviewing.Should().NotContain("ReviewRevisionSortPlanner.Sort(");
        avaloniaReviewing.Should().NotContain("RevisionList.Accept(_editor");
        avaloniaReviewing.Should().NotContain("TrackChanges.AcceptAll(_editor");
    }

    [Fact]
    public void Notes_renderers_delegate_projection_wrapper_and_mutation_targeting()
    {
        var (wpf, _, _, avaloniaNotes) = Sources();

        wpf.Should().Contain("new DocumentNotesPaneSession(");
        avaloniaNotes.Should().Contain("new DocumentNotesPaneSession(");
        wpf.Should().NotContain("private sealed record NoteStub");
        wpf.Should().NotContain("_activeNote");
        wpf.Should().NotContain(".Footnotes.Values.OrderBy(");
        avaloniaNotes.Should().NotContain("private sealed record NoteItem");
        avaloniaNotes.Should().NotContain("DocumentMerge.CloneBlock(");
        avaloniaNotes.Should().NotContain(".Footnotes.Values.OrderBy(");
    }

    private static (string Wpf, string Navigation, string Reviewing, string Notes) Sources()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return (
            Read(root, "freew", "FreeW.App.Host", "MainWindow.cs"),
            Read(root, "freew", "FreeW.App.Avalonia", "NavigationPane.cs"),
            Read(root, "freew", "FreeW.App.Avalonia", "ReviewingPane.cs"),
            Read(root, "freew", "FreeW.App.Avalonia", "NotesPane.cs"));
    }

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(parts.Aggregate(root, Path.Combine));
}

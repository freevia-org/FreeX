using System.Linq;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// STA coverage for the data path behind the Reviewing Pane (Word's Review > Reviewing Pane): the dockable
/// revisions list plus single-revision Accept/Reject and Previous/Next navigation. The pane is built from the
/// editor surface <see cref="DocumentView.ListRevisions"/> (the pure <see cref="RevisionList"/> over the
/// committed model) and acts on the selection via <see cref="DocumentView.AcceptRevision"/> /
/// <see cref="DocumentView.RejectRevision"/> / <see cref="DocumentView.NavigateToRevision"/>. These tests
/// assert that the list reflects the document, that resolving a single entry leaves the others pending, and
/// that navigation runs without disturbing the model. Needs STA + a Dispatcher for the RichTextBox, so
/// <c>[StaFact]</c>.
/// </summary>
public sealed class ReviewingPaneTests
{
    private static DocumentView ViewWithRevisions()
    {
        // "Keep " + [inserted "added "] + [deleted "removed "] + "tail", plus a second paragraph insertion.
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();

        var p0 = new Paragraph();
        p0.Runs.Add(new Run("Keep "));
        p0.Runs.Add(new Run("added ") { Revision = RevisionKind.Inserted, RevisionAuthor = "Alice", RevisionDateXml = "2026-06-19T10:00:00Z" });
        p0.Runs.Add(new Run("removed ") { Revision = RevisionKind.Deleted, RevisionAuthor = "Bob" });
        p0.Runs.Add(new Run("tail"));
        doc.Blocks.Add(p0);

        var p1 = new Paragraph();
        p1.Runs.Add(new Run("plain"));
        p1.Runs.Add(new Run("more") { Revision = RevisionKind.Inserted, RevisionAuthor = "Carol" });
        doc.Blocks.Add(p1);

        var view = new DocumentView();
        view.LoadModel(doc);
        return view;
    }

    [StaFact]
    public void ListRevisions_SurfacesEveryTrackedChangeInReadingOrder()
    {
        var view = ViewWithRevisions();

        var entries = view.ListRevisions();

        entries.Select(e => e.Kind).Should().Equal(
            RevisionEntryKind.Insertion, RevisionEntryKind.Deletion, RevisionEntryKind.Insertion);
        entries.Select(e => e.Author).Should().Equal("Alice", "Bob", "Carol");
    }

    [StaFact]
    public void AcceptRevision_ResolvesOnlyTheSelectedChange()
    {
        var view = ViewWithRevisions();
        var insertion = view.ListRevisions()[0];

        view.AcceptRevision(insertion).Should().BeTrue();

        // The accepted insertion is gone from the list; the other two remain pending.
        var remaining = view.ListRevisions();
        remaining.Should().HaveCount(2);
        remaining.Select(e => e.Author).Should().Equal("Bob", "Carol");
    }

    [StaFact]
    public void RejectRevision_ResolvesOnlyTheSelectedChange_AndRemovesInsertedText()
    {
        var view = ViewWithRevisions();
        var insertion = view.ListRevisions()[0];

        view.RejectRevision(insertion).Should().BeTrue();

        var remaining = view.ListRevisions();
        remaining.Should().HaveCount(2);
        // The rejected insertion's text ("added ") is no longer in the document.
        view.Model.Paragraphs.First().PlainText.Should().NotContain("added");
    }

    [StaFact]
    public void AcceptRevision_ResolvesTheReviewingPaneSelectedEntry_NotTheCaretRelativeEntry()
    {
        var view = ViewWithRevisions();
        var selectedEntry = view.ListRevisions()[1];

        view.AcceptRevision(selectedEntry).Should().BeTrue();

        view.ListRevisions().Select(entry => entry.Author).Should().Equal("Alice", "Carol");
        view.Model.Paragraphs.First().PlainText.Should().NotContain("removed");
    }

    [StaFact]
    public void RejectRevision_ResolvesTheReviewingPaneSelectedEntry_NotTheCaretRelativeEntry()
    {
        var view = ViewWithRevisions();
        var selectedEntry = view.ListRevisions()[1];

        view.RejectRevision(selectedEntry).Should().BeTrue();

        view.ListRevisions().Select(entry => entry.Author).Should().Equal("Alice", "Carol");
        view.Model.Paragraphs.First().PlainText.Should().Contain("removed");
    }

    [StaFact]
    public void ResolvingEveryRevisionOneAtATime_LeavesNoTrackedChanges()
    {
        // The Previous/Next + Accept loop the pane drives: re-list after each single accept until empty.
        var view = ViewWithRevisions();

        while (view.ListRevisions() is { Count: > 0 } list)
            view.AcceptRevision(list[0]).Should().BeTrue();

        view.HasRevisions().Should().BeFalse();
    }

    [StaFact]
    public void NavigateToRevision_DoesNotMutateTheModel()
    {
        var view = ViewWithRevisions();
        var before = view.ListRevisions().Count;

        // Click-to-navigate / Previous-Next target: must be read-only with respect to the revisions.
        foreach (var entry in view.ListRevisions())
            view.NavigateToRevision(entry);

        view.ListRevisions().Should().HaveCount(before);
    }
}

using FreeW.App.Host.Editing;

namespace FreeW.App.Host.Tests;

public sealed class DocumentViewTrackEditTests
{
    private static DocumentView BuildView(string text)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph(text));

        var view = new DocumentView();
        view.LoadModel(doc);
        return view;
    }

    private static Paragraph ParagraphOf(DocumentView view)
    {
        view.CommitToModel();
        return (Paragraph)view.Model.Blocks[0];
    }

    [StaFact]
    public void InsertText_WithTrackChangesOn_RecordsInsertedRevision()
    {
        var view = BuildView("Hello ");
        view.TrackChangesEnabled = true;
        view.MoveCaretToBlockForTest(0, 6);

        view.InsertText("world");

        var paragraph = ParagraphOf(view);
        paragraph.PlainText.Should().Be("Hello world");
        var inserted = paragraph.Runs.Single(r => r.Text == "world");
        inserted.Revision.Should().Be(RevisionKind.Inserted);
        inserted.RevisionAuthor.Should().Be("FreeW User");
        inserted.RevisionDateXml.Should().NotBeNullOrEmpty();
    }

    [StaFact]
    public void Backspace_WithTrackChangesOn_MarksDeletion()
    {
        var view = BuildView("abc");
        view.TrackChangesEnabled = true;
        view.MoveCaretToBlockForTest(0, 3);

        view.BackspaceForTest();

        var paragraph = ParagraphOf(view);
        paragraph.PlainText.Should().Be("abc");
        var deleted = paragraph.Runs.Single(r => r.Revision == RevisionKind.Deleted);
        deleted.Text.Should().Be("c");
        deleted.RevisionAuthor.Should().Be("FreeW User");
    }

    [StaFact]
    public void Delete_WithTrackChangesOn_MarksDeletion()
    {
        var view = BuildView("abc");
        view.TrackChangesEnabled = true;
        view.MoveCaretToBlockForTest(0, 0);

        view.DeleteForwardForTest();

        var paragraph = ParagraphOf(view);
        paragraph.PlainText.Should().Be("abc");
        var deleted = paragraph.Runs.Single(r => r.Revision == RevisionKind.Deleted);
        deleted.Text.Should().Be("a");
    }

    [StaFact]
    public void TypingOverSelection_WithTrackChangesOn_MarksOldDeletedAndNewInserted()
    {
        var view = BuildView("abcdef");
        view.TrackChangesEnabled = true;
        view.SetSelectionRangeForTest(0, 2, 0, 5);

        view.InsertText("Z");

        var paragraph = ParagraphOf(view);
        paragraph.PlainText.Should().Be("abZcdef");
        paragraph.Runs.Should().Contain(r => r.Text == "Z" && r.Revision == RevisionKind.Inserted);
        paragraph.Runs.Should().Contain(r => r.Text == "cde" && r.Revision == RevisionKind.Deleted);
    }

    [StaFact]
    public void AcceptReject_AfterLiveTrackedEdits_ResolvesCorrectly()
    {
        var acceptView = BuildView("abc");
        acceptView.TrackChangesEnabled = true;
        acceptView.MoveCaretToBlockForTest(0, 3);
        acceptView.BackspaceForTest();
        acceptView.AcceptAllRevisions();
        ParagraphOf(acceptView).PlainText.Should().Be("ab");
        ParagraphOf(acceptView).Runs.Should().OnlyContain(r => r.Revision == RevisionKind.None);

        var rejectView = BuildView("abc");
        rejectView.TrackChangesEnabled = true;
        rejectView.MoveCaretToBlockForTest(0, 3);
        rejectView.BackspaceForTest();
        rejectView.RejectAllRevisions();
        ParagraphOf(rejectView).PlainText.Should().Be("abc");
        ParagraphOf(rejectView).Runs.Should().OnlyContain(r => r.Revision == RevisionKind.None);
    }
}

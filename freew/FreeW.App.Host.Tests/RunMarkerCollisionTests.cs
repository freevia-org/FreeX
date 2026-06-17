using FreeW.App.Host.Editing;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Regression coverage for the HIGH QA finding "Run <c>Tag</c> collision": revision, comment and
/// content-control marks were each stamped onto the same <see cref="System.Windows.Documents.Run.Tag"/>,
/// so a run carrying more than one mark lost all but the last on <see cref="DocumentView.CommitToModel"/>.
/// The fix carries a composite marker record so every facet survives the round-trip.
/// </summary>
public sealed class RunMarkerCollisionTests
{
    private static Run RoundTripSingleRun(TextDocument document)
    {
        var view = new DocumentView();
        view.LoadModel(document);
        view.CommitToModel();
        return ((Paragraph)view.Model.Blocks[0]).Runs[0];
    }

    // Build a one-paragraph, one-run document where the run carries the requested marks.
    private static TextDocument DocWithMarkedRun(System.Action<Run> mark, int commentId = 0)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var run = new Run("marked text");
        mark(run);
        var para = new Paragraph();
        para.Runs.Add(run);
        doc.Blocks.Add(para);
        doc.Comments[commentId] = new Comment(commentId) { Author = "QA" };
        return doc;
    }

    [StaFact]
    public void CommentAndRevision_BothSurvive()
    {
        var doc = DocWithMarkedRun(r =>
        {
            r.CommentId = 0;
            r.Revision = RevisionKind.Inserted;
            r.RevisionAuthor = "Reviewer";
        });

        var run = RoundTripSingleRun(doc);

        run.Text.Should().Be("marked text");
        run.CommentId.Should().Be(0, "the comment mark must survive alongside the revision mark");
        run.Revision.Should().Be(RevisionKind.Inserted, "the revision mark must survive alongside the comment mark");
        run.RevisionAuthor.Should().Be("Reviewer");
    }

    [StaFact]
    public void ContentControlAndComment_BothSurvive()
    {
        var doc = DocWithMarkedRun(r =>
        {
            r.CommentId = 0;
            r.Control = new ContentControl(ContentControlKind.PlainText, Alias: "Field");
        });

        var run = RoundTripSingleRun(doc);

        run.CommentId.Should().Be(0, "the comment mark must survive alongside the content control");
        run.Control.Should().NotBeNull("the content control must survive alongside the comment mark");
        run.Control!.Alias.Should().Be("Field");
    }

    [StaFact]
    public void ContentControlAndRevision_BothSurvive()
    {
        var doc = DocWithMarkedRun(r =>
        {
            r.Revision = RevisionKind.Deleted;
            r.Control = new ContentControl(ContentControlKind.PlainText, Tag: "ctl");
        });

        var run = RoundTripSingleRun(doc);

        run.Revision.Should().Be(RevisionKind.Deleted, "the revision must survive alongside the content control");
        run.Control.Should().NotBeNull("the content control must survive alongside the revision");
        run.Control!.Tag.Should().Be("ctl");
    }

    [StaFact]
    public void CommentRevisionAndContentControl_AllThreeSurvive()
    {
        var doc = DocWithMarkedRun(r =>
        {
            r.CommentId = 0;
            r.Revision = RevisionKind.Inserted;
            r.RevisionAuthor = "Reviewer";
            r.Control = new ContentControl(ContentControlKind.PlainText, Alias: "All");
        });

        var run = RoundTripSingleRun(doc);

        run.CommentId.Should().Be(0);
        run.Revision.Should().Be(RevisionKind.Inserted);
        run.RevisionAuthor.Should().Be("Reviewer");
        run.Control.Should().NotBeNull();
        run.Control!.Alias.Should().Be("All");
    }

    [StaFact]
    public void SingleMarks_StillRoundTrip()
    {
        // Guard the common single-mark paths against a regression from the composite-marker change.
        var commentOnly = RoundTripSingleRun(DocWithMarkedRun(r => r.CommentId = 0));
        commentOnly.CommentId.Should().Be(0);
        commentOnly.Revision.Should().Be(RevisionKind.None);
        commentOnly.Control.Should().BeNull();

        var revisionOnly = RoundTripSingleRun(DocWithMarkedRun(r => r.Revision = RevisionKind.Inserted));
        revisionOnly.Revision.Should().Be(RevisionKind.Inserted);
        revisionOnly.CommentId.Should().BeNull();

        var controlOnly = RoundTripSingleRun(DocWithMarkedRun(r =>
            r.Control = new ContentControl(ContentControlKind.PlainText)));
        controlOnly.Control.Should().NotBeNull();
        controlOnly.CommentId.Should().BeNull();
        controlOnly.Revision.Should().Be(RevisionKind.None);
    }
}

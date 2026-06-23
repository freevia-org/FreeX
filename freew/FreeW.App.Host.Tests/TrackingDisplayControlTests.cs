using System.Linq;
using FreeW.App.Host.Editing;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Coverage for Review > Tracking display controls: Show Markup toggles (Insertions/Deletions,
/// Comments) and Display for Review. The primary invariant is round-trip safety: suppressing
/// the visual chrome (colour, decoration, highlight) must never drop revision or comment markers
/// from the model, because <see cref="DocumentView.CommitToModel"/> re-derives the model from
/// the WPF visual tree. These tests run on an STA thread (<c>[StaFact]</c>) because the
/// RichTextBox/FlowDocument need STA + a Dispatcher.
/// </summary>
public sealed class TrackingDisplayControlTests
{
    // ── Default state ──────────────────────────────────────────────────────────────────────────

    [StaFact]
    public void DefaultState_ShowMarkupInsertionsDeletions_IsTrue()
    {
        var view = new DocumentView();
        view.ShowMarkupInsertionsAndDeletions.Should().BeTrue(
            "default must match today's unconditional behaviour");
    }

    [StaFact]
    public void DefaultState_ShowMarkupComments_IsTrue()
    {
        var view = new DocumentView();
        view.ShowMarkupComments.Should().BeTrue(
            "default must match today's unconditional behaviour");
    }

    [StaFact]
    public void DefaultState_DisplayForReview_IsAllMarkup()
    {
        var view = new DocumentView();
        view.DisplayForReview.Should().Be(DocumentView.MarkupDisplayMode.AllMarkup,
            "default must preserve current all-markup rendering");
    }

    // ── Round-trip safety — revisions ─────────────────────────────────────────────────────────

    [StaFact]
    public void ShowMarkupInsertionsDeletions_WhenToggedOff_RevisionMarkerSurvivesCommit()
    {
        // Arrange: a document with one inserted and one deleted run.
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("plain "));
        para.Runs.Add(new Run("added") { Revision = RevisionKind.Inserted, RevisionAuthor = "Alice", RevisionDateXml = "2026-06-23T00:00:00Z" });
        para.Runs.Add(new Run(" "));
        para.Runs.Add(new Run("removed") { Revision = RevisionKind.Deleted, RevisionAuthor = "Bob" });
        doc.Blocks.Add(para);

        var view = new DocumentView();
        view.LoadModel(doc);

        // Act: suppress the decoration chrome and commit back to model.
        view.ApplyShowMarkupInsertionsAndDeletions(false);
        view.CommitToModel();

        // Assert: revisions must still be present in the model (round-trip safe).
        var committed = view.Model;
        var runs = ((Paragraph)committed.Blocks[0]).Runs;

        runs.Any(r => r.Revision == RevisionKind.Inserted).Should().BeTrue(
            "inserted revision must survive CommitToModel even with Show Markup Insertions/Deletions OFF");
        runs.Any(r => r.Revision == RevisionKind.Deleted).Should().BeTrue(
            "deleted revision must survive CommitToModel even with Show Markup Insertions/Deletions OFF");
    }

    [StaFact]
    public void ShowMarkupInsertionsDeletions_WhenToggedOff_AuthorAndDatePreserved()
    {
        // Arrange: a revision run with author and date metadata.
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("ins") { Revision = RevisionKind.Inserted, RevisionAuthor = "Alice", RevisionDateXml = "2026-06-23T00:00:00Z" });
        doc.Blocks.Add(para);

        var view = new DocumentView();
        view.LoadModel(doc);

        // Act: suppress decoration and commit.
        view.ApplyShowMarkupInsertionsAndDeletions(false);
        view.CommitToModel();

        // Assert: author and date round-trip unchanged.
        var run = ((Paragraph)view.Model.Blocks[0]).Runs[0];
        run.RevisionAuthor.Should().Be("Alice");
        run.RevisionDateXml.Should().Be("2026-06-23T00:00:00Z");
    }

    [StaFact]
    public void ShowMarkupInsertionsDeletions_CanBeReenabled_AfterCommit()
    {
        // Verify the flag can be toggled back ON and the model remains intact.
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("text") { Revision = RevisionKind.Inserted, RevisionAuthor = "A" });
        doc.Blocks.Add(para);

        var view = new DocumentView();
        view.LoadModel(doc);

        view.ApplyShowMarkupInsertionsAndDeletions(false);
        view.ApplyShowMarkupInsertionsAndDeletions(true);
        view.CommitToModel();

        var run = ((Paragraph)view.Model.Blocks[0]).Runs[0];
        run.Revision.Should().Be(RevisionKind.Inserted,
            "revision kind must survive toggle-off then toggle-on");
    }

    // ── Round-trip safety — comments ──────────────────────────────────────────────────────────

    [StaFact]
    public void ShowMarkupComments_WhenToggedOff_CommentIdSurvivesCommit()
    {
        // Arrange: a document with a commented run.
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("annotated") { CommentId = 42 });
        doc.Blocks.Add(para);
        doc.Comments[42] = new Comment(42, "Some text", "Alice");

        var view = new DocumentView();
        view.LoadModel(doc);

        // Act: suppress comment highlights and commit.
        view.ApplyShowMarkupComments(false);
        view.CommitToModel();

        // Assert: the comment id must still be on the run (round-trip safe).
        var run = ((Paragraph)view.Model.Blocks[0]).Runs[0];
        run.CommentId.Should().Be(42,
            "comment id must survive CommitToModel even with Show Markup Comments OFF");
    }

    [StaFact]
    public void ShowMarkupComments_CanBeReenabled_AfterCommit()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("x") { CommentId = 1 });
        doc.Blocks.Add(para);
        doc.Comments[1] = new Comment(1, "B", "A");

        var view = new DocumentView();
        view.LoadModel(doc);

        view.ApplyShowMarkupComments(false);
        view.ApplyShowMarkupComments(true);
        view.CommitToModel();

        var run = ((Paragraph)view.Model.Blocks[0]).Runs[0];
        run.CommentId.Should().Be(1,
            "comment id must survive toggle-off then toggle-on");
    }

    // ── Display for Review ─────────────────────────────────────────────────────────────────────

    [StaFact]
    public void DisplayForReview_SetToAllMarkup_DoesNotAffectModel()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("hello"));

        var view = new DocumentView();
        view.LoadModel(doc);

        // Setting the only implemented mode is a no-op; model must survive.
        view.DisplayForReview = DocumentView.MarkupDisplayMode.AllMarkup;
        view.CommitToModel();

        view.Model.PlainText.Should().Be("hello");
    }

    [StaFact]
    public void DisplayForReview_NoMarkup_DeletedRunSurvivesCommitWithKindAndText()
    {
        // Arrange: a document with a deleted run.
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("kept "));
        para.Runs.Add(new Run("removed") { Revision = RevisionKind.Deleted, RevisionAuthor = "Bob", RevisionDateXml = "2026-06-23T00:00:00Z" });
        doc.Blocks.Add(para);

        var view = new DocumentView();
        view.LoadModel(doc);

        // Act: switch to No Markup (deletions become invisible) and commit.
        view.ApplyDisplayForReview(DocumentView.MarkupDisplayMode.NoMarkup);
        view.CommitToModel();

        // Assert: the deleted run must still be in the model with kind/author/date/text intact.
        var runs = ((Paragraph)view.Model.Blocks[0]).Runs;
        var deleted = runs.FirstOrDefault(r => r.Revision == RevisionKind.Deleted);
        deleted.Should().NotBeNull("deleted run must survive No Markup mode");
        deleted!.Text.Should().Be("removed", "deleted run text must be preserved");
        deleted.RevisionAuthor.Should().Be("Bob");
        deleted.RevisionDateXml.Should().Be("2026-06-23T00:00:00Z");
    }

    [StaFact]
    public void DisplayForReview_Original_InsertedRunSurvivesCommitWithKindAndText()
    {
        // Arrange: a document with an inserted run.
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("base "));
        para.Runs.Add(new Run("added") { Revision = RevisionKind.Inserted, RevisionAuthor = "Alice", RevisionDateXml = "2026-06-23T00:00:00Z" });
        doc.Blocks.Add(para);

        var view = new DocumentView();
        view.LoadModel(doc);

        // Act: switch to Original (insertions become invisible) and commit.
        view.ApplyDisplayForReview(DocumentView.MarkupDisplayMode.Original);
        view.CommitToModel();

        // Assert: the inserted run must still be in the model with kind/author/date/text intact.
        var runs = ((Paragraph)view.Model.Blocks[0]).Runs;
        var inserted = runs.FirstOrDefault(r => r.Revision == RevisionKind.Inserted);
        inserted.Should().NotBeNull("inserted run must survive Original mode");
        inserted!.Text.Should().Be("added", "inserted run text must be preserved");
        inserted.RevisionAuthor.Should().Be("Alice");
        inserted.RevisionDateXml.Should().Be("2026-06-23T00:00:00Z");
    }

    [StaFact]
    public void DisplayForReview_TogglingBackToAllMarkup_RestoresNormalRenderingAndLosesNothing()
    {
        // Arrange: both an insertion and a deletion.
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("ins") { Revision = RevisionKind.Inserted, RevisionAuthor = "A" });
        para.Runs.Add(new Run("del") { Revision = RevisionKind.Deleted, RevisionAuthor = "B" });
        doc.Blocks.Add(para);

        var view = new DocumentView();
        view.LoadModel(doc);

        // Act: cycle through modes and return to AllMarkup.
        view.ApplyDisplayForReview(DocumentView.MarkupDisplayMode.NoMarkup);
        view.ApplyDisplayForReview(DocumentView.MarkupDisplayMode.Original);
        view.ApplyDisplayForReview(DocumentView.MarkupDisplayMode.AllMarkup);
        view.CommitToModel();

        // Assert: both revision runs survived every mode transition.
        var runs = ((Paragraph)view.Model.Blocks[0]).Runs;
        runs.Any(r => r.Revision == RevisionKind.Inserted).Should().BeTrue();
        runs.Any(r => r.Revision == RevisionKind.Deleted).Should().BeTrue();
    }

    // ── Show Markup > Formatting ───────────────────────────────────────────────────────────────

    [StaFact]
    public void ShowMarkupFormatting_DefaultIsTrue()
    {
        var view = new DocumentView();
        view.ShowMarkupFormatting.Should().BeTrue(
            "default must be ON so tracked formatting changes are visible by default");
    }

    [StaFact]
    public void ShowMarkupFormatting_WhenOff_FormatRevisionSurvivesCommit()
    {
        // Arrange: a run with a tracked formatting change (w:rPrChange).
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        var previousFmt = RunFormatting.Default with { Bold = false };
        var fmtRevision = new FormatRevision(previousFmt, "Alice", "2026-06-23T00:00:00Z");
        para.Runs.Add(new Run("bold") { Formatting = RunFormatting.Default with { Bold = true }, FormatRevision = fmtRevision });
        doc.Blocks.Add(para);

        var view = new DocumentView();
        view.LoadModel(doc);

        // Act: toggle OFF the formatting decoration and commit.
        view.ApplyShowMarkupFormatting(false);
        view.CommitToModel();

        // Assert: the FormatRevision must still be on the run.
        var run = ((Paragraph)view.Model.Blocks[0]).Runs[0];
        run.FormatRevision.Should().NotBeNull(
            "FormatRevision must survive CommitToModel even with Show Markup Formatting OFF");
        run.FormatRevision!.Author.Should().Be("Alice");
        run.FormatRevision.DateXml.Should().Be("2026-06-23T00:00:00Z");
        run.FormatRevision.PreviousFormatting.Bold.Should().BeFalse();
    }

    [StaFact]
    public void ShowMarkupFormatting_CanBeReenabled_AfterCommit()
    {
        // Verify toggling OFF then ON leaves the FormatRevision intact.
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        var fmtRevision = new FormatRevision(RunFormatting.Default, "X", "2026-06-23T00:00:00Z");
        para.Runs.Add(new Run("text") { FormatRevision = fmtRevision });
        doc.Blocks.Add(para);

        var view = new DocumentView();
        view.LoadModel(doc);

        view.ApplyShowMarkupFormatting(false);
        view.ApplyShowMarkupFormatting(true);
        view.CommitToModel();

        var run = ((Paragraph)view.Model.Blocks[0]).Runs[0];
        run.FormatRevision.Should().NotBeNull("FormatRevision must survive toggle-off then toggle-on");
        run.FormatRevision!.Author.Should().Be("X");
    }

    // ── Combined: all flags default to ON means existing tests still pass ─────────────────────

    [StaFact]
    public void AllFlagsOn_RevisionAndCommentRenderPathIsUnchanged()
    {
        // Arrange: a document with a revision and a comment (default flags → ON).
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("ins") { Revision = RevisionKind.Inserted, RevisionAuthor = "X" });
        para.Runs.Add(new Run("c") { CommentId = 7 });
        doc.Blocks.Add(para);
        doc.Comments[7] = new Comment(7, "Z", "Y");

        var view = new DocumentView();
        // Default: ShowMarkupInsertionsAndDeletions = true, ShowMarkupComments = true
        view.LoadModel(doc);
        view.CommitToModel();

        var runs = ((Paragraph)view.Model.Blocks[0]).Runs;
        runs[0].Revision.Should().Be(RevisionKind.Inserted);
        runs[1].CommentId.Should().Be(7);
    }
}

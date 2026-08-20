using System.Threading;
using Free.Shared.AppServices;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Editing;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// AV-CLIP: a rich paste lands AT the caret. It used to require an empty destination paragraph, so
/// pasting into real text silently fell back to plain text — and the payload itself was HTML, which
/// cannot express a content control or a tracked change's author. Copy now also writes FreeW's own
/// flavour, so a FreeW-to-FreeW round trip keeps what the model holds.
/// </summary>
public sealed class DocumentViewRichPasteTests
{
    [Fact]
    public void Pasting_into_the_middle_of_a_paragraph_splices_the_clipboard_runs_in()
    {
        var view = LoadDocument(Paragraph("Head tail"));

        view.MoveCaretToBlockForTest(0, 5);
        view.PasteKeepSourceFormatting(ClipboardDocument()).Should().BeTrue();

        var paragraph = view.Document.Paragraphs.Single();
        paragraph.PlainText.Should().Be("Head Name: Bob addedtail");
        var field = paragraph.Runs.Single(run => run.Control is not null);
        field.Text.Should().Be("Bob");
        field.Control!.Tag.Should().Be("Applicant");
        paragraph.Runs.Single(run => run.Revision == RevisionKind.Inserted).RevisionAuthor.Should().Be("Ada");
        view.CaretOffsetForTest.Should().Be("Head Name: Bob added".Length, "the caret follows the pasted text");
    }

    [Fact]
    public void Pasting_multiple_paragraphs_splits_the_destination_around_them()
    {
        var view = LoadDocument(Paragraph("Head tail"));
        var clipboard = ClipboardDocument();
        clipboard.Blocks.Add(Paragraph("Second"));

        view.MoveCaretToBlockForTest(0, 5);
        view.PasteKeepSourceFormatting(clipboard).Should().BeTrue();

        view.Document.Blocks.Should().HaveCount(2);
        view.Document.Paragraphs.Select(paragraph => paragraph.PlainText)
            .Should().Equal("Head Name: Bob added", "Secondtail");
        view.Document.Paragraphs.First().Runs.Should().Contain(run => run.Control != null);
    }

    [Fact]
    public void Pasting_over_a_selection_replaces_it_as_one_undo_step()
    {
        var view = LoadDocument(Paragraph("Head REPLACE tail"));

        view.SetBodySelectionForTest(0, 5, 0, 12);
        view.PasteKeepSourceFormatting(ClipboardDocument()).Should().BeTrue();
        view.Document.Paragraphs.Single().PlainText.Should().Be("Head Name: Bob added tail");
        view.Undo();
        view.Document.Paragraphs.Single().PlainText.Should().Be(
            "Head REPLACE tail",
            "the delete and the insert undo together");
    }

    [Fact]
    public void Pasting_beside_a_field_works_rather_than_falling_back_to_plain_text()
    {
        var destination = new Paragraph();
        destination.Runs.Add(new Run("Name: "));
        destination.Runs.Add(Run.PlainTextControl("Bob", tag: "Applicant"));
        destination.Runs.Add(new Run(" tail"));
        var view = LoadDocument(destination);

        // Caret between the field and the trailing text — the destination holds a field, which used to
        // make the whole paragraph ineligible for a rich paste.
        view.MoveCaretToBlockForTest(0, 9);
        view.PasteKeepSourceFormatting(ClipboardDocument()).Should().BeTrue();

        var paragraph = view.Document.Paragraphs.Single();
        paragraph.PlainText.Should().Be("Name: BobName: Bob added tail");
        paragraph.Runs.Where(run => run.Control != null).Should().HaveCount(2, "both fields survive");
    }

    [Fact]
    public void Pasting_with_track_changes_on_records_the_pasted_runs_as_an_insertion()
    {
        var view = LoadDocument(Paragraph("Head tail"));
        view.ToggleTrackChanges().Should().BeTrue();

        view.MoveCaretToBlockForTest(0, 5);
        view.PasteKeepSourceFormatting(ClipboardDocument()).Should().BeTrue(
            "a rich paste used to be refused outright while tracking, degrading to plain text");

        var paragraph = view.Document.Paragraphs.Single();
        paragraph.Runs.Where(run => run.Revision == RevisionKind.Inserted)
            .Select(run => run.Text)
            .Should().Equal("Name: ", "Bob", " added");
        // The paste is this author's insertion — except the run the SOURCE had already marked, whose
        // recorded history belongs to whoever made it.
        paragraph.Runs.Where(run => run.Revision == RevisionKind.Inserted)
            .Select(run => run.RevisionAuthor)
            .Should().Equal(view.RevisionAuthor, view.RevisionAuthor, "Ada");
        paragraph.Runs.Single(run => run.Control != null).Text.Should().Be("Bob");
    }

    [Fact]
    public void Pasting_inside_a_content_control_is_declined_rather_than_tearing_the_field()
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.PlainTextControl("Bob", tag: "Applicant"));
        var view = LoadDocument(paragraph);

        view.MoveCaretToBlockForTest(0, 2);
        view.PasteKeepSourceFormatting(ClipboardDocument()).Should().BeFalse(
            "splitting the destination run there would emit that one w:sdt twice");

        view.Document.Paragraphs.Single().PlainText.Should().Be("Bob");
        view.Document.Paragraphs.Single().Runs.Should().ContainSingle();
    }

    [Fact]
    public async Task A_copy_round_trip_through_the_shared_workflow_keeps_the_field()
    {
        var source = LoadDocument(SourceParagraph());
        source.SetBodySelectionForTest(0, 0, 0, source.PlainText.Length);
        var (document, ranges) = source.GetSelectionRichSnapshot();

        var content = FreeWClipboardApplicationWorkflow.CreateWriteContent(
            source.SelectedText,
            FreeWClipboardApplicationWorkflow.BuildSelectionRichDocument(document, ranges),
            FreeWClipboardApplicationWorkflow.BuildSelectionNativeDocument(document, ranges))!;

        // Read it back the way a paste does — through the workflow, off a clipboard holding exactly
        // what the copy wrote.
        var clipboard = new StubClipboard(content);
        var payload = (await FreeWClipboardApplicationWorkflow.ReadPasteSpecialAsync(clipboard)).Payload!;
        payload.RichDocument.Should().NotBeNull();

        var target = LoadDocument(Paragraph(string.Empty));
        target.MoveCaretToBlockForTest(0, 0);
        target.PasteKeepSourceFormatting(payload.RichDocument!).Should().BeTrue();

        var pasted = target.Document.Paragraphs.Single();
        pasted.PlainText.Should().Be("Name: Bob added");
        pasted.Runs.Single(run => run.Control is not null).Control!.Tag.Should().Be("Applicant");
    }

    /// <summary>
    /// A copied run keeps only the ID of the footnote or comment it points at, and an id means nothing on
    /// its own: pasted into a document with its own footnote 1, a copied reference to footnote 1 would
    /// silently aim at that unrelated note. The clipboard carries the referenced note and comment thread
    /// so the insertion can renumber them.
    /// </summary>
    [Fact]
    public async Task A_copied_note_and_comment_travel_with_the_selection_and_are_renumbered()
    {
        var sourceParagraph = new Paragraph();
        sourceParagraph.Runs.Add(new Run("Body"));
        sourceParagraph.Runs.Add(new Run("1") { FootnoteId = 1 });
        sourceParagraph.Runs.Add(new Run("commented") { CommentId = 0 });

        var sourceDocument = TextDocument.CreateEmpty();
        sourceDocument.Blocks.Clear();
        sourceDocument.Blocks.Add(sourceParagraph);
        sourceDocument.Footnotes[1] = new Footnote(1, "Copied note body");
        var comment = new Comment(0) { Author = "Ada" };
        comment.Content.Add(new Paragraph("Copied comment body"));
        sourceDocument.Comments[0] = comment;

        var source = new DocumentView();
        source.LoadDocument(sourceDocument);
        source.SetBodySelectionForTest(0, 0, 0, sourceParagraph.PlainText.Length);
        var (document, ranges) = source.GetSelectionRichSnapshot();
        var content = FreeWClipboardApplicationWorkflow.CreateWriteContent(
            source.SelectedText,
            FreeWClipboardApplicationWorkflow.BuildSelectionRichDocument(document, ranges),
            FreeWClipboardApplicationWorkflow.BuildSelectionNativeDocument(document, ranges))!;
        var payload = (await FreeWClipboardApplicationWorkflow.ReadPasteSpecialAsync(
            new StubClipboard(content))).Payload!;
        payload.RichDocument.Should().NotBeNull();

        // The destination already owns a footnote 1 and a comment 0 of its own.
        var targetDocument = TextDocument.CreateEmpty();
        targetDocument.Blocks.Clear();
        targetDocument.Blocks.Add(Paragraph("Target"));
        targetDocument.Footnotes[1] = new Footnote(1, "The target's own note");
        var targetComment = new Comment(0) { Author = "Grace" };
        targetComment.Content.Add(new Paragraph("The target's own comment"));
        targetDocument.Comments[0] = targetComment;
        var target = new DocumentView();
        target.LoadDocument(targetDocument);

        target.MoveCaretToBlockForTest(0, "Target".Length);
        target.PasteKeepSourceFormatting(payload.RichDocument!).Should().BeTrue();

        var pastedFootnoteId = target.Document.Paragraphs.Single()
            .Runs.Single(run => run.FootnoteId is not null).FootnoteId!.Value;
        pastedFootnoteId.Should().NotBe(1, "the destination's own footnote 1 must not be hijacked");
        targetDocument.Footnotes[pastedFootnoteId].PlainText.Should().Be("Copied note body");
        targetDocument.Footnotes[1].PlainText.Should().Be("The target's own note");

        var pastedCommentId = target.Document.Paragraphs.Single()
            .Runs.Single(run => run.CommentId is not null).CommentId!.Value;
        pastedCommentId.Should().NotBe(0);
        targetDocument.Comments[pastedCommentId].Content.Single().PlainText
            .Should().Be("Copied comment body");
        targetDocument.Comments[0].Author.Should().Be("Grace");
    }

    private static Paragraph Paragraph(string text)
    {
        var paragraph = new Paragraph();
        if (text.Length > 0)
            paragraph.Runs.Add(new Run(text));
        return paragraph;
    }

    private static Paragraph SourceParagraph()
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Name: "));
        paragraph.Runs.Add(Run.PlainTextControl("Bob", tag: "Applicant"));
        paragraph.Runs.Add(new Run(" added") { Revision = RevisionKind.Inserted, RevisionAuthor = "Ada" });
        return paragraph;
    }

    private static TextDocument ClipboardDocument()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(SourceParagraph());
        return document;
    }

    /// <summary>A clipboard holding exactly what a copy wrote, so the read path runs for real.</summary>
    private sealed class StubClipboard(PlatformClipboardContent content) : IPlatformClipboard
    {
        public ValueTask<PlatformClipboardReadResult<PlatformClipboardContent>> ReadAsync(
            PlatformClipboardReadRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(PlatformClipboardReadResult<PlatformClipboardContent>.Success(content));

        public ValueTask<PlatformClipboardWriteResult> WriteAsync(
            PlatformClipboardContent value,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(PlatformClipboardWriteResult.Success());

        public ValueTask<PlatformClipboardWriteResult> ClearAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(PlatformClipboardWriteResult.Success());
    }

    private static DocumentView LoadDocument(Paragraph paragraph)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(paragraph);
        var view = new DocumentView();
        view.LoadDocument(document);
        return view;
    }
}

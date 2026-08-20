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

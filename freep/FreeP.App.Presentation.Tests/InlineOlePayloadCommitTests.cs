using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// An in-place OLE host is handed one payload out of an edit-session copy of a text body; to move
/// the server's bytes onto the live model it has to address the matching payload there. These
/// cover the addressing pair -- position lookup and guarded write -- including the guard the
/// end-to-end shell tests cannot reach: a commit arriving after the surrounding text changed must
/// never overwrite a different embedded object.
/// </summary>
public sealed class InlineOlePayloadCommitTests
{
    private static InlineOleObjectInfo Payload(string fileName, params byte[] bytes) => new()
    {
        EmbeddedBytes = bytes,
        FileName = fileName,
        ClassName = "Excel.Sheet.12",
    };

    private static TextBody BodyWith(params (string Text, InlineOleObjectInfo? Ole)[] runs)
    {
        var body = new TextBody();
        var paragraph = new Paragraph();
        foreach (var (text, ole) in runs)
            paragraph.Runs.Add(new Run { Text = text, InlineOleObject = ole });
        body.Paragraphs.Add(paragraph);
        return body;
    }

    [Fact]
    public void FoundPosition_ResolvesBackToTheSamePayload()
    {
        var second = Payload("Second.xlsx", 2);
        var body = BodyWith(
            ("lead", null),
            ("￼", Payload("First.xlsx", 1)),
            ("middle", null),
            ("￼", second));

        InCanvasRichTextEditBuffer.TryFindInlineOleObjectPosition(body, second, out int position)
            .Should().BeTrue();
        InCanvasRichTextEditBuffer.FindInlineOleObjectAt(body, position, out var resolved)
            .Should().BeTrue();
        resolved.Should().BeSameAs(second);
    }

    [Fact]
    public void FoundPosition_SpansMultipleParagraphs()
    {
        var target = Payload("Target.xlsx", 3);
        var body = new TextBody();
        var first = new Paragraph();
        first.Runs.Add(new Run { Text = "first line" });
        body.Paragraphs.Add(first);
        var second = new Paragraph();
        second.Runs.Add(new Run { Text = "￼", InlineOleObject = target });
        body.Paragraphs.Add(second);

        InCanvasRichTextEditBuffer.TryFindInlineOleObjectPosition(body, target, out int position)
            .Should().BeTrue();
        InCanvasRichTextEditBuffer.FindInlineOleObjectAt(body, position, out var resolved)
            .Should().BeTrue();
        resolved.Should().BeSameAs(target);
    }

    [Fact]
    public void FoundPosition_IsFalse_ForAPayloadThatIsNotInTheBody()
    {
        var body = BodyWith(("￼", Payload("Present.xlsx", 1)));

        InCanvasRichTextEditBuffer.TryFindInlineOleObjectPosition(
            body,
            Payload("Absent.xlsx", 9),
            out int position)
            .Should().BeFalse();
        position.Should().Be(-1);
    }

    [Fact]
    public void Commit_WritesTheEditedBytesOntoTheLivePayload()
    {
        var live = Payload("Book.xlsx", 1, 2, 3);
        var body = BodyWith(("￼", live));

        InCanvasRichTextEditBuffer.TryCommitInlineOlePayload(body, 0, new byte[] { 8, 8 })
            .Should().BeTrue();

        live.EmbeddedBytes.Should().Equal(8, 8);
    }

    [Fact]
    public void Commit_IsRefused_WhenADifferentObjectNowOccupiesThePosition()
    {
        var live = Payload("Replacement.docx", 1, 2, 3);
        var body = BodyWith(("￼", live));

        InCanvasRichTextEditBuffer.TryCommitInlineOlePayload(
            body,
            0,
            new byte[] { 8, 8 },
            expected: Payload("Book.xlsx", 1, 2, 3))
            .Should().BeFalse("the payload that was edited is no longer the one at this position");

        live.EmbeddedBytes.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Commit_IsRefused_ForAnEmptyPayloadOrAPositionWithNoObject()
    {
        var live = Payload("Book.xlsx", 1, 2, 3);
        var body = BodyWith(("text", null), ("￼", live));

        InCanvasRichTextEditBuffer.TryCommitInlineOlePayload(body, 1, [])
            .Should().BeFalse();
        InCanvasRichTextEditBuffer.TryCommitInlineOlePayload(body, 0, new byte[] { 8 })
            .Should().BeFalse();

        live.EmbeddedBytes.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void SessionCommit_WritesTheLiveShapeBody()
    {
        var live = Payload("Book.xlsx", 1, 2, 3);
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        slide.Shapes.Add(new SlideShape { Id = 44, TextBody = BodyWith(("￼", live)) });
        var session = new EditingSession(presentation, new PresentationCommandBus(presentation));

        session.TryCommitInlineOlePayload(44, 0, new byte[] { 6, 6 }, live).Should().BeTrue();
        live.EmbeddedBytes.Should().Equal(6, 6);

        session.TryCommitInlineOlePayload(45, 0, new byte[] { 7 }).Should().BeFalse(
            "an unknown shape id must not be silently treated as a successful commit");
    }
}

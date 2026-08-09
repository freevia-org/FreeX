using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Find &amp; Replace is a modeless dialog, so the user can edit the slide canvas between finding a
/// match and replacing it, and only retyping the search text re-runs the search. The captured
/// paragraph index, run index and character offsets were used as raw indexers, so a replace (or an
/// undo of one) after such an edit threw ArgumentOutOfRangeException out of the click handler with
/// nothing to catch it. A stale match must be a no-op instead.
/// </summary>
public sealed class FindReplaceStaleMatchTests
{
    private static Presentation PresentationWithText(string text, out SlideShape shape)
    {
        var presentation = new Presentation();
        var slide = new Slide();
        var body = new TextBody();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run { Text = text });
        body.Paragraphs.Add(paragraph);
        shape = new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.AutoShape,
            ExtentCxEmu = 914400,
            ExtentCyEmu = 685800,
            TextBody = body
        };
        slide.Shapes.Add(shape);
        presentation.Slides.Add(slide);
        return presentation;
    }

    [Fact]
    public void ReplaceOne_ParagraphDeletedAfterTheMatchWasFound_DoesNotThrow()
    {
        var presentation = PresentationWithText("hello world", out var shape);
        var match = PresentationTextSearch.FindAll(presentation, "world").Single();

        // The user edits the canvas while the modeless dialog still holds the match.
        shape.TextBody!.Paragraphs.Clear();

        var command = new ReplaceOneCommand(match, "there");

        command.Invoking(c => c.Apply(presentation)).Should().NotThrow();
    }

    [Fact]
    public void ReplaceOne_MatchedTextShortenedAfterTheMatchWasFound_DoesNotThrow()
    {
        var presentation = PresentationWithText("hello world", out var shape);
        var match = PresentationTextSearch.FindAll(presentation, "world").Single();

        // Offsets now point past the end of the run.
        shape.TextBody!.Paragraphs[0].Runs[0].Text = "hi";

        var command = new ReplaceOneCommand(match, "there");

        command.Invoking(c => c.Apply(presentation)).Should().NotThrow();
    }

    [Fact]
    public void ReplaceOne_UndoAfterTheParagraphWasDeleted_DoesNotThrow()
    {
        var presentation = PresentationWithText("hello world", out var shape);
        var match = PresentationTextSearch.FindAll(presentation, "world").Single();
        var command = new ReplaceOneCommand(match, "there");
        command.Apply(presentation);

        shape.TextBody!.Paragraphs.Clear();

        command.Invoking(c => c.Revert(presentation)).Should().NotThrow();
    }

    [Fact]
    public void ReplaceAll_UndoAfterTheParagraphWasDeleted_DoesNotThrow()
    {
        var presentation = PresentationWithText("hello world", out var shape);
        var command = new ReplaceAllCommand("world", "there", new TextSearchOptions());
        command.Apply(presentation);

        shape.TextBody!.Paragraphs.Clear();

        command.Invoking(c => c.Revert(presentation)).Should().NotThrow();
    }

    [Fact]
    public void ReplaceOne_MatchStillValid_StillReplaces()
    {
        // The guards must not stop an ordinary replace from working.
        var presentation = PresentationWithText("hello world", out var shape);
        var match = PresentationTextSearch.FindAll(presentation, "world").Single();

        new ReplaceOneCommand(match, "there").Apply(presentation);

        shape.TextBody!.Paragraphs[0].Runs[0].Text.Should().Be("hello there");
    }
}

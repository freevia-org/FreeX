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

    // sweep98 F1: a stale match (paragraph deleted underneath a modeless Find & Replace dialog)
    // must not push a phantom undo-stack entry -- Apply is already a documented no-op for it
    // (see the comment on ReplaceOneCommand.Apply), but until HasEffect agreed, the command bus
    // pushed the undo entry anyway because IPresentationCommand.HasEffect defaults to true.
    [Fact]
    public void ReplaceOne_StaleMatch_DoesNotRecordUndoEntry()
    {
        var presentation = PresentationWithText("hello world", out var shape);
        var match = PresentationTextSearch.FindAll(presentation, "world").Single();

        // The user edits the canvas while the modeless dialog still holds the match.
        shape.TextBody!.Paragraphs.Clear();

        var bus = new PresentationCommandBus(presentation);
        bus.CanUndo.Should().BeFalse();

        bus.Execute(new ReplaceOneCommand(match, "there"));

        bus.CanUndo.Should().BeFalse("the replace changed nothing, so it must not consume the next Ctrl+Z");
    }

    // Sibling / no-regression: an ordinary, still-valid replace must still record a real undo
    // entry through the same bus path exercised above.
    [Fact]
    public void ReplaceOne_MatchStillValid_RecordsUndoEntryThroughBus()
    {
        var presentation = PresentationWithText("hello world", out var shape);
        var match = PresentationTextSearch.FindAll(presentation, "world").Single();

        var bus = new PresentationCommandBus(presentation);
        bus.Execute(new ReplaceOneCommand(match, "there"));

        bus.CanUndo.Should().BeTrue();
        shape.TextBody!.Paragraphs[0].Runs[0].Text.Should().Be("hello there");
    }

    // sweep98 F2: TryResolveSpan validated only that the offsets were numerically in range for
    // whatever now sits at that (paragraph, run) position -- not that the text there is still the
    // word that was matched. Editing the SAME run (not deleting it) so the old offsets still fall
    // inside it must therefore still be treated as stale, not spliced into.
    [Fact]
    public void ReplaceOne_SameRunEditedInPlace_DoesNotOverwriteUnrelatedText()
    {
        var presentation = PresentationWithText("hello world", out var shape);
        var match = PresentationTextSearch.FindAll(presentation, "world").Single();

        // The user retypes the same run's text to something else of at least the same length,
        // without touching the dialog. The old numeric offsets (6..11) still fall inside the run,
        // but they no longer cover "world" -- they now cover "onligh".
        shape.TextBody!.Paragraphs[0].Runs[0].Text = "goodbye moonlight";

        new ReplaceOneCommand(match, "THERE").Apply(presentation);

        shape.TextBody!.Paragraphs[0].Runs[0].Text.Should().Be("goodbye moonlight",
            "a stale match must not overwrite text the user never searched for");
    }

    // Sibling / no-regression: the same in-place-edit guard must not stop a replace whose
    // offsets still point at the actual matched text (e.g. the run grew via an appended
    // sentence, but the matched word itself was untouched).
    [Fact]
    public void ReplaceOne_SameRunEditedButMatchedTextUnchanged_StillReplaces()
    {
        var presentation = PresentationWithText("hello world", out var shape);
        var match = PresentationTextSearch.FindAll(presentation, "world").Single();

        // Text is appended after the match; the matched span (6..11 = "world") is untouched.
        shape.TextBody!.Paragraphs[0].Runs[0].Text = "hello world, again";

        new ReplaceOneCommand(match, "there").Apply(presentation);

        shape.TextBody!.Paragraphs[0].Runs[0].Text.Should().Be("hello there, again");
    }
}

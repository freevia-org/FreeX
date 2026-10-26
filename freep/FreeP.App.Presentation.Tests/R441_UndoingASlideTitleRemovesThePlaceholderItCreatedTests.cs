using FluentAssertions;
using FreeP.Core.Model;
using Xunit;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r441: undoing "set slide title" on a slide that had no title placeholder must remove the
/// placeholder the edit created, not just blank its text.
///
/// <para>Found by the r441 auto-driver on its first run against FreeP. <see cref="Slide.Title"/> is a
/// computed property whose SETTER creates a title placeholder and inserts it at index 0 when the
/// slide has none; the command's Revert wrote the old text back, and writing empty text into a shape
/// that now exists cannot remove it.</para>
///
/// <para>The damage is the r438 shape again -- undo restores the VALUE but not the STRUCTURE the
/// value's setter created. The slide keeps an empty "Click to add title" placeholder the author
/// never added, it is written into the .pptx, and because it goes in at index 0 every other shape on
/// the slide shifts position behind it.</para>
/// </summary>
public sealed class R441_UndoingASlideTitleRemovesThePlaceholderItCreatedTests
{
    private static SlideShape BodyShape() => new()
    {
        Id = 2,
        Name = "Body",
        OffsetXEmu = 100000,
        OffsetYEmu = 200000,
        ExtentCxEmu = 1000000,
        ExtentCyEmu = 500000,
    };

    private static Presentation WithUntitledSlide()
    {
        var presentation = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(BodyShape());
        presentation.Slides.Add(slide);
        return presentation;
    }

    private static SlideShape? TitleShape(Slide slide) =>
        slide.Shapes.FirstOrDefault(shape =>
            shape.Placeholder?.Type is PlaceholderType.Title or PlaceholderType.CenteredTitle);

    [Fact]
    public void UndoLeavesNoPlaceholderBehindOnASlideThatHadNone()
    {
        var presentation = WithUntitledSlide();
        var slide = presentation.Slides[0];

        var command = new SetSlideTitleCommand(0, "Quarterly review");
        command.Apply(presentation);
        TitleShape(slide).Should().NotBeNull("setting the title must create the placeholder");

        command.Revert(presentation);

        TitleShape(slide).Should().BeNull(
            "a placeholder the author never added must not survive undo -- it is saved into the " +
            "file and shows as an empty title box the user cannot account for");
        slide.Shapes.Should().ContainSingle("the slide is back to just its body shape")
            .Which.Name.Should().Be("Body");
    }

    [Fact]
    public void UndoRestoresTheOriginalTitleWhenThePlaceholderAlreadyExisted()
    {
        // The other half: when the slide DID have a title, undo must restore its text and keep the
        // shape. A fix that removed the placeholder unconditionally would pass the test above while
        // destroying a title the author had written.
        var presentation = WithUntitledSlide();
        var slide = presentation.Slides[0];
        slide.Title = "Original";

        var shapeCountBefore = slide.Shapes.Count;
        var command = new SetSlideTitleCommand(0, "Replaced");
        command.Apply(presentation);
        slide.Title.Should().Be("Replaced");

        command.Revert(presentation);

        slide.Title.Should().Be("Original", "an existing title must come back, not be deleted");
        slide.Shapes.Should().HaveCount(shapeCountBefore, "and its placeholder must stay");
    }

    [Fact]
    public void TheOtherShapesKeepTheirPositionAfterUndo()
    {
        // The placeholder is inserted at index 0, so leaving it behind pushes every other shape one
        // place along -- which matters to anything that addresses shapes by index or draws by
        // z-order, not just to what the slide looks like.
        var presentation = WithUntitledSlide();
        var slide = presentation.Slides[0];

        var command = new SetSlideTitleCommand(0, "Quarterly review");
        command.Apply(presentation);
        command.Revert(presentation);

        slide.Shapes[0].Name.Should().Be("Body", "the body shape must return to the front of the list");
    }

    [Fact]
    public void RedoAfterUndoStillSetsTheTitle()
    {
        // Revert clears the "I created it" flag, so a second Apply must be able to create the
        // placeholder again -- otherwise the fix would break redo, which is undo's other half.
        var presentation = WithUntitledSlide();
        var slide = presentation.Slides[0];

        var command = new SetSlideTitleCommand(0, "Quarterly review");
        command.Apply(presentation);
        command.Revert(presentation);
        command.Apply(presentation);

        slide.Title.Should().Be("Quarterly review", "redo must put the title back");
        TitleShape(slide).Should().NotBeNull("along with the placeholder that carries it");
    }
}

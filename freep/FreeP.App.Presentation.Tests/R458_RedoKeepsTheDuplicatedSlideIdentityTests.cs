using FluentAssertions;
using FreeP.Core.Model;
using Xunit;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r458: redoing "Duplicate Slide" must give the duplicate back its ORIGINAL id.
///
/// <para>Found by porting r457's redo check from FreeX to this app's driver -- and it found the same
/// defect here on the first run. <c>SlideCloner.CloneSlide</c> mints a new <c>Slide.Id</c> on every
/// call, so undo-then-redo produced a slide with a different identity than the first Apply created.</para>
///
/// <para>In FreeP that identity is load-bearing: slide-jump hyperlinks resolve to a slide BY ID, so a
/// link pointing at the duplicate silently stopped finding it after a redo. Nothing looks broken --
/// the slide is there, the link is there, and clicking it just does not go anywhere.</para>
///
/// <para><c>SlideCloner.CloneSlidePreservingIdentity</c> already existed for exactly this need;
/// <c>DuplicateSlideCommand.Revert</c>'s own comment even names it. The clone-with-a-new-id path was
/// simply used for redo as well as for the first Apply.</para>
/// </summary>
public sealed class R458_RedoKeepsTheDuplicatedSlideIdentityTests
{
    private static Presentation Setup()
    {
        var presentation = new Presentation();

        for (var index = 0; index < 2; index++)
        {
            var slide = new Slide();
            slide.Shapes.Add(new SlideShape
            {
                Id = (uint)(index + 2),
                Name = "Body" + index,
                OffsetXEmu = 100000,
                OffsetYEmu = 200000,
                ExtentCxEmu = 900000,
                ExtentCyEmu = 400000,
            });
            presentation.Slides.Add(slide);
        }

        return presentation;
    }

    [Fact]
    public void RedoGivesTheDuplicateBackItsOriginalId()
    {
        var presentation = Setup();
        var command = new DuplicateSlideCommand(0);

        command.Apply(presentation);
        var firstId = presentation.Slides[1].Id;

        command.Revert(presentation);
        command.Apply(presentation);

        presentation.Slides[1].Id.Should().Be(
            firstId,
            "slide-jump hyperlinks resolve by id, so a redone duplicate with a new identity leaves " +
            "every link to it pointing at nothing while the slide sits visibly in the deck");
    }

    [Fact]
    public void UndoStillRemovesTheDuplicate()
    {
        // Holding the identity across undo must not hold the SLIDE: undo has to remove it.
        var presentation = Setup();
        var command = new DuplicateSlideCommand(0);

        command.Apply(presentation);
        presentation.Slides.Should().HaveCount(3);

        command.Revert(presentation);

        presentation.Slides.Should().HaveCount(2, "undo removes the slide it created");
    }

    [Fact]
    public void TheRedoneDuplicateIsAFreshObjectNotTheRetainedOne()
    {
        // The fix re-clones rather than re-inserting the retained instance. Re-inserting would make
        // the command's own field alias the live document, so a later edit to the slide would
        // silently mutate the undo state as well.
        var presentation = Setup();
        var command = new DuplicateSlideCommand(0);

        command.Apply(presentation);
        var firstInstance = presentation.Slides[1];

        command.Revert(presentation);
        command.Apply(presentation);

        presentation.Slides[1].Should().NotBeSameAs(
            firstInstance, "each Apply produces its own instance, with only the identity held stable");
    }

    [Fact]
    public void TheDuplicateStillCarriesTheSourceContent()
    {
        // Identity is not the whole contract: the redone duplicate must still be a copy of the
        // source slide, not an empty slide wearing the right id.
        var presentation = Setup();
        var command = new DuplicateSlideCommand(0);

        command.Apply(presentation);
        command.Revert(presentation);
        command.Apply(presentation);

        presentation.Slides[1].Shapes.Should().ContainSingle("the duplicate copies the source's shapes")
            .Which.Name.Should().Be("Body0");
    }

    [Fact]
    public void RepeatedUndoRedoKeepsTheSameIdentity()
    {
        // A user can press Ctrl+Z/Ctrl+Y repeatedly. The identity must be stable across every cycle,
        // not just the first -- a fix that captured the id only once would pass the headline test
        // and drift on the second cycle.
        var presentation = Setup();
        var command = new DuplicateSlideCommand(0);

        command.Apply(presentation);
        var firstId = presentation.Slides[1].Id;

        for (var cycle = 0; cycle < 3; cycle++)
        {
            command.Revert(presentation);
            command.Apply(presentation);
            presentation.Slides[1].Id.Should().Be(firstId, "cycle {0} must not drift", cycle);
        }
    }
}

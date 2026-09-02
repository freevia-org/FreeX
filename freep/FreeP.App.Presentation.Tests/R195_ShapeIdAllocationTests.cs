using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r195 (backlog items 41 and the r194 header/footer HIGH): FreeP hands out shape ids from a
/// watermark in <see cref="EditingSession"/>, but two commands outside that class --
/// <c>SetSlideLayoutCommand</c> and <c>ApplyHeaderFooterCommand</c> -- add shapes with ids from
/// their own local scan and cannot update it. The watermark was seeded ONCE per session, so after
/// either command the next insert re-issued an id that shape already had. Two shapes on one slide
/// then share an Id, and every by-id lookup (delete, reorder, select) resolves to whichever comes
/// first in the list -- so deleting the shape the user clicked can remove the other one.
///
/// The watermark is now a floor raised from the live document on each allocation. It is still a
/// counter, because <c>AssignShapeIds</c> allocates in a loop for a pasted subtree not yet in the
/// presentation: a live scan alone would return the same value every iteration and give every
/// pasted shape one id.
/// </summary>
public sealed class R195_ShapeIdAllocationTests
{
    private static EditingSession CreateEditor()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Clear();
        presentation.Slides.Add(new Slide { Id = "slide-1", Title = "One" });
        return new EditingSession(presentation, new PresentationCommandBus(presentation));
    }

    private static IReadOnlyList<uint> ShapeIdsOnFirstSlide(EditingSession editor) =>
        editor.Presentation.Slides[0].Shapes.Select(shape => shape.Id).ToList();

    [Fact]
    public void InsertingAfterAnotherAllocatorAddedAShape_DoesNotReuseThatId()
    {
        // The exact sequence: insert (seeds the watermark), then something OUTSIDE the session adds
        // a shape with the next id, then insert again.
        var editor = CreateEditor();
        editor.InsertDefaultTextBox().Should().NotBeNull();

        var slide = editor.Presentation.Slides[0];
        var idFromElsewhere = slide.Shapes.Select(s => s.Id).Max() + 1u;
        slide.Shapes.Add(new SlideShape { Id = idFromElsewhere, Kind = SlideShapeKind.AutoShape });

        editor.InsertDefaultTextBox().Should().NotBeNull();

        ShapeIdsOnFirstSlide(editor).Should().OnlyHaveUniqueItems(
            "an id another allocator already used must not be handed out again");
    }

    [Fact]
    public void RepeatedInsertsStillProduceDistinctIds()
    {
        var editor = CreateEditor();

        for (var i = 0; i < 5; i++)
            editor.InsertDefaultTextBox().Should().NotBeNull();

        ShapeIdsOnFirstSlide(editor).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void IdsOnlyEverIncrease_SoADeletedIdIsNotReissued()
    {
        // The watermark is a floor, not a live max: deleting the highest shape must not free its id
        // for reuse, or a selection or connector endpoint still remembering it would rebind to a
        // different shape. This is the property the pre-existing doc comment relies on.
        var editor = CreateEditor();
        editor.InsertDefaultTextBox().Should().NotBeNull();
        editor.InsertDefaultTextBox().Should().NotBeNull();

        var slide = editor.Presentation.Slides[0];
        var retired = slide.Shapes.Select(s => s.Id).Max();
        slide.Shapes.RemoveAll(s => s.Id == retired);

        editor.InsertDefaultTextBox().Should().NotBeNull();

        slide.Shapes.Select(s => s.Id).Should().NotContain(
            retired,
            "an id handed out once is retired, even after the shape holding it is deleted");
    }
}

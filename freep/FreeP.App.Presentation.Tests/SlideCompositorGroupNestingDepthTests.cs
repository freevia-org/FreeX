using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Composition descends one call frame per nested shape group. A deck whose group nesting is deep
/// enough would overflow the stack, and StackOverflowException cannot be caught: it terminates the
/// process outright, bypassing the render-pass guards that contain every other content-driven
/// fault. These pin the depth limit that keeps that from happening.
/// </summary>
public sealed class SlideCompositorGroupNestingDepthTests
{
    [Fact]
    public void Compose_DeeplyNestedShapeGroups_ReturnsInsteadOfOverflowingTheStack()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];

        // Far past any depth a real authoring tool produces. Verified to overflow the stack and
        // abort the test host when the depth limit is removed.
        slide.Shapes.Add(BuildNestedGroups(12_000));

        var ops = SlideCompositor.Compose(presentation, slide, 0);

        ops.Should().NotBeNull();
    }

    [Fact]
    public void Compose_ShallowNestedShapeGroups_StillComposesTheInnermostShape()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        var baseline = SlideCompositor.Compose(presentation, slide, 0).Count;

        slide.Shapes.Add(BuildNestedGroups(4));

        // The depth limit must not clip ordinary decks: the leaf shape still reaches the plan.
        SlideCompositor.Compose(presentation, slide, 0).Count.Should().BeGreaterThan(baseline);
    }

    private static SlideShape BuildNestedGroups(int depth)
    {
        SlideShape current = new()
        {
            Id = 100,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 180 * 9525L,
            ExtentCyEmu = 90 * 9525L,
        };

        for (int i = 0; i < depth; i++)
        {
            var group = new SlideShape
            {
                Id = (uint)(200 + i),
                Kind = SlideShapeKind.Group,
                ExtentCxEmu = 180 * 9525L,
                ExtentCyEmu = 90 * 9525L,
            };
            group.Children.Add(current);
            current = group;
        }

        return current;
    }
}

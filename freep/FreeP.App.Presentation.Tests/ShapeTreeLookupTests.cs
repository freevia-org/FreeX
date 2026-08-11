using FluentAssertions;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class ShapeTreeLookupTests
{
    [Fact]
    public void Find_and_enumerate_walk_the_complete_shape_tree_in_document_order()
    {
        var slide = new Slide();
        var topLevel = new SlideShape { Id = 1, Name = "Top level" };
        var group = new SlideShape { Id = 2, Name = "Group", Kind = SlideShapeKind.Group };
        var nestedGroup = new SlideShape { Id = 3, Name = "Nested group", Kind = SlideShapeKind.Group };
        var nestedChild = new SlideShape { Id = 4, Name = "Nested child" };

        nestedGroup.Children.Add(nestedChild);
        group.Children.Add(nestedGroup);
        slide.Shapes.Add(topLevel);
        slide.Shapes.Add(group);

        ShapeTreeLookup.Find(slide, nestedChild.Id).Should().BeSameAs(nestedChild);
        ShapeTreeLookup.Find(slide, 999).Should().BeNull();
        ShapeTreeLookup.Enumerate(slide).Select(shape => shape.Id)
            .Should().Equal(1, 2, 3, 4);
    }
}

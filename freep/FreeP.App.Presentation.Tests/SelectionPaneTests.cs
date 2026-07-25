using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class SelectionPaneTests
{
    [Fact]
    public void Planner_ListsFrontMostObjectsAndPreservesVisibilityState()
    {
        var slide = new Slide { Title = "Selection" };
        slide.Shapes.Clear();
        slide.Shapes.Add(MakeShape(1, "Back"));
        var middle = MakeShape(2, "Middle");
        middle.IsHidden = true;
        slide.Shapes.Add(middle);
        slide.Shapes.Add(MakeShape(3, "Front"));

        var plan = PresentationSelectionPanePlanner.Build(slide, 2, [3]);

        plan.HasSlide.Should().BeTrue();
        plan.SlideIndex.Should().Be(2);
        plan.Items.Select(item => item.ShapeName).Should().Equal("Front", "Middle", "Back");
        plan.Items[0].IsSelected.Should().BeTrue();
        plan.Items[1].IsHidden.Should().BeTrue();
        plan.Items[1].SelectionIndex.Should().Be(1);
    }

    [Fact]
    public void SetShapeHiddenCommand_IsUndoableAndRedoable()
    {
        var presentation = new Presentation();
        var slide = new Slide { Title = "Visibility" };
        slide.Shapes.Clear();
        slide.Shapes.Add(MakeShape(17, "Object"));
        presentation.Slides.Add(slide);
        var bus = new PresentationCommandBus(presentation);

        bus.Execute(new SetShapeHiddenCommand(0, 17, true));
        slide.Shapes[0].IsHidden.Should().BeTrue();

        bus.Undo();
        slide.Shapes[0].IsHidden.Should().BeFalse();

        bus.Redo();
        slide.Shapes[0].IsHidden.Should().BeTrue();
    }

    [Fact]
    public void HiddenState_RoundTripsThroughPowerPointPackage()
    {
        var presentation = new Presentation();
        var slide = new Slide { Title = "Visibility" };
        slide.Shapes.Clear();
        var shape = MakeShape(17, "Object");
        shape.IsHidden = true;
        slide.Shapes.Add(shape);
        presentation.Slides.Add(slide);

        using var stream = new MemoryStream();
        FreeP.Core.IO.PptxPackageWriter.Write(presentation, stream);
        stream.Position = 0;
        var reopened = FreeP.Core.IO.PptxPackageReader.Read(stream);

        reopened.Slides.Should().ContainSingle();
        reopened.Slides[0].Shapes.Should().ContainSingle();
        reopened.Slides[0].Shapes[0].IsHidden.Should().BeTrue();
    }

    private static SlideShape MakeShape(uint id, string name) => new()
    {
        Id = id,
        Name = name,
        Kind = SlideShapeKind.AutoShape,
        OffsetXEmu = 100,
        OffsetYEmu = 200,
        ExtentCxEmu = 300,
        ExtentCyEmu = 400,
    };
}

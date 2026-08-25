using FreeP.App.Compositor;
using FreeP.Core.IO;

namespace FreeP.App.Compositor.Tests;

public sealed class MasterEditingSessionTests
{
    [Fact]
    public void Master_target_edits_are_undoable_and_do_not_touch_slide_shapes()
    {
        var presentation = CreatePresentation();
        presentation.Slides.Add(new Slide());
        presentation.Slides[0].Shapes.Add(new SlideShape { Id = 1, OffsetXEmu = 10, ExtentCxEmu = 30, ExtentCyEmu = 40 });
        var masterShape = new SlideShape { Id = 2, OffsetXEmu = 20, OffsetYEmu = 30, ExtentCxEmu = 40, ExtentCyEmu = 50 };
        presentation.Masters[0].Placeholders.Add(masterShape);
        var bus = new PresentationCommandBus(presentation);
        var session = new MasterEditingSession(presentation, bus);

        session.SelectTarget(MasterEditTarget.Master("master-1")).Should().BeTrue();
        session.Move(2, 100, 200);

        masterShape.OffsetXEmu.Should().Be(120);
        masterShape.OffsetYEmu.Should().Be(230);
        presentation.Slides[0].Shapes[0].OffsetXEmu.Should().Be(10);

        session.Undo();
        masterShape.OffsetXEmu.Should().Be(20);
        masterShape.OffsetYEmu.Should().Be(30);

        session.Redo();
        masterShape.OffsetXEmu.Should().Be(120);
        masterShape.OffsetYEmu.Should().Be(230);
    }

    [Fact]
    public void Layout_target_can_add_and_delete_placeholder_with_undo()
    {
        var presentation = CreatePresentation();
        var bus = new PresentationCommandBus(presentation);
        var session = new MasterEditingSession(presentation, bus);

        session.SelectTarget(MasterEditTarget.Layout("layout-1")).Should().BeTrue();
        var placeholder = session.AddTextPlaceholder(PlaceholderType.Title);

        placeholder.Id.Should().BeGreaterThan(0);
        presentation.Layouts[0].Placeholders.Should().ContainSingle();
        presentation.Layouts[0].Placeholders[0].Placeholder!.Type.Should().Be(PlaceholderType.Title);

        session.Delete(placeholder.Id);
        presentation.Layouts[0].Placeholders.Should().BeEmpty();

        session.Undo();
        presentation.Layouts[0].Placeholders.Should().ContainSingle();
        presentation.Layouts[0].Placeholders[0].Id.Should().Be(placeholder.Id);
    }

    [Fact]
    public void Master_and_layout_targets_are_grouped_by_their_master()
    {
        var presentation = CreatePresentation();
        presentation.Layouts.Add(new SlideLayout { Id = "other-layout", MasterId = "other-master" });
        presentation.Masters.Add(new SlideMaster { Id = "other-master" });
        var session = new MasterEditingSession(presentation, new PresentationCommandBus(presentation));

        session.Targets.Should().Equal(
            MasterEditTarget.Master("master-1"),
            MasterEditTarget.Layout("layout-1"),
            MasterEditTarget.Master("other-master"),
            MasterEditTarget.Layout("other-layout"));
    }

    [Fact]
    public void Master_and_layout_composition_include_authored_placeholders()
    {
        var presentation = CreatePresentation();
        presentation.Masters[0].Placeholders.Add(new SlideShape
        {
            Id = 20,
            Kind = SlideShapeKind.AutoShape,
            OffsetXEmu = 100,
            OffsetYEmu = 100,
            ExtentCxEmu = 1000,
            ExtentCyEmu = 1000,
            Placeholder = new Placeholder { Type = PlaceholderType.Title },
        });
        presentation.Layouts[0].Placeholders.Add(new SlideShape
        {
            Id = 21,
            Kind = SlideShapeKind.AutoShape,
            OffsetXEmu = 200,
            OffsetYEmu = 200,
            ExtentCxEmu = 1000,
            ExtentCyEmu = 1000,
            Placeholder = new Placeholder { Type = PlaceholderType.Body, Idx = 1 },
        });

        SlideCompositor.ComposeMaster(presentation, presentation.Masters[0])
            .OfType<DrawOp.Shape>()
            .Select(op => op.ShapeId)
            .Should().Contain(20);
        SlideCompositor.ComposeLayout(presentation, presentation.Layouts[0])
            .OfType<DrawOp.Shape>()
            .Select(op => op.ShapeId)
            .Should().Contain(new uint[] { 20, 21 });
    }

    [Fact]
    public void Deleting_a_nested_master_shape_restores_it_to_its_group_on_undo()
    {
        var presentation = CreatePresentation();
        var group = new SlideShape { Id = 30, Kind = SlideShapeKind.Group };
        group.Children.Add(new SlideShape { Id = 31, OffsetXEmu = 10, ExtentCxEmu = 50, ExtentCyEmu = 50 });
        presentation.Masters[0].Placeholders.Add(group);
        var session = new MasterEditingSession(presentation, new PresentationCommandBus(presentation));

        session.Delete(31);
        group.Children.Should().BeEmpty();

        session.Undo();
        group.Children.Should().ContainSingle().Which.Id.Should().Be(31);
    }

    [Fact]
    public void Master_edits_survive_pptx_save_and_reopen()
    {
        var presentation = Presentation.CreateEmpty();
        var master = presentation.Masters[0];
        master.Placeholders.Add(new SlideShape
        {
            Id = 77,
            Name = "Master title",
            OffsetXEmu = 100,
            OffsetYEmu = 200,
            ExtentCxEmu = 1000,
            ExtentCyEmu = 700,
            Placeholder = new Placeholder { Type = PlaceholderType.Title },
        });
        var session = new MasterEditingSession(presentation, new PresentationCommandBus(presentation));
        session.Move(77, 300, 400);

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        stream.Position = 0;
        var reopened = PptxPackageReader.Read(stream);

        var saved = reopened.Masters.Single().Placeholders.Single(shape => shape.Id == 77);
        saved.OffsetXEmu.Should().Be(400);
        saved.OffsetYEmu.Should().Be(600);
    }

    private static Presentation CreatePresentation()
    {
        var presentation = new Presentation();
        presentation.Masters.Add(new SlideMaster { Id = "master-1", Name = "Main Master" });
        presentation.Layouts.Add(new SlideLayout { Id = "layout-1", MasterId = "master-1", Name = "Title" });
        return presentation;
    }
}

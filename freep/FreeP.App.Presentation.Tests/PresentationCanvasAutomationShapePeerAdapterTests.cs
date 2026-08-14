using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationCanvasAutomationShapePeerAdapterTests
{
    [Fact]
    public void AdapterProjectsDescriptorRoleBoundsAndSelectionState()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        var shape = new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.Picture,
            Name = "Product image",
            AlternativeText = "A product preview"
        };
        slide.Shapes.Add(shape);
        var selectedShapeIds = new List<uint> { shape.Id };
        var coordinator = new PresentationCanvasAutomationPeerCoordinator<FakePeer>(
            new PresentationCanvasAutomationSession(),
            () => presentation,
            () => slide,
            () => selectedShapeIds,
            FakePeer.Create);
        var adapter = new PresentationCanvasAutomationShapePeerAdapter<
            FakePeer,
            string,
            string>(
                coordinator,
                shape.Id,
                role => $"native:{role}",
                fallbackRole: "native:fallback",
                shapeId => $"bounds:{shapeId}");

        adapter.Name.Should().Be("Product image");
        adapter.AutomationId.Should().Be("Shape_7");
        adapter.ClassName.Should().Be(PresentationCanvasAutomationSession.ShapeClassName);
        adapter.HelpText.Should().Be("A product preview");
        adapter.LocalizedControlType.Should().Be(
            PresentationCanvasAutomationSession.ShapeLocalizedControlType);
        adapter.Role.Should().Be("native:Image");
        adapter.Bounds.Should().Be("bounds:7");
        adapter.IsSelected.Should().BeTrue();
        adapter.HasKeyboardFocus.Should().BeTrue();
    }

    [Fact]
    public void AdapterUsesFallbacksAndPreservesReadOnlySelectionPolicy()
    {
        var coordinator = new PresentationCanvasAutomationPeerCoordinator<FakePeer>(
            new PresentationCanvasAutomationSession(),
            () => null,
            () => null,
            () => null,
            FakePeer.Create);
        var adapter = new PresentationCanvasAutomationShapePeerAdapter<
            FakePeer,
            string,
            int>(coordinator, 99, role => role.ToString(), "fallback", _ => 42);

        adapter.Name.Should().BeEmpty();
        adapter.AutomationId.Should().BeEmpty();
        adapter.ClassName.Should().Be(PresentationCanvasAutomationSession.ShapeClassName);
        adapter.Role.Should().Be("fallback");
        adapter.Bounds.Should().Be(42);
        adapter.Invoking(subject => subject.Select())
            .Should().Throw<InvalidOperationException>()
            .WithMessage(PresentationCanvasAutomationSession.SelectionMutationNotSupportedMessage);
    }

    private sealed record FakePeer(uint ShapeId)
    {
        public static FakePeer Create(uint shapeId) => new(shapeId);
    }
}

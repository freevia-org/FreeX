namespace FreeP.App.Compositor.Tests;

public sealed class PresentationCanvasAutomationPeerCoordinatorTests
{
    [Fact]
    public void CoordinatorOwnsPeerProjectionReuseAndSelectionNotificationOrder()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        slide.Shapes.AddRange([Shape(1), Shape(2)]);
        var selectedShapeIds = new List<uint> { 1 };
        var automation = new PresentationCanvasAutomationSession();
        automation.ResetSelection(slide, selectedShapeIds);
        var createdIds = new List<uint>();
        var coordinator = new PresentationCanvasAutomationPeerCoordinator<FakePeer>(
            automation,
            () => presentation,
            () => slide,
            () => selectedShapeIds,
            shapeId =>
            {
                createdIds.Add(shapeId);
                return new FakePeer(shapeId);
            });

        var children = coordinator.SynchronizeChildren();
        var selectedPeer = coordinator.GetSelection().Should().ContainSingle().Subject;

        children.Select(peer => peer.ShapeId).Should().Equal(1u, 2u);
        selectedPeer.Should().BeSameAs(children[0]);
        createdIds.Should().Equal(1u, 2u);
        coordinator.CanvasDescriptor.Name.Should().Be("Slide 1 canvas");

        selectedShapeIds.Clear();
        selectedShapeIds.Add(2);
        var delta = automation.CaptureSelectionDelta(slide, selectedShapeIds);
        var changes = coordinator.GetSelectionChanges(delta);
        var focus = coordinator.GetFocusChange(delta);

        changes.Select(change => (change.Peer.ShapeId, change.WasSelected, change.IsSelected))
            .Should().Equal((1u, true, false), (2u, false, true));
        focus.PreviousPeer.Should().BeSameAs(children[0]);
        focus.CurrentPeer.Should().BeSameAs(children[1]);
        createdIds.Should().Equal(1u, 2u);
    }

    [Fact]
    public void CoordinatorProjectsLiveBoundsAndEvictsPeersMissingFromTheSlide()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        slide.Shapes.AddRange([Shape(1), Shape(2)]);
        var createdIds = new List<uint>();
        var coordinator = new PresentationCanvasAutomationPeerCoordinator<FakePeer>(
            new PresentationCanvasAutomationSession(),
            () => presentation,
            () => slide,
            () => [],
            shapeId =>
            {
                createdIds.Add(shapeId);
                return new FakePeer(shapeId);
            });

        var first = coordinator.SynchronizeChildren();
        slide.Shapes.RemoveAt(0);
        var second = coordinator.SynchronizeChildren();

        second.Should().ContainSingle().Which.Should().BeSameAs(first[1]);
        coordinator.TryProjectLocalBounds(
                2,
                new SlideTransformCore(2, 100, 50, 960, 540),
                out var bounds)
            .Should().BeTrue();
        bounds.Left.Should().Be(100);
        bounds.Top.Should().Be(50);
        bounds.Width.Should().BeApproximately(30d / 9525 * 2, 0.000000001);
        bounds.Height.Should().BeApproximately(40d / 9525 * 2, 0.000000001);
        coordinator.TryProjectLocalBounds(1, SlideTransformCore.Identity, out _).Should().BeFalse();
        createdIds.Should().Equal(1u, 2u);
    }

    private static SlideShape Shape(uint id) =>
        new()
        {
            Id = id,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 30,
            ExtentCyEmu = 40,
        };

    private sealed record FakePeer(uint ShapeId);
}

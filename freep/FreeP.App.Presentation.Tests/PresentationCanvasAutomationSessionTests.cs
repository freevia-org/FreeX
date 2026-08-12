namespace FreeP.App.Compositor.Tests;

public sealed class PresentationCanvasAutomationSessionTests
{
    [Fact]
    public void ProjectionOwnsCanvasShapeIdentityRolesVisibilitySelectionAndFocus()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        slide.Shapes.AddRange(
        [
            Shape(
                1,
                SlideShapeKind.Picture,
                alternativeTextTitle: "Cover photo",
                alternativeText: "A product photograph"),
            Shape(2, SlideShapeKind.Table, alternativeText: "Quarterly results"),
            Shape(3, SlideShapeKind.AutoShape),
            Shape(4, SlideShapeKind.Picture, name: "Hidden", isHidden: true),
        ]);
        var selectedShapeIds = new List<uint> { 2, 4, 99, 1, 2 };
        var session = new PresentationCanvasAutomationSession();

        var canvas = session.ProjectCanvas(presentation, slide);
        var shapes = session.ProjectShapes(slide, selectedShapeIds);
        var selection = session.ProjectSelection(slide, selectedShapeIds);

        canvas.Should().Match<PresentationCanvasAutomationDescriptor>(descriptor =>
            descriptor.ShapeId == null &&
            descriptor.ClassName == "SlideCanvas" &&
            descriptor.Name == "Slide 1 canvas" &&
            descriptor.Role == PresentationCanvasAutomationRole.Canvas);
        shapes.Select(shape => shape.ShapeId).Should().Equal(1u, 2u, 3u);
        shapes.Select(shape => shape.AutomationId).Should().Equal("Shape_1", "Shape_2", "Shape_3");
        shapes.Select(shape => shape.Name).Should().Equal("Cover photo", "Quarterly results", "Shape 3");
        shapes.Select(shape => shape.Role).Should().Equal(
            PresentationCanvasAutomationRole.Image,
            PresentationCanvasAutomationRole.DataGrid,
            PresentationCanvasAutomationRole.Shape);
        shapes.Single(shape => shape.ShapeId == 1).Should().Match<PresentationCanvasAutomationDescriptor>(
            descriptor => descriptor.HelpText == "A product photograph" &&
                          descriptor.Bounds == new PresentationCanvasAutomationBounds(10, 20, 30, 40) &&
                          descriptor.IsSelected &&
                          descriptor.HasKeyboardFocus);
        shapes.Single(shape => shape.ShapeId == 2).Should().Match<PresentationCanvasAutomationDescriptor>(
            descriptor => descriptor.IsSelected && !descriptor.HasKeyboardFocus);
        selection.Select(shape => shape.ShapeId).Should().Equal(2u, 1u);
        session.TryProjectShape(slide, 4, selectedShapeIds, out _).Should().BeFalse();
        session.TryProjectShape(slide, 99, selectedShapeIds, out _).Should().BeFalse();
    }

    [Fact]
    public void CaptureSelectionDeltaDetachesSnapshotsFromInPlaceSelectionMutation()
    {
        var slide = Presentation.CreateEmpty().Slides[0];
        slide.Shapes.Clear();
        slide.Shapes.AddRange([Shape(1), Shape(2)]);
        var selectedShapeIds = new List<uint> { 1 };
        var session = new PresentationCanvasAutomationSession();
        session.ResetSelection(slide, selectedShapeIds);

        selectedShapeIds.Clear();
        selectedShapeIds.Add(2);
        var delta = session.CaptureSelectionDelta(slide, selectedShapeIds);

        delta.HasChanges.Should().BeTrue();
        delta.Previous.ShapeIds.Should().Equal(1u);
        delta.Current.ShapeIds.Should().Equal(2u);
        delta.RemovedShapeIds.Should().Equal(1u);
        delta.AddedShapeIds.Should().Equal(2u);
        delta.FocusIntent.Should().Be(PresentationCanvasAutomationFocusIntent.MoveToShape);
        delta.Previous.FocusedShapeId.Should().Be(1);
        delta.Current.FocusedShapeId.Should().Be(2);

        selectedShapeIds.Clear();
        delta.Current.ShapeIds.Should().Equal(2u);
        session.Selection.ShapeIds.Should().Equal(2u);
    }

    [Fact]
    public void FocusIntentTracksReorderFallbackAndClearWithoutInventingSelectionChanges()
    {
        var slide = Presentation.CreateEmpty().Slides[0];
        slide.Shapes.Clear();
        slide.Shapes.AddRange([Shape(1), Shape(2)]);
        var selectedShapeIds = new List<uint> { 1, 2 };
        var session = new PresentationCanvasAutomationSession();
        session.ResetSelection(slide, selectedShapeIds);

        selectedShapeIds.Clear();
        selectedShapeIds.AddRange([2, 1]);
        var reordered = session.CaptureSelectionDelta(slide, selectedShapeIds);

        reordered.HasChanges.Should().BeTrue();
        reordered.AddedShapeIds.Should().BeEmpty();
        reordered.RemovedShapeIds.Should().BeEmpty();
        reordered.FocusIntent.Should().Be(PresentationCanvasAutomationFocusIntent.MoveToShape);
        reordered.Previous.FocusedShapeId.Should().Be(2);
        reordered.Current.FocusedShapeId.Should().Be(1);

        var unchanged = session.CaptureSelectionDelta(slide, selectedShapeIds);
        unchanged.HasChanges.Should().BeFalse();
        unchanged.FocusIntent.Should().Be(PresentationCanvasAutomationFocusIntent.None);

        selectedShapeIds.Clear();
        var cleared = session.CaptureSelectionDelta(slide, selectedShapeIds);
        cleared.RemovedShapeIds.Should().Equal(2u, 1u);
        cleared.FocusIntent.Should().Be(PresentationCanvasAutomationFocusIntent.ClearShapeFocus);
        cleared.Current.FocusedShapeId.Should().BeNull();
    }

    [Fact]
    public void SelectionProviderPolicyIsSharedAndKeepsEditingSessionAsMutationOwner()
    {
        var session = new PresentationCanvasAutomationSession();

        session.CanSelectMultiple.Should().BeTrue();
        session.IsSelectionRequired.Should().BeFalse();
        foreach (var mutation in Enum.GetValues<PresentationCanvasAutomationSelectionMutation>())
        {
            var request = () => session.RequestSelectionMutation(42, mutation);
            request.Should().Throw<InvalidOperationException>()
                .WithMessage(PresentationCanvasAutomationSession.SelectionMutationNotSupportedMessage);
        }
    }

    [Fact]
    public void ProjectLocalBoundsUsesTheLiveCanvasTransformWithoutNativeGeometry()
    {
        var slide = Presentation.CreateEmpty().Slides[0];
        slide.Shapes.Clear();
        slide.Shapes.Add(new SlideShape
        {
            Id = 7,
            OffsetXEmu = 10 * 9525,
            OffsetYEmu = 20 * 9525,
            ExtentCxEmu = 30 * 9525,
            ExtentCyEmu = 40 * 9525,
        });
        var session = new PresentationCanvasAutomationSession();
        session.TryProjectShape(slide, 7, [], out var descriptor).Should().BeTrue();

        session.TryProjectLocalBounds(
                descriptor,
                new SlideTransformCore(2, 100, 50, 960, 540),
                out var bounds)
            .Should().BeTrue();

        bounds.Should().Be(new SlideScreenRect(120, 90, 60, 80));
        session.TryProjectLocalBounds(
                session.ProjectCanvas(null, null),
                SlideTransformCore.Identity,
                out _)
            .Should().BeFalse();
    }

    [Fact]
    public void RendererPeersOnlyTranslateSharedAutomationPolicy()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var sources = new[]
        {
            Read(root, "freep", "FreeP.App.Rendering.Wpf", "SlideCanvas.cs"),
            Read(root, "freep", "FreeP.App.Rendering.Avalonia", "SlideCanvas.cs"),
        };

        foreach (var source in sources)
        {
            var automationSource = source[source.IndexOf("Accessibility: UI Automation", StringComparison.Ordinal)..];
            var boundsSource = automationSource[
                automationSource.IndexOf("internal Rect GetShapeBoundingRectangle", StringComparison.Ordinal)..
                automationSource.IndexOf("internal void NotifySelectionChanged", StringComparison.Ordinal)];

            automationSource.Should().Contain("_canvasAutomation.ProjectCanvas(")
                .And.Contain("_canvasAutomation.ProjectShapes(")
                .And.Contain("_canvasAutomation.ProjectSelection(")
                .And.Contain("PresentationCanvasAutomationSelectionDelta delta")
                .And.Contain("_canvasAutomation.CanSelectMultiple")
                .And.Contain("_canvasAutomation.RequestSelectionMutation(")
                .And.Contain("PresentationCanvasAutomationRole.Image => AutomationControlType.Image")
                .And.NotContain("_lastNotifiedSelection")
                .And.NotContain("SequenceEqual(previous)")
                .And.NotContain("AlternativeTextTitle")
                .And.NotContain("ShapeKindToControlType")
                .And.NotContain("Shape selection is owned by the slide canvas's editing session.")
                .And.NotContain("$\"Shape_{shapeId}\"");

            boundsSource.Should().Contain("_canvasAutomation.TryProjectLocalBounds(")
                .And.NotContain("SlideTransformCore.EmuToDip(")
                .And.NotContain("SlideTransform.EmuToDip(")
                .And.NotContain(".SlideToScreen(");
        }
    }

    private static SlideShape Shape(
        uint id,
        SlideShapeKind kind = SlideShapeKind.AutoShape,
        string name = "",
        string alternativeTextTitle = "",
        string alternativeText = "",
        bool isHidden = false) =>
        new()
        {
            Id = id,
            Kind = kind,
            Name = name,
            AlternativeTextTitle = alternativeTextTitle,
            AlternativeText = alternativeText,
            IsHidden = isHidden,
            OffsetXEmu = 10,
            OffsetYEmu = 20,
            ExtentCxEmu = 30,
            ExtentCyEmu = 40,
        };

    private static string Read(string root, params string[] relativeParts) =>
        File.ReadAllText(Path.Combine(new[] { root }.Concat(relativeParts).ToArray()));
}

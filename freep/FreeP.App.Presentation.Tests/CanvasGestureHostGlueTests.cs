using FreeP.Core.Model;
using Free.Shared.Drawing;

namespace FreeP.App.Compositor.Tests;

public sealed class CanvasGestureHostGlueTests
{
    private const long EmusPerDip = 9525;

    private sealed class NullPreviewSurface : ICanvasGesturePreviewSurface
    {
        public void UpdatePreview(SlideScreenRect? bounds, double rotationDegrees = 0) { }
        public void UpdateSnapGuides(IReadOnlyList<SnapGuideLine>? guides, SlideTransformCore transform) { }
        public void UpdateTransformPreview(CanvasMultiTransformPlan plan) { }
        public void UpdateGeometryPreview(string handleName, CanvasGesturePoint screenPoint) { }
        public void UpdateMarquee(SlideScreenRect bounds) { }
    }

    private static (EditingSession Editor, CanvasGestureHostGlue Glue, SlideShape Shape) Create()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        var shape = new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 10 * EmusPerDip,
            OffsetYEmu = 20 * EmusPerDip,
            ExtentCxEmu = 200 * EmusPerDip,
            ExtentCyEmu = 100 * EmusPerDip,
        };
        slide.Shapes.Add(shape);
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var glue = new CanvasGestureHostGlue(
            editor,
            new CanvasGestureRouter(editor),
            new NullPreviewSurface(),
            onChartPointDoubleClick: null,
            tryOpenOleInPlace: null,
            tryActivateOleExternally: null);
        return (editor, glue, shape);
    }

    [Fact]
    public void BuildAdornerProjection_IsEmptyWithoutSelectionAndFramesTheSelectedShape()
    {
        var (editor, glue, shape) = Create();

        glue.BuildAdornerProjection(SlideTransformCore.Identity).Selections.Should().BeEmpty();

        editor.Select(shape.Id);
        glue.BuildAdornerProjection(SlideTransformCore.Identity).Selections.Should().HaveCount(1);
    }

    [Fact]
    public void PlanCursor_MapsSelectionAndHitTestingToCursorKinds()
    {
        var (editor, glue, shape) = Create();
        var inside = new CanvasGesturePoint(110, 70);
        var outside = new CanvasGesturePoint(600, 400);

        glue.PlanCursor(inside, SlideTransformCore.Identity).Should().Be(CanvasGestureCursorKind.Pointer);
        glue.PlanCursor(outside, SlideTransformCore.Identity).Should().Be(CanvasGestureCursorKind.Default);

        editor.Select(shape.Id);
        glue.PlanCursor(inside, SlideTransformCore.Identity).Should().Be(CanvasGestureCursorKind.Move);
    }

    [Fact]
    public void HandleOleDoubleClick_RejectsShapesThatAreNotOleObjects()
    {
        var (_, glue, shape) = Create();

        glue.HandleOleDoubleClick(shape).Should().BeFalse();
    }
}

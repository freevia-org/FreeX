using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class CanvasGestureRouterTests
{
    private const long EmusPerDip = 9525;

    [Fact]
    public void SelectedHandlePress_BeginsResizeBeforeShapeSelectionAndCommitsThroughRouter()
    {
        var (editor, shape) = CreateEditorWithShape();
        editor.Select(shape.Id);
        var router = new CanvasGestureRouter(editor)
        {
            SnapToGrid = false,
            SnapToShapes = false,
        };

        var press = router.HandlePointerPressed(Request(
            screen: new CanvasGesturePoint(110, 70),
            slide: new CanvasGesturePoint(110, 70),
            selectionHandle: CanvasGestureHandleKind.ResizeSE,
            hasSingleSelectionFrame: true));

        press.Handled.Should().BeTrue();
        press.CapturePointer.Should().BeTrue();
        router.Kind.Should().Be(CanvasGestureKind.Resize);

        var preview = router.PreviewPointer(
            new CanvasGesturePoint(120, 80),
            SlideTransformCore.Identity,
            CanvasGestureModifiers.None);
        preview.Kind.Should().Be(CanvasGestureKind.Resize);
        preview.ShapeId.Should().Be(shape.Id);
        preview.Resize.Should().Be(new CanvasResizeBounds(
            10 * EmusPerDip,
            20 * EmusPerDip,
            110 * EmusPerDip,
            60 * EmusPerDip));

        router.CompletePointer(
            new CanvasGesturePoint(120, 80),
            SlideTransformCore.Identity,
            CanvasGestureModifiers.None).Should().BeTrue();

        shape.ExtentCxEmu.Should().Be(110 * EmusPerDip);
        shape.ExtentCyEmu.Should().Be(60 * EmusPerDip);
        router.IsActive.Should().BeFalse();
        editor.CanUndo.Should().BeTrue();
    }

    [Theory]
    [InlineData(CanvasGestureModifiers.Control)]
    [InlineData(CanvasGestureModifiers.Shift)]
    [InlineData(CanvasGestureModifiers.Meta)]
    public void AdditiveSelectionModifiers_SelectWithoutStartingAnAccidentalMove(
        CanvasGestureModifiers modifier)
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        var first = Shape(1, 10, 20, 100, 50);
        var second = Shape(2, 200, 20, 100, 50);
        slide.Shapes.Add(first);
        slide.Shapes.Add(second);
        var editor = new EditingSession(
            presentation,
            new PresentationCommandBus(presentation));
        editor.Select(first.Id);
        var router = new CanvasGestureRouter(editor);

        var plan = router.HandlePointerPressed(Request(
            screen: new CanvasGesturePoint(220, 30),
            slide: new CanvasGesturePoint(220, 30),
            modifiers: modifier));

        plan.Handled.Should().BeTrue();
        plan.CapturePointer.Should().BeFalse();
        editor.SelectedShapeIds.Should().Equal(first.Id, second.Id);
        router.IsActive.Should().BeFalse();
    }

    [Fact]
    public void EmptyPress_MarqueeUsesSharedDragThresholdAndSelectionCommit()
    {
        var (editor, shape) = CreateEditorWithShape();
        editor.Select(shape.Id);
        var router = new CanvasGestureRouter(editor);

        var press = router.HandlePointerPressed(Request(
            screen: new CanvasGesturePoint(-10, -10),
            slide: new CanvasGesturePoint(-10, -10)));

        press.CapturePointer.Should().BeTrue();
        router.Kind.Should().Be(CanvasGestureKind.Marquee);
        editor.SelectedShapeIds.Should().BeEmpty();
        router.PreviewPointer(
                new CanvasGesturePoint(-8, -10),
                SlideTransformCore.Identity,
                CanvasGestureModifiers.None)
            .Should().Be(CanvasGesturePreviewPlan.Empty);

        var preview = router.PreviewPointer(
            new CanvasGesturePoint(200, 200),
            SlideTransformCore.Identity,
            CanvasGestureModifiers.None);
        preview.Kind.Should().Be(CanvasGestureKind.Marquee);
        preview.Marquee.Should().Be(new SlideScreenRect(-10, -10, 210, 210));

        router.CompletePointer(
            new CanvasGesturePoint(200, 200),
            SlideTransformCore.Identity,
            CanvasGestureModifiers.None).Should().BeTrue();

        editor.SelectedShapeIds.Should().Equal(shape.Id);
    }

    [Fact]
    public void PreviewProjector_OwnsScreenBoundsGuidesRotationAndGeometryProjection()
    {
        var (editor, shape) = CreateEditorWithShape();
        var transform = new SlideTransformCore(2, 5, 7, 960, 540);
        var moveGuide = new SnapGuideLine { IsHorizontal = true, Position = 25 };

        var move = CanvasGesturePreviewProjector.Project(
            new CanvasGesturePreviewPlan(
                CanvasGestureKind.Move,
                0,
                new CanvasMovePlan(0, 0, [], new SlideScreenRect(1, 2, 3, 4), [moveGuide]),
                null,
                null,
                null,
                null,
                null),
            editor.CurrentSlide,
            editor.Presentation,
            transform);
        var resize = CanvasGesturePreviewProjector.Project(
            new CanvasGesturePreviewPlan(
                CanvasGestureKind.Resize,
                shape.Id,
                null,
                new CanvasResizeBounds(
                    10 * EmusPerDip,
                    20 * EmusPerDip,
                    100 * EmusPerDip,
                    50 * EmusPerDip),
                null,
                null,
                null,
                null),
            editor.CurrentSlide,
            editor.Presentation,
            transform);
        var rotate = CanvasGesturePreviewProjector.Project(
            new CanvasGesturePreviewPlan(
                CanvasGestureKind.Rotate,
                shape.Id,
                null,
                null,
                null,
                45,
                null,
                null),
            editor.CurrentSlide,
            editor.Presentation,
            transform);
        var geometry = CanvasGesturePreviewProjector.Project(
            new CanvasGesturePreviewPlan(
                CanvasGestureKind.GeometryAdjustment,
                shape.Id,
                null,
                null,
                null,
                null,
                new CanvasGeometryPreviewPlan("adj", new CanvasGesturePoint(3, 4)),
                null),
            editor.CurrentSlide,
            editor.Presentation,
            transform);

        move.PreviewBounds.Should().Be(new SlideScreenRect(1, 2, 3, 4));
        move.SnapGuides.Should().ContainSingle().Which.Should().Be(moveGuide);
        resize.PreviewBounds.Should().Be(new SlideScreenRect(25, 47, 200, 100));
        rotate.PreviewBounds.Should().Be(new SlideScreenRect(25, 47, 200, 100));
        rotate.RotationDegrees.Should().Be(45);
        geometry.GeometryHandleName.Should().Be("adj");
        geometry.GeometryScreenPoint.Should().Be(new CanvasGesturePoint(11, 15));
    }

    [Fact]
    public void DoubleClickRouting_DefersText_ExternalizesOle_AndTerminatesZoomNavigation()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Add(new Slide { NumericId = 257 });
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        var text = Shape(1, 10, 10, 80, 40);
        text.TextBody = new TextBody();
        var ole = Shape(2, 120, 10, 80, 40);
        ole.Kind = SlideShapeKind.Ole;
        var zoom = Shape(3, 230, 10, 80, 40);
        zoom.Kind = SlideShapeKind.Zoom;
        zoom.PreservedObject = new PreservedObjectInfo
        {
            ObjectKind = PreservedObjectKind.Zoom,
            ZoomTargetSlideNumericId = 257,
        };
        slide.Shapes.Add(text);
        slide.Shapes.Add(ole);
        slide.Shapes.Add(zoom);
        var editor = new EditingSession(
            presentation,
            new PresentationCommandBus(presentation));
        var router = new CanvasGestureRouter(editor);

        router.HandlePointerPressed(Request(
                new CanvasGesturePoint(20, 20),
                new CanvasGesturePoint(20, 20),
                clickCount: 2))
            .Should().Be(CanvasGesturePressPlan.Unhandled);

        var olePlan = router.HandlePointerPressed(Request(
            new CanvasGesturePoint(130, 20),
            new CanvasGesturePoint(130, 20),
            clickCount: 2));
        olePlan.Action.Should().Be(CanvasGesturePressActionKind.ActivateOle);
        olePlan.Shape.Should().BeSameAs(ole);
        olePlan.Handled.Should().BeTrue();

        var zoomPlan = router.HandlePointerPressed(Request(
            new CanvasGesturePoint(240, 20),
            new CanvasGesturePoint(240, 20),
            clickCount: 2));
        zoomPlan.Should().Be(CanvasGesturePressPlan.HandledOnly);
        editor.CurrentSlideIndex.Should().Be(1);
        editor.SelectedShapeIds.Should().BeEmpty();
    }

    [Fact]
    public void FormatPainterPress_IsTerminalBeforeSelectionOrGesturePlanning()
    {
        var (editor, shape) = CreateEditorWithShape();
        shape.Fill = new ShapeFill.Solid(
            new ThemeAwareColor(SrgbColor.FromRgb(0x336699)));
        editor.Select(shape.Id);
        editor.BeginFormatPainter().Should().BeTrue();
        var router = new CanvasGestureRouter(editor);

        var plan = router.HandlePointerPressed(Request(
            screen: new CanvasGesturePoint(500, 500),
            slide: new CanvasGesturePoint(500, 500)));

        plan.Should().Be(CanvasGesturePressPlan.HandledOnly);
        router.IsActive.Should().BeFalse();
        editor.SelectedShapeIds.Should().Equal(shape.Id);
    }

    [Fact]
    public void KeyboardRouting_OwnsNudgeModifierDeleteAndGestureCancelActions()
    {
        var (editor, shape) = CreateEditorWithShape();
        editor.Select(shape.Id);
        var router = new CanvasGestureRouter(editor);

        router.HandleKeyDown(CanvasGestureKey.Right, CanvasGestureModifiers.None)
            .Handled.Should().BeTrue();
        router.HandleKeyDown(CanvasGestureKey.Down, CanvasGestureModifiers.Shift)
            .Handled.Should().BeTrue();
        shape.OffsetXEmu.Should().Be(10 * EmusPerDip + CanvasGesturePlanner.SmallNudgeEmu);
        shape.OffsetYEmu.Should().Be(20 * EmusPerDip + CanvasGesturePlanner.LargeNudgeEmu);

        router.BeginMove(
            editor.CurrentSlide!,
            editor.SelectedShapeIds,
            new CanvasGesturePoint(0, 0));
        var escape = router.HandleKeyDown(
            CanvasGestureKey.Escape,
            CanvasGestureModifiers.None);
        escape.Should().Be(new CanvasGestureKeyboardPlan(
            true,
            CanvasGestureKeyboardActionKind.CancelGesture));
        router.IsActive.Should().BeTrue("the native host still owns pointer release and visual cleanup");

        router.Cancel();
        router.HandleKeyDown(CanvasGestureKey.Delete, CanvasGestureModifiers.None)
            .Handled.Should().BeTrue();
        editor.CurrentSlide!.Shapes.Should().NotContain(shape);
    }

    private static CanvasGesturePressRequest Request(
        CanvasGesturePoint screen,
        CanvasGesturePoint slide,
        int clickCount = 1,
        CanvasGestureModifiers modifiers = CanvasGestureModifiers.None,
        CanvasGestureHandleKind selectionHandle = CanvasGestureHandleKind.None,
        string? geometryHandle = null,
        bool hasSingleSelectionFrame = false,
        bool canNotifyChartPointDoubleClick = false) => new(
            screen,
            slide,
            clickCount,
            modifiers,
            selectionHandle,
            geometryHandle,
            hasSingleSelectionFrame,
            canNotifyChartPointDoubleClick);

    private static (EditingSession Editor, SlideShape Shape) CreateEditorWithShape()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        var shape = Shape(1, 10, 20, 100, 50);
        slide.Shapes.Add(shape);
        return (
            new EditingSession(
                presentation,
                new PresentationCommandBus(presentation)),
            shape);
    }

    private static SlideShape Shape(
        uint id,
        long xDip,
        long yDip,
        long widthDip,
        long heightDip) => new()
        {
            Id = id,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = xDip * EmusPerDip,
            OffsetYEmu = yDip * EmusPerDip,
            ExtentCxEmu = widthDip * EmusPerDip,
            ExtentCyEmu = heightDip * EmusPerDip,
        };
}

using System.Windows;
using System.Windows.Input;
using FreeP.App.Compositor;
using FreeP.Core.Model;
using Free.Shared.Drawing;

namespace FreeP.App.Rendering.Wpf;

public sealed partial class CanvasGestureHandler
{
    internal bool IsGestureActiveForTests => _gestureRouter.IsActive;

    internal bool HasPendingGestureStateForTests => _gestureRouter.HasPendingState;

    internal bool HasTransientInteractionVisualsForTests =>
        _adorner.HasTransientInteractionVisualsForTests ||
        _canvas.HasLiveTransformPreviewForTests;

    internal SelectionAdorner AdornerForTests => _adorner;

    internal bool HandleEscapeForTests() => HandleKeyDown(Key.Escape, ModifierKeys.None);

    internal bool HandleKeyDownForTests(Key key, ModifierKeys modifiers) =>
        HandleKeyDown(key, modifiers);

    internal void SimulateStaleMouseUpForTests() =>
        CompleteGesture(
            new Point(0, 0),
            SlideTransform.Identity,
            CanvasGestureModifiers.None);

    internal void SeedTransientInteractionVisualsForTests()
    {
        if (_editor.CurrentSlide is { } slide)
        {
            var previewRouter = new CanvasGestureRouter(_editor)
            {
                SnapToGrid = false,
                SnapToShapes = false,
            };
            if (previewRouter.BeginMultiResize(
                slide,
                _editor.SelectedShapeIds,
                CanvasGestureHandleKind.ResizeSE,
                new CanvasGesturePoint(0, 0)))
            {
                var preview = previewRouter.PreviewPointer(
                    new CanvasGesturePoint(4, 4),
                    ToCoreTransform(_canvas.CurrentTransform),
                    CanvasGestureModifiers.None);
                if (preview.MultiTransform is { } transform)
                    _canvas.UpdateTransformPreview(transform);
            }
        }

        _adorner.UpdatePreview(new Rect(1, 1, 10, 10));
        _adorner.UpdateGeometryPreview("test", new Point(2, 2));
        _adorner.UpdateMarquee(new Rect(3, 3, 8, 8));
        _adorner.UpdateSnapGuides(
            [new SnapGuideLine { IsHorizontal = true, Position = 4, Label = "test" }],
            SlideTransform.Identity);
    }

    internal void SeedResizeStateForTests(
        Point startScreen,
        SlideShape shape,
        CanvasGestureHandleKind handle)
    {
        if (_editor.CurrentSlide is null ||
            !_gestureRouter.BeginResize(
                _editor.CurrentSlide,
                shape.Id,
                handle,
                ToGesturePoint(startScreen)))
        {
            throw new InvalidOperationException("The shape must belong to the current slide.");
        }
    }

    internal void SeedMoveStateForTests(Point startScreen)
    {
        if (_editor.CurrentSlide is null)
            throw new InvalidOperationException("A current slide is required to seed a move gesture.");

        _gestureRouter.BeginMove(
            _editor.CurrentSlide,
            _editor.SelectedShapeIds,
            ToGesturePoint(startScreen));
    }

    internal void CompleteGestureForTests(Point currentScreen) =>
        CompleteGesture(
            currentScreen,
            _canvas.CurrentTransform,
            CanvasGestureModifiers.None);
}

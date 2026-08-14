namespace FreeP.App.Compositor.Tests;

public sealed class CanvasGesturePressActionDispatcherTests
{
    [Fact]
    public void DispatchRoutesChartAndOleActionsToTheirHostCallbacks()
    {
        ChartPointHit? chartHit = null;
        SlideShape? activatedShape = null;
        var expectedHit = new ChartPointHit(3, 4, 5);
        var expectedShape = new SlideShape { Id = 42 };

        CanvasGesturePressActionDispatcher.Dispatch(
            new CanvasGesturePressPlan(
                Handled: true,
                CapturePointer: false,
                Action: CanvasGesturePressActionKind.NotifyChartPointDoubleClick,
                ChartPoint: expectedHit,
                Shape: null),
            hit => chartHit = hit,
            shape => activatedShape = shape);
        CanvasGesturePressActionDispatcher.Dispatch(
            new CanvasGesturePressPlan(
                Handled: true,
                CapturePointer: false,
                Action: CanvasGesturePressActionKind.ActivateOle,
                ChartPoint: null,
                Shape: expectedShape),
            hit => chartHit = hit,
            shape => activatedShape = shape);

        chartHit.Should().Be(expectedHit);
        activatedShape.Should().BeSameAs(expectedShape);
    }

    [Fact]
    public void DispatchIgnoresActionsWithoutRequiredPayloads()
    {
        var callbackCount = 0;
        var plan = new CanvasGesturePressPlan(
            Handled: false,
            CapturePointer: false,
            Action: CanvasGesturePressActionKind.NotifyChartPointDoubleClick,
            ChartPoint: null,
            Shape: null);

        CanvasGesturePressActionDispatcher.Dispatch(
            plan,
            _ => callbackCount++,
            _ => callbackCount++);

        callbackCount.Should().Be(0);
    }
}

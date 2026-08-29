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
            shape => { activatedShape = shape; return true; });
        CanvasGesturePressActionDispatcher.Dispatch(
            new CanvasGesturePressPlan(
                Handled: true,
                CapturePointer: false,
                Action: CanvasGesturePressActionKind.ActivateOle,
                ChartPoint: null,
                Shape: expectedShape),
            hit => chartHit = hit,
            shape => { activatedShape = shape; return true; });

        chartHit.Should().Be(expectedHit);
        activatedShape.Should().BeSameAs(expectedShape);
    }

    [Fact]
    public void DispatchReturnsTheActivateOleCallbackResultInsteadOfDiscardingIt()
    {
        var expectedShape = new SlideShape { Id = 7 };
        var plan = new CanvasGesturePressPlan(
            Handled: true,
            CapturePointer: false,
            Action: CanvasGesturePressActionKind.ActivateOle,
            ChartPoint: null,
            Shape: expectedShape);

        // The whole point of the `Func<SlideShape, bool>` parameter (rather than the previous
        // `Action<SlideShape>`) is that a failed activation is observable by the caller instead
        // of being silently swallowed by a void delegate conversion.
        CanvasGesturePressActionDispatcher.Dispatch(plan, notifyChartPointDoubleClick: null, activateOle: _ => false)
            .Should().BeFalse();
        CanvasGesturePressActionDispatcher.Dispatch(plan, notifyChartPointDoubleClick: null, activateOle: _ => true)
            .Should().BeTrue();
    }

    [Fact]
    public void DispatchReturnsNullForNonOleActions()
    {
        var expectedHit = new ChartPointHit(1, 2, 3);
        var chartPlan = new CanvasGesturePressPlan(
            Handled: true,
            CapturePointer: false,
            Action: CanvasGesturePressActionKind.NotifyChartPointDoubleClick,
            ChartPoint: expectedHit,
            Shape: null);

        CanvasGesturePressActionDispatcher.Dispatch(chartPlan, _ => { }, _ => true)
            .Should().BeNull();
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
            _ => { callbackCount++; return true; });

        callbackCount.Should().Be(0);
    }
}

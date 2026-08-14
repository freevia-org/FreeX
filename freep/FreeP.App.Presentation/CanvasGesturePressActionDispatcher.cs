using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Applies renderer-neutral press actions to host callbacks.
/// </summary>
public static class CanvasGesturePressActionDispatcher
{
    public static void Dispatch(
        CanvasGesturePressPlan plan,
        Action<ChartPointHit>? notifyChartPointDoubleClick,
        Action<SlideShape> activateOle)
    {
        ArgumentNullException.ThrowIfNull(activateOle);

        switch (plan.Action)
        {
            case CanvasGesturePressActionKind.NotifyChartPointDoubleClick
                when plan.ChartPoint is { } chartPoint:
                notifyChartPointDoubleClick?.Invoke(chartPoint);
                break;
            case CanvasGesturePressActionKind.ActivateOle when plan.Shape is { } shape:
                activateOle(shape);
                break;
        }
    }
}

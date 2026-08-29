using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Applies renderer-neutral press actions to host callbacks.
/// </summary>
public static class CanvasGesturePressActionDispatcher
{
    /// <summary>
    /// Dispatches <paramref name="plan"/> to the matching host callback.
    /// </summary>
    /// <returns>
    /// For <see cref="CanvasGesturePressActionKind.ActivateOle"/>, the success/failure result
    /// returned by <paramref name="activateOle"/> -- callers must observe this (rather than
    /// invoking a void callback whose result an `Action&lt;SlideShape&gt;` conversion would
    /// silently discard) so a failed activation can surface feedback instead of doing nothing.
    /// <see langword="null"/> when no action matched or the matched action has no success/failure
    /// result to report.
    /// </returns>
    public static bool? Dispatch(
        CanvasGesturePressPlan plan,
        Action<ChartPointHit>? notifyChartPointDoubleClick,
        Func<SlideShape, bool> activateOle)
    {
        ArgumentNullException.ThrowIfNull(activateOle);

        switch (plan.Action)
        {
            case CanvasGesturePressActionKind.NotifyChartPointDoubleClick
                when plan.ChartPoint is { } chartPoint:
                notifyChartPointDoubleClick?.Invoke(chartPoint);
                return null;
            case CanvasGesturePressActionKind.ActivateOle when plan.Shape is { } shape:
                return activateOle(shape);
            default:
                return null;
        }
    }
}

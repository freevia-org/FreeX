using System.Windows.Threading;

namespace FreeX.App.Host.Tests;

internal static class DispatcherTestPump
{
    /// <summary>
    /// Runs pending dispatcher work and returns once the window has settled.
    /// </summary>
    /// <remarks>
    /// r444: the sentinel is posted at <c>SystemIdle</c>, the LOWEST priority, not <c>Background</c>.
    /// A Background sentinel drains only Background and above, so anything the window posted at
    /// ContextIdle, ApplicationIdle or SystemIdle survived the pump -- and so did work posted BY
    /// work the pump had just run, which is how a UI actually settles, one stage handing off to the
    /// next. Tests treat this call as "the window has settled" and then assert, so those gaps turned
    /// such assertions into timing races: they passed or failed by machine load rather than by
    /// behaviour. Both gaps are pinned by R444_PumpDispatcherDrainsIdleWorkTests against the
    /// identical sibling in R49MainWindowTestHarness, and both fail against the old sentinel.
    ///
    /// The deadline exists so that a component re-posting work forever fails its test slowly instead
    /// of hanging the whole lane with no output to diagnose.
    /// </remarks>
    public static void PumpDispatcher()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var frame = new DispatcherFrame();

        dispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(() => frame.Continue = false));

        var deadline = new DispatcherTimer(
            TimeSpan.FromSeconds(10),
            DispatcherPriority.Send,
            (_, _) => frame.Continue = false,
            dispatcher);
        deadline.Start();

        try
        {
            Dispatcher.PushFrame(frame);
        }
        finally
        {
            deadline.Stop();
        }
    }
}

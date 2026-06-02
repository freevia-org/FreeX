using System.Windows.Threading;

namespace FreeX.App.Host.Tests;

internal static class DispatcherTestPump
{
    public static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}

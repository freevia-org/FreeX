using System.Windows.Threading;

namespace Freexcel.App.Host.Tests;

internal static class StaTestRunner
{
    private static readonly Lazy<Dispatcher> StaDispatcher = new(CreateDispatcher);

    public static void Run(Action action)
    {
        Exception? exception = null;
        StaDispatcher.Value.Invoke(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        if (exception is not null)
            throw exception;
    }

    private static Dispatcher CreateDispatcher()
    {
        Dispatcher? dispatcher = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        ready.Wait();

        return dispatcher ?? throw new InvalidOperationException("STA dispatcher was not created.");
    }
}

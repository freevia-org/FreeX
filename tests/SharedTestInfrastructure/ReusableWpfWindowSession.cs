using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Threading;

namespace Free.Shared.Testing;

/// <summary>
/// Owns one offscreen WPF window for deterministic UI interaction tests.
///
/// <para>The session deliberately does not replace fresh-window tests. Tests covering startup,
/// file recovery, native dialogs, focus/activation, clipboard ownership, or multiple windows must
/// construct their own window because that lifecycle is their subject. Borrowers instead receive a
/// serial, fully reset shell for state-local command and rendering assertions.</para>
/// </summary>
internal sealed class ReusableWpfWindowSession<TWindow> : IDisposable
    where TWindow : Window
{
    private readonly Func<TWindow> _createWindow;
    private readonly Action<TWindow> _resetWindow;
    private readonly object _runLock = new();
    private readonly Dispatcher _dispatcher;
    private TWindow? _window;
    private bool _disposed;

    public ReusableWpfWindowSession(Func<TWindow> createWindow, Action<TWindow> resetWindow)
    {
        _createWindow = createWindow ?? throw new ArgumentNullException(nameof(createWindow));
        _resetWindow = resetWindow ?? throw new ArgumentNullException(nameof(resetWindow));
        _dispatcher = CreateDispatcher();
    }

    public void Run(Action<TWindow> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        lock (_runLock)
        {
            ThrowIfDisposed();

            Exception? exception = null;
            _dispatcher.Invoke(() =>
            {
                var window = EnsureWindow();
                try
                {
                    _resetWindow(window);
                    action(window);
                }
                catch (Exception ex)
                {
                    exception = ex;
                }
                finally
                {
                    try
                    {
                        _resetWindow(window);
                    }
                    catch (Exception resetException) when (exception is null)
                    {
                        exception = resetException;
                    }
                }
            });

            if (exception is not null)
                ExceptionDispatchInfo.Capture(exception).Throw();
        }
    }

    public void Dispose()
    {
        lock (_runLock)
        {
            if (_disposed)
                return;

            _disposed = true;
            _dispatcher.Invoke(() =>
            {
                if (_window is { IsVisible: true })
                    _window.Close();
                _window = null;
            });
        }
    }

    private TWindow EnsureWindow()
    {
        if (_window is not null)
            return _window;

        _window = _createWindow();
        _window.WindowState = WindowState.Normal;
        _window.Left = -32000;
        _window.Top = -32000;
        _window.ShowInTaskbar = false;
        _window.ShowActivated = false;
        _window.Show();
        _window.UpdateLayout();
        return _window;
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
        })
        {
            IsBackground = true,
            Name = $"{typeof(TWindow).Name}ReusableTestWindowDispatcher"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return dispatcher ?? throw new InvalidOperationException("The reusable WPF test dispatcher was not created.");
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ReusableWpfWindowSession<TWindow>));
    }
}

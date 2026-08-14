namespace FreeP.App.Compositor;

/// <summary>
/// Owns the framework-neutral lifecycle of the native presenter window. The adapter
/// supplies native construction, ownership, close observation, and refresh calls.
/// </summary>
public sealed class SlideShowNativePresenterWindowHost<TWindow>
    where TWindow : class
{
    private readonly Func<SlideShowPresenterViewOperations, TWindow> _create;
    private readonly Action<TWindow, Action> _observeClosed;
    private readonly Action<TWindow> _show;
    private readonly Action<TWindow> _close;
    private readonly Action<TWindow> _refresh;
    private readonly Action _notifyClosed;
    private TWindow? _window;

    public SlideShowNativePresenterWindowHost(
        Func<SlideShowPresenterViewOperations, TWindow> create,
        Action<TWindow, Action> observeClosed,
        Action<TWindow> show,
        Action<TWindow> close,
        Action<TWindow> refresh,
        Action notifyClosed)
    {
        _create = create ?? throw new ArgumentNullException(nameof(create));
        _observeClosed = observeClosed ?? throw new ArgumentNullException(nameof(observeClosed));
        _show = show ?? throw new ArgumentNullException(nameof(show));
        _close = close ?? throw new ArgumentNullException(nameof(close));
        _refresh = refresh ?? throw new ArgumentNullException(nameof(refresh));
        _notifyClosed = notifyClosed ?? throw new ArgumentNullException(nameof(notifyClosed));
    }

    public bool IsOpen => _window is not null;

    public void Open(SlideShowPresenterViewOperations operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        if (_window is not null)
        {
            _refresh(_window);
            return;
        }

        var window = _create(operations);
        _window = window;
        _observeClosed(window, () => HandleClosed(window));
        _show(window);
    }

    public void Close()
    {
        if (_window is { } window)
            _close(window);
    }

    public void Refresh()
    {
        if (_window is { } window)
            _refresh(window);
    }

    private void HandleClosed(TWindow window)
    {
        if (!ReferenceEquals(_window, window))
            return;

        _window = null;
        _notifyClosed();
    }
}

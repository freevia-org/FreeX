namespace Free.Shared.Shell;

/// <summary>
/// Adapts application actions to a Backstage host's dismiss-before-dispatch lifecycle.
/// </summary>
public sealed class BackstageActionBinder
{
    private readonly Action? _beforeDispatch;

    private BackstageActionBinder(Action? beforeDispatch) => _beforeDispatch = beforeDispatch;

    public static BackstageActionBinder Identity { get; } = new(null);

    public static BackstageActionBinder DismissBefore(Action dismiss)
    {
        ArgumentNullException.ThrowIfNull(dismiss);
        return new BackstageActionBinder(dismiss);
    }

    public Action Bind(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return () =>
        {
            _beforeDispatch?.Invoke();
            action();
        };
    }

    public Action<T> Bind<T>(Action<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return value =>
        {
            _beforeDispatch?.Invoke();
            action(value);
        };
    }

    public Action<T1, T2> Bind<T1, T2>(Action<T1, T2> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return (first, second) =>
        {
            _beforeDispatch?.Invoke();
            action(first, second);
        };
    }
}

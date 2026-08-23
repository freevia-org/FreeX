using System.Windows.Threading;

namespace Free.Shared.Shell.Wpf;

/// <summary>
/// Owns the WPF dispatcher timer used to schedule a renderer-local autosave session.
/// </summary>
public sealed class WpfAutosaveTimer
{
    private readonly IWpfDispatcherTimer _timer;

    public WpfAutosaveTimer(TimeSpan interval, Action snapshot)
        : this(new WpfDispatcherTimer(), interval, snapshot)
    {
    }

    internal WpfAutosaveTimer(
        IWpfDispatcherTimer timer,
        TimeSpan interval,
        Action snapshot)
    {
        ArgumentNullException.ThrowIfNull(timer);
        ArgumentNullException.ThrowIfNull(snapshot);

        _timer = timer;
        _timer.Interval = interval;
        _timer.Tick += (_, _) => snapshot();
    }

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();
}

internal interface IWpfDispatcherTimer
{
    TimeSpan Interval { get; set; }

    event EventHandler Tick;

    void Start();

    void Stop();
}

internal sealed class WpfDispatcherTimer : IWpfDispatcherTimer
{
    private readonly DispatcherTimer _timer = new();

    public TimeSpan Interval
    {
        get => _timer.Interval;
        set => _timer.Interval = value;
    }

    public event EventHandler Tick
    {
        add => _timer.Tick += value;
        remove => _timer.Tick -= value;
    }

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();
}

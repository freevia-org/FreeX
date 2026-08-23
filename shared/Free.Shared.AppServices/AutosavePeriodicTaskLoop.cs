namespace Free.Shared.AppServices;

/// <summary>
/// Owns the cancellable fixed-delay loop used by renderer adapters to trigger periodic autosave.
/// </summary>
public sealed class AutosavePeriodicTaskLoop : IDisposable
{
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly object _gate = new();
    private readonly Action _snapshot;
    private CancellationTokenSource? _cancellation;
    private bool _disposed;
    private Task? _runTask;
    private Task? _stopTask;

    public AutosavePeriodicTaskLoop(TimeSpan interval, Action snapshot)
        : this(interval, snapshot, static (delay, cancellationToken) => Task.Delay(delay, cancellationToken))
    {
    }

    internal AutosavePeriodicTaskLoop(
        TimeSpan interval,
        Action snapshot,
        Func<TimeSpan, CancellationToken, Task> delayAsync)
    {
        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval));
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(delayAsync);

        Interval = interval;
        _snapshot = snapshot;
        _delayAsync = delayAsync;
    }

    public TimeSpan Interval { get; }

    /// <summary>Starts the loop once. Repeated calls while it is running are no-ops.</summary>
    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_runTask is not null || _stopTask is not null)
                return;

            _cancellation = new CancellationTokenSource();
            _runTask = RunAsync(_cancellation.Token);
        }
    }

    /// <summary>
    /// Cancels the pending delay and waits until the loop has stopped, guaranteeing that no later
    /// periodic callback can run after this task completes.
    /// </summary>
    public Task StopAsync()
    {
        lock (_gate)
            return BeginStopLocked();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _ = BeginStopLocked();
        }
    }

    private Task BeginStopLocked()
    {
        if (_stopTask is not null)
            return _stopTask;
        if (_runTask is null || _cancellation is null)
            return Task.CompletedTask;

        var cancellation = _cancellation;
        var runTask = _runTask;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopTask = completion.Task;
        _stopTask = stopTask;
        cancellation.Cancel();
        _ = CompleteStopAsync(cancellation, runTask, completion);
        return stopTask;
    }

    private async Task CompleteStopAsync(
        CancellationTokenSource cancellation,
        Task runTask,
        TaskCompletionSource completion)
    {
        Exception? failure = null;
        try
        {
            await runTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_cancellation, cancellation))
                {
                    _cancellation = null;
                    _runTask = null;
                    _stopTask = null;
                }
            }

            cancellation.Dispose();
        }

        if (failure is null)
            completion.TrySetResult();
        else
            completion.TrySetException(failure);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                await _delayAsync(Interval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (cancellationToken.IsCancellationRequested)
                return;

            _snapshot();
        }
    }
}

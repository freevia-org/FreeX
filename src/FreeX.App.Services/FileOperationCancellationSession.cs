namespace FreeX.App.Services;

/// <summary>
/// Owns the cancellation source for at most one live file operation. Beginning a new operation
/// retires the previous source; disposing an operation lease clears it only when it is still current.
/// </summary>
public sealed class FileOperationCancellationSession : IDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _current;
    private bool _disposed;

    public bool IsActive
    {
        get
        {
            lock (_gate)
                return _current is not null;
        }
    }

    public bool CanCancel
    {
        get
        {
            lock (_gate)
                return _current is { IsCancellationRequested: false };
        }
    }

    public FileOperationCancellationLease Begin()
    {
        CancellationTokenSource? previous;
        FileOperationCancellationLease lease;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            previous = _current;
            var current = new CancellationTokenSource();
            _current = current;
            lease = new FileOperationCancellationLease(this, current);
        }

        previous?.Dispose();
        return lease;
    }

    public void CancelCurrent()
    {
        CancellationTokenSource? current;
        lock (_gate)
        {
            if (_disposed)
                return;

            current = _current;
        }

        try
        {
            current?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // A concurrent operation completion already retired the source.
        }
    }

    public void Dispose()
    {
        CancellationTokenSource? current;
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            current = _current;
            _current = null;
        }

        current?.Dispose();
    }

    private void Complete(CancellationTokenSource source)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_current, source))
                _current = null;
        }

        source.Dispose();
    }

    public sealed class FileOperationCancellationLease : IDisposable
    {
        private FileOperationCancellationSession? _owner;
        private CancellationTokenSource? _source;
        private readonly CancellationToken _token;

        internal FileOperationCancellationLease(
            FileOperationCancellationSession owner,
            CancellationTokenSource source)
        {
            _owner = owner;
            _source = source;
            _token = source.Token;
        }

        public CancellationToken Token =>
            _source is not null
                ? _token
                : throw new ObjectDisposedException(nameof(FileOperationCancellationLease));

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            var source = Interlocked.Exchange(ref _source, null);
            if (owner is not null && source is not null)
                owner.Complete(source);
        }
    }
}

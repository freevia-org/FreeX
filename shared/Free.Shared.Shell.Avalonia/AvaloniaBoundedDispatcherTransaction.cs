using Avalonia.Threading;
using System.Runtime.ExceptionServices;

namespace Free.Shared.Shell.Avalonia;

/// <summary>
/// Runs one indivisible snapshot transaction on the Avalonia UI thread without allowing a wedged
/// dispatcher to block a crash handler indefinitely.
/// </summary>
public static class AvaloniaBoundedDispatcherTransaction
{
    public static bool TryExecute(Action snapshotTransaction, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(snapshotTransaction);
        return TryExecute(
            snapshotTransaction,
            timeout,
            Dispatcher.UIThread.CheckAccess,
            callback => Dispatcher.UIThread.Post(callback, DispatcherPriority.Send));
    }

    internal static bool TryExecute(
        Action snapshotTransaction,
        TimeSpan timeout,
        Func<bool> checkAccess,
        Action<Action> post)
    {
        ArgumentNullException.ThrowIfNull(snapshotTransaction);
        ArgumentNullException.ThrowIfNull(checkAccess);
        ArgumentNullException.ThrowIfNull(post);
        if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(nameof(timeout));

        if (checkAccess())
        {
            snapshotTransaction();
            return true;
        }

        var attempt = new DispatchAttempt(snapshotTransaction);
        post(attempt.Run);
        return attempt.Wait(timeout);
    }

    private sealed class DispatchAttempt
    {
        private const int Pending = 0;
        private const int Running = 1;
        private const int Expired = 2;
        private const int Completed = 3;

        private readonly TaskCompletionSource<bool> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Action _transaction;
        private ExceptionDispatchInfo? _failure;
        private int _state;

        public DispatchAttempt(Action transaction) => _transaction = transaction;

        public void Run()
        {
            if (Interlocked.CompareExchange(ref _state, Running, Pending) != Pending)
                return;

            try
            {
                _transaction();
            }
            catch (Exception ex)
            {
                _failure = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                Volatile.Write(ref _state, Completed);
                _completion.TrySetResult(true);
            }
        }

        public bool Wait(TimeSpan timeout)
        {
            if (_completion.Task.Wait(timeout))
            {
                _failure?.Throw();
                return true;
            }

            if (Interlocked.CompareExchange(ref _state, Expired, Pending) == Pending)
            {
                _completion.TrySetResult(false);
                return false;
            }

            if (Volatile.Read(ref _state) == Completed)
            {
                _failure?.Throw();
                return true;
            }

            // The callback began before the deadline but did not finish within it. It may complete
            // safely later; no disposable waiter or caller-owned state is captured by the callback.
            return false;
        }
    }
}

namespace Free.Shared.Shell.Avalonia;

/// <summary>
/// Bridges Avalonia's synchronous Closing event to an asynchronous dirty-document gate.
/// Each close request is cancelled until the gate approves it, then exactly one resumed close
/// is allowed through. Reentrant close requests share the in-flight decision.
/// </summary>
public sealed class SisterAvaloniaAsyncWindowCloseCoordinator
{
    private readonly Func<Task<bool>> _confirmCloseAllowedAsync;
    private readonly Action _requestClose;
    private readonly Action _restoreOwnerFocus;
    private Task? _pendingClose;
    private bool _allowResumedClose;

    public SisterAvaloniaAsyncWindowCloseCoordinator(
        Func<Task<bool>> confirmCloseAllowedAsync,
        Action requestClose,
        Action restoreOwnerFocus)
    {
        ArgumentNullException.ThrowIfNull(confirmCloseAllowedAsync);
        ArgumentNullException.ThrowIfNull(requestClose);
        ArgumentNullException.ThrowIfNull(restoreOwnerFocus);

        _confirmCloseAllowedAsync = confirmCloseAllowedAsync;
        _requestClose = requestClose;
        _restoreOwnerFocus = restoreOwnerFocus;
    }

    public bool IsClosePending => _pendingClose is not null;

    /// <summary>Returns true when the current synchronous Closing event must be cancelled.</summary>
    public bool ShouldCancelClosing()
    {
        if (_allowResumedClose)
        {
            _allowResumedClose = false;
            return false;
        }

        if (_pendingClose is null)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingClose = completion.Task;
            _ = ConfirmAndResumeCloseAsync(completion);
        }

        return true;
    }

    private async Task ConfirmAndResumeCloseAsync(TaskCompletionSource completion)
    {
        // Always leave the current synchronous Closing callback before evaluating or resuming.
        await Task.Yield();

        var allowed = false;
        try
        {
            allowed = await _confirmCloseAllowedAsync();
        }
        catch
        {
            allowed = false;
        }
        finally
        {
            _pendingClose = null;
            completion.TrySetResult();
        }

        if (allowed)
        {
            _allowResumedClose = true;
            try
            {
                _requestClose();
            }
            catch
            {
                _allowResumedClose = false;
                _restoreOwnerFocus();
            }
            return;
        }

        _restoreOwnerFocus();
    }
}

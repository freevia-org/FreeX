namespace FreeW.App.Avalonia;

/// <summary>Observes asynchronous work started by synchronous Avalonia event and command ports.</summary>
internal static class AvaloniaUiTaskGuard
{
    /// <summary>
    /// r283: where a failure goes when the caller supplies no reporter of its own.
    ///
    /// <para>r282 consolidated four dialog funnels onto this guard but left them silent, because a
    /// null <c>onFailure</c> meant the exception was caught and dropped. A dialog has no status bar
    /// to write to, so the shell installs this once and every unreported failure -- from a dialog,
    /// or from any of the call sites that pass no handler -- reaches the user through it.</para>
    ///
    /// <para>Null until the shell sets it, which keeps headless construction and tests working
    /// without a window; an unset reporter degrades to the previous behaviour rather than throwing.</para>
    /// </summary>
    internal static Action<Exception>? FallbackFailureReporter { get; set; }

    internal static void Run(Func<Task> operation, Action<Exception>? onFailure = null) =>
        _ = ObserveAsync(operation, onFailure);

    internal static async Task ObserveAsync(Func<Task> operation, Action<Exception>? onFailure = null)
    {
        ArgumentNullException.ThrowIfNull(operation);

        try
        {
            await operation();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            try
            {
                (onFailure ?? FallbackFailureReporter)?.Invoke(ex);
            }
            catch (Exception reportingFailure) when (reportingFailure is not OutOfMemoryException)
            {
                // Failure reporting runs on the same dispatcher boundary and must not become a
                // second unhandled exception.
            }
        }
    }
}

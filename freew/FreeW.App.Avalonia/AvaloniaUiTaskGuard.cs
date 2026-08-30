namespace FreeW.App.Avalonia;

/// <summary>Observes asynchronous work started by synchronous Avalonia event and command ports.</summary>
internal static class AvaloniaUiTaskGuard
{
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
                onFailure?.Invoke(ex);
            }
            catch (Exception reportingFailure) when (reportingFailure is not OutOfMemoryException)
            {
                // Failure reporting runs on the same dispatcher boundary and must not become a
                // second unhandled exception.
            }
        }
    }
}

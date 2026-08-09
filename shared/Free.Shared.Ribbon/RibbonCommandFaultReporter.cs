namespace Free.Shared.Ribbon;

/// <summary>
/// Seam for reporting an exception thrown by a ribbon/menu command invoked from a UI event handler.
/// Neither toolkit contains such an exception on its own: Avalonia has no dispatcher-level
/// unhandled-exception hook at all, and the WPF hosts' DispatcherUnhandledException handler only
/// records the fault without marking it handled — so in both cases an exception escaping a click
/// handler tears the process down. The renderers catch instead, report here, and leave the shell
/// running; hosts assign <see cref="Handler"/> at startup to route the fault into their own
/// diagnostics store.
/// </summary>
public static class RibbonCommandFaultReporter
{
    /// <summary>Receives (exception, commandId) for each command fault caught by a renderer.</summary>
    public static Action<Exception, string>? Handler { get; set; }

    /// <summary>Reports a command fault, swallowing any fault in the handler itself.</summary>
    public static void Report(Exception exception, string commandId)
    {
        var handler = Handler;
        if (handler is null)
            return;
        try
        {
            handler(exception, commandId);
        }
        catch
        {
            // Diagnostics must never be the thing that crashes the shell.
        }
    }
}

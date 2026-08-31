namespace Free.Shared.Ribbon;

/// <summary>
/// Seam for reporting an exception thrown by a ribbon/menu command invoked from a UI event handler.
/// The shell runners provide a final dispatcher safety net, but catching at the command boundary
/// preserves the command id and prevents partially unwound routed events. Renderers report here
/// and leave the shell running; hosts assign <see cref="Handler"/> at startup to route the fault
/// into their own diagnostics store.
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

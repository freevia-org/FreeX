namespace Free.Shared.AppServices.Printing;

/// <summary>
/// Resolves the concrete queue name used by platform print submission adapters after successful
/// discovery, preserving the discovered queue's canonical casing.
/// </summary>
public static class PrinterSubmissionSelectionPolicy
{
    /// <summary>
    /// Resolves an explicitly requested queue, or the discovery default/first queue when none was
    /// requested. The supplied discovery result must contain at least one printer.
    /// </summary>
    public static string? Resolve(string? requested, PrinterDiscoveryResult discovery)
    {
        if (requested is { Length: > 0 })
        {
            return discovery.Printers.FirstOrDefault(printer =>
                string.Equals(printer.Name, requested, StringComparison.OrdinalIgnoreCase))?.Name;
        }

        return discovery.DefaultPrinter ?? discovery.Printers[0].Name;
    }
}

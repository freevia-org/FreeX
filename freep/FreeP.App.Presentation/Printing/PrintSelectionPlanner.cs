using Free.Shared.AppServices.Printing;

namespace FreeP.App.Compositor.Printing;

public static class PrintSelectionPlanner
{
    public static PrintDialogPlan Build(
        PrinterDiscoveryResult discovery,
        PrintSelection? requested = null)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        requested ??= new PrintSelection();
        requested.Validate();

        var printers = discovery.Printers
            .Where(printer => !string.IsNullOrWhiteSpace(printer.Name))
            .GroupBy(printer => printer.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        if (printers.Length == 0 || discovery.Status == PrinterDiscoveryStatus.NoPrinters)
        {
            return new PrintDialogPlan(
                PrintCapabilityStatus.NoPrinters,
                printers,
                null,
                requested.Copies,
                requested.EffectivePageRange,
                requested.Orientation,
                discovery.Message ?? "No printers are installed or available.");
        }

        var selected = requested.PrinterName is { Length: > 0 } name &&
                       printers.Any(printer => string.Equals(printer.Name, name, StringComparison.OrdinalIgnoreCase))
            ? printers.First(printer => string.Equals(printer.Name, name, StringComparison.OrdinalIgnoreCase)).Name
            : discovery.DefaultPrinter is { Length: > 0 } defaultName &&
              printers.Any(printer => string.Equals(printer.Name, defaultName, StringComparison.OrdinalIgnoreCase))
                ? printers.First(printer => string.Equals(printer.Name, defaultName, StringComparison.OrdinalIgnoreCase)).Name
                : printers[0].Name;

        var status = discovery.Status switch
        {
            PrinterDiscoveryStatus.Available => PrintCapabilityStatus.Ready,
            PrinterDiscoveryStatus.Unavailable => PrintCapabilityStatus.Unavailable,
            _ => PrintCapabilityStatus.Failed,
        };
        return new PrintDialogPlan(
            status,
            printers,
            selected,
            requested.Copies,
            requested.EffectivePageRange,
            requested.Orientation,
            discovery.Message);
    }
}

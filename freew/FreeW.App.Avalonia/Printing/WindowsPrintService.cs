using Free.Shared.AppServices.Printing;
using Free.Shared.AppServices.Windows;

namespace FreeW.App.Avalonia.Printing;

/// <summary>
/// Windows printer discovery and PDF shell handoff for the portable Avalonia host.
/// </summary>
internal sealed class WindowsPrintService : IPlatformPrintService
{
    private readonly IWindowsPrinterCatalog _catalog;
    private readonly IWindowsPdfPrintHandoff _handoff;
    private readonly bool? _isSupportedOverride;

    public WindowsPrintService(
        IWindowsPrinterCatalog? catalog = null,
        IWindowsPdfPrintHandoff? handoff = null,
        bool? isSupportedOverride = null)
    {
        _catalog = catalog ?? new WindowsPrinterCatalog();
        _handoff = handoff ?? new WindowsShellPdfPrintHandoff();
        _isSupportedOverride = isSupportedOverride;
    }

    public bool IsSupported => _isSupportedOverride ?? OperatingSystem.IsWindows();

    public PrintRangeAndOrientationHandling RangeAndOrientationHandling =>
        PrintRangeAndOrientationHandling.PreparedPdf;

    public Task<PrinterDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromResult(CancelledDiscovery());
        if (!IsSupported)
        {
            return Task.FromResult(new PrinterDiscoveryResult(
                PrinterDiscoveryStatus.Unavailable,
                [],
                null,
                "Windows printing is available only on Windows hosts."));
        }

        try
        {
            var snapshot = _catalog.Discover();
            if (snapshot.Status == WindowsPrinterCatalogStatus.Unavailable)
            {
                return Task.FromResult(new PrinterDiscoveryResult(
                    PrinterDiscoveryStatus.Unavailable,
                    [],
                    null,
                    snapshot.FailureReason));
            }
            if (snapshot.Status == WindowsPrinterCatalogStatus.Failed)
            {
                return Task.FromResult(new PrinterDiscoveryResult(
                    PrinterDiscoveryStatus.Failed,
                    [],
                    null,
                    snapshot.FailureReason));
            }

            var names = snapshot.Printers
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (names.Length == 0)
            {
                return Task.FromResult(new PrinterDiscoveryResult(
                    PrinterDiscoveryStatus.NoPrinters,
                    [],
                    null,
                    "No Windows printer queues are installed or available."));
            }

            var defaultPrinter = names.FirstOrDefault(name =>
                string.Equals(name, snapshot.DefaultPrinter, StringComparison.OrdinalIgnoreCase));
            var printers = names
                .Select(name => new PrinterInfo(
                    name,
                    string.Equals(name, defaultPrinter, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            return Task.FromResult(new PrinterDiscoveryResult(
                PrinterDiscoveryStatus.Available,
                printers,
                defaultPrinter));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(CancelledDiscovery());
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Task.FromResult(new PrinterDiscoveryResult(
                PrinterDiscoveryStatus.Failed,
                [],
                null,
                $"Windows printer discovery failed: {ex.Message}"));
        }
    }

    public async Task<PrintSubmissionResult> SubmitAsync(
        string pdfPath,
        PrintSelection selection,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfPath);
        ArgumentNullException.ThrowIfNull(selection);
        selection.Validate();

        if (cancellationToken.IsCancellationRequested)
            return CancelledSubmission(selection.PrinterName);
        if (!IsSupported)
        {
            return new PrintSubmissionResult(
                PrintSubmissionStatus.Unavailable,
                selection.PrinterName,
                Message: "Windows printing is available only on Windows hosts.");
        }
        if (!File.Exists(pdfPath))
        {
            return new PrintSubmissionResult(
                PrintSubmissionStatus.Failed,
                selection.PrinterName,
                Message: $"The generated PDF does not exist: {pdfPath}");
        }
        if (selection.EffectivePageRange.Kind != PrintPageRangeKind.All ||
            selection.Orientation != PrintOrientation.Document)
        {
            return new PrintSubmissionResult(
                PrintSubmissionStatus.Failed,
                selection.PrinterName,
                Message: "The Windows PDF handoff supports all pages in document orientation. Use Create PDF for custom page ranges or orientation.");
        }

        var discovery = await DiscoverAsync(cancellationToken).ConfigureAwait(false);
        if (discovery.Status == PrinterDiscoveryStatus.Cancelled)
            return CancelledSubmission(selection.PrinterName);
        if (discovery.Status == PrinterDiscoveryStatus.NoPrinters)
            return new PrintSubmissionResult(PrintSubmissionStatus.NoPrinters, null, Message: discovery.Message);
        if (!discovery.IsAvailable)
        {
            return new PrintSubmissionResult(
                discovery.Status == PrinterDiscoveryStatus.Unavailable
                    ? PrintSubmissionStatus.Unavailable
                    : PrintSubmissionStatus.Failed,
                null,
                Message: discovery.Message);
        }

        var printer = ResolvePrinter(selection.PrinterName, discovery);
        if (printer is null)
        {
            return new PrintSubmissionResult(
                PrintSubmissionStatus.Failed,
                selection.PrinterName,
                Message: $"The selected printer is not available: {selection.PrinterName}");
        }

        try
        {
            for (var copy = 0; copy < selection.Copies; copy++)
            {
                var result = await _handoff.SubmitAsync(pdfPath, printer, cancellationToken)
                    .ConfigureAwait(false);
                if (result.Status == WindowsShellPdfPrintHandoffStatus.Canceled)
                    return CancelledSubmission(printer);
                if (!result.Started || result.ExitCode is not null and not 0)
                {
                    return new PrintSubmissionResult(
                        PrintSubmissionStatus.Failed,
                        printer,
                        Message: result.FailureReason ?? "Windows could not start the PDF print handoff.");
                }
            }

            return new PrintSubmissionResult(
                PrintSubmissionStatus.Submitted,
                printer,
                $"{selection.Copies} print job{(selection.Copies == 1 ? string.Empty : "s")} handed to Windows.");
        }
        catch (OperationCanceledException)
        {
            return CancelledSubmission(printer);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new PrintSubmissionResult(
                PrintSubmissionStatus.Failed,
                printer,
                Message: $"Windows PDF print handoff failed: {ex.Message}");
        }
    }

    private static string? ResolvePrinter(string? requested, PrinterDiscoveryResult discovery)
    {
        if (requested is { Length: > 0 })
        {
            return discovery.Printers.FirstOrDefault(printer =>
                string.Equals(printer.Name, requested, StringComparison.OrdinalIgnoreCase))?.Name;
        }

        return discovery.DefaultPrinter ?? discovery.Printers[0].Name;
    }

    private static PrinterDiscoveryResult CancelledDiscovery() =>
        new(PrinterDiscoveryStatus.Cancelled, [], null, "Printer discovery was cancelled.");

    private static PrintSubmissionResult CancelledSubmission(string? printerName) =>
        new(PrintSubmissionStatus.Cancelled, printerName, Message: "Print submission was cancelled.");
}

internal static class PlatformPrintServiceFactory
{
    public static IPlatformPrintService Create() =>
        OperatingSystem.IsWindows()
            ? new WindowsPrintService()
            : new CupsPrintService();
}

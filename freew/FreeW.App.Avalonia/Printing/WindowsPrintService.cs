using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Free.Shared.AppServices.Printing;

namespace FreeW.App.Avalonia.Printing;

internal sealed record WindowsPrinterSnapshot(
    IReadOnlyList<string> Printers,
    string? DefaultPrinter);

internal interface IWindowsPrinterCatalog
{
    WindowsPrinterSnapshot Discover();
}

internal sealed record WindowsPrintHandoffResult(
    bool Started,
    int? ExitCode = null,
    string? Message = null);

internal interface IWindowsPdfPrintHandoff
{
    Task<WindowsPrintHandoffResult> SubmitAsync(
        string pdfPath,
        string printerName,
        CancellationToken cancellationToken);
}

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
                if (!result.Started || result.ExitCode is not null and not 0)
                {
                    return new PrintSubmissionResult(
                        PrintSubmissionStatus.Failed,
                        printer,
                        Message: result.Message ?? "Windows could not start the PDF print handoff.");
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

internal sealed class WindowsShellPdfPrintHandoff : IWindowsPdfPrintHandoff
{
    public async Task<WindowsPrintHandoffResult> SubmitAsync(
        string pdfPath,
        string printerName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = pdfPath,
                Verb = "printto",
                Arguments = $"\"{printerName.Replace("\"", "\\\"", StringComparison.Ordinal)}\"",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (process is null)
                return new WindowsPrintHandoffResult(false, Message: "Windows did not start the registered PDF print handler.");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                return new WindowsPrintHandoffResult(
                    Started: true,
                    ExitCode: process.ExitCode,
                    Message: process.ExitCode == 0 ? null : $"The PDF print handler exited with code {process.ExitCode}.");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Registered PDF applications may remain open after accepting the shell verb.
                return new WindowsPrintHandoffResult(Started: true);
            }
        }
        catch (Win32Exception ex)
        {
            return new WindowsPrintHandoffResult(
                false,
                ex.NativeErrorCode,
                $"Windows could not start the registered PDF print handler: {ex.Message}");
        }
    }
}

internal sealed class WindowsPrinterCatalog : IWindowsPrinterCatalog
{
    public WindowsPrinterSnapshot Discover() =>
        new(GetPrinters(), TryGetDefaultPrinter());

    private static IReadOnlyList<string> GetPrinters()
    {
        const uint printerEnumLocal = 0x00000002;
        const uint printerEnumConnections = 0x00000004;
        const int errorInsufficientBuffer = 122;
        var flags = printerEnumLocal | printerEnumConnections;
        if (EnumPrinters(flags, null, 4, IntPtr.Zero, 0, out var bytesNeeded, out _) || bytesNeeded == 0)
            return [];
        if (Marshal.GetLastWin32Error() != errorInsufficientBuffer)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        var buffer = Marshal.AllocHGlobal(checked((int)bytesNeeded));
        try
        {
            if (!EnumPrinters(flags, null, 4, buffer, bytesNeeded, out _, out var count))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var entrySize = Marshal.SizeOf<PrinterInfo4>();
            var printers = new List<string>((int)count);
            for (var index = 0; index < count; index++)
            {
                var info = Marshal.PtrToStructure<PrinterInfo4>(buffer + index * entrySize);
                var name = Marshal.PtrToStringUni(info.PrinterName);
                if (!string.IsNullOrWhiteSpace(name))
                    printers.Add(name.Trim());
            }

            return printers;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string? TryGetDefaultPrinter()
    {
        var length = 0;
        GetDefaultPrinter(null, ref length);
        if (length <= 1)
            return null;

        var buffer = new StringBuilder(length);
        return GetDefaultPrinter(buffer, ref length) && buffer.Length > 0
            ? buffer.ToString()
            : null;
    }

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetDefaultPrinter(StringBuilder? printerName, ref int bufferLength);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumPrinters(
        uint flags,
        string? name,
        uint level,
        IntPtr printerEnum,
        uint cbBuf,
        out uint pcbNeeded,
        out uint pcReturned);

    [StructLayout(LayoutKind.Sequential)]
    private struct PrinterInfo4
    {
        public IntPtr PrinterName;
        public IntPtr ServerName;
        public uint Attributes;
    }
}

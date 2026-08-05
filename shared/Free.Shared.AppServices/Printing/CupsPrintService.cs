using System.Globalization;
using System.Text.RegularExpressions;

namespace Free.Shared.AppServices.Printing;

/// <summary>
/// Builds CUPS process invocations without launching processes.
/// </summary>
public static class CupsPrintCommandPlanner
{
    public static ProcessInvocation ListPrinters() =>
        new("lpstat", ["-p"]);

    public static ProcessInvocation ReadDefaultPrinter() =>
        new("lpstat", ["-d"]);

    public static ProcessInvocation Submit(
        string pdfPath,
        PrintSelection selection,
        string printerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(printerName);
        ArgumentNullException.ThrowIfNull(selection);
        selection.Validate();

        var arguments = new List<string>
        {
            "-d", printerName,
            "-n", selection.Copies.ToString(CultureInfo.InvariantCulture),
            "-o", $"collate={(selection.Collate ? "true" : "false")}",
        };
        if (selection.EffectivePageRange.ToCupsPageList() is { } pageList)
            arguments.AddRange(["-P", pageList]);
        if (selection.Orientation is PrintOrientation.Portrait or PrintOrientation.Landscape)
        {
            var requested = selection.Orientation == PrintOrientation.Portrait ? "3" : "4";
            arguments.AddRange(["-o", $"orientation-requested={requested}"]);
        }

        arguments.Add(pdfPath);
        return new ProcessInvocation("lp", arguments);
    }
}

/// <summary>
/// Portable Linux/macOS printer adapter backed by the CUPS-compatible <c>lpstat</c> and <c>lp</c>
/// commands. Process execution is injected so discovery and submission stay deterministic in tests.
/// </summary>
public sealed class CupsPrintService : IPlatformPrintService
{
    private static readonly Regex PrinterLine = new(
        "^printer\\s+(?<name>\\S+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly IProcessRunner _processRunner;
    private readonly bool? _isSupportedOverride;

    public CupsPrintService(IProcessRunner? processRunner = null, bool? isSupportedOverride = null)
    {
        _processRunner = processRunner ?? new SystemProcessRunner();
        _isSupportedOverride = isSupportedOverride;
    }

    public bool IsSupported =>
        _isSupportedOverride ?? (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS());

    public async Task<PrinterDiscoveryResult> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return new(PrinterDiscoveryStatus.Cancelled, [], null, "Printer discovery was cancelled.");
        if (!IsSupported)
        {
            return new(
                PrinterDiscoveryStatus.Unavailable,
                [],
                null,
                "CUPS printing is available only on Linux and macOS hosts.");
        }

        try
        {
            var printersResult = await _processRunner.RunAsync(
                CupsPrintCommandPlanner.ListPrinters(), cancellationToken).ConfigureAwait(false);
            if (!printersResult.Succeeded)
            {
                return new(
                    PrinterDiscoveryStatus.Unavailable,
                    [],
                    null,
                    FormatProcessFailure("lpstat -p", printersResult));
            }

            var names = printersResult.StandardOutput
                .Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => PrinterLine.Match(line))
                .Where(match => match.Success)
                .Select(match => match.Groups["name"].Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (names.Length == 0)
            {
                return new(
                    PrinterDiscoveryStatus.NoPrinters,
                    [],
                    null,
                    "No printers are installed or available.");
            }

            var defaultResult = await _processRunner.RunAsync(
                CupsPrintCommandPlanner.ReadDefaultPrinter(), cancellationToken).ConfigureAwait(false);
            var defaultName = defaultResult.Succeeded
                ? ParseDefaultPrinter(defaultResult.StandardOutput)
                : null;
            var printers = names
                .Select(name => new PrinterInfo(
                    name,
                    string.Equals(name, defaultName, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            return new(
                PrinterDiscoveryStatus.Available,
                printers,
                defaultName,
                defaultResult.Succeeded ? null : FormatProcessFailure("lpstat -d", defaultResult));
        }
        catch (OperationCanceledException)
        {
            return new(PrinterDiscoveryStatus.Cancelled, [], null, "Printer discovery was cancelled.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new(PrinterDiscoveryStatus.Failed, [], null, $"Printer discovery failed: {ex.Message}");
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
        {
            return new(
                PrintSubmissionStatus.Cancelled,
                selection.PrinterName,
                Message: "Print submission was cancelled.");
        }
        if (!IsSupported)
        {
            return new(
                PrintSubmissionStatus.Unavailable,
                selection.PrinterName,
                Message: "CUPS printing is available only on Linux and macOS hosts.");
        }
        if (!File.Exists(pdfPath))
        {
            return new(
                PrintSubmissionStatus.Failed,
                selection.PrinterName,
                Message: $"The generated PDF does not exist: {pdfPath}");
        }

        var discovery = await DiscoverAsync(cancellationToken).ConfigureAwait(false);
        if (discovery.Status == PrinterDiscoveryStatus.Cancelled)
            return new(PrintSubmissionStatus.Cancelled, null, Message: discovery.Message);
        if (discovery.Status == PrinterDiscoveryStatus.NoPrinters)
            return new(PrintSubmissionStatus.NoPrinters, null, Message: discovery.Message);
        if (!discovery.IsAvailable)
        {
            var status = discovery.Status == PrinterDiscoveryStatus.Unavailable
                ? PrintSubmissionStatus.Unavailable
                : PrintSubmissionStatus.Failed;
            return new(status, null, Message: discovery.Message);
        }

        var printer = ResolvePrinter(selection.PrinterName, discovery);
        if (printer is null)
        {
            return new(
                PrintSubmissionStatus.Failed,
                selection.PrinterName,
                Message: $"The selected printer is not available: {selection.PrinterName}");
        }

        try
        {
            var result = await _processRunner.RunAsync(
                CupsPrintCommandPlanner.Submit(
                    pdfPath,
                    selection with { PrinterName = printer },
                    printer),
                cancellationToken).ConfigureAwait(false);
            return result.Succeeded
                ? new PrintSubmissionResult(
                    PrintSubmissionStatus.Submitted,
                    printer,
                    result.StandardOutput.Trim())
                : new PrintSubmissionResult(
                    PrintSubmissionStatus.Failed,
                    printer,
                    Message: FormatProcessFailure("lp", result));
        }
        catch (OperationCanceledException)
        {
            return new(
                PrintSubmissionStatus.Cancelled,
                printer,
                Message: "Print submission was cancelled.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new(
                PrintSubmissionStatus.Failed,
                printer,
                Message: $"Print submission failed: {ex.Message}");
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

    private static string? ParseDefaultPrinter(string output)
    {
        var line = output.Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (line is null || line.Contains("no system default", StringComparison.OrdinalIgnoreCase))
            return null;

        var colon = line.IndexOf(':');
        return colon >= 0 && colon + 1 < line.Length ? line[(colon + 1)..].Trim() : null;
    }

    private static string FormatProcessFailure(string command, ProcessResult result)
    {
        var detail = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput.Trim()
            : result.StandardError.Trim();
        return string.IsNullOrWhiteSpace(detail)
            ? $"{command} exited with code {result.ExitCode}."
            : $"{command} exited with code {result.ExitCode}: {detail}";
    }
}

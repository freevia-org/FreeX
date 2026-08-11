using System.Globalization;

namespace Free.Shared.AppServices.Printing;

/// <summary>
/// Builds CUPS process invocations without launching processes.
/// </summary>
public static class CupsPrintCommandPlanner
{
    public static ProcessInvocation ListPrinters(
        CupsPrinterDiscoveryMode mode = CupsPrinterDiscoveryMode.PrinterStatus) =>
        mode switch
        {
            CupsPrinterDiscoveryMode.DestinationNames => new("lpstat", ["-e"]),
            _ => new("lpstat", ["-p"]),
        };

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

        var arguments = new List<string> { "-d", printerName };
        if (selection.Copies > 1)
            arguments.AddRange(["-n", selection.Copies.ToString(CultureInfo.InvariantCulture)]);
        if (selection.EffectivePageRange.ToCupsPageList() is { } pageList)
            arguments.AddRange(["-P", pageList]);
        arguments.AddRange(["-o", $"collate={(selection.Collate ? "true" : "false")}"]);
        if (selection.Orientation is PrintOrientation.Portrait or PrintOrientation.Landscape)
        {
            var requested = selection.Orientation == PrintOrientation.Portrait ? "3" : "4";
            arguments.AddRange(["-o", $"orientation-requested={requested}"]);
        }
        if (!string.IsNullOrWhiteSpace(selection.JobTitle))
            arguments.AddRange(["-t", selection.JobTitle.Trim()]);

        arguments.Add(pdfPath);
        return new ProcessInvocation("lp", arguments);
    }
}

public enum CupsPrinterDiscoveryMode
{
    PrinterStatus,
    DestinationNames,
}

/// <summary>
/// Portable Linux/macOS printer adapter backed by the CUPS-compatible <c>lpstat</c> and <c>lp</c>
/// commands. Process execution is injected so discovery and submission stay deterministic in tests.
/// </summary>
public sealed class CupsPrintService : IPlatformPrintService
{
    private static readonly TimeSpan DefaultCommandTimeout = TimeSpan.FromSeconds(10);

    private readonly IProcessRunner _processRunner;
    private readonly bool? _isSupportedOverride;
    private readonly TimeSpan _commandTimeout;
    private readonly CupsPrinterDiscoveryMode _discoveryMode;

    public CupsPrintService(
        IProcessRunner? processRunner = null,
        bool? isSupportedOverride = null,
        TimeSpan? commandTimeout = null,
        CupsPrinterDiscoveryMode discoveryMode = CupsPrinterDiscoveryMode.PrinterStatus)
    {
        _processRunner = processRunner ?? new SystemProcessRunner();
        _isSupportedOverride = isSupportedOverride;
        _commandTimeout = commandTimeout ?? DefaultCommandTimeout;
        if (_commandTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(commandTimeout), "The CUPS command timeout must be positive.");
        _discoveryMode = discoveryMode;
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
            var listPrinters = CupsPrintCommandPlanner.ListPrinters(_discoveryMode);
            var printersResult = await RunAsync(listPrinters, cancellationToken).ConfigureAwait(false);
            if (!printersResult.Succeeded)
            {
                return new(
                    PrinterDiscoveryStatus.Unavailable,
                    [],
                    null,
                    FormatProcessFailure($"lpstat {listPrinters.Arguments[0]}", printersResult));
            }

            var names = ParsePrinterNames(printersResult.StandardOutput, _discoveryMode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var defaultResult = await RunAsync(
                CupsPrintCommandPlanner.ReadDefaultPrinter(), cancellationToken).ConfigureAwait(false);
            var defaultName = defaultResult.Succeeded
                ? ParseDefaultPrinter(defaultResult.StandardOutput)
                : null;
            if (defaultName is not null &&
                !names.Contains(defaultName, StringComparer.OrdinalIgnoreCase))
            {
                names.Add(defaultName);
            }
            if (names.Count == 0)
            {
                return new(
                    PrinterDiscoveryStatus.NoPrinters,
                    [],
                    null,
                    "No printers are installed or available.");
            }

            var printers = names
                .Select(name => new PrinterInfo(
                    name,
                    string.Equals(name, defaultName, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(printer => printer.IsDefault)
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
        catch (TimeoutException)
        {
            return new(PrinterDiscoveryStatus.Unavailable, [], null, "CUPS printer discovery timed out.");
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
            var result = await RunAsync(
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
        catch (TimeoutException)
        {
            return new(
                PrintSubmissionStatus.Failed,
                printer,
                Message: "Printing failed: the CUPS command timed out.");
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

    private static IEnumerable<string> ParsePrinterNames(
        string output,
        CupsPrinterDiscoveryMode mode)
    {
        foreach (var rawLine in output.Split(
                     ["\r\n", "\n", "\r"],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;
            if (mode == CupsPrinterDiscoveryMode.DestinationNames)
            {
                yield return line;
                continue;
            }

            const string prefix = "printer ";
            if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;
            var nameEnd = line.IndexOf(' ', prefix.Length);
            var name = nameEnd < 0 ? line[prefix.Length..] : line[prefix.Length..nameEnd];
            if (name.Length > 0)
                yield return name;
        }
    }

    private async Task<ProcessResult> RunAsync(
        ProcessInvocation invocation,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_commandTimeout);
        try
        {
            return await _processRunner.RunAsync(invocation, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"{invocation.FileName} did not exit within {_commandTimeout.TotalSeconds:n0} seconds.");
        }
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

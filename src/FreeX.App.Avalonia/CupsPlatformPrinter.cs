using System.IO;
using Free.Shared.AppServices.Printing;
using FreeX.App.Services;

namespace FreeX.App.Avalonia;

/// <summary>
/// Linux/CUPS binding for <see cref="IPlatformPrinter"/>. It only launches the standard CUPS utilities
/// (<c>lpstat</c> to enumerate destinations and the default, <c>lp</c> to spool a job) — every decision
/// about which flags those get, and how their output is parsed, lives in the shared, unit-tested
/// <see cref="CupsPrintCommandPlanner"/>. This file is the platform glue the constraint allows in the
/// shell layer; the actual print contract stays portable so macOS (which also ships CUPS) and a future
/// Windows binding can plug into the same seam.
///
/// Submission writes the print-ready PDF to a temp file and hands its path to <c>lp</c>; the spooler copies
/// the bytes, after which the temp file is removed.
/// </summary>
internal sealed class CupsPlatformPrinter : IPlatformPrinter
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(10);
    private readonly IProcessRunner _processRunner;
    private readonly TimeSpan _commandTimeout;
    private readonly bool? _canPrintOverride;

    public CupsPlatformPrinter(
        IProcessRunner? processRunner = null,
        TimeSpan? commandTimeout = null,
        bool? canPrintOverride = null)
    {
        _processRunner = processRunner ?? new SystemProcessRunner();
        _commandTimeout = commandTimeout ?? CommandTimeout;
        _canPrintOverride = canPrintOverride;
    }

    public bool CanPrint => _canPrintOverride ?? (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS());

    public async Task<IReadOnlyList<PrinterDescriptor>> GetPrintersAsync(CancellationToken cancellationToken = default)
    {
        if (!CanPrint)
            return [];

        try
        {
            var listResult = await RunAsync(
                CupsPrintCommandPlanner.StatusProgram,
                CupsPrintCommandPlanner.BuildListPrintersArguments(),
                cancellationToken).ConfigureAwait(false);

            var defaultResult = await RunAsync(
                CupsPrintCommandPlanner.StatusProgram,
                CupsPrintCommandPlanner.BuildDefaultPrinterArguments(),
                cancellationToken).ConfigureAwait(false);

            var defaultId = CupsPrintCommandPlanner.ParseDefaultPrinter(defaultResult.StandardOutput);
            return CupsPrintCommandPlanner.ParsePrinters(listResult.StandardOutput, defaultId);
        }
        catch (Exception ex) when (IsToolingUnavailable(ex))
        {
            return [];
        }
        catch (TimeoutException)
        {
            return [];
        }
    }

    public async Task<PrintSubmissionResult> SubmitAsync(
        PrintJobSubmission submission,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);

        if (!CanPrint)
            return PrintSubmissionResult.Failure("Printing requires a CUPS spooler, which is not available on this host.");

        var tempPath = Path.Combine(Path.GetTempPath(), $"freex-print-{Guid.NewGuid():N}.pdf");
        try
        {
            await File.WriteAllBytesAsync(tempPath, submission.DocumentBytes, cancellationToken).ConfigureAwait(false);

            var arguments = CupsPrintCommandPlanner.BuildSubmitArguments(submission, tempPath);
            var result = await RunAsync(CupsPrintCommandPlanner.SubmitProgram, arguments, cancellationToken)
                .ConfigureAwait(false);

            if (result.ExitCode == 0)
            {
                var target = string.IsNullOrWhiteSpace(submission.PrinterId)
                    ? "the default printer"
                    : submission.PrinterId;
                return PrintSubmissionResult.Success($"Sent to {target}.");
            }

            var detail = string.IsNullOrWhiteSpace(result.StandardError)
                ? $"the spooler returned exit code {result.ExitCode}"
                : result.StandardError.Trim();
            return PrintSubmissionResult.Failure($"Printing failed: {detail}");
        }
        catch (Exception ex) when (IsToolingUnavailable(ex))
        {
            return PrintSubmissionResult.Failure("Printing failed: the CUPS 'lp' utility is not installed on this host.");
        }
        catch (TimeoutException)
        {
            return PrintSubmissionResult.Failure("Printing failed: the CUPS command timed out.");
        }
        catch (Exception ex)
        {
            return PrintSubmissionResult.Failure($"Printing failed: {ex.Message}");
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    private async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_commandTimeout);
        try
        {
            return await _processRunner.RunAsync(
                new ProcessInvocation(fileName, arguments),
                timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"{fileName} did not exit within {_commandTimeout.TotalSeconds:n0} seconds.");
        }
    }

    private static bool IsToolingUnavailable(Exception ex) =>
        ex is System.ComponentModel.Win32Exception or FileNotFoundException or PlatformNotSupportedException;

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup; the OS temp sweeper reclaims it otherwise.
        }
    }
}

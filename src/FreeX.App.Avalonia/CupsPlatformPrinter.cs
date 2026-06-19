using System.Diagnostics;
using System.IO;
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

    public bool CanPrint => OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();

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

    private static async Task<ProcessRunResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CommandTimeout);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException($"{fileName} did not exit within {CommandTimeout.TotalSeconds:n0} seconds.");
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        return new ProcessRunResult(process.ExitCode, stdout, stderr);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best-effort cleanup; the timeout result is already reported to the caller.
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

    private readonly record struct ProcessRunResult(int ExitCode, string StandardOutput, string StandardError);
}

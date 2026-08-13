using System.ComponentModel;
using System.Diagnostics;

namespace Free.Shared.AppServices.Windows;

public enum WindowsShellPdfPrintHandoffStatus
{
    Accepted,
    HandlerExited,
    Canceled,
    Unavailable,
    Failed,
}

public sealed record WindowsShellPdfPrintHandoffResult(
    WindowsShellPdfPrintHandoffStatus Status,
    int? ExitCode = null,
    int? NativeErrorCode = null,
    string? FailureReason = null)
{
    public bool Started => Status is
        WindowsShellPdfPrintHandoffStatus.Accepted or
        WindowsShellPdfPrintHandoffStatus.HandlerExited;

    public static WindowsShellPdfPrintHandoffResult Accepted() =>
        new(WindowsShellPdfPrintHandoffStatus.Accepted);

    public static WindowsShellPdfPrintHandoffResult HandlerExited(int exitCode) =>
        new(
            WindowsShellPdfPrintHandoffStatus.HandlerExited,
            exitCode,
            FailureReason: exitCode == 0
                ? null
                : $"The PDF print handler exited with code {exitCode}.");

    public static WindowsShellPdfPrintHandoffResult Canceled() =>
        new(WindowsShellPdfPrintHandoffStatus.Canceled);

    public static WindowsShellPdfPrintHandoffResult Unavailable(string reason) =>
        new(
            WindowsShellPdfPrintHandoffStatus.Unavailable,
            FailureReason: Normalize(reason, "Windows PDF print handoff is unavailable."));

    public static WindowsShellPdfPrintHandoffResult Failed(
        string reason,
        int? nativeErrorCode = null) =>
        new(
            WindowsShellPdfPrintHandoffStatus.Failed,
            NativeErrorCode: nativeErrorCode,
            FailureReason: Normalize(reason, "Windows PDF print handoff failed."));

    private static string Normalize(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}

public interface IWindowsPdfPrintHandoff
{
    Task<WindowsShellPdfPrintHandoffResult> SubmitAsync(
        string pdfPath,
        string printerName,
        CancellationToken cancellationToken = default);
}

internal interface IWindowsShellProcess : IDisposable
{
    int ExitCode { get; }

    Task WaitForExitAsync(CancellationToken cancellationToken);
}

internal interface IWindowsShellProcessStarter
{
    IWindowsShellProcess? Start(ProcessStartInfo startInfo);
}

/// <summary>
/// App-neutral Windows shell <c>printto</c> handoff for an already-rendered PDF.
/// </summary>
public sealed class WindowsShellPdfPrintHandoff : IWindowsPdfPrintHandoff
{
    internal static readonly TimeSpan DefaultAcceptanceTimeout = TimeSpan.FromSeconds(8);

    private readonly IWindowsShellProcessStarter _processStarter;
    private readonly TimeSpan _acceptanceTimeout;
    private readonly bool? _isSupportedOverride;

    public WindowsShellPdfPrintHandoff()
        : this(new SystemWindowsShellProcessStarter(), DefaultAcceptanceTimeout, null)
    {
    }

    internal WindowsShellPdfPrintHandoff(
        IWindowsShellProcessStarter processStarter,
        TimeSpan acceptanceTimeout,
        bool? isSupportedOverride = null)
    {
        _processStarter = processStarter ?? throw new ArgumentNullException(nameof(processStarter));
        if (acceptanceTimeout < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(acceptanceTimeout));
        _acceptanceTimeout = acceptanceTimeout;
        _isSupportedOverride = isSupportedOverride;
    }

    public async Task<WindowsShellPdfPrintHandoffResult> SubmitAsync(
        string pdfPath,
        string printerName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(printerName);

        if (cancellationToken.IsCancellationRequested)
            return WindowsShellPdfPrintHandoffResult.Canceled();
        if (!(_isSupportedOverride ?? OperatingSystem.IsWindows()))
        {
            return WindowsShellPdfPrintHandoffResult.Unavailable(
                "Windows PDF print handoff is available only on Windows.");
        }

        try
        {
            using var process = _processStarter.Start(BuildStartInfo(pdfPath, printerName));
            if (process is null)
            {
                return WindowsShellPdfPrintHandoffResult.Failed(
                    "Windows did not start the registered PDF print handler.");
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_acceptanceTimeout);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                return WindowsShellPdfPrintHandoffResult.HandlerExited(process.ExitCode);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Registered PDF applications may remain open after accepting the shell verb.
                return WindowsShellPdfPrintHandoffResult.Accepted();
            }
            catch (OperationCanceledException)
            {
                return WindowsShellPdfPrintHandoffResult.Canceled();
            }
        }
        catch (Win32Exception ex)
        {
            return WindowsShellPdfPrintHandoffResult.Failed(
                $"Windows could not start the registered PDF print handler: {ex.Message}",
                ex.NativeErrorCode);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return WindowsShellPdfPrintHandoffResult.Failed(ex.Message);
        }
    }

    internal static ProcessStartInfo BuildStartInfo(string pdfPath, string printerName) =>
        new()
        {
            FileName = pdfPath,
            Verb = "printto",
            Arguments = Quote(printerName),
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };

    private static string Quote(string value) =>
        $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private sealed class SystemWindowsShellProcessStarter : IWindowsShellProcessStarter
    {
        public IWindowsShellProcess? Start(ProcessStartInfo startInfo) =>
            Process.Start(startInfo) is { } process
                ? new SystemWindowsShellProcess(process)
                : null;
    }

    private sealed class SystemWindowsShellProcess(Process process) : IWindowsShellProcess
    {
        public int ExitCode => process.ExitCode;

        public Task WaitForExitAsync(CancellationToken cancellationToken) =>
            process.WaitForExitAsync(cancellationToken);

        public void Dispose() => process.Dispose();
    }
}

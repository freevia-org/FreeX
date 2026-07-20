using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FreeP.App.Recording;

public sealed record LinuxRecordingProcessStopResult(
    bool Exited,
    bool WasForced,
    int? ExitCode,
    string StandardError)
{
    public bool HasExpectedRecorderExitCode => ExitCode is 0 or 130 or 143;
}

public interface ILinuxRecordingChildProcess : IDisposable
{
    int ProcessId { get; }

    bool HasExited { get; }

    int? ExitCode { get; }

    string StandardError { get; }

    bool WaitForExit(TimeSpan timeout);

    void SendInterrupt();

    void Kill();
}

public interface ILinuxRecordingChildProcessFactory
{
    ILinuxRecordingChildProcess Start(LinuxNarrationCaptureCommand command);
}

public interface ILinuxRecordingProcessAdapter
{
    ILinuxRecordingChildProcess Start(LinuxNarrationCaptureCommand command);

    LinuxRecordingProcessStopResult Stop(
        ILinuxRecordingChildProcess process,
        TimeSpan gracefulTimeout);

    void Cancel(
        ILinuxRecordingChildProcess process,
        TimeSpan gracefulTimeout);
}

public sealed class LinuxRecordingProcessAdapter : ILinuxRecordingProcessAdapter
{
    private static readonly TimeSpan ForcedExitTimeout = TimeSpan.FromSeconds(1);

    private readonly ILinuxRecordingChildProcessFactory _processFactory;

    public LinuxRecordingProcessAdapter()
        : this(new SystemLinuxRecordingChildProcessFactory())
    {
    }

    public LinuxRecordingProcessAdapter(ILinuxRecordingChildProcessFactory processFactory)
    {
        _processFactory = processFactory ?? throw new ArgumentNullException(nameof(processFactory));
    }

    public ILinuxRecordingChildProcess Start(LinuxNarrationCaptureCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return _processFactory.Start(command);
    }

    public LinuxRecordingProcessStopResult Stop(
        ILinuxRecordingChildProcess process,
        TimeSpan gracefulTimeout)
    {
        ArgumentNullException.ThrowIfNull(process);
        ValidateTimeout(gracefulTimeout);

        if (process.HasExited)
            return Snapshot(process, wasForced: false);

        process.SendInterrupt();
        if (process.WaitForExit(gracefulTimeout))
            return Snapshot(process, wasForced: false);

        process.Kill();
        var exited = process.WaitForExit(ForcedExitTimeout);
        return new LinuxRecordingProcessStopResult(
            exited,
            WasForced: true,
            process.ExitCode,
            process.StandardError);
    }

    public void Cancel(
        ILinuxRecordingChildProcess process,
        TimeSpan gracefulTimeout)
    {
        ArgumentNullException.ThrowIfNull(process);
        ValidateTimeout(gracefulTimeout);

        if (process.HasExited)
            return;

        process.SendInterrupt();
        if (!process.WaitForExit(gracefulTimeout))
        {
            process.Kill();
            _ = process.WaitForExit(ForcedExitTimeout);
        }
    }

    private static LinuxRecordingProcessStopResult Snapshot(
        ILinuxRecordingChildProcess process,
        bool wasForced) =>
        new(
            Exited: process.HasExited,
            wasForced,
            process.ExitCode,
            process.StandardError);

    private static void ValidateTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));
    }
}

public sealed class SystemLinuxRecordingChildProcessFactory : ILinuxRecordingChildProcessFactory
{
    public ILinuxRecordingChildProcess Start(LinuxNarrationCaptureCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = command.FileName,
                UseShellExecute = false,
                RedirectStandardOutput = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };
        foreach (var argument in command.Arguments)
            process.StartInfo.ArgumentList.Add(argument);

        try
        {
            if (!process.Start())
                throw new InvalidOperationException($"Could not start Linux recorder '{command.FileName}'.");
            return new SystemLinuxRecordingChildProcess(process);
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }
}

public sealed class SystemLinuxRecordingChildProcess : ILinuxRecordingChildProcess
{
    private const int SigInt = 2;

    private readonly Process _process;
    private readonly Task<string> _standardErrorTask;
    private bool _disposed;

    public SystemLinuxRecordingChildProcess(Process process)
    {
        _process = process ?? throw new ArgumentNullException(nameof(process));
        if (!process.StartInfo.RedirectStandardError)
            throw new ArgumentException("The recorder process must redirect standard error.", nameof(process));
        _standardErrorTask = process.StandardError.ReadToEndAsync();
    }

    public int ProcessId => _process.Id;

    public bool HasExited => SafeHasExited();

    public int? ExitCode => SafeHasExited() ? _process.ExitCode : null;

    public string StandardError =>
        SafeHasExited() && _standardErrorTask.IsCompletedSuccessfully
            ? _standardErrorTask.Result.Trim()
            : string.Empty;

    public bool WaitForExit(TimeSpan timeout)
    {
        ThrowIfDisposed();
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));

        var milliseconds = checked((int)Math.Min(int.MaxValue, Math.Ceiling(timeout.TotalMilliseconds)));
        return _process.WaitForExit(milliseconds);
    }

    public void SendInterrupt()
    {
        ThrowIfDisposed();
        if (SafeHasExited())
            return;
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("POSIX recorder interruption is only available on Linux.");

        if (kill(_process.Id, SigInt) != 0)
            throw new InvalidOperationException($"Could not send SIGINT to recorder process {_process.Id} (errno {Marshal.GetLastPInvokeError()}).");
    }

    public void Kill()
    {
        ThrowIfDisposed();
        if (!SafeHasExited())
            _process.Kill(entireProcessTree: true);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            if (!SafeHasExited())
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(1000);
            }
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            _process.Dispose();
            _disposed = true;
        }
    }

    private bool SafeHasExited()
    {
        if (_disposed)
            return true;

        try
        {
            return _process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);

    [DllImport("libc", SetLastError = true)]
    private static extern int kill(int processId, int signal);
}

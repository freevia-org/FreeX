using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

/// <summary>
/// Local, dependency-free speech adapter for the Avalonia host. It uses an installed OS speech command
/// when one is available and otherwise completes each segment asynchronously as a deterministic no-op.
/// Speech backends are paused by suspending or signalling the exact owned child process.
/// Backends without that capability remain honest and report it as unsupported.
/// </summary>
public sealed class AvaloniaSpeechEngine : ISpeechEngine, IDisposable
{
    private readonly object _gate = new();
    private readonly Func<string, SpeechBackend?> _backendFactory;
    private readonly Action<Action> _post;
    private readonly ISpeechProcessRunner _processRunner;
    private ISpeechProcess? _process;
    private CancellationTokenSource? _completionCancellation;
    private Action? _pendingCompletion;
    private long _generation;
    private bool _paused;
    private bool _disposed;

    public AvaloniaSpeechEngine()
        : this(CreateBackend, action => Dispatcher.UIThread.Post(action), new ProcessSpeechRunner())
    {
    }

    internal AvaloniaSpeechEngine(
        Func<string, SpeechBackend?> backendFactory,
        Action<Action>? post = null,
        ISpeechProcessRunner? processRunner = null)
    {
        _backendFactory = backendFactory ?? throw new ArgumentNullException(nameof(backendFactory));
        _post = post ?? (action => Dispatcher.UIThread.Post(action));
        _processRunner = processRunner ?? new ProcessSpeechRunner();
    }

    /// <summary>Whether an installed speech command was found during the last backend probe.</summary>
    public bool IsBackendAvailable => TryCreateBackend(string.Empty) is not null;

    /// <summary>Whether the selected platform backend supports process-level pause/resume.</summary>
    public bool SupportsPause => TryCreateBackend(string.Empty)?.SupportsPause == true;

    internal int? OwnedProcessIdForSmoke
    {
        get
        {
            lock (_gate)
                return _process?.ProcessId;
        }
    }

    public void SpeakAsync(string text, Action onCompleted)
    {
        ArgumentNullException.ThrowIfNull(onCompleted);

        Stop();

        SpeechBackend? backend;
        long generation;
        CancellationToken cancellationToken;
        lock (_gate)
        {
            if (_disposed)
                return;

            generation = ++_generation;
            _paused = false;
            _pendingCompletion = onCompleted;
            _completionCancellation = new CancellationTokenSource();
            cancellationToken = _completionCancellation.Token;
        }

        try
        {
            backend = TryCreateBackend(text);
        }
        catch (Exception)
        {
            backend = null;
        }

        if (backend is null)
        {
            // Post rather than invoke inline so a long document cannot recurse through every segment and
            // so callers observe Playing until the UI has processed the completion.
            PostCompletion(generation, cancellationToken);
            return;
        }

        try
        {
            var process = _processRunner.Start(
                backend,
                text,
                () => PostCompletion(generation, cancellationToken));

            lock (_gate)
            {
                if (_disposed || generation != _generation || cancellationToken.IsCancellationRequested
                    || _pendingCompletion is null)
                {
                    StopAndDisposeProcess(process);
                    return;
                }

                _process = process;
            }
        }
        catch (Exception)
        {
            PostCompletion(generation, cancellationToken);
        }
    }

    public bool TryPause()
    {
        lock (_gate)
        {
            if (_disposed || _process is null || _pendingCompletion is null || !_process.SupportsPause)
                return false;

            if (!_process.TryPause())
                return false;

            _paused = true;
            return true;
        }
    }

    public bool TryResume()
    {
        lock (_gate)
        {
            if (_disposed || _process is null || _pendingCompletion is null || !_process.SupportsPause)
                return false;

            if (!_process.TryResume())
                return false;

            _paused = false;
            return true;
        }
    }

    public void Pause() => _ = TryPause();

    public void Resume() => _ = TryResume();

    public void Stop()
    {
        ISpeechProcess? process;
        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            ++_generation;
            _paused = false;
            _pendingCompletion = null;
            cancellation = _completionCancellation;
            _completionCancellation = null;
            process = _process;
            _process = null;
        }

        cancellation?.Cancel();
        cancellation?.Dispose();
        if (process is null)
            return;

        StopAndDisposeProcess(process);
    }

    private static void StopAndDisposeProcess(ISpeechProcess process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill();
        }
        catch (Exception)
        {
            // Best-effort cancellation. The generation guard still suppresses a late Exited callback.
        }
        finally
        {
            try
            {
                process.Dispose();
            }
            catch (Exception)
            {
                // Cleanup is best-effort and must not make Stop/Dispose crash the host.
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
        }

        Stop();
    }

    private void PostCompletion(long generation, CancellationToken cancellationToken)
    {
        try
        {
            _post(() => CompleteIfCurrent(generation, cancellationToken));
        }
        catch (Exception)
        {
            // A host dispatcher can be unavailable during shutdown. Complete on the callback thread so a
            // missing UI loop cannot leave the controller active forever.
            CompleteIfCurrent(generation, cancellationToken);
        }
    }

    private void CompleteIfCurrent(long generation, CancellationToken cancellationToken)
    {
        Action? callback;
        CancellationTokenSource? completionCancellation;
        ISpeechProcess? process;
        lock (_gate)
        {
            if (_disposed || _paused || generation != _generation || cancellationToken.IsCancellationRequested
                || _pendingCompletion is null)
                return;

            callback = _pendingCompletion;
            _pendingCompletion = null;
            completionCancellation = _completionCancellation;
            _completionCancellation = null;
            process = _process;
            _process = null;
        }

        completionCancellation?.Dispose();
        process?.Dispose();
        callback();
    }

    private SpeechBackend? TryCreateBackend(string text)
    {
        try
        {
            return _backendFactory(text);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static SpeechBackend? CreateBackend(string text)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var say = FindExecutable("say");
            return say is null ? null : new SpeechBackend(say, [text], SupportsPause: true);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var powershell = FindExecutable("powershell.exe", "pwsh.exe");
            return powershell is null
                ? null
                : new SpeechBackend(
                    powershell,
                    ["-NoProfile", "-NonInteractive", "-Command",
                        "$s=New-Object System.Speech.Synthesis.SpeechSynthesizer; $s.Speak([Console]::In.ReadToEnd())"],
                    WriteTextToStandardInput: true,
                    SupportsPause: true);
        }

        var speechDispatcher = FindExecutable("spd-say");
        if (speechDispatcher is not null)
            // spd-say waits for a separate speech-dispatcher daemon. Suspending this client does not
            // suspend the daemon's audio stream, so do not claim process-level pause parity here.
            return new SpeechBackend(speechDispatcher, ["-w", text], SupportsPause: false);

        var espeak = FindExecutable("espeak-ng", "espeak");
        var supportsUnixPause = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            || RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        return espeak is null ? null : new SpeechBackend(espeak, [text], SupportsPause: supportsUnixPause);
    }

    private static string? FindExecutable(params string[] names)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            return null;

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in names)
            {
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    internal sealed record SpeechBackend(
        string FileName,
        IReadOnlyList<string> Arguments,
        bool WriteTextToStandardInput = false,
        bool SupportsPause = false);

    internal interface ISpeechProcessRunner
    {
        ISpeechProcess Start(SpeechBackend backend, string text, Action onExited);
    }

    internal interface ISpeechProcess : IDisposable
    {
        bool HasExited { get; }
        int? ProcessId { get; }
        bool SupportsPause { get; }
        bool TryPause();
        bool TryResume();
        void Kill();
    }

    internal sealed class ProcessSpeechRunner : ISpeechProcessRunner
    {
        private readonly Func<ProcessStartInfo, Action, bool, IPlatformSpeechProcess> _processFactory;

        public ProcessSpeechRunner()
            : this((startInfo, onExited, supportsPause) =>
                new PlatformSpeechProcess(startInfo, onExited, supportsPause))
        {
        }

        internal ProcessSpeechRunner(
            Func<ProcessStartInfo, Action, bool, IPlatformSpeechProcess> processFactory)
        {
            _processFactory = processFactory ?? throw new ArgumentNullException(nameof(processFactory));
        }

        // Kept for focused fakes that do not need to vary process capability.
        internal ProcessSpeechRunner(
            Func<ProcessStartInfo, Action, IPlatformSpeechProcess> processFactory)
            : this((startInfo, onExited, _) => processFactory(startInfo, onExited))
        {
        }

        public ISpeechProcess Start(SpeechBackend backend, string text, Action onExited)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = backend.FileName,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = backend.WriteTextToStandardInput,
            };
            foreach (var argument in backend.Arguments)
                startInfo.ArgumentList.Add(argument);

            var process = _processFactory(startInfo, onExited, backend.SupportsPause);
            try
            {
                if (!process.Start())
                    throw new InvalidOperationException("The speech process could not be started.");

                if (backend.WriteTextToStandardInput)
                    process.WriteStandardInput(text);

                return process;
            }
            catch
            {
                // Start may have created the child before reporting an error, and stdin can fail after a
                // successful launch. In either case this runner owns the exact process and must reap it.
                StopAndDisposeProcess(process);
                throw;
            }
        }
    }

    internal interface IPlatformSpeechProcess : ISpeechProcess
    {
        bool Start();
        void WriteStandardInput(string text);
    }

    private sealed class PlatformSpeechProcess : IPlatformSpeechProcess
    {
        private readonly Process _process;
        private readonly object _pauseGate = new();
        private bool _isPaused;

        public PlatformSpeechProcess(ProcessStartInfo startInfo, Action onExited, bool supportsPause = false)
        {
            _process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true,
            };
            _process.Exited += (_, _) => onExited();
            SupportsPause = supportsPause;
        }

        public bool SupportsPause { get; }

        public int? ProcessId
        {
            get
            {
                try
                {
                    return _process.HasExited ? null : _process.Id;
                }
                catch (InvalidOperationException)
                {
                    return null;
                }
            }
        }

        public bool Start() => _process.Start();

        public bool HasExited => _process.HasExited;

        public void WriteStandardInput(string text)
        {
            _process.StandardInput.Write(text);
            _process.StandardInput.Close();
        }

        public void Kill()
        {
            if (_process.HasExited)
                return;

            _process.Kill(entireProcessTree: true);
            try
            {
                _process.WaitForExit(2000);
            }
            catch (InvalidOperationException)
            {
                // The child exited between the state check and WaitForExit.
            }
        }

        public bool TryPause() => SetPaused(paused: true);

        public bool TryResume() => SetPaused(paused: false);

        private bool SetPaused(bool paused)
        {
            lock (_pauseGate)
            {
                if (!SupportsPause || _isPaused == paused || HasExited)
                    return false;

                try
                {
                    var succeeded = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                        ? (paused ? NtSuspendProcess(_process.Handle) : NtResumeProcess(_process.Handle)) >= 0
                        : IsUnixSignalPlatform && UnixKill(
                            _process.Id,
                            paused
                                ? RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? MacStopSignal : UnixStopSignal
                                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? MacContinueSignal : UnixContinueSignal) == 0;
                    if (succeeded)
                        _isPaused = paused;
                    return succeeded;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        public void Dispose() => _process.Dispose();

        private static bool IsUnixSignalPlatform =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            || RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        private const int UnixStopSignal = 19;
        private const int UnixContinueSignal = 18;
        private const int MacStopSignal = 17;
        private const int MacContinueSignal = 19;

        [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
        private static extern int UnixKill(int processId, int signal);

        [DllImport("ntdll.dll")]
        private static extern int NtSuspendProcess(IntPtr processHandle);

        [DllImport("ntdll.dll")]
        private static extern int NtResumeProcess(IntPtr processHandle);
    }
}

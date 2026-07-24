using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

/// <summary>
/// Local, dependency-free speech adapter for the Avalonia host. It uses an installed OS speech command
/// when one is available and otherwise completes each segment asynchronously as a deterministic no-op.
/// The adapter deliberately reports that pause/resume is unsupported: the portable command-line backends
/// can be stopped, but cannot reliably suspend and continue an utterance.
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

    /// <summary>Command-line backends do not provide honest pause/resume semantics.</summary>
    public bool SupportsPause => false;

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

    public void Pause()
    {
        // No portable command-line backend offers a reliable pause operation.
    }

    public void Resume()
    {
        // No portable command-line backend offers a reliable resume operation.
    }

    public void Stop()
    {
        ISpeechProcess? process;
        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            ++_generation;
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
            if (_disposed || generation != _generation || cancellationToken.IsCancellationRequested
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
            return say is null ? null : new SpeechBackend(say, [text]);
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
                    WriteTextToStandardInput: true);
        }

        var speechDispatcher = FindExecutable("spd-say");
        if (speechDispatcher is not null)
            return new SpeechBackend(speechDispatcher, ["-w", text]);

        var espeak = FindExecutable("espeak-ng", "espeak");
        return espeak is null ? null : new SpeechBackend(espeak, [text]);
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
        bool WriteTextToStandardInput = false);

    internal interface ISpeechProcessRunner
    {
        ISpeechProcess Start(SpeechBackend backend, string text, Action onExited);
    }

    internal interface ISpeechProcess : IDisposable
    {
        bool HasExited { get; }
        void Kill();
    }

    internal sealed class ProcessSpeechRunner : ISpeechProcessRunner
    {
        private readonly Func<ProcessStartInfo, Action, IPlatformSpeechProcess> _processFactory;

        public ProcessSpeechRunner()
            : this((startInfo, onExited) => new PlatformSpeechProcess(startInfo, onExited))
        {
        }

        internal ProcessSpeechRunner(
            Func<ProcessStartInfo, Action, IPlatformSpeechProcess> processFactory)
        {
            _processFactory = processFactory ?? throw new ArgumentNullException(nameof(processFactory));
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

            var process = _processFactory(startInfo, onExited);
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

        public PlatformSpeechProcess(ProcessStartInfo startInfo, Action onExited)
        {
            _process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true,
            };
            _process.Exited += (_, _) => onExited();
        }

        public bool Start() => _process.Start();

        public bool HasExited => _process.HasExited;

        public void WriteStandardInput(string text)
        {
            _process.StandardInput.Write(text);
            _process.StandardInput.Close();
        }

        public void Kill() => _process.Kill(entireProcessTree: true);

        public void Dispose() => _process.Dispose();
    }
}

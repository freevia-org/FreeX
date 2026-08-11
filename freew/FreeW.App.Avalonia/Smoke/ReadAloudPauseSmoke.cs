using System.Diagnostics;
using System.Runtime.InteropServices;
using Free.Shared.AppServices;

namespace FreeW.App.Avalonia.Smoke;

/// <summary>
/// Headless Linux validation for the production command-line speech adapter. It synthesizes to a WAV file,
/// so the probe never depends on an audio device or audible output.
/// </summary>
internal static class ReadAloudPauseSmoke
{
    private const string Option = "--read-aloud-pause-smoke";

    public static bool TryRun(IReadOnlyList<string> args, TextWriter output, TextWriter error, out int exitCode)
    {
        if (!args.Contains(Option, StringComparer.Ordinal))
        {
            exitCode = 0;
            return false;
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            error.WriteLine("read-aloud-pause-smoke requires Linux.");
            exitCode = 1;
            return true;
        }

        var executable = FindExecutable("espeak-ng", "espeak");
        if (executable is null)
        {
            error.WriteLine("read-aloud-pause-smoke requires espeak-ng or espeak.");
            exitCode = 1;
            return true;
        }

        using var wavFile = TemporaryFileLease.CreateForExternalWriter(
            "freew-read-aloud-",
            ".wav");
        var wavPath = wavFile.Path;
        var text = string.Join(' ', Enumerable.Repeat(
            "FreeW pause and resume validation keeps the owned speech process suspended until resumed.",
            4000));
        AvaloniaSpeechEngine? engine = null;

        try
        {
            engine = new AvaloniaSpeechEngine(
                _ => new AvaloniaSpeechEngine.SpeechBackend(
                    executable,
                    ["--stdin", "-w", wavPath],
                    WriteTextToStandardInput: true,
                    SupportsPause: true),
                action => action());

            var completion = 0;
            engine.SpeakAsync(text, () => Interlocked.Exchange(ref completion, 1));
            var processId = WaitForProcessId(engine, 2000);
            if (processId is null)
                throw new InvalidOperationException("The speech child did not expose an owned PID.");

            var outputStarted = WaitForOutputToStart(wavPath, 10000, out var outputBeforePause);
            if (!outputStarted || Volatile.Read(ref completion) != 0)
                throw new InvalidOperationException(
                    "The owned espeak process did not produce incremental output before completion.");

            var pauseStarted = Stopwatch.GetTimestamp();
            var pause = engine.TryPause();
            var pauseElapsedMs = ElapsedMilliseconds(pauseStarted);
            var pausedLength = OutputLength(wavPath);
            var pausedStable = pause && WaitForPausedStability(processId.Value, wavPath, pausedLength, 750);
            var completionWhilePaused = Volatile.Read(ref completion) != 0;
            var pausedState = ReadLinuxProcessState(processId.Value);

            var resumeStarted = Stopwatch.GetTimestamp();
            var resume = pause && engine.TryResume();
            var resumeElapsedMs = ElapsedMilliseconds(resumeStarted);
            var outputAfterResume = pausedLength;
            var naturallyCompleted = false;
            var resumedProgress = resume && WaitForResumedProgress(
                processId.Value,
                wavPath,
                pausedLength,
                ref completion,
                10000,
                out outputAfterResume,
                out naturallyCompleted);
            engine.Stop();
            var processReleased = engine.OwnedProcessIdForSmoke is null
                && WaitFor(() => ReadLinuxProcessState(processId.Value) is null, 2000);

            output.WriteLine("backend=" + Path.GetFileName(executable));
            output.WriteLine("pause=" + (pause ? "passed" : "failed"));
            output.WriteLine("resume=" + (resume ? "passed" : "failed"));
            output.WriteLine("owned_pid=" + processId.Value);
            output.WriteLine("output_before_pause_bytes=" + outputBeforePause);
            output.WriteLine("output_while_paused_bytes=" + pausedLength);
            output.WriteLine("output_after_resume_bytes=" + outputAfterResume);
            output.WriteLine("pause_signal_ms=" + pauseElapsedMs);
            output.WriteLine("resume_signal_ms=" + resumeElapsedMs);
            output.WriteLine("paused_process_state=" + (pausedState?.ToString() ?? "missing"));
            output.WriteLine("paused_stable=" + pausedStable.ToString().ToLowerInvariant());
            output.WriteLine("completion_while_paused=" + completionWhilePaused.ToString().ToLowerInvariant());
            output.WriteLine("resumed_progress=" + resumedProgress.ToString().ToLowerInvariant());
            output.WriteLine("natural_completion_after_resume=" + naturallyCompleted.ToString().ToLowerInvariant());
            output.WriteLine("stop=" + (processReleased ? "passed" : "failed"));

            exitCode = pause && resume && pausedStable && pausedState == 'T' && !completionWhilePaused
                && resumedProgress && processReleased
                ? 0
                : 1;
            output.WriteLine("status=" + (exitCode == 0 ? "passed" : "failed"));
            return true;
        }
        catch (Exception ex)
        {
            error.WriteLine(ex.ToString());
            exitCode = 1;
            return true;
        }
        finally
        {
            // Resume before stopping so a SIGSTOP'ed child cannot survive a failure path. Both calls are
            // best-effort and idempotent: a naturally completed or already stopped child is harmless.
            try
            {
                engine?.TryResume();
                engine?.Stop();
            }
            catch
            {
                // Disposal below remains best-effort cleanup for shutdown paths.
            }
            engine?.Dispose();
        }
    }

    private static int? WaitForProcessId(AvaloniaSpeechEngine engine, int timeoutMs)
    {
        int? processId = null;
        WaitFor(() => (processId = engine.OwnedProcessIdForSmoke) is not null, timeoutMs);
        return processId;
    }

    private static bool WaitForOutputToStart(string path, int timeoutMs, out long length)
    {
        var firstLength = -1L;
        var observedLength = -1L;
        var result = WaitFor(() =>
        {
            observedLength = OutputLength(path);
            if (observedLength <= 44)
                return false;
            if (firstLength < 0)
            {
                firstLength = observedLength;
                return false;
            }

            return observedLength > firstLength;
        }, timeoutMs);
        length = observedLength;
        return result;
    }

    private static bool WaitForPausedStability(int processId, string path, long expectedLength, int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (ReadLinuxProcessState(processId) != 'T' || OutputLength(path) != expectedLength)
                return false;
            Thread.Sleep(100);
        }

        return ReadLinuxProcessState(processId) == 'T' && OutputLength(path) == expectedLength;
    }

    private static bool WaitForResumedProgress(
        int processId,
        string path,
        long pausedLength,
        ref int completion,
        int timeoutMs,
        out long outputLength,
        out bool naturallyCompleted)
    {
        outputLength = pausedLength;
        naturallyCompleted = false;
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            outputLength = OutputLength(path);
            if (outputLength > pausedLength)
                return true;
            if (Volatile.Read(ref completion) != 0)
            {
                naturallyCompleted = true;
                return true;
            }

            if (ReadLinuxProcessState(processId) is null)
                return false;
            Thread.Sleep(100);
        }

        naturallyCompleted = Volatile.Read(ref completion) != 0;
        return outputLength > pausedLength || naturallyCompleted;
    }

    private static bool WaitFor(Func<bool> condition, int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
                return true;
            Thread.Sleep(50);
        }

        return condition();
    }

    private static long OutputLength(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : -1;
        }
        catch (IOException)
        {
            return -1;
        }
    }

    private static char? ReadLinuxProcessState(int processId)
    {
        try
        {
            var stat = File.ReadAllText($"/proc/{processId}/stat");
            var closingParenthesis = stat.LastIndexOf(')');
            return closingParenthesis >= 0 && stat.Length > closingParenthesis + 2
                ? stat[closingParenthesis + 2]
                : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static long ElapsedMilliseconds(long started)
    {
        return (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }

    private static string? FindExecutable(params string[] names)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            return null;

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        foreach (var name in names)
        {
            var candidate = Path.Combine(directory, name);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Free.Shared.Shell.Avalonia;

/// <summary>Result of one probe for a display the Avalonia render timer can attach to.</summary>
public enum SisterAvaloniaDisplayProbeResult
{
    /// <summary>A display link could be created: Avalonia's native render timer can start.</summary>
    Available,

    /// <summary>No active display (display asleep, session locked, or headless console session).</summary>
    Unavailable,

    /// <summary>The probe itself could not run (library or symbol missing); fail open and start.</summary>
    ProbeFailed
}

/// <summary>What the startup runner should do after the display readiness gate returns.</summary>
public enum SisterAvaloniaDisplayGateOutcome
{
    /// <summary>Start the Avalonia application lifetime.</summary>
    Continue,

    /// <summary>No display became available within the wait budget: exit cleanly without starting.</summary>
    Exit
}

/// <summary>
/// macOS startup guard: Avalonia.Native creates its compositor render timer from a CoreVideo display
/// link during <c>AppBuilder.Setup</c>. When the app is launched while the display is asleep or the
/// session is locked, <c>CVDisplayLinkCreateWithActiveCGDisplays</c> fails with
/// <c>kCVReturnInvalidDisplay</c> (-6661) and Avalonia throws an unrecoverable
/// <see cref="InvalidOperationException"/> ("not able to start the RenderTimer") that aborts the
/// process. This gate probes the same CoreVideo call before Avalonia initialises and waits for a
/// display instead of crashing; the decision logic is display-independent so it can be unit tested.
/// </summary>
public static class SisterAvaloniaDisplayReadinessGate
{
    /// <summary>Process exit code used when startup is abandoned because no display is available (EX_TEMPFAIL).</summary>
    public const int DisplayUnavailableExitCode = 75;

    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(1);

    public static readonly TimeSpan DefaultMaximumWait = TimeSpan.FromMinutes(30);

    internal const string RenderTimerFailureMessageFragment = "not able to start the RenderTimer";

    /// <summary>
    /// Production entry point: a no-op on non-macOS platforms; on macOS probes CoreVideo and waits
    /// (polling every second, logging once) for up to <see cref="DefaultMaximumWait"/>.
    /// </summary>
    public static SisterAvaloniaDisplayGateOutcome WaitForDisplayIfNeeded()
    {
        if (!OperatingSystem.IsMacOS())
            return SisterAvaloniaDisplayGateOutcome.Continue;

        var stopwatch = Stopwatch.StartNew();
        return WaitForDisplay(
            MacOsCoreVideoDisplayProbe.Probe,
            Thread.Sleep,
            () => stopwatch.Elapsed,
            Log,
            DefaultPollInterval,
            DefaultMaximumWait);
    }

    /// <summary>
    /// Display-independent decision loop. Available/ProbeFailed continue immediately; Unavailable logs
    /// once, sleeps <paramref name="pollInterval"/> and re-probes until a display appears (continue) or
    /// <paramref name="maximumWait"/> elapses (exit). A throwing probe is treated as ProbeFailed.
    /// </summary>
    public static SisterAvaloniaDisplayGateOutcome WaitForDisplay(
        Func<SisterAvaloniaDisplayProbeResult> probe,
        Action<TimeSpan> sleep,
        Func<TimeSpan> elapsed,
        Action<string> log,
        TimeSpan pollInterval,
        TimeSpan maximumWait)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(sleep);
        ArgumentNullException.ThrowIfNull(elapsed);
        ArgumentNullException.ThrowIfNull(log);

        var loggedWait = false;
        while (true)
        {
            var result = SafeProbe(probe);
            if (result != SisterAvaloniaDisplayProbeResult.Unavailable)
            {
                if (loggedWait)
                    SafeLog(log, $"A display became available after {elapsed().TotalSeconds:0} s; starting.");
                return SisterAvaloniaDisplayGateOutcome.Continue;
            }

            if (elapsed() >= maximumWait)
            {
                SafeLog(log, $"No display became available within {maximumWait.TotalMinutes:0} minute(s); exiting without starting the UI.");
                return SisterAvaloniaDisplayGateOutcome.Exit;
            }

            if (!loggedWait)
            {
                SafeLog(log, "No active display is available (display asleep or session locked); waiting for one before starting the UI.");
                loggedWait = true;
            }

            sleep(pollInterval);
        }
    }

    /// <summary>
    /// True when <paramref name="exception"/> (or an inner exception) is Avalonia.Native's render-timer
    /// start failure, i.e. the display went away between the gate's probe and Avalonia's own setup.
    /// </summary>
    public static bool IsDisplayUnavailableStartupFailure(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is InvalidOperationException &&
                current.Message.Contains(RenderTimerFailureMessageFragment, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    internal static void Log(string message)
    {
        var line = "[startup] " + message;
        try
        {
            Console.Error.WriteLine(line);
        }
        catch
        {
            // stderr may be closed for LaunchServices-launched bundles; logging is best effort.
        }

        Trace.WriteLine(line);
    }

    private static SisterAvaloniaDisplayProbeResult SafeProbe(Func<SisterAvaloniaDisplayProbeResult> probe)
    {
        try
        {
            return probe();
        }
        catch
        {
            return SisterAvaloniaDisplayProbeResult.ProbeFailed;
        }
    }

    private static void SafeLog(Action<string> log, string message)
    {
        try
        {
            log(message);
        }
        catch
        {
            // Logging must never turn a recoverable wait into a startup crash.
        }
    }
}

/// <summary>
/// Calls CoreVideo's <c>CVDisplayLinkCreateWithActiveCGDisplays</c> -- the exact call Avalonia.Native's
/// render timer makes -- through <see cref="NativeLibrary"/> so the portable assembly carries no static
/// native import, then releases the link immediately.
/// </summary>
internal static class MacOsCoreVideoDisplayProbe
{
    private const string CoreVideoPath = "/System/Library/Frameworks/CoreVideo.framework/CoreVideo";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int CreateWithActiveDisplaysFunction(out IntPtr displayLink);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ReleaseFunction(IntPtr displayLink);

    private static readonly Lazy<(CreateWithActiveDisplaysFunction Create, ReleaseFunction Release)?> Functions =
        new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    public static SisterAvaloniaDisplayProbeResult Probe()
    {
        if (!OperatingSystem.IsMacOS() || Functions.Value is not { } functions)
            return SisterAvaloniaDisplayProbeResult.ProbeFailed;

        var status = functions.Create(out var displayLink);
        if (displayLink != IntPtr.Zero)
            functions.Release(displayLink);

        return status == 0 && displayLink != IntPtr.Zero
            ? SisterAvaloniaDisplayProbeResult.Available
            : SisterAvaloniaDisplayProbeResult.Unavailable;
    }

    private static (CreateWithActiveDisplaysFunction, ReleaseFunction)? Load()
    {
        try
        {
            if (!NativeLibrary.TryLoad(CoreVideoPath, out var library) ||
                !NativeLibrary.TryGetExport(library, "CVDisplayLinkCreateWithActiveCGDisplays", out var create) ||
                !NativeLibrary.TryGetExport(library, "CVDisplayLinkRelease", out var release))
            {
                return null;
            }

            return (
                Marshal.GetDelegateForFunctionPointer<CreateWithActiveDisplaysFunction>(create),
                Marshal.GetDelegateForFunctionPointer<ReleaseFunction>(release));
        }
        catch
        {
            return null;
        }
    }
}

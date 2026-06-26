using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace FreeP.App.Avalonia.Smoke;

/// <summary>
/// Platform-neutral launch-smoke options. Uses the same argument spelling as the FreeX/FreeW Linux
/// lanes (<c>--launch-smoke &lt;report&gt;</c>) so one CI lane can drive all sister apps.
/// A headless Avalonia window is shown under Xvfb, a snapshot is captured, and the app exits.
/// </summary>
internal sealed record LaunchSmokeOptions(string ReportPath, string? DiagnosticsDirectory)
{
    public const string Argument = "--launch-smoke";
    public const string DiagnosticsDirectoryArgument = "--launch-smoke-diagnostics-dir";

    public static bool TryParse(
        IReadOnlyList<string> args,
        out LaunchSmokeOptions? options,
        out string[] startupArguments,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(args);
        options = null;
        error = "";
        var filtered = new List<string>();
        string? reportPath = null;
        string? diagnosticsDirectory = null;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, Argument, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Count)
                {
                    startupArguments = [];
                    error = $"{Argument} requires a report path.";
                    return false;
                }
                reportPath = args[++i];
                continue;
            }

            if (string.Equals(arg, DiagnosticsDirectoryArgument, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Count)
                {
                    startupArguments = [];
                    error = $"{DiagnosticsDirectoryArgument} requires a directory path.";
                    return false;
                }
                diagnosticsDirectory = args[++i];
                continue;
            }

            filtered.Add(arg);
        }

        if (reportPath is not null)
            options = new LaunchSmokeOptions(reportPath, diagnosticsDirectory);

        startupArguments = filtered.ToArray();
        return true;
    }
}

internal sealed record LaunchSmokeSnapshot(
    bool WindowShown,
    bool HasToolbar,
    int SlideCount,
    int CurrentSlideIndex)
{
    public bool IsPassed => WindowShown && HasToolbar && SlideCount >= 0;

    public string ToReport() =>
        $"freep_launch_smoke={(IsPassed ? "passed" : "failed")}\n" +
        $"window_shown={WindowShown.ToString().ToLowerInvariant()}\n" +
        $"has_toolbar={HasToolbar.ToString().ToLowerInvariant()}\n" +
        $"slide_count={SlideCount}\n" +
        $"current_slide={CurrentSlideIndex}\n";
}

internal static class LaunchSmokeCoordinator
{
    private const int MaxAttempts = 60;
    private const int PollMilliseconds = 200;

    public static void Start(MainWindow window, LaunchSmokeOptions options)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(options);

        window.Opened += (_, _) =>
        {
            var attempts = 0;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PollMilliseconds) };
            timer.Tick += (_, _) =>
            {
                attempts++;
                var snapshot = Capture(window);
                if (snapshot.IsPassed || attempts >= MaxAttempts)
                {
                    timer.Stop();
                    Finish(snapshot, options);
                }
            };
            timer.Start();
        };
    }

    private static LaunchSmokeSnapshot Capture(MainWindow window) => new(
        WindowShown: window.IsVisible,
        HasToolbar: window.HasToolbar,
        SlideCount: window.SlideCount,
        CurrentSlideIndex: window.CurrentSlideIndex);

    private static void Finish(LaunchSmokeSnapshot snapshot, LaunchSmokeOptions options)
    {
        var report = snapshot.ToReport();
        try
        {
            var directory = Path.GetDirectoryName(options.ReportPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(options.ReportPath, report);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"launch-smoke: failed to write report: {ex.Message}");
        }

        Console.Out.Write(report);
        Console.Out.Flush();

        var exitCode = snapshot.IsPassed ? 0 : 1;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown(exitCode);
        else
            Environment.Exit(exitCode);
    }
}

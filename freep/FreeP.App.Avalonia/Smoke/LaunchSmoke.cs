using Free.Shared.Shell.Avalonia;

namespace FreeP.App.Avalonia.Smoke;

/// <summary>
/// Platform-neutral launch-smoke options. Uses the same argument spelling as the FreeX/FreeW Linux
/// lanes (<c>--launch-smoke &lt;report&gt;</c>) so one CI lane can drive all sister apps.
/// A headless Avalonia window is shown under Xvfb, a snapshot is captured, and the app exits.
/// </summary>
internal sealed record LaunchSmokeOptions(string ReportPath, string? DiagnosticsDirectory)
    : SisterAppLaunchSmokeOptions(ReportPath, DiagnosticsDirectory)
{
    public new const string Argument = SisterAppLaunchSmokeOptions.Argument;
    public new const string DiagnosticsDirectoryArgument = SisterAppLaunchSmokeOptions.DiagnosticsDirectoryArgument;

    public static bool TryParse(
        IReadOnlyList<string> args,
        out LaunchSmokeOptions? options,
        out string[] startupArguments,
        out string error)
    {
        var result = SisterAppLaunchSmokeOptions.TryParse(
            args,
            out var sharedOptions,
            out startupArguments,
            out error);
        options = null;

        if (sharedOptions is not null)
            options = new LaunchSmokeOptions(sharedOptions.ReportPath, sharedOptions.DiagnosticsDirectory);

        return result;
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

        SisterAppLaunchSmokeCoordinator.Start(
            window,
            options,
            mainWindow =>
            {
                var snapshot = Capture(mainWindow);
                return new SisterAppLaunchSmokeReport(snapshot.IsPassed, snapshot.ToReport());
            },
            MaxAttempts,
            PollMilliseconds);
    }

    private static LaunchSmokeSnapshot Capture(MainWindow window) => new(
        WindowShown: window.IsVisible,
        HasToolbar: window.HasToolbar,
        SlideCount: window.SlideCount,
        CurrentSlideIndex: window.CurrentSlideIndex);
}

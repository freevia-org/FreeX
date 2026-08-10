global using LaunchSmokeOptions = Free.Shared.Shell.Avalonia.SisterAppLaunchSmokeOptions;

using Free.Shared.Shell.Avalonia;

namespace FreeP.App.Avalonia.Smoke;

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
            });
    }

    private static LaunchSmokeSnapshot Capture(MainWindow window) => new(
        WindowShown: window.IsVisible,
        HasToolbar: window.HasToolbar,
        SlideCount: window.SlideCount,
        CurrentSlideIndex: window.CurrentSlideIndex);
}

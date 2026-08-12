using Free.Shared.Shell.Avalonia;
using FreeP.App.Avalonia;

namespace FreeP.Validation.Avalonia;

internal sealed record LaunchSmokeSnapshot(
    bool WindowShown,
    bool HasToolbar,
    int SlideCount,
    int CurrentSlideIndex)
{
    internal bool IsPassed => WindowShown && HasToolbar && SlideCount >= 0;

    internal string ToReport() =>
        $"freep_launch_smoke={(IsPassed ? "passed" : "failed")}\n" +
        $"window_shown={WindowShown.ToString().ToLowerInvariant()}\n" +
        $"has_toolbar={HasToolbar.ToString().ToLowerInvariant()}\n" +
        $"slide_count={SlideCount}\n" +
        $"current_slide={CurrentSlideIndex}\n";
}

internal static class LaunchSmokeCoordinator
{
    internal static void Start(
        MainWindow.ValidationAccessAdapter access,
        SisterAppLaunchSmokeOptions options)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(options);

        SisterAppLaunchSmokeCoordinator.Start(
            startWhenOpened: start => access.StartWhenOpened(() =>
            {
                start();
                return Task.CompletedTask;
            }),
            options,
            capture: () =>
            {
                var snapshot = Capture(access);
                return new SisterAppLaunchSmokeReport(snapshot.IsPassed, snapshot.ToReport());
            },
            shutdown: access.Shutdown);
    }

    internal static LaunchSmokeSnapshot Capture(MainWindow.ValidationAccessAdapter access) => new(
        WindowShown: access.IsVisible,
        HasToolbar: access.HasToolbar,
        SlideCount: access.SlideCount,
        CurrentSlideIndex: access.CurrentSlideIndex);
}

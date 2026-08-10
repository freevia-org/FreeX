global using LaunchSmokeOptions = Free.Shared.Shell.Avalonia.SisterAppLaunchSmokeOptions;

using Free.Shared.Shell.Avalonia;

namespace FreeW.App.Avalonia.Smoke;

internal sealed record LaunchSmokeSnapshot(
    bool WindowShown,
    bool HasToolbar,
    int BlockCount,
    int ParagraphCount,
    int PlacedGlyphCount)
{
    public bool IsPassed => WindowShown && HasToolbar && ParagraphCount > 0 && PlacedGlyphCount > 0;

    public string ToReport() =>
        $"freew_launch_smoke={(IsPassed ? "passed" : "failed")}\n" +
        $"window_shown={WindowShown.ToString().ToLowerInvariant()}\n" +
        $"has_toolbar={HasToolbar.ToString().ToLowerInvariant()}\n" +
        $"block_count={BlockCount}\n" +
        $"paragraph_count={ParagraphCount}\n" +
        $"placed_glyphs={PlacedGlyphCount}\n";
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
        BlockCount: window.Editor.BlockCount,
        ParagraphCount: window.Editor.ParagraphCount,
        PlacedGlyphCount: window.Editor.PlacedGlyphCount);
}

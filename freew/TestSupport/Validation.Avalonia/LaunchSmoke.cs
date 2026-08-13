using Free.Shared.Shell.Avalonia;
using FreeW.App.Avalonia;

namespace FreeW.Validation.Avalonia;

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
    public static void Start(
        MainWindow.ValidationAccessAdapter access,
        SisterAppLaunchSmokeOptions options)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(options);

        access.StartLaunchSmoke(
            options,
            current =>
            {
                var snapshot = Capture(current);
                return new SisterAppLaunchSmokeReport(snapshot.IsPassed, snapshot.ToReport());
            });
    }

    private static LaunchSmokeSnapshot Capture(MainWindow.ValidationAccessAdapter access) => new(
        access.IsWindowVisible,
        access.HasToolbar,
        access.BlockCount,
        access.ParagraphCount,
        access.PlacedGlyphCount);
}

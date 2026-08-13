using Avalonia.Controls;
using Free.Shared.AppServices;

namespace Free.Shared.Shell.Avalonia;

/// <summary>Adapts an Avalonia top-level launcher to the shared external URI policy.</summary>
public static class AvaloniaExternalUriLauncher
{
    public static Task<ExternalUriLaunchResult> OpenAsync(Control relativeTo, string target)
    {
        ArgumentNullException.ThrowIfNull(relativeTo);

        var launcher = TopLevel.GetTopLevel(relativeTo)?.Launcher;
        Func<Uri, Task<bool>>? launchAsync = launcher is null
            ? null
            : launcher.LaunchUriAsync;
        return ExternalUriLauncher.OpenAsync(target, launchAsync);
    }
}

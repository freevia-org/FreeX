using Free.Shared.Shell.Avalonia;

namespace FreeP.App.Avalonia;

// Compiled into isolated Avalonia tool-host renderer variants only.
internal static partial class Program
{
    internal static int RunToolHost(Action<MainWindow> coordinator) =>
        RunToolHostCore([], coordinator);

    internal static int RunToolHostCore(
        IReadOnlyList<string> startupArguments,
        Action<MainWindow> coordinator)
    {
        ArgumentNullException.ThrowIfNull(startupArguments);
        ArgumentNullException.ThrowIfNull(coordinator);
        return SisterAvaloniaStandardDesktopFactory.Run(
            [],
            App.DesktopProfile,
            new SisterAvaloniaStandardDesktopLaunch<MainWindow>(
                startupArguments.ToArray(),
                coordinator));
    }
}

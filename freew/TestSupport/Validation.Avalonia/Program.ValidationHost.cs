using Free.Shared.Shell.Avalonia;

namespace FreeW.App.Avalonia;

internal static partial class Program
{
    internal static int RunToolHost(
        IReadOnlyList<string> startupArguments,
        Action<MainWindow.ValidationAccessAdapter> coordinator)
    {
        ArgumentNullException.ThrowIfNull(startupArguments);
        ArgumentNullException.ThrowIfNull(coordinator);
        return SisterAvaloniaStandardDesktopFactory.Run(
            [],
            App.DesktopProfile,
            new SisterAvaloniaStandardDesktopLaunch<MainWindow>(
                startupArguments.ToArray(),
                window => coordinator(window.CreateValidationAccessAdapter())));
    }
}

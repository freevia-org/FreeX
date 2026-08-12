namespace FreeP.App.Avalonia;

// Compiled into isolated Avalonia tool-host renderer variants only.
public sealed partial class App
{
    private static Action<MainWindow>? _toolHostStartupCoordinator;

    internal static void ConfigureToolHost(
        IReadOnlyList<string> startupArguments,
        Action<MainWindow> coordinator)
    {
        ArgumentNullException.ThrowIfNull(startupArguments);
        ArgumentNullException.ThrowIfNull(coordinator);

        StartupArguments = startupArguments.ToArray();
        _toolHostStartupCoordinator = coordinator;
    }

    static partial void CoordinateToolHostStartup(MainWindow mainWindow) =>
        _toolHostStartupCoordinator?.Invoke(mainWindow);
}

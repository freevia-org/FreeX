using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace FreeX.App.Avalonia;

public sealed class App : Application
{
    public static IReadOnlyList<string> StartupArguments { get; set; } = [];

    internal static MacOsLaunchSmokeOptions? LaunchSmokeOptions { get; set; }

    internal static AvaloniaAppDiagnostics? Diagnostics { get; set; }

    public override void OnFrameworkInitializationCompleted()
    {
        RequestedThemeVariant = ThemeVariant.Light;
        Styles.Add(new FluentTheme());

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new MainWindow(StartupArguments);
            desktop.MainWindow = mainWindow;
            Diagnostics?.RecordEvent("app_ready", new Dictionary<string, string?>
            {
                ["source"] = "avalonia",
                ["scope"] = "app",
                ["status"] = "ready"
            });

            if (this.TryGetFeature<IActivatableLifetime>() is { } activatableLifetime)
                activatableLifetime.Activated += async (_, args) => await MainWindow_ActivatedAsync(mainWindow, args);

            if (LaunchSmokeOptions is { } launchSmokeOptions)
                MacOsLaunchSmokeCoordinator.Start(mainWindow, launchSmokeOptions, Diagnostics);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task MainWindow_ActivatedAsync(MainWindow mainWindow, ActivatedEventArgs args)
    {
        if (args is not FileActivatedEventArgs fileArgs ||
            fileArgs.Kind != ActivationKind.File)
        {
            return;
        }

        mainWindow.Show();
        mainWindow.Activate();
        await mainWindow.OpenActivatedFilesAsync(fileArgs.Files);
    }
}

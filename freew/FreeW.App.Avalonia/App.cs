using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using FreeW.App.Avalonia.Smoke;

namespace FreeW.App.Avalonia;

public sealed class App : Application
{
    public static IReadOnlyList<string> StartupArguments { get; set; } = [];
    internal static LaunchSmokeOptions? LaunchSmokeOptions { get; set; }

    public override void OnFrameworkInitializationCompleted()
    {
        RequestedThemeVariant = ThemeVariant.Light;
        Styles.Add(new FluentTheme());

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new MainWindow(StartupArguments);
            desktop.MainWindow = mainWindow;

            if (LaunchSmokeOptions is { } options)
                LaunchSmokeCoordinator.Start(mainWindow, options);
        }

        base.OnFrameworkInitializationCompleted();
    }
}

using Avalonia;
using Free.Shared.Theme;
using Free.Shared.Theme.Avalonia;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Avalonia.Smoke;
using FreeW.App.Presentation.Shell;

namespace FreeW.App.Avalonia;

public sealed class App : Application
{
    public static IReadOnlyList<string> StartupArguments { get; set; } = [];
    internal static LaunchSmokeOptions? LaunchSmokeOptions { get; set; }
    internal static Theme ActiveTheme { get; private set; } = FreeWApplicationStartup.Theme.DefaultTheme;

    public override void OnFrameworkInitializationCompleted()
    {
        AvaloniaAppLocalizationBootstrap.InstallSharedSeams(
            UiText.Get,
            UiText.Format,
            UiText.CreateAutomationName);

        FreeWApplicationStartup.Theme.Apply(
            Environment.GetEnvironmentVariable,
            theme => ActiveTheme = theme,
            (theme, resourceKeyPrefix) =>
                Resources.MergedDictionaries.Add(
                    AvaloniaThemeApplier.BuildResources(theme, resourceKeyPrefix)));

        SisterAvaloniaAppBootstrap.Initialize(
            this,
            new SisterAvaloniaAppBootstrapSpec<MainWindow>(
                StartupArguments,
                args => new MainWindow(args),
                mainWindow =>
                {
                    if (LaunchSmokeOptions is { } options)
                        LaunchSmokeCoordinator.Start(mainWindow, options);
                }));

        base.OnFrameworkInitializationCompleted();
    }
}

using Avalonia;
using Free.Shared.Theme;
using Free.Shared.Theme.Avalonia;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Avalonia.Smoke;

namespace FreeW.App.Avalonia;

public sealed class App : Application
{
    public static IReadOnlyList<string> StartupArguments { get; set; } = [];
    internal static LaunchSmokeOptions? LaunchSmokeOptions { get; set; }
    internal static Theme ActiveTheme { get; private set; } = BrandThemes.FreeW;

    public override void OnFrameworkInitializationCompleted()
    {
        var theme = string.Equals(
            Environment.GetEnvironmentVariable("FREEW_THEME"),
            "midnight",
            StringComparison.OrdinalIgnoreCase)
            ? BrandThemes.FreeXMidnight
            : BrandThemes.FreeW;
        ActiveTheme = theme;
        Resources.MergedDictionaries.Add(AvaloniaThemeApplier.BuildResources(theme, "FreeW"));

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

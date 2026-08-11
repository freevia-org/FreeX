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
        // Route the shared shell's OK/Cancel button text and generic message-box titles
        // (AvaloniaDialogButtonRowFactory.CreateOkCancel, AvaloniaUserMessageDialog) through
        // FreeW's own localized resource catalog instead of the shared shell's neutral-English
        // ShellStrings.Current default — mirrors the WPF host's
        // AppLocalization.Bootstrap.InstallSharedSeams() (App.xaml.cs). Must run before any
        // window/dialog can be shown, so it goes first.
        AvaloniaAppLocalizationBootstrap.InstallSharedSeams(UiText.Get, UiText.Format, UiText.CreateAutomationName);

        var theme = string.Equals(
            Environment.GetEnvironmentVariable("FREEW_THEME"),
            "midnight",
            StringComparison.OrdinalIgnoreCase)
            ? BrandThemes.FreeXMidnight
            : BrandThemes.FreeW;
        ActiveTheme = theme;
        AvaloniaThemeApplier.Apply(this, theme, "FreeW");

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

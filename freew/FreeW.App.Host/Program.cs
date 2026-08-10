using Free.Shared.Theme;
using Free.Shared.Theme.Wpf;
using FreeW.App.Presentation.Options;

namespace FreeW.App.Host;

/// <summary>
/// FreeW entry point. Installs FreeW identity/seams, then delegates the common WPF options,
/// diagnostics, theme/language startup, and application-run lifecycle to the shared startup runner.
/// </summary>
public static class Program
{
    /// <summary>
    /// The active brand theme selected at startup (default: <see cref="BrandThemes.FreeW"/>).
    /// Stored so tests and future windows can read the active palette.
    /// </summary>
    internal static Theme ActiveTheme { get; private set; } = BrandThemes.FreeW;

    [STAThread]
    public static void Main(string[] args)
        => WpfApplicationStartupRunner.Run(new WpfApplicationStartupSpec<FreeWOptions>(
            new AppProductIdentity("FreeW", "FREEW_DIAGNOSTICS", "FreeW"),
            (options, optionsStore, startupFilePaths) =>
                new MainWindow(options, optionsStore, startupFilePaths: startupFilePaths))
        {
            InstallSharedSeams = AppLocalization.Bootstrap.InstallSharedSeams,
            Theme = new WpfApplicationThemeStartupSpec<Theme>(
                EnvironmentVariableName: "FREEW_THEME",
                AlternateThemeValue: "midnight",
                DefaultTheme: BrandThemes.FreeW,
                AlternateTheme: BrandThemes.FreeXMidnight,
                ResourceKeyPrefix: "FreeW",
                ApplyTheme: WpfThemeApplier.Apply)
            {
                SetActiveTheme = theme => ActiveTheme = theme
            },
            Localization = new WpfApplicationLocalizationStartupSpec<FreeWOptions>(
                SelectUiLanguage: options => options.UiLanguage,
                ApplyUiLanguage: AppLocalization.Bootstrap.ApplyAppLanguage,
                ApplyCurrentCultureToWpf: AppLocalization.Bootstrap.ApplyCurrentCultureToWpf)
        }, args);
}

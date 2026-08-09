using Free.Shared.Theme;
using Free.Shared.Theme.Wpf;
using FreeW.App.Presentation.Options;
using FreeW.App.Presentation.Shell;

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
    internal static Theme ActiveTheme { get; private set; } = FreeWApplicationStartup.Theme.DefaultTheme;

    [STAThread]
    public static void Main()
        => WpfApplicationStartupRunner.Run(new WpfApplicationStartupSpec<FreeWOptions>(
            FreeWApplicationStartup.ProductIdentity,
            (options, optionsStore) => new MainWindow(options, optionsStore))
        {
            InstallSharedSeams = AppLocalization.Bootstrap.InstallSharedSeams,
            Theme = new WpfApplicationThemeStartupSpec<Theme>(
                Plan: FreeWApplicationStartup.Theme,
                ApplyTheme: WpfThemeApplier.Apply)
            {
                SetActiveTheme = theme => ActiveTheme = theme
            },
            Localization = new WpfApplicationLocalizationStartupSpec<FreeWOptions>(
                SelectUiLanguage: options => options.UiLanguage,
                ApplyUiLanguage: AppLocalization.Bootstrap.ApplyAppLanguage,
                ApplyCurrentCultureToWpf: AppLocalization.Bootstrap.ApplyCurrentCultureToWpf)
        });
}

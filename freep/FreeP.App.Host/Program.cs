using Free.Shared.Theme;
using Free.Shared.Theme.Wpf;
using FreeP.App.Compositor;

namespace FreeP.App.Host;

/// <summary>
/// FreeP entry point. Keeps FreeP-specific identity/seam/window choices local while the shared WPF
/// runner owns options loading, local diagnostics, theme/language startup, crash hooks, and app lifetime events.
/// </summary>
public static class Program
{
    /// <summary>
    /// The active brand theme selected at startup (default: <see cref="BrandThemes.FreeP"/>).
    /// Stored so tests and future windows can read the active palette.
    /// </summary>
    internal static Theme ActiveTheme { get; private set; } = BrandThemes.FreeP;

    [STAThread]
    public static int Main(string[] args)
    {
        if (WpfWholeWindowVisualEvidenceCapture.TryRun(args, out var wholeWindowCaptureExitCode))
            return wholeWindowCaptureExitCode;

        if (WpfDialogPaneVisualEvidenceCapture.TryRun(args, out var captureExitCode))
            return captureExitCode;

        // TODO(velopack): if/when a shared Velopack bootstrap helper lands, call it here before the WPF
        // Application is created. The scaffold ships without self-update.

        WpfApplicationStartupRunner.Run(new WpfApplicationStartupSpec<FreePOptions>(
            FreePApplicationStartupDescriptor.ProductIdentity,
            (options, optionsStore) => new MainWindow(options, optionsStore))
        {
            InstallSharedSeams = AppComposition.InstallSharedSeams,
            Theme = new WpfApplicationThemeStartupSpec<Theme>(
                Plan: FreePApplicationStartupDescriptor.Theme,
                ApplyTheme: WpfThemeApplier.Apply)
            {
                SetActiveTheme = theme => ActiveTheme = theme
            },
            Localization = new WpfApplicationLocalizationStartupSpec<FreePOptions>(
                SelectUiLanguage: FreePOptionsPolicy.SelectUiLanguage,
                ApplyUiLanguage: AppLocalization.Bootstrap.ApplyAppLanguage,
                ApplyCurrentCultureToWpf: AppLocalization.Bootstrap.ApplyCurrentCultureToWpf)
        });
        return 0;
    }
}

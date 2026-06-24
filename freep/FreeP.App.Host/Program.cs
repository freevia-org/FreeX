using System.Windows;
using Free.Shared.Theme;
using Free.Shared.Theme.Wpf;

namespace FreeP.App.Host;

/// <summary>
/// FreeP entry point. Keeps FreeP-specific identity/seam/window choices local while the shared WPF
/// runner owns options loading, local diagnostics, crash hooks, and app lifetime events.
/// </summary>
public static class Program
{
    /// <summary>
    /// The active brand theme selected at startup (default: <see cref="BrandThemes.FreeP"/>).
    /// Stored so tests and future windows can read the active palette.
    /// </summary>
    internal static Theme ActiveTheme { get; private set; } = BrandThemes.FreeP;

    [STAThread]
    public static void Main()
    {
        // TODO(velopack): if/when a shared Velopack bootstrap helper lands, call it here before the WPF
        // Application is created. The scaffold ships without self-update.

        WpfApplicationStartupRunner.Run(new WpfApplicationStartupSpec<FreePOptions>(
            new AppProductIdentity("FreeP", "FREEP_DIAGNOSTICS", "FreeP"),
            (options, optionsStore) =>
            {
                // Apply the brand theme early — before the main window loads — so that
                // DynamicResource references in the chrome pick up the correct brushes.
                // The theme values are BYTE-IDENTICAL to FreeP's current chrome palette,
                // so the visual result is unchanged.  FREEP_THEME=midnight swaps in the
                // alternate palette (currently reuses FreeXMidnight as a demo).
                var theme = string.Equals(
                    System.Environment.GetEnvironmentVariable("FREEP_THEME"),
                    "midnight",
                    StringComparison.OrdinalIgnoreCase)
                    ? BrandThemes.FreeXMidnight
                    : BrandThemes.FreeP;
                ActiveTheme = theme;
                WpfThemeApplier.Apply(Application.Current, theme, "FreeP");
                return new MainWindow(options, optionsStore);
            })
        {
            InstallSharedSeams = AppComposition.InstallSharedSeams
        });
    }
}

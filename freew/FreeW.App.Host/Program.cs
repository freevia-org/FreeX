using System.Windows;
using Free.Shared.Theme;
using Free.Shared.Theme.Wpf;

namespace FreeW.App.Host;

/// <summary>
/// FreeW entry point. Installs FreeW identity/seams, then delegates the common WPF options,
/// diagnostics, and application-run lifecycle to the shared startup runner.
/// </summary>
public static class Program
{
    /// <summary>
    /// The active brand theme selected at startup (default: <see cref="BrandThemes.FreeW"/>).
    /// Stored so tests and future windows can read the active palette.
    /// </summary>
    internal static Theme ActiveTheme { get; private set; } = BrandThemes.FreeW;

    [STAThread]
    public static void Main()
        => WpfApplicationStartupRunner.Run(new WpfApplicationStartupSpec<FreeWOptions>(
            new AppProductIdentity("FreeW", "FREEW_DIAGNOSTICS", "FreeW"),
            (options, optionsStore) =>
            {
                // Apply the brand theme early — before the main window loads — so that
                // DynamicResource references in the chrome pick up the correct brushes.
                // The theme values are BYTE-IDENTICAL to FreeW's current chrome palette,
                // so the visual result is unchanged.  FREEW_THEME=midnight swaps in the
                // alternate palette (currently reuses FreeXMidnight as a demo).
                var theme = string.Equals(
                    System.Environment.GetEnvironmentVariable("FREEW_THEME"),
                    "midnight",
                    StringComparison.OrdinalIgnoreCase)
                    ? BrandThemes.FreeXMidnight
                    : BrandThemes.FreeW;
                ActiveTheme = theme;
                WpfThemeApplier.Apply(Application.Current, theme, "FreeW");
                return new MainWindow(options, optionsStore);
            })
        {
            InstallSharedSeams = () => ShellStrings.Current = new DefaultShellStrings()
        });
}

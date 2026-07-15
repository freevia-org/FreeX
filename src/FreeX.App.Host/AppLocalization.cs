using System.Globalization;
using Free.Shared.Shell;

namespace FreeX.App.Host;

internal static class AppLocalization
{
    private static readonly CultureInfo StartupUiCulture = CultureInfo.CurrentUICulture;
    public static void ApplyAppLanguage(string? cultureName)
        => WpfLocalizationCultureBootstrap.ApplyUiCulture(
            cultureName,
            name => AppLanguageCatalog.ResolveCulture(name, StartupUiCulture),
            StartupUiCulture);

    public static void ApplyCurrentCultureToWpf() =>
        WpfLocalizationCultureBootstrap.ApplyCurrentCultureToWpf();
}

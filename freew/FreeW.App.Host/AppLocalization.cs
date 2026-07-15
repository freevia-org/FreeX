using System.Globalization;
using Free.Shared.Shell;
using FreeW.App.Localization;

namespace FreeW.App.Host;

internal static class AppLocalization
{
    private static readonly CultureInfo StartupUiCulture = CultureInfo.CurrentUICulture;
    public static void InstallSharedSeams()
    {
        ShellStrings.Current = new ResourceShellStrings(
            () => UiText.Ok,
            () => UiText.Cancel,
            () => UiText.ErrorTitle,
            () => UiText.WarningTitle,
            () => UiText.InformationTitle,
            () => UiText.ConfirmTitle,
            UiText.CreateAutomationName);
        BackstageStrings.Current = new ResourceBackstageStrings(UiText.Get, UiText.Format);
    }

    public static void ApplyAppLanguage(string? cultureName)
        => WpfLocalizationCultureBootstrap.ApplyUiCulture(
            cultureName,
            name => AppLanguageCatalog.ResolveCulture(name, StartupUiCulture),
            StartupUiCulture);

    public static void ApplyCurrentCultureToWpf() =>
        WpfLocalizationCultureBootstrap.ApplyCurrentCultureToWpf();
}

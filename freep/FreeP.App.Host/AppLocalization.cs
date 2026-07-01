using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using FreeP.App.Localization;

namespace FreeP.App.Host;

internal static class AppLocalization
{
    private static readonly CultureInfo StartupUiCulture = CultureInfo.CurrentUICulture;
    private static int _wpfLanguageMetadataApplied;

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
    {
        var uiCulture = AppLanguageCatalog.ResolveCulture(cultureName, StartupUiCulture);

        CultureInfo.DefaultThreadCurrentUICulture = uiCulture;
        Thread.CurrentThread.CurrentUICulture = uiCulture;
    }

    public static void ApplyCurrentCultureToWpf()
    {
        if (Interlocked.Exchange(ref _wpfLanguageMetadataApplied, 1) == 1)
            return;

        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));
    }
}

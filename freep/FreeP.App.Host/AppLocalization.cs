using FreeP.App.Localization;
using Free.Shared.Shell;

namespace FreeP.App.Host;

internal static class AppLocalization
{
    public static readonly WpfAppLocalizationBootstrap Bootstrap = new(
        UiText.Get,
        UiText.Format,
        AppLanguageCatalog.ResolveCulture);
}

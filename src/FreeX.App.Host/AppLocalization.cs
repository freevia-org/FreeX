using Free.Shared.Shell;
using FreeX.App.Localization;

namespace FreeX.App.Host;

internal static class AppLocalization
{
    public static readonly WpfAppLocalizationBootstrap Bootstrap = new(
        UiText.Get,
        UiText.Format,
        AppLanguageCatalog.ResolveCulture);
}

using System.Globalization;
using FreeX.App.Localization;
using PortableAppLanguageCatalog = FreeX.App.Localization.AppLanguageCatalog;

namespace FreeX.App.Host;

internal static class AppLanguageCatalog
{
    public const string SystemDefaultCultureName = PortableAppLanguageCatalog.SystemDefaultCultureName;
    public const string EnglishUnitedStatesCultureName = PortableAppLanguageCatalog.EnglishUnitedStatesCultureName;
    public const string PseudoLocalizationCultureName = PortableAppLanguageCatalog.PseudoLocalizationCultureName;

    public static IReadOnlyList<AppLanguageOption> GetAvailableLanguages() =>
        PortableAppLanguageCatalog.GetAvailableLanguages();

    internal static IReadOnlyList<AppLanguageOption> CreateOptions(IEnumerable<string> satelliteCultureNames) =>
        PortableAppLanguageCatalog.CreateOptions(satelliteCultureNames);

    public static string NormalizeCultureName(string? cultureName) =>
        PortableAppLanguageCatalog.NormalizeCultureName(cultureName);

    internal static CultureInfo ResolveCulture(string? cultureName, CultureInfo fallbackCulture)
        => PortableAppLanguageCatalog.ResolveCulture(cultureName, fallbackCulture);

    internal static bool IsPseudoLocalizationCulture(string? cultureName) =>
        PortableAppLanguageCatalog.IsPseudoLocalizationCulture(cultureName);
}

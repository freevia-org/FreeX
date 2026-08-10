using System.Globalization;

namespace Free.Shared.Localization;

public abstract class LocalizedAppLanguageCatalog<TCatalog>
    where TCatalog : class
{
    public const string SystemDefaultCultureName = AppLanguageCatalogCore.SystemDefaultCultureName;
    public const string EnglishUnitedStatesCultureName = AppLanguageCatalogCore.EnglishUnitedStatesCultureName;
    public const string PseudoLocalizationCultureName = AppLanguageCatalogCore.PseudoLocalizationCultureName;

    protected LocalizedAppLanguageCatalog()
    {
    }

    public static IReadOnlyList<AppLanguageOption> GetAvailableLanguages() =>
        GetAvailableLanguages(AppContext.BaseDirectory);

    public static IReadOnlyList<AppLanguageOption> GetAvailableLanguages(string resourceProbeDirectory) =>
        AppLanguageCatalogCore.GetAvailableLanguages(
            resourceProbeDirectory,
            LocalizedResourceCatalog<TCatalog>.LanguageDefinition,
            static (cultureName, displayName) => new AppLanguageOption(cultureName, displayName));

    public static IReadOnlyList<AppLanguageOption> CreateOptions(IEnumerable<string> satelliteCultureNames) =>
        AppLanguageCatalogCore.CreateOptions(
            satelliteCultureNames,
            LocalizedResourceCatalog<TCatalog>.LanguageDefinition,
            static (cultureName, displayName) => new AppLanguageOption(cultureName, displayName));

    public static string NormalizeCultureName(string? cultureName) =>
        AppLanguageCatalogCore.NormalizeCultureName(cultureName);

    public static CultureInfo ResolveCulture(string? cultureName, CultureInfo fallbackCulture) =>
        AppLanguageCatalogCore.ResolveCulture(cultureName, fallbackCulture);

    public static bool IsPseudoLocalizationCulture(string? cultureName) =>
        AppLanguageCatalogCore.IsPseudoLocalizationCulture(cultureName);
}

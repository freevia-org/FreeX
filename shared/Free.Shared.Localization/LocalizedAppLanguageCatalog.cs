using System.Globalization;

namespace Free.Shared.Localization;

public abstract class LocalizedAppLanguageCatalog<TOption, TCatalog>
    where TCatalog : class
{
    public const string SystemDefaultCultureName = AppLanguageCatalogCore.SystemDefaultCultureName;
    public const string EnglishUnitedStatesCultureName = AppLanguageCatalogCore.EnglishUnitedStatesCultureName;
    public const string PseudoLocalizationCultureName = AppLanguageCatalogCore.PseudoLocalizationCultureName;

    private static readonly Func<string, string, TOption> CreateOption = CreateOptionFactory();

    protected LocalizedAppLanguageCatalog()
    {
    }

    public static IReadOnlyList<TOption> GetAvailableLanguages() =>
        GetAvailableLanguages(AppContext.BaseDirectory);

    public static IReadOnlyList<TOption> GetAvailableLanguages(string resourceProbeDirectory) =>
        AppLanguageCatalogCore.GetAvailableLanguages(
            resourceProbeDirectory,
            LocalizedResourceCatalog<TCatalog>.LanguageDefinition,
            CreateOption);

    public static IReadOnlyList<TOption> CreateOptions(IEnumerable<string> satelliteCultureNames) =>
        AppLanguageCatalogCore.CreateOptions(
            satelliteCultureNames,
            LocalizedResourceCatalog<TCatalog>.LanguageDefinition,
            CreateOption);

    public static string NormalizeCultureName(string? cultureName) =>
        AppLanguageCatalogCore.NormalizeCultureName(cultureName);

    public static CultureInfo ResolveCulture(string? cultureName, CultureInfo fallbackCulture) =>
        AppLanguageCatalogCore.ResolveCulture(cultureName, fallbackCulture);

    public static bool IsPseudoLocalizationCulture(string? cultureName) =>
        AppLanguageCatalogCore.IsPseudoLocalizationCulture(cultureName);

    private static Func<string, string, TOption> CreateOptionFactory()
    {
        var constructor = typeof(TOption).GetConstructor([typeof(string), typeof(string)]);
        if (constructor is null)
        {
            throw new InvalidOperationException(
                $"App language option type '{typeof(TOption).FullName}' must expose a public constructor with culture and display-name strings.");
        }

        return (cultureName, displayName) => (TOption)constructor.Invoke([cultureName, displayName]);
    }
}

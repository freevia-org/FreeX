using System.Globalization;
using Free.Shared.Localization;

namespace FreeX.App.Localization;

public sealed record AppLanguageOption(string CultureName, string DisplayName);

public static class AppLanguageCatalog
{
    public const string SystemDefaultCultureName = "";
    public const string EnglishUnitedStatesCultureName = "en-US";
    public const string PseudoLocalizationCultureName = Loc.PseudoLocalizationCultureName;

    private const string SatelliteAssemblyName = "FreeX.App.Localization.resources.dll";
    private static readonly AppLanguageCatalogDefinition Definition = new(SatelliteAssemblyName, Loc.Get, Loc.GetNeutral);

    public static IReadOnlyList<AppLanguageOption> GetAvailableLanguages() =>
        GetAvailableLanguages(AppContext.BaseDirectory);

    public static IReadOnlyList<AppLanguageOption> GetAvailableLanguages(string resourceProbeDirectory) =>
        AppLanguageCatalogCore.GetAvailableLanguages(resourceProbeDirectory, Definition, CreateOption);

    public static IReadOnlyList<AppLanguageOption> CreateOptions(IEnumerable<string> satelliteCultureNames) =>
        AppLanguageCatalogCore.CreateOptions(satelliteCultureNames, Definition, CreateOption);

    public static string NormalizeCultureName(string? cultureName) =>
        AppLanguageCatalogCore.NormalizeCultureName(cultureName);

    public static CultureInfo ResolveCulture(string? cultureName, CultureInfo fallbackCulture) =>
        AppLanguageCatalogCore.ResolveCulture(cultureName, fallbackCulture);

    public static bool IsPseudoLocalizationCulture(string? cultureName) =>
        AppLanguageCatalogCore.IsPseudoLocalizationCulture(cultureName);

    private static AppLanguageOption CreateOption(string cultureName, string displayName) =>
        new(cultureName, displayName);
}

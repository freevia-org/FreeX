using System.Globalization;

namespace Free.Shared.Localization;

public sealed class AppLanguageCatalogDefinition
{
    public AppLanguageCatalogDefinition(
        string satelliteAssemblyName,
        string sharedSatelliteAssemblyName,
        Func<string, string> getText,
        Func<string, string> getNeutralText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(satelliteAssemblyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sharedSatelliteAssemblyName);
        ArgumentNullException.ThrowIfNull(getText);
        ArgumentNullException.ThrowIfNull(getNeutralText);

        SatelliteAssemblyName = satelliteAssemblyName;
        SharedSatelliteAssemblyName = sharedSatelliteAssemblyName;
        GetText = getText;
        GetNeutralText = getNeutralText;
    }

    public string SatelliteAssemblyName { get; }

    public string SharedSatelliteAssemblyName { get; }

    public Func<string, string> GetText { get; }

    public Func<string, string> GetNeutralText { get; }
}

public static class AppLanguageCatalogCore
{
    public const string SystemDefaultCultureName = "";
    public const string EnglishUnitedStatesCultureName = "en-US";
    public const string PseudoLocalizationCultureName = LocalizedTextCatalog.PseudoLocalizationCultureName;

    public static IReadOnlyList<TOption> GetAvailableLanguages<TOption>(
        string resourceProbeDirectory,
        AppLanguageCatalogDefinition definition,
        Func<string, string, TOption> createOption) =>
        CreateOptions(
            EnumerateSatelliteCultureNames(resourceProbeDirectory, definition),
            definition,
            createOption);

    public static IReadOnlyList<TOption> CreateOptions<TOption>(
        IEnumerable<string> satelliteCultureNames,
        AppLanguageCatalogDefinition definition,
        Func<string, string, TOption> createOption)
    {
        ArgumentNullException.ThrowIfNull(satelliteCultureNames);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(createOption);

        var seenCultureNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            EnglishUnitedStatesCultureName,
            PseudoLocalizationCultureName
        };

        var options = new List<TOption>
        {
            createOption(SystemDefaultCultureName, definition.GetText("Options_AppLanguageSystemDefault")),
            createOption(EnglishUnitedStatesCultureName, definition.GetText("Options_AppLanguageEnglishUnitedStates")),
            createOption(
                PseudoLocalizationCultureName,
                PseudoLocalization.Expand(definition.GetNeutralText("Options_AppLanguageEnglishUnitedStates")))
        };

        var satelliteOptions = satelliteCultureNames
            .Select(NormalizeCultureName)
            .Where(cultureName => !string.IsNullOrWhiteSpace(cultureName))
            .Where(seenCultureNames.Add)
            .Select(cultureName => CultureInfo.GetCultureInfo(cultureName))
            .OrderBy(culture => culture.NativeName, StringComparer.CurrentCultureIgnoreCase)
            .Select(culture => createOption(culture.Name, culture.NativeName));

        options.AddRange(satelliteOptions);
        return options;
    }

    public static string NormalizeCultureName(string? cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName))
            return SystemDefaultCultureName;

        try
        {
            var trimmedCultureName = cultureName.Trim();
            if (LocalizedTextCatalog.IsPseudoLocalizationCulture(trimmedCultureName))
                return PseudoLocalizationCultureName;

            var culture = CultureInfo.GetCultureInfo(trimmedCultureName);
            return string.Equals(trimmedCultureName, culture.Name, StringComparison.OrdinalIgnoreCase)
                ? culture.Name
                : SystemDefaultCultureName;
        }
        catch (CultureNotFoundException)
        {
            return SystemDefaultCultureName;
        }
    }

    public static CultureInfo ResolveCulture(string? cultureName, CultureInfo fallbackCulture)
    {
        ArgumentNullException.ThrowIfNull(fallbackCulture);

        var normalizedCultureName = NormalizeCultureName(cultureName);
        return string.IsNullOrEmpty(normalizedCultureName)
            ? fallbackCulture
            : CultureInfo.GetCultureInfo(normalizedCultureName);
    }

    public static bool IsPseudoLocalizationCulture(string? cultureName) =>
        LocalizedTextCatalog.IsPseudoLocalizationCulture(NormalizeCultureName(cultureName));

    private static IEnumerable<string> EnumerateSatelliteCultureNames(
        string baseDirectory,
        AppLanguageCatalogDefinition definition)
    {
        var appCultures = SatelliteCultureCatalog.GetPackagedCultureNames(
            baseDirectory,
            definition.SatelliteAssemblyName);
        var sharedCultures = SatelliteCultureCatalog.GetPackagedCultureNames(
            baseDirectory,
            definition.SharedSatelliteAssemblyName);

        return appCultures.Intersect(sharedCultures, StringComparer.OrdinalIgnoreCase);
    }
}

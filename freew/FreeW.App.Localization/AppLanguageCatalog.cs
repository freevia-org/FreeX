using System.Globalization;
using Free.Shared.Localization;

namespace FreeW.App.Localization;

public sealed record AppLanguageOption(string CultureName, string DisplayName);

public static class AppLanguageCatalog
{
    public const string SystemDefaultCultureName = "";
    public const string EnglishUnitedStatesCultureName = "en-US";
    public const string PseudoLocalizationCultureName = Loc.PseudoLocalizationCultureName;

    private const string SatelliteAssemblyName = "FreeW.App.Localization.resources.dll";

    public static IReadOnlyList<AppLanguageOption> GetAvailableLanguages() =>
        GetAvailableLanguages(AppContext.BaseDirectory);

    public static IReadOnlyList<AppLanguageOption> GetAvailableLanguages(string resourceProbeDirectory) =>
        CreateOptions(EnumerateSatelliteCultureNames(resourceProbeDirectory));

    public static IReadOnlyList<AppLanguageOption> CreateOptions(IEnumerable<string> satelliteCultureNames)
    {
        var seenCultureNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            EnglishUnitedStatesCultureName,
            PseudoLocalizationCultureName
        };

        var options = new List<AppLanguageOption>
        {
            new(SystemDefaultCultureName, Loc.Get("Options_AppLanguageSystemDefault")),
            new(EnglishUnitedStatesCultureName, Loc.Get("Options_AppLanguageEnglishUnitedStates")),
            new(PseudoLocalizationCultureName, PseudoLocalization.Expand(Loc.GetNeutral("Options_AppLanguageEnglishUnitedStates")))
        };

        var satelliteOptions = satelliteCultureNames
            .Select(NormalizeCultureName)
            .Where(cultureName => !string.IsNullOrWhiteSpace(cultureName))
            .Where(seenCultureNames.Add)
            .Select(cultureName => CultureInfo.GetCultureInfo(cultureName))
            .Select(culture => new AppLanguageOption(culture.Name, culture.NativeName))
            .OrderBy(option => option.DisplayName, StringComparer.CurrentCultureIgnoreCase);

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
            if (Loc.IsPseudoLocalizationCulture(trimmedCultureName))
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
        var normalizedCultureName = NormalizeCultureName(cultureName);
        return string.IsNullOrEmpty(normalizedCultureName)
            ? fallbackCulture
            : CultureInfo.GetCultureInfo(normalizedCultureName);
    }

    public static bool IsPseudoLocalizationCulture(string? cultureName) =>
        Loc.IsPseudoLocalizationCulture(NormalizeCultureName(cultureName));

    private static IEnumerable<string> EnumerateSatelliteCultureNames(string baseDirectory)
    {
        if (!Directory.Exists(baseDirectory))
            return [];

        try
        {
            return Directory
                .EnumerateDirectories(baseDirectory)
                .Where(directory => File.Exists(Path.Combine(directory, SatelliteAssemblyName)))
                .Select(Path.GetFileName)
                .Where(cultureName => !string.IsNullOrWhiteSpace(cultureName))
                .Select(cultureName => cultureName!)
                .ToArray();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }
}

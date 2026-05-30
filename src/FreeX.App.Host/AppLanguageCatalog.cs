using System.Globalization;
using System.IO;

namespace FreeX.App.Host;

internal sealed record AppLanguageOption(string CultureName, string DisplayName);

internal static class AppLanguageCatalog
{
    public const string SystemDefaultCultureName = "";
    public const string EnglishUnitedStatesCultureName = "en-US";

    private const string SatelliteAssemblyName = "FreeX.App.Host.resources.dll";

    public static IReadOnlyList<AppLanguageOption> GetAvailableLanguages() =>
        CreateOptions(EnumerateSatelliteCultureNames(GetResourceProbeDirectory()));

    internal static IReadOnlyList<AppLanguageOption> CreateOptions(IEnumerable<string> satelliteCultureNames)
    {
        var seenCultureNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            EnglishUnitedStatesCultureName
        };

        var options = new List<AppLanguageOption>
        {
            new(SystemDefaultCultureName, UiText.Get("Options_AppLanguageSystemDefault")),
            new(EnglishUnitedStatesCultureName, UiText.Get("Options_AppLanguageEnglishUnitedStates"))
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

    internal static CultureInfo ResolveCulture(string? cultureName, CultureInfo fallbackCulture)
    {
        var normalizedCultureName = NormalizeCultureName(cultureName);
        return string.IsNullOrEmpty(normalizedCultureName)
            ? fallbackCulture
            : CultureInfo.GetCultureInfo(normalizedCultureName);
    }

    private static string GetResourceProbeDirectory()
    {
        var assemblyLocation = typeof(UiText).Assembly.Location;
        return string.IsNullOrWhiteSpace(assemblyLocation)
            ? AppContext.BaseDirectory
            : Path.GetDirectoryName(assemblyLocation) ?? AppContext.BaseDirectory;
    }

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

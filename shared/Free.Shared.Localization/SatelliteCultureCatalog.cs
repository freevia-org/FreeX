using System.Globalization;

namespace Free.Shared.Localization;

internal static class SatelliteCultureCatalog
{
    public static IReadOnlySet<string> GetPackagedCultureNames(
        string baseDirectory,
        string satelliteAssemblyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(satelliteAssemblyName);

        var cultures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(baseDirectory))
            return cultures;

        try
        {
            foreach (var directory in Directory.EnumerateDirectories(baseDirectory))
            {
                var cultureName = Path.GetFileName(directory);
                if (string.IsNullOrWhiteSpace(cultureName) ||
                    !File.Exists(Path.Combine(directory, satelliteAssemblyName)))
                {
                    continue;
                }

                try
                {
                    cultures.Add(CultureInfo.GetCultureInfo(cultureName).Name);
                }
                catch (CultureNotFoundException)
                {
                    // A product output directory is not a culture just because it has a name.
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return cultures;
    }

    public static IReadOnlySet<string> GetSharedFallbackCultureNames(
        string appBaseDirectory,
        string appSatelliteAssemblyName,
        string sharedBaseDirectory,
        string sharedSatelliteAssemblyName)
    {
        var appCultures = new HashSet<string>(
            GetPackagedCultureNames(appBaseDirectory, appSatelliteAssemblyName),
            StringComparer.OrdinalIgnoreCase);
        var sharedCultures = GetPackagedCultureNames(sharedBaseDirectory, sharedSatelliteAssemblyName);
        appCultures.IntersectWith(sharedCultures);
        return appCultures;
    }
}

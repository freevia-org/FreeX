using System.Reflection;
using System.Resources;

namespace Free.Shared.Localization;

public sealed class LocalizedResourceFacade
{
    private readonly LocalizedTextCatalog _catalog;

    public LocalizedResourceFacade(
        string resourceBaseName,
        Assembly resourceAssembly,
        string sharedResourceBaseName = LocalizedResourceCatalogAttribute.DefaultSharedResourceBaseName,
        Assembly? sharedResourceAssembly = null,
        string satelliteAssemblyName = "",
        string sharedSatelliteAssemblyName = LocalizedResourceCatalogAttribute.DefaultSharedSatelliteAssemblyName)
        : this(
            new ResourceManager(resourceBaseName, resourceAssembly),
            new ResourceManager(
                sharedResourceBaseName,
                sharedResourceAssembly ?? typeof(LocalizedResourceFacade).Assembly),
            GetSharedFallbackCultureNames(
                resourceAssembly,
                satelliteAssemblyName,
                sharedResourceAssembly ?? typeof(LocalizedResourceFacade).Assembly,
                sharedSatelliteAssemblyName))
    {
    }

    public LocalizedResourceFacade(
        ResourceManager resourceManager,
        ResourceManager? sharedResourceManager = null,
        IReadOnlySet<string>? sharedSatelliteCultureNames = null)
    {
        _catalog = new LocalizedTextCatalog(
            resourceManager,
            sharedResourceManager,
            sharedSatelliteCultureNames);
    }

    public string Get(string key) => _catalog.Get(key);

    public string GetNeutral(string key) => _catalog.GetNeutral(key);

    public string Format(string key, params object?[] args) => _catalog.Format(key, args);

    public IReadOnlySet<string> GetNeutralResourceKeys() => _catalog.GetNeutralResourceKeys();

    public bool IsPseudoLocalizationCulture(string? cultureName) =>
        LocalizedTextCatalog.IsPseudoLocalizationCulture(cultureName);

    public string CreateAutomationName(string textWithAccessKey) =>
        LocalizedTextCatalog.CreateAutomationName(textWithAccessKey);

    public string CreateMissingText(string key) => LocalizedTextCatalog.CreateMissingText(key);

    private static IReadOnlySet<string> GetSharedFallbackCultureNames(
        Assembly resourceAssembly,
        string satelliteAssemblyName,
        Assembly sharedResourceAssembly,
        string sharedSatelliteAssemblyName)
    {
        satelliteAssemblyName = string.IsNullOrWhiteSpace(satelliteAssemblyName)
            ? resourceAssembly.GetName().Name + ".resources.dll"
            : satelliteAssemblyName;

        return SatelliteCultureCatalog.GetSharedFallbackCultureNames(
            GetAssemblyDirectory(resourceAssembly),
            satelliteAssemblyName,
            GetAssemblyDirectory(sharedResourceAssembly),
            sharedSatelliteAssemblyName);
    }

    private static string GetAssemblyDirectory(Assembly assembly) =>
        Path.GetDirectoryName(assembly.Location) ?? AppContext.BaseDirectory;
}

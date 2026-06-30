namespace Free.Shared.Localization;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class LocalizedResourceCatalogAttribute : Attribute
{
    public LocalizedResourceCatalogAttribute(string resourceBaseName, string satelliteAssemblyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceBaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(satelliteAssemblyName);

        ResourceBaseName = resourceBaseName;
        SatelliteAssemblyName = satelliteAssemblyName;
    }

    public string ResourceBaseName { get; }

    public string SatelliteAssemblyName { get; }
}

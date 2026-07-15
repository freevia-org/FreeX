namespace Free.Shared.Localization;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class LocalizedResourceCatalogAttribute : Attribute
{
    public const string DefaultSharedResourceBaseName =
        "Free.Shared.Localization.Resources.Strings";
    public const string DefaultSharedSatelliteAssemblyName =
        "Free.Shared.Localization.resources.dll";

    public LocalizedResourceCatalogAttribute(
        string resourceBaseName,
        string satelliteAssemblyName,
        string sharedResourceBaseName = DefaultSharedResourceBaseName,
        string sharedSatelliteAssemblyName = DefaultSharedSatelliteAssemblyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceBaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(satelliteAssemblyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sharedResourceBaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sharedSatelliteAssemblyName);

        ResourceBaseName = resourceBaseName;
        SatelliteAssemblyName = satelliteAssemblyName;
        SharedResourceBaseName = sharedResourceBaseName;
        SharedSatelliteAssemblyName = sharedSatelliteAssemblyName;
    }

    public string ResourceBaseName { get; }

    public string SatelliteAssemblyName { get; }

    public string SharedResourceBaseName { get; }

    public string SharedSatelliteAssemblyName { get; }
}

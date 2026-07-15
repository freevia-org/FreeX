namespace Free.Shared.Localization;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class LocalizedResourceCatalogAttribute : Attribute
{
    public const string DefaultSharedResourceBaseName =
        "Free.Shared.Localization.Resources.Strings";

    public LocalizedResourceCatalogAttribute(
        string resourceBaseName,
        string satelliteAssemblyName,
        string sharedResourceBaseName = DefaultSharedResourceBaseName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceBaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(satelliteAssemblyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sharedResourceBaseName);

        ResourceBaseName = resourceBaseName;
        SatelliteAssemblyName = satelliteAssemblyName;
        SharedResourceBaseName = sharedResourceBaseName;
    }

    public string ResourceBaseName { get; }

    public string SatelliteAssemblyName { get; }

    public string SharedResourceBaseName { get; }
}

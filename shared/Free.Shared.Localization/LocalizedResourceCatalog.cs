using System.Reflection;

namespace Free.Shared.Localization;

public abstract class LocalizedResourceCatalog<TCatalog>
    where TCatalog : class
{
    public const string PseudoLocalizationCultureName = LocalizedTextCatalog.PseudoLocalizationCultureName;

    private static readonly LocalizedResourceCatalogAttribute CatalogDefinition = GetCatalogDefinition();

    internal static LocalizedResourceFacade Resources { get; } =
        new(
            CatalogDefinition.ResourceBaseName,
            typeof(TCatalog).Assembly,
            CatalogDefinition.SharedResourceBaseName,
            typeof(LocalizedResourceFacade).Assembly,
            CatalogDefinition.SatelliteAssemblyName,
            CatalogDefinition.SharedSatelliteAssemblyName);

    internal static AppLanguageCatalogDefinition LanguageDefinition { get; } =
        new(
            CatalogDefinition.SatelliteAssemblyName,
            CatalogDefinition.SharedSatelliteAssemblyName,
            Get,
            GetNeutral);

    protected LocalizedResourceCatalog()
    {
    }

    public static string Get(string key) => Resources.Get(key);

    public static string GetNeutral(string key) => Resources.GetNeutral(key);

    public static string Format(string key, params object?[] args) => Resources.Format(key, args);

    public static IReadOnlySet<string> GetNeutralResourceKeys() => Resources.GetNeutralResourceKeys();

    public static bool IsPseudoLocalizationCulture(string? cultureName) =>
        Resources.IsPseudoLocalizationCulture(cultureName);

    public static string CreateAutomationName(string textWithAccessKey) =>
        Resources.CreateAutomationName(textWithAccessKey);

    public static string CreateMissingText(string key) => Resources.CreateMissingText(key);

    private static LocalizedResourceCatalogAttribute GetCatalogDefinition()
    {
        var catalogType = typeof(TCatalog);
        var explicitDefinition = catalogType.GetCustomAttribute<LocalizedResourceCatalogAttribute>();
        if (explicitDefinition is not null)
            return explicitDefinition;

        var catalogNamespace = catalogType.Namespace;
        var assemblyName = catalogType.Assembly.GetName().Name;
        if (string.IsNullOrWhiteSpace(catalogNamespace) || string.IsNullOrWhiteSpace(assemblyName))
        {
            throw new InvalidOperationException(
                $"Localization catalog type '{catalogType.FullName}' must have a namespace and assembly name or declare {nameof(LocalizedResourceCatalogAttribute)}.");
        }

        return new LocalizedResourceCatalogAttribute(
            $"{catalogNamespace}.Resources.Strings",
            $"{assemblyName}.resources.dll");
    }
}

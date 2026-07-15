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
        Assembly? sharedResourceAssembly = null)
        : this(
            new ResourceManager(resourceBaseName, resourceAssembly),
            new ResourceManager(
                sharedResourceBaseName,
                sharedResourceAssembly ?? typeof(LocalizedResourceFacade).Assembly))
    {
    }

    public LocalizedResourceFacade(
        ResourceManager resourceManager,
        ResourceManager? sharedResourceManager = null)
    {
        _catalog = new LocalizedTextCatalog(resourceManager, sharedResourceManager);
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
}

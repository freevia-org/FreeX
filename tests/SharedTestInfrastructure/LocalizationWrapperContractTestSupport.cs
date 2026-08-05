using System.Reflection;
using System.Resources;
using FluentAssertions;
using Free.Shared.Localization;
internal static class LocalizationWrapperContractTestSupport
{
    public static void AssertAppWrappers<TLoc, TUiText, TLanguageOption, TLanguageCatalog>(
        string[] localizationProjectParts)
        where TLoc : class
    {
        typeof(TLoc).BaseType.Should().Be(typeof(LocalizedResourceCatalog<TLoc>));
        typeof(TUiText).BaseType.Should().Be(typeof(LocalizedUiTextCatalog<TLoc>));
        typeof(TLanguageCatalog).BaseType.Should().Be(typeof(LocalizedAppLanguageCatalog<TLanguageOption, TLoc>));

        var definition = typeof(TLoc).GetCustomAttribute<LocalizedResourceCatalogAttribute>();
        definition.Should().BeNull("app catalogs follow the shared namespace and assembly convention");

        var resourceBaseName = $"{typeof(TLoc).Namespace}.Resources.Strings";
        using var neutralResources = new ResourceManager(resourceBaseName, typeof(TLoc).Assembly)
            .GetResourceSet(System.Globalization.CultureInfo.InvariantCulture, true, false);
        neutralResources.Should().NotBeNull("the convention-derived resource base name must resolve");

        string Read(string fileName) => TestWorkspaceFileLocator.ReadAllText([.. localizationProjectParts, fileName]);
        var loc = Read("Loc.cs");
        var uiText = Read("LocalizedUiText.cs");
        var languageCatalog = Read("AppLanguageCatalog.cs");
        loc.Should().Contain("LocalizedResourceCatalog<Loc>")
            .And.NotContain("[LocalizedResourceCatalog(")
            .And.NotContain(resourceBaseName)
            .And.NotContain($"{typeof(TLoc).Assembly.GetName().Name}.resources.dll")
            .And.NotContain("LocalizedResourceFacade").And.NotContain("public static string Get(").And.NotContain("public static string Format(");

        uiText.Should().Contain("LocalizedUiTextCatalog<Loc>").And.NotContain("LocalizedUiTextFacade")
            .And.NotContain("public static string Ok").And.NotContain("public static string Get(");

        languageCatalog.Should().Contain("LocalizedAppLanguageCatalog<AppLanguageOption, Loc>")
            .And.NotContain("AppLanguageCatalogDefinition").And.NotContain("CreateOption").And.NotContain("GetAvailableLanguages(");
    }
}

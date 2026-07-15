using System.Reflection;
using FluentAssertions;
using Free.Shared.Localization;
internal static class LocalizationWrapperContractTestSupport
{
    public static void AssertAppWrappers<TLoc, TUiText, TLanguageOption, TLanguageCatalog>(string[] localizationProjectParts, string resourceBaseName, string satelliteAssemblyName)
        where TLoc : class
    {
        typeof(TLoc).BaseType.Should().Be(typeof(LocalizedResourceCatalog<TLoc>));
        typeof(TUiText).BaseType.Should().Be(typeof(LocalizedUiTextCatalog<TLoc>));
        typeof(TLanguageCatalog).BaseType.Should().Be(typeof(LocalizedAppLanguageCatalog<TLanguageOption, TLoc>));
        var definition = typeof(TLoc).GetCustomAttribute<LocalizedResourceCatalogAttribute>();
        definition.Should().NotBeNull();
        definition!.ResourceBaseName.Should().Be(resourceBaseName);
        definition.SatelliteAssemblyName.Should().Be(satelliteAssemblyName);
        definition.SharedResourceBaseName.Should().Be(LocalizedResourceCatalogAttribute.DefaultSharedResourceBaseName);
        definition.SharedSatelliteAssemblyName.Should().Be(LocalizedResourceCatalogAttribute.DefaultSharedSatelliteAssemblyName);
        string Read(string fileName) => TestWorkspaceFileLocator.ReadAllText([.. localizationProjectParts, fileName]);
        var loc = Read("Loc.cs");
        var uiText = Read("LocalizedUiText.cs");
        var languageCatalog = Read("AppLanguageCatalog.cs");
        loc.Should().Contain(resourceBaseName).And.Contain(satelliteAssemblyName).And.Contain("LocalizedResourceCatalog<Loc>")
            .And.NotContain("LocalizedResourceFacade").And.NotContain("public static string Get(").And.NotContain("public static string Format(");

        uiText.Should().Contain("LocalizedUiTextCatalog<Loc>").And.NotContain("LocalizedUiTextFacade")
            .And.NotContain("public static string Ok").And.NotContain("public static string Get(");

        languageCatalog.Should().Contain("LocalizedAppLanguageCatalog<AppLanguageOption, Loc>")
            .And.NotContain("AppLanguageCatalogDefinition").And.NotContain("CreateOption").And.NotContain("GetAvailableLanguages(");
    }
}

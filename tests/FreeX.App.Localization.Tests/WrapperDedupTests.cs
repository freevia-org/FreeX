using FluentAssertions;
using Free.Shared.Localization;
using Xunit;

namespace FreeX.App.Localization.Tests;

public sealed class WrapperDedupTests
{
    [Fact]
    public void AppWrappers_InheritSharedGenericBehavior()
    {
        typeof(Loc).BaseType.Should().Be(typeof(LocalizedResourceCatalog<Loc>));
        typeof(LocalizedUiText).BaseType.Should().Be(typeof(LocalizedUiTextCatalog<Loc>));
        typeof(AppLanguageCatalog).BaseType.Should().Be(typeof(LocalizedAppLanguageCatalog<AppLanguageOption, Loc>));
    }

    [Fact]
    public void AppWrappers_KeepOnlyAppSpecificMetadata()
    {
        var loc = TestWorkspaceFileLocator.ReadAllText("src", "FreeX.App.Localization", "Loc.cs");
        var uiText = TestWorkspaceFileLocator.ReadAllText("src", "FreeX.App.Localization", "LocalizedUiText.cs");
        var languageCatalog = TestWorkspaceFileLocator.ReadAllText("src", "FreeX.App.Localization", "AppLanguageCatalog.cs");

        loc.Should().Contain("FreeX.App.Localization.Resources.Strings");
        loc.Should().Contain("FreeX.App.Localization.resources.dll");
        loc.Should().Contain("LocalizedResourceCatalog<Loc>");
        loc.Should().NotContain("LocalizedResourceFacade");
        loc.Should().NotContain("public static string Get(");
        loc.Should().NotContain("public static string Format(");

        uiText.Should().Contain("LocalizedUiTextCatalog<Loc>");
        uiText.Should().NotContain("LocalizedUiTextFacade");
        uiText.Should().NotContain("public static string Ok");
        uiText.Should().NotContain("public static string Get(");

        languageCatalog.Should().Contain("LocalizedAppLanguageCatalog<AppLanguageOption, Loc>");
        languageCatalog.Should().NotContain("AppLanguageCatalogDefinition");
        languageCatalog.Should().NotContain("CreateOption");
        languageCatalog.Should().NotContain("GetAvailableLanguages(");
    }
}

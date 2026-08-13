using FreeX.App.Localization;
using FluentAssertions;
using Xunit;

namespace FreeX.App.Localization.Tests;

public sealed class WrapperDedupTests
{
    [Fact]
    public void AppWrappers_UseSharedContractsAndConventionOwnedResourceMetadata() =>
        LocalizationWrapperContractTestSupport.AssertAppWrappers<
            Loc,
            LocalizedUiText,
            AppLanguageCatalog>(
            ["src", "FreeX.App.Localization"],
            ["src", "FreeX.App.Host", "FreeX.App.Host.csproj"],
            ["src", "FreeX.App.Avalonia", "FreeX.App.Avalonia.csproj"]);

    [Fact]
    public void SharedUiTextCatalog_UsesResourceCatalogDirectly()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var sharedLocalization = Path.Combine(root, "shared", "Free.Shared.Localization");
        var source = File.ReadAllText(Path.Combine(sharedLocalization, "LocalizedUiTextCatalog.cs"));

        File.Exists(Path.Combine(sharedLocalization, "LocalizedUiTextFacade.cs")).Should().BeFalse();
        source.Should().Contain("LocalizedResourceCatalog<TCatalog>.Get(key)")
            .And.Contain("LocalizedResourceCatalog<TCatalog>.Format(key, args)")
            .And.NotContain("LocalizedUiTextFacade")
            .And.NotContain("Facade.");
    }
}

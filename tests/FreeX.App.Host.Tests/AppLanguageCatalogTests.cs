using FluentAssertions;
using FreeX.App.Localization;
using PortableAppLanguageCatalog = FreeX.App.Localization.AppLanguageCatalog;

namespace FreeX.App.Host.Tests;

public sealed class AppLanguageCatalogTests
{
    [Fact]
    public void HostFacade_IsRemovedInFavorOfPortableCatalog()
    {
        File.Exists(Path.Combine(
            WorkspaceFileLocator.FindWorkspaceRoot(),
            "src",
            "FreeX.App.Host",
            "AppLanguageCatalog.cs")).Should().BeFalse();
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("  en-us  ", "en-US")]
    [InlineData(" QPS-PLOC ", "qps-ploc")]
    [InlineData("uk-UA", "uk-UA")]
    [InlineData("not-a-culture", "")]
    public void NormalizeCultureName_ReturnsCanonicalSupportedCultureOrSystemDefault(
        string? input,
        string expected)
    {
        AppLocalizationContractTestSupport.AssertNormalizedCultureName(
            PortableAppLanguageCatalog.NormalizeCultureName,
            input,
            expected);
    }
}

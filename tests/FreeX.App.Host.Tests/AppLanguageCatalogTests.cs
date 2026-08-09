using FluentAssertions;
using FreeX.App.Host;
using FreeX.App.Localization;
using HostAppLanguageCatalog = FreeX.App.Host.AppLanguageCatalog;
using PortableAppLanguageCatalog = FreeX.App.Localization.AppLanguageCatalog;

namespace FreeX.App.Host.Tests;

public sealed class AppLanguageCatalogTests
{
    [Fact]
    public void CreateOptions_ForwardsToPortableCatalog()
    {
        var cultureNames = new[]
        {
            "uk-UA",
            "en-US",
            "not-a-culture",
            "fr-FR"
        };

        HostAppLanguageCatalog.CreateOptions(cultureNames)
            .Should()
            .Equal(PortableAppLanguageCatalog.CreateOptions(cultureNames));
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
            HostAppLanguageCatalog.NormalizeCultureName,
            input,
            expected);
    }
}

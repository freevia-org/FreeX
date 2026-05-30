using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed class AppLanguageCatalogTests
{
    [Fact]
    public void CreateOptions_IncludesSystemEnglishAndSatelliteCultures()
    {
        var options = AppLanguageCatalog.CreateOptions([
            "uk-UA",
            "en-US",
            "not-a-culture",
            "fr-FR"
        ]);

        options[0].Should().Be(new AppLanguageOption(
            AppLanguageCatalog.SystemDefaultCultureName,
            UiText.Get("Options_AppLanguageSystemDefault")));
        options[1].Should().Be(new AppLanguageOption(
            AppLanguageCatalog.EnglishUnitedStatesCultureName,
            UiText.Get("Options_AppLanguageEnglishUnitedStates")));
        options.Select(option => option.CultureName)
            .Should()
            .Contain(["uk-UA", "fr-FR"]);
        options.Select(option => option.CultureName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Should()
            .HaveCount(options.Count);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("  en-us  ", "en-US")]
    [InlineData("uk-UA", "uk-UA")]
    [InlineData("not-a-culture", "")]
    public void NormalizeCultureName_ReturnsCanonicalSupportedCultureOrSystemDefault(
        string? input,
        string expected)
    {
        AppLanguageCatalog.NormalizeCultureName(input).Should().Be(expected);
    }
}

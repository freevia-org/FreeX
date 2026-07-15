using FluentAssertions;
using Free.Shared.Localization;
using Xunit;

namespace FreeW.App.Localization.Tests;

public sealed class AppLanguageCatalogTests
{
    [Fact]
    public void CreateOptions_IncludesSystemEnglishPseudoAndSatelliteCultures()
    {
        var options = AppLanguageCatalog.CreateOptions([
            "fr-FR",
            "en-US",
            "not-a-culture",
            "uk-UA"
        ]);

        options[0].Should().Be(new AppLanguageOption(
            AppLanguageCatalog.SystemDefaultCultureName,
            Loc.Get("Options_AppLanguageSystemDefault")));
        options[1].Should().Be(new AppLanguageOption(
            AppLanguageCatalog.EnglishUnitedStatesCultureName,
            Loc.Get("Options_AppLanguageEnglishUnitedStates")));
        options[2].Should().Be(new AppLanguageOption(
            AppLanguageCatalog.PseudoLocalizationCultureName,
            PseudoLocalization.Expand(Loc.GetNeutral("Options_AppLanguageEnglishUnitedStates"))));
        options.Select(option => option.CultureName)
            .Should()
            .Contain(["fr-FR", "uk-UA"]);
        options.Select(option => option.CultureName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Should()
            .HaveCount(options.Count);
    }

    [Fact]
    public void GetAvailableLanguages_DiscoversFreeWLocalizationSatelliteDirectories()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "FreeWLanguageCatalogTests", Guid.NewGuid().ToString("N"));
        try
        {
            var satelliteDirectory = Path.Combine(baseDirectory, "fr-FR");
            Directory.CreateDirectory(satelliteDirectory);
            File.WriteAllText(Path.Combine(satelliteDirectory, "FreeW.App.Localization.resources.dll"), "");
            File.WriteAllText(Path.Combine(satelliteDirectory, "Free.Shared.Localization.resources.dll"), "");
            var sharedOnlyDirectory = Path.Combine(baseDirectory, "uk-UA");
            Directory.CreateDirectory(sharedOnlyDirectory);
            File.WriteAllText(Path.Combine(sharedOnlyDirectory, "Free.Shared.Localization.resources.dll"), "");

            var options = AppLanguageCatalog.GetAvailableLanguages(baseDirectory);

            options.Select(option => option.CultureName)
                .Should()
                .Contain("fr-FR")
                .And
                .NotContain("uk-UA");
        }
        finally
        {
            if (Directory.Exists(baseDirectory))
                Directory.Delete(baseDirectory, recursive: true);
        }
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
        AppLanguageCatalog.NormalizeCultureName(input).Should().Be(expected);
    }
}

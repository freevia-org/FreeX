using Free.Shared.Localization;
using Xunit;

namespace FreeW.App.Localization.Tests;

public sealed class AppLanguageCatalogTests
{
    [Fact]
    public void CreateOptions_IncludesSystemEnglishPseudoAndSatelliteCultures() =>
        AppLocalizationContractTestSupport.AssertCreateOptions(
            AppLanguageCatalog.CreateOptions,
            option => option.CultureName,
            new AppLanguageOption(
                AppLanguageCatalog.SystemDefaultCultureName,
                Loc.Get("Options_AppLanguageSystemDefault")),
            new AppLanguageOption(
                AppLanguageCatalog.EnglishUnitedStatesCultureName,
                Loc.Get("Options_AppLanguageEnglishUnitedStates")),
            new AppLanguageOption(
                AppLanguageCatalog.PseudoLocalizationCultureName,
                PseudoLocalization.Expand(Loc.GetNeutral("Options_AppLanguageEnglishUnitedStates"))));

    [Fact]
    public void GetAvailableLanguages_DiscoversFreeWLocalizationSatelliteDirectories() =>
        AppLocalizationContractTestSupport.AssertAvailableLanguages(
            "FreeW",
            "FreeW.App.Localization.resources.dll",
            AppLanguageCatalog.GetAvailableLanguages,
            option => option.CultureName);

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
            AppLanguageCatalog.NormalizeCultureName,
            input,
            expected);
    }
}

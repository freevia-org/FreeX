using System.Globalization;
using System.IO;
using System.Resources;
using FluentAssertions;
using FreeX.App.Host;
using static FreeX.App.Host.Tests.LocalizationResourceTestSupport;

namespace FreeX.App.Host.Tests;

public sealed partial class EuLocalizationResourceTests
{
    private const string DataImportHelpTextResourceKey =
        "MainWindow_TooltipDescription_ImportDataFromALocalCSVFileDatabaseWebAndPowerQueryConnectorsAreExcluded";

    private static readonly string[] ExpectedOfficeSatelliteCultures =
    [
        "bg-BG",
        "cs-CZ",
        "da-DK",
        "de-DE",
        "de-AT",
        "de-CH",
        "el-GR",
        "en-AU",
        "en-CA",
        "en-GB",
        "en-IE",
        "en-NZ",
        "en-ZA",
        "es-AR",
        "es-CL",
        "es-CO",
        "es-ES",
        "es-MX",
        "et-EE",
        "fi-FI",
        "fr-CA",
        "fr-FR",
        "ga-IE",
        "hr-HR",
        "hu-HU",
        "it-IT",
        "lt-LT",
        "lv-LV",
        "mt-MT",
        "nb-NO",
        "nl-NL",
        "nl-BE",
        "pl-PL",
        "pt-BR",
        "pt-PT",
        "ro-RO",
        "sk-SK",
        "sl-SI",
        "sr-Cyrl-RS",
        "sr-Latn-RS",
        "sv-SE",
        "tr-TR",
        "uk-UA",
    ];

    private static readonly string[] EnglishVariantCultures =
    [
        "en-AU",
        "en-CA",
        "en-GB",
        "en-IE",
        "en-NZ",
        "en-ZA",
    ];

    private static readonly string[] PivotTableFieldHeaderAndPlusMinusKeys =
    [
        "MainWindow_Content_FieldHeaders",
        "MainWindow_Content_PlusMinusButtons",
        "MainWindow_TooltipDescription_ShowOrHideExpandCollapseButtonsForTheSelectedPivotTable",
        "MainWindow_TooltipDescription_ShowOrHideFieldCaptionsAndFilterDropDownsForTheSelectedPivotTable",
        "MainWindow_TooltipTitle_FieldHeaders",
        "MainWindow_TooltipTitle_PlusMinusButtons",
    ];

    public static IEnumerable<object[]> ExpectedEuOfficeSatelliteCultureData() =>
        ExpectedOfficeSatelliteCultures.Select(culture => new object[] { culture });

    public static IEnumerable<object[]> EnglishVariantCultureData() =>
        EnglishVariantCultures.Select(culture => new object[] { culture });

    [Fact]
    public void Resources_IncludeEveryEuOfficeSatelliteCulture()
    {
        var availableCultures = Directory
            .EnumerateFiles(ResourceDirectory, "Strings.*.resx", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileNameWithoutExtension)
            .Select(fileName => fileName!["Strings.".Length..])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        availableCultures.Should().Contain(ExpectedOfficeSatelliteCultures);
    }

    [Fact]
    public void AppLanguageCatalog_DiscoversEveryEuOfficeSatelliteCultureAfterBuild()
    {
        var availableCultures = AppLanguageCatalog.GetAvailableLanguages()
            .Select(option => option.CultureName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        availableCultures.Should().Contain(ExpectedOfficeSatelliteCultures);
    }

    [Theory]
    [MemberData(nameof(ExpectedEuOfficeSatelliteCultureData))]
    public void SatelliteResx_ContainsPivotTableFieldHeaderAndPlusMinusKeys(string cultureName)
    {
        var localized = ReadResxValues($"Strings.{cultureName}.resx");

        localized.Keys.Should().Contain(PivotTableFieldHeaderAndPlusMinusKeys);
        localized
            .Where(entry => PivotTableFieldHeaderAndPlusMinusKeys.Contains(entry.Key, StringComparer.Ordinal))
            .Should()
            .OnlyContain(entry => !string.IsNullOrWhiteSpace(entry.Value));
    }

    [Theory]
    [MemberData(nameof(ExpectedEuOfficeSatelliteCultureData))]
    public void SatelliteResx_MatchesNeutralKeysPlaceholdersAndAccessKeyCounts(string cultureName)
    {
        var neutral = ReadResxValues("Strings.resx");
        var localized = ReadResxValues($"Strings.{cultureName}.resx");

        localized.Keys.Should().BeEquivalentTo(neutral.Keys);

        var blankValues = localized
            .Where(entry => !string.IsNullOrEmpty(neutral[entry.Key]) && string.IsNullOrEmpty(entry.Value))
            .Select(entry => entry.Key)
            .ToArray();
        blankValues.Should().BeEmpty();

        var placeholderMismatches = neutral
            .Where(entry => !CompositePlaceholderTokens(entry.Value)
                .SetEquals(CompositePlaceholderTokens(localized[entry.Key])))
            .Select(entry => entry.Key)
            .ToArray();
        placeholderMismatches.Should().BeEmpty();

        var accessKeyMismatches = neutral
            .Where(entry => AccessKeyCount(entry.Value) != AccessKeyCount(localized[entry.Key]))
            .Select(entry => entry.Key)
            .ToArray();
        accessKeyMismatches.Should().BeEmpty();

        if (!EnglishVariantCultures.Contains(cultureName, StringComparer.OrdinalIgnoreCase))
        {
            var translatedValueCount = localized
                .Count(entry => !string.IsNullOrWhiteSpace(neutral[entry.Key])
                    && !string.Equals(entry.Value, neutral[entry.Key], StringComparison.Ordinal));

            translatedValueCount.Should().BeGreaterThan(1500);
        }
    }

    [Theory]
    [MemberData(nameof(EnglishVariantCultureData))]
    public void EnglishVariantSatelliteResx_DataImportHelpTextMatchesNeutralSupportedFormats(string cultureName)
    {
        var neutral = ReadResxValues("Strings.resx");
        var localized = ReadResxValues($"Strings.{cultureName}.resx");

        localized[DataImportHelpTextResourceKey].Should().Be(neutral[DataImportHelpTextResourceKey]);
        localized[DataImportHelpTextResourceKey]
            .Should()
            .ContainAll("local CSV file", "text/TSV/TAB", "SpreadsheetML XML", "Power Query connectors are excluded");
    }

    [Theory]
    [MemberData(nameof(ExpectedEuOfficeSatelliteCultureData))]
    public void SatelliteAssembly_ContainsFullResourceSetWithoutParentFallback(string cultureName)
    {
        var resourceManager = new ResourceManager("FreeX.App.Host.Resources.Strings", typeof(UiText).Assembly);
        var resourceSet = resourceManager.GetResourceSet(
            CultureInfo.GetCultureInfo(cultureName),
            createIfNotExists: true,
            tryParents: false);

        resourceSet.Should().NotBeNull();

        var localizedEntries = resourceSet!
            .Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!, StringComparer.Ordinal);
        var neutralKeys = UiText.GetNeutralResourceKeys();

        localizedEntries.Keys.Should().BeEquivalentTo(neutralKeys);
        localizedEntries.Should().OnlyContain(
            entry => !string.IsNullOrEmpty(entry.Value),
            "each EU satellite should be complete and not depend on parent fallback");
    }

}

using System.Globalization;
using System.IO;
using System.Resources;
using FluentAssertions;
using FreeX.App.Host;
using FreeX.App.Localization;
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
        var neutral = ReadEffectiveNeutralValues();
        var localized = ReadResxValues($"Strings.{cultureName}.resx");

        // The effective neutral catalog composes app-owned and shared generic resources;
        // satellites are translated subsets and may override keys owned by either catalog.
        // Verify no orphan satellite keys, no blank values, and consistent placeholder/access-key counts
        // for all keys actually present in the satellite resx.
        neutral.Keys.Should().Contain(localized.Keys,
            because: $"every '{cultureName}' satellite key must exist in the neutral catalog (no orphan keys)");

        var blankValues = localized
            .Where(entry => !string.IsNullOrEmpty(neutral[entry.Key]) && string.IsNullOrEmpty(entry.Value))
            .Select(entry => entry.Key)
            .ToArray();
        blankValues.Should().BeEmpty();

        var placeholderMismatches = localized.Keys
            .Where(key => !CompositePlaceholderTokens(neutral[key])
                .SetEquals(CompositePlaceholderTokens(localized[key])))
            .ToArray();
        placeholderMismatches.Should().BeEmpty();

        var accessKeyMismatches = localized.Keys
            .Where(key => AccessKeyCount(neutral[key]) != AccessKeyCount(localized[key]))
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
        // The Loc neutral is a superset; satellites are translated subsets (new superset keys
        // await translation). Verify: satellite loads, has no blank values for its own keys,
        // and its key set is a subset of neutral (no orphans).
        var resourceManager = new ResourceManager("FreeX.App.Localization.Resources.Strings", typeof(Loc).Assembly);
        var resourceSet = resourceManager.GetResourceSet(
            CultureInfo.GetCultureInfo(cultureName),
            createIfNotExists: true,
            tryParents: false);

        resourceSet.Should().NotBeNull();

        var localizedEntries = resourceSet!
            .Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!, StringComparer.Ordinal);
        var neutralKeys = UiText.GetNeutralResourceKeys();

        neutralKeys.Should().Contain(localizedEntries.Keys,
            because: $"every '{cultureName}' satellite key must have a corresponding neutral key (no orphans)");
        localizedEntries.Should().OnlyContain(
            entry => !string.IsNullOrEmpty(entry.Value),
            $"each '{cultureName}' satellite key must have a non-blank value (no empty/fallback slots)");
    }

}

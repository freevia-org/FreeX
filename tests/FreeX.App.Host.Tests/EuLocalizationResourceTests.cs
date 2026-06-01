using System.Globalization;
using System.IO;
using System.Resources;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed partial class EuLocalizationResourceTests
{
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

    public static IEnumerable<object[]> ExpectedEuOfficeSatelliteCultureData() =>
        ExpectedOfficeSatelliteCultures.Select(culture => new object[] { culture });

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
            .Where(entry => !TokenSet(entry.Value, PlaceholderPattern())
                .SetEquals(TokenSet(localized[entry.Key], PlaceholderPattern())))
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

    private static string ResourceDirectory =>
        Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "Resources", "Strings.resx"))!;

    private static Dictionary<string, string> ReadResxValues(string fileName)
    {
        var path = Path.Combine(ResourceDirectory, fileName);
        return XDocument.Load(path)
            .Descendants("data")
            .ToDictionary(
                element => element.Attribute("name")!.Value,
                element => element.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);
    }

    private static HashSet<string> TokenSet(string value, Regex pattern) =>
        pattern.Matches(value)
            .Select(match => match.Value)
            .ToHashSet(StringComparer.Ordinal);

    private static int AccessKeyCount(string value) =>
        AccessKeyPattern().Matches(value).Count;

    [GeneratedRegex(@"\{[^{}]+\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();

    [GeneratedRegex(@"(?<!_)_(?!_)", RegexOptions.CultureInvariant)]
    private static partial Regex AccessKeyPattern();
}

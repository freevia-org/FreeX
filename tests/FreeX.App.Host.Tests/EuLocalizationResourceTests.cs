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
    private static readonly string[] ExpectedEuOfficeSatelliteCultures =
    [
        "bg-BG",
        "cs-CZ",
        "da-DK",
        "de-DE",
        "el-GR",
        "en-GB",
        "es-ES",
        "et-EE",
        "fi-FI",
        "fr-FR",
        "ga-IE",
        "hr-HR",
        "hu-HU",
        "it-IT",
        "lt-LT",
        "lv-LV",
        "mt-MT",
        "nl-NL",
        "pl-PL",
        "pt-PT",
        "ro-RO",
        "sk-SK",
        "sl-SI",
        "sv-SE",
    ];

    public static IEnumerable<object[]> ExpectedEuOfficeSatelliteCultureData() =>
        ExpectedEuOfficeSatelliteCultures.Select(culture => new object[] { culture });

    [Fact]
    public void Resources_IncludeEveryEuOfficeSatelliteCulture()
    {
        var availableCultures = Directory
            .EnumerateFiles(ResourceDirectory, "Strings.*.resx", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileNameWithoutExtension)
            .Select(fileName => fileName!["Strings.".Length..])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        availableCultures.Should().Contain(ExpectedEuOfficeSatelliteCultures);
    }

    [Fact]
    public void AppLanguageCatalog_DiscoversEveryEuOfficeSatelliteCultureAfterBuild()
    {
        var availableCultures = AppLanguageCatalog.GetAvailableLanguages()
            .Select(option => option.CultureName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        availableCultures.Should().Contain(ExpectedEuOfficeSatelliteCultures);
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

        if (!string.Equals(cultureName, "en-GB", StringComparison.OrdinalIgnoreCase))
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

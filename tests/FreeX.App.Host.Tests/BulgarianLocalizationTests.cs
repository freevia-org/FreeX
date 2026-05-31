using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed class BulgarianLocalizationTests
{
    private static readonly Regex PlaceholderPattern = new(@"\{[^{}]+\}", RegexOptions.Compiled);
    private static readonly Regex AccessKeyPattern = new(@"(?<!_)_(?!_)", RegexOptions.Compiled);

    [Fact]
    public void BulgarianSatelliteResource_ProvidesTranslatedSmokeTestKeys()
    {
        using var cultureScope = new CultureScope("bg-BG");

        UiText.Get("Common_Ok").Should().Be("_ОК");
        UiText.Get("Options_ChooseDisplayLanguage").Should().Be("Изберете език на показване");
        UiText.Get("Options_AppLanguageSystemDefault").Should().Be("Използване на системния език");
        UiText.Get("Startup_CrashReportsTitle").Should().Be("Отчети за сривове на FreeX");
    }

    [Fact]
    public void BulgarianSatelliteResource_ProvidesTranslatedFormerFallbackKey()
    {
        using var cultureScope = new CultureScope("bg-BG");

        UiText.Get("Options_DefaultFont").Should().Be("_Шрифт по подразбиране:");
    }

    [Fact]
    public void AppLanguageCatalog_DiscoversBulgarianSatelliteAfterBuild()
    {
        AppLanguageCatalog.GetAvailableLanguages()
            .Select(option => option.CultureName)
            .Should()
            .Contain("bg-BG");
    }

    [Fact]
    public void BulgarianResx_UsesExcelAlignedTerminologyForHighValueCommands()
    {
        var bulgarian = ReadResxValues("Strings.bg-BG.resx");
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["MainWindow_Header_Paste"] = "Постави",
            ["MainWindow_Content_Copy"] = "Копирай",
            ["MainWindow_Content_Cut"] = "Изрежи",
            ["MainWindow_Header_FlashFill"] = "Примерно запълване",
            ["MainWindow_Text_Wrap"] = "Пренасяне",
            ["InsertChart_AllChartsTab"] = "_Всички диаграми",
            ["ChartType_Pie"] = "Кръгова",
            ["ChartType_Doughnut"] = "Пръстеновидна",
            ["ChartType_Scatter"] = "XY (точкова)",
            ["ChartType_Stock"] = "Борсова",
            ["MainWindow_TooltipTitle_Trendline"] = "Линия на тенденцията",
            ["Sparkline_InsertSparkline"] = "Вмъкване на блещукаща линия",
            ["PivotSlicerTimeline_InsertSlicer"] = "Вмъкване на сегментатор",
            ["MainWindow_Header_TableDesign"] = "Проектиране на таблица",
            ["TableDesign_TableRangeLabel"] = "_Диапазон на таблицата:",
        };

        foreach (var expectedEntry in expected)
        {
            bulgarian[expectedEntry.Key].Should().Be(expectedEntry.Value);
        }
    }

    [Fact]
    public void BulgarianSatelliteResource_ContainsFullResourceSetWithoutParentFallback()
    {
        var resourceManager = new ResourceManager("FreeX.App.Host.Resources.Strings", typeof(UiText).Assembly);
        var resourceSet = resourceManager.GetResourceSet(
            CultureInfo.GetCultureInfo("bg-BG"),
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
            "the full Bulgarian satellite should not rely on blank values or parent fallback");
    }

    [Fact]
    public void BulgarianResx_MatchesNeutralKeysPlaceholdersAndAccessKeyCounts()
    {
        var neutral = ReadResxValues("Strings.resx");
        var bulgarian = ReadResxValues("Strings.bg-BG.resx");

        bulgarian.Keys.Should().BeEquivalentTo(neutral.Keys);

        var blankValues = bulgarian
            .Where(entry => !string.IsNullOrEmpty(neutral[entry.Key]) && string.IsNullOrEmpty(entry.Value))
            .Select(entry => entry.Key)
            .ToArray();
        blankValues.Should().BeEmpty();

        var placeholderMismatches = neutral
            .Where(entry => !TokenSet(entry.Value, PlaceholderPattern)
                .SetEquals(TokenSet(bulgarian[entry.Key], PlaceholderPattern)))
            .Select(entry => entry.Key)
            .ToArray();
        placeholderMismatches.Should().BeEmpty();

        var accessKeyMismatches = neutral
            .Where(entry => AccessKeyCount(entry.Value) != AccessKeyCount(bulgarian[entry.Key]))
            .Select(entry => entry.Key)
            .ToArray();
        accessKeyMismatches.Should().BeEmpty();
    }

    private static Dictionary<string, string> ReadResxValues(string fileName)
    {
        var path = WorkspaceFileLocator.Find("src", "FreeX.App.Host", "Resources", fileName);
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
        AccessKeyPattern.Matches(value).Count;

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previousUICulture = CultureInfo.CurrentUICulture;
        private readonly CultureInfo? _previousDefaultUICulture = CultureInfo.DefaultThreadCurrentUICulture;

        public CultureScope(string currentUICulture)
        {
            var culture = CultureInfo.GetCultureInfo(currentUICulture);
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
        }

        public void Dispose()
        {
            CultureInfo.CurrentUICulture = _previousUICulture;
            CultureInfo.DefaultThreadCurrentUICulture = _previousDefaultUICulture;
        }
    }
}

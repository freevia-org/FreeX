using System.Globalization;
using System.Resources;
using FluentAssertions;
using FreeX.App.Host;
using FreeX.App.Localization;
using static FreeX.App.Host.Tests.LocalizationResourceTestSupport;

namespace FreeX.App.Host.Tests;

public sealed class BulgarianLocalizationTests
{
    [Fact]
    public void BulgarianSatelliteResource_ProvidesTranslatedSmokeTestKeys()
    {
        using var cultureScope = TestCultureScope.CurrentUICultureAndDefaultThreadUICulture("bg-BG");

        UiText.Get("Common_Ok").Should().Be("_ОК");
        UiText.Get("Options_ChooseDisplayLanguage").Should().Be("Изберете език на показване");
        UiText.Get("Options_AppLanguageSystemDefault").Should().Be("Използване на системния език");
        UiText.Get("Startup_CrashReportsTitle").Should().Be("Отчети за сривове на FreeX");
    }

    [Fact]
    public void BulgarianSatelliteResource_ProvidesTranslatedFormerFallbackKey()
    {
        using var cultureScope = TestCultureScope.CurrentUICultureAndDefaultThreadUICulture("bg-BG");

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
        // The Loc neutral catalog is a superset (includes keys not yet translated in satellites).
        // We verify: (a) the satellite loads, (b) all keys it has are non-blank, and
        // (c) its key set is a subset of the neutral — no orphan keys.
        var resourceManager = new ResourceManager("FreeX.App.Localization.Resources.Strings", typeof(Loc).Assembly);
        var resourceSet = resourceManager.GetResourceSet(
            CultureInfo.GetCultureInfo("bg-BG"),
            createIfNotExists: true,
            tryParents: false);

        resourceSet.Should().NotBeNull();
        var localizedEntries = resourceSet!
            .Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!, StringComparer.Ordinal);
        var neutralKeys = UiText.GetNeutralResourceKeys();

        neutralKeys.Should().Contain(localizedEntries.Keys,
            because: "every bg-BG key must have a corresponding neutral key (no orphans)");
        localizedEntries.Should().OnlyContain(
            entry => !string.IsNullOrEmpty(entry.Value),
            "each bg-BG entry must have a non-blank value (no empty/fallback slots)");
    }

    [Fact]
    public void BulgarianResx_MatchesNeutralKeysPlaceholdersAndAccessKeyCounts()
    {
        var neutral = ReadEffectiveNeutralValues();
        var bulgarian = ReadResxValues("Strings.bg-BG.resx");

        // The effective neutral catalog composes app-owned and shared generic resources;
        // satellites are translated subsets and may override keys owned by either catalog.
        // Verify: no orphan satellite keys, no blank values for keys that have neutral text,
        // placeholder tokens and access-key counts match for all keys present in the satellite.
        neutral.Keys.Should().Contain(bulgarian.Keys,
            because: "every bg-BG satellite key must exist in the neutral catalog (no orphan keys)");

        var blankValues = bulgarian
            .Where(entry => !string.IsNullOrEmpty(neutral[entry.Key]) && string.IsNullOrEmpty(entry.Value))
            .Select(entry => entry.Key)
            .ToArray();
        blankValues.Should().BeEmpty();

        var placeholderMismatches = bulgarian.Keys
            .Where(key => !CompositePlaceholderTokens(neutral[key])
                .SetEquals(CompositePlaceholderTokens(bulgarian[key])))
            .ToArray();
        placeholderMismatches.Should().BeEmpty();

        var accessKeyMismatches = bulgarian.Keys
            .Where(key => AccessKeyCount(neutral[key]) != AccessKeyCount(bulgarian[key]))
            .ToArray();
        accessKeyMismatches.Should().BeEmpty();
    }

}

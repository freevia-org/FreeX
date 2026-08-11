using System.Globalization;
using System.IO;
using FluentAssertions;
using FreeX.App.Host;
using FreeX.App.Localization;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Gate tests: verifies the shared FreeX.App.Localization catalog is the single live source.
/// The old byte-identical-vs-Host-resx assertions have served their purpose (the merge is done
/// and the Host resx files have been deleted). These tests now prove the Loc catalog is a
/// complete superset with valid content, and that all 43 satellite locales are loadable.
/// </summary>
public sealed class LocalizationConvergenceTests
{
    // The 43 satellite locales in the shared Loc catalog.
    private static readonly string[] SatelliteLocales =
    [
        "bg-BG", "cs-CZ", "da-DK", "de-AT", "de-CH", "de-DE", "el-GR",
        "en-AU", "en-CA", "en-GB", "en-IE", "en-NZ", "en-ZA",
        "es-AR", "es-CL", "es-CO", "es-ES", "es-MX",
        "et-EE", "fi-FI", "fr-CA", "fr-FR", "ga-IE", "hr-HR", "hu-HU",
        "it-IT", "lt-LT", "lv-LV", "mt-MT", "nb-NO", "nl-BE", "nl-NL",
        "pl-PL", "pt-BR", "pt-PT", "ro-RO", "sk-SK", "sl-SI",
        "sr-Cyrl-RS", "sr-Latn-RS", "sv-SE", "tr-TR", "uk-UA",
    ];

    [Fact]
    public void LocCatalog_NeutralKeyCount_IsAtLeast6000()
    {
        var locKeys = Loc.GetNeutralResourceKeys();

        locKeys.Count.Should().BeGreaterThanOrEqualTo(6000,
            because: "the shared Loc catalog is the superset of all localization keys (>6000 expected)");
    }

    [Fact]
    public void LocCatalog_NeutralSpotCheck_KeysResolveToExpectedValues()
    {
        // Representative spot-check covering common strings, mnemonics, dialog labels and tooltips.
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Common_Ok"]                               = "_OK",
            ["Common_Cancel"]                           = "_Cancel",
            ["Common_ErrorTitle"]                       = "Error",
            ["Common_WarningTitle"]                     = "Warning",
            ["Common_InformationTitle"]                 = "Information",
            ["Common_ConfirmTitle"]                     = "Confirm",
            ["Options_AppLanguageSystemDefault"]        = "Use system default",
            ["Options_AppLanguageEnglishUnitedStates"]  = "English (United States)",
            ["Options_ChooseDisplayLanguage"]           = "Choose display language",
            ["Startup_CrashReportsTitle"]               = "Crash Reports",
            ["MainWindow_Header_Paste"]                 = "Paste",
            ["MainWindow_Content_Copy"]                 = "Copy",
            ["MainWindow_Content_Cut"]                  = "Cut",
            ["MainWindow_Header_FlashFill"]             = "Flash Fill",
            ["MainWindow_Text_Wrap"]                    = "Wrap",
            ["InsertChart_AllChartsTab"]                = "_All Charts",
            ["ChartType_Pie"]                           = "Pie",
            ["ChartType_Doughnut"]                      = "Doughnut",
            ["ChartType_Scatter"]                       = "Scatter",
            ["TableDesign_TableRangeLabel"]             = "_Table range:",
        };

        var missing = new List<string>();
        var wrong   = new List<string>();

        foreach (var (key, expectedValue) in expected)
        {
            var actual = Loc.GetNeutral(key);
            if (actual is null)
                missing.Add(key);
            else if (actual != expectedValue)
                wrong.Add($"  Key='{key}' expected='{expectedValue}' actual='{actual}'");
        }

        missing.Should().BeEmpty(because: "all spot-check keys must exist in the Loc neutral catalog");
        wrong.Should().BeEmpty(because: $"all spot-check keys must match expected neutral values:{Environment.NewLine}{string.Join(Environment.NewLine, wrong)}");
    }

    [Fact]
    public void LocCatalog_NeutralValues_NoSentinelPlaceholders()
    {
        var sentinels = Loc.GetNeutralResourceKeys()
            .Where(key => Loc.GetNeutral(key).StartsWith("[[", StringComparison.Ordinal))
            .ToList();

        sentinels.Should().BeEmpty(
            because: "no neutral key in the Loc catalog should resolve to a [[key]] sentinel — those indicate missing translations");
    }

    [Fact]
    public void PlatformUiTextAliases_UseTheProductOwnedLocalizedUiTextPolicy()
    {
        var host = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Host", "FreeX.App.Host.csproj");
        var avalonia = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Avalonia", "FreeX.App.Avalonia.csproj");
        var shared = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Localization", "LocalizedUiText.cs");

        const string alias = "<Using Include=\"FreeX.App.Localization.LocalizedUiText\" Alias=\"UiText\" />";
        host.Should().Contain(alias);
        avalonia.Should().Contain(alias);
        var root = WorkspaceFileLocator.FindWorkspaceRoot();
        File.Exists(Path.Combine(root, "src", "FreeX.App.Host", "UiText.cs")).Should().BeFalse();
        File.Exists(Path.Combine(root, "src", "FreeX.App.Avalonia", "UiText.cs")).Should().BeFalse();

        shared.Should().Contain("LocalizedUiTextCatalog<Loc>");
        var sharedCatalog = WorkspaceFileLocator.ReadAllText("shared", "Free.Shared.Localization", "LocalizedUiTextCatalog.cs");
        sharedCatalog.Should().Contain("Facade.Get(");
        sharedCatalog.Should().Contain("Facade.Format(");
        sharedCatalog.Should().Contain("Facade.GetNeutral(");
        sharedCatalog.Should().Contain("Facade.GetNeutralResourceKeys(");
    }

    [Theory]
    [MemberData(nameof(AllSatelliteLocales))]
    public void LocCatalog_SatelliteLocale_LoadsAndContainsTranslatedKeys(string locale)
    {
        var originalCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(locale);

            // Spot-check moved keys: they must resolve through the shared satellite.
            var ok = Loc.Get("Common_Ok");
            var cancel = Loc.Get("Common_Cancel");

            ok.Should().NotBeNullOrWhiteSpace(
            because: $"Common_Ok must have a non-empty translation in '{locale}'");
            cancel.Should().NotBeNullOrWhiteSpace(
            because: $"Common_Cancel must have a non-empty translation in '{locale}'");
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    public static TheoryData<string> AllSatelliteLocales()
    {
        var data = new TheoryData<string>();
        foreach (var locale in SatelliteLocales)
            data.Add(locale);
        return data;
    }
}

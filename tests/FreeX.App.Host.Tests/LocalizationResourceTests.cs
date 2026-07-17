using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Free.Shared.Localization;
using Free.Shared.Shell;
using FreeX.App.Host;
using FreeX.App.Localization;

namespace FreeX.App.Host.Tests;

public sealed partial class LocalizationResourceTests
{
    [Fact]
    public void UiText_CommonProperties_ReturnNeutralStrings()
    {
        using var cultureScope = TestCultureScope.CurrentCultureAndUICulture(currentCulture: "ja-JP", currentUICulture: "ja-JP");

        UiText.Ok.Should().Be("_OK");
        UiText.Cancel.Should().Be("_Cancel");
        UiText.ErrorTitle.Should().Be("Error");
        UiText.WarningTitle.Should().Be("Warning");
        UiText.InformationTitle.Should().Be("Information");
        UiText.ConfirmTitle.Should().Be("Confirm");
    }

    [Fact]
    public void UiText_MissingKey_ReturnsSentinel()
    {
        UiText.Get("Missing_Localization_Key").Should().Be("[[Missing_Localization_Key]]");
    }

    [Fact]
    public void UiText_SharedHelpers_MatchLocalizationCatalog()
    {
        UiText.CreateAutomationName("_Cancel").Should().Be(LocalizedTextCatalog.CreateAutomationName("_Cancel"));
        UiText.CreateMissingText("Missing_Key").Should().Be(LocalizedTextCatalog.CreateMissingText("Missing_Key"));
        UiText.GetNeutralResourceKeys().Should().Contain("Common_Cancel");
    }

    [Fact]
    public void UiText_Format_UsesCurrentCultureForArguments()
    {
        using var cultureScope = TestCultureScope.CurrentCultureAndUICulture(currentCulture: "fr-FR", currentUICulture: "en-US");
        const string key = "Missing_Format_{0:N2}";

        var expected = string.Format(CultureInfo.CurrentCulture, "[[Missing_Format_{0:N2}]]", 1234.5);

        UiText.Format(key, 1234.5).Should().Be(expected);
    }

    [Fact]
    public void UiText_PseudoLocalizationCulture_ExpandsNeutralResourcesAtRuntime()
    {
        using var cultureScope = TestCultureScope.CurrentCultureAndUICulture(currentCulture: "en-US", currentUICulture: "qps-ploc");

        UiText.Get("Common_Cancel")
            .Should()
            .Be(PseudoLocalization.Expand(UiText.GetNeutral("Common_Cancel")));

        UiText.Format("Export_PageRangeMultiple", 2, 4)
            .Should()
            .Be(string.Format(
                CultureInfo.CurrentCulture,
                PseudoLocalization.Expand(UiText.GetNeutral("Export_PageRangeMultiple")),
                2,
                4));
    }

    [Fact]
    public void LocExtension_ProvideValue_ReturnsResourceText()
    {
        new LocExtension("Common_Ok")
            .ProvideValue(serviceProvider: null!)
            .Should()
            .Be("_OK");
    }

    [Fact]
    public void LocExtension_ProvideValue_ReturnsEmptyStringWhenKeyPropertyIsMissing()
    {
        new LocExtension()
            .ProvideValue(serviceProvider: null!)
            .Should()
            .Be(string.Empty);
    }

    [Fact]
    public void AppLocalization_ApplyAppLanguage_UpdatesUiCultureWithoutChangingRegionalCulture()
    {
        using var cultureScope = TestCultureScope.CurrentCultureAndUICulture(currentCulture: "fr-FR", currentUICulture: "fr-FR");
        var expectedDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;

        AppLocalization.Bootstrap.ApplyAppLanguage("uk-UA");

        CultureInfo.CurrentUICulture.Name.Should().Be("uk-UA");
        CultureInfo.DefaultThreadCurrentUICulture?.Name.Should().Be("uk-UA");
        CultureInfo.CurrentCulture.Name.Should().Be("fr-FR");
        CultureInfo.DefaultThreadCurrentCulture.Should().BeSameAs(expectedDefaultCulture);
    }

    [Fact]
    public void AppLocalizationBootstrap_InstallsAppResourceSeamsBeforeCultureSelection()
    {
        var originalShellStrings = ShellStrings.Current;
        var originalBackstageStrings = BackstageStrings.Current;
        try
        {
            using var cultureScope = TestCultureScope.CurrentCultureAndUICulture("en-US");
            AppLocalization.Bootstrap.InstallSharedSeams();
            AppLocalization.Bootstrap.ApplyAppLanguage("fr-FR");

            ShellStrings.Current.Cancel.Should().Be("_Annuler");
            BackstageStrings.Current.Format("File_CommandFailedFormat", "Open", "Denied")
                .Should()
                .Be("Open failed: Denied");
            BackstageStrings.Current.Get("Backstage_GreetingMorning").Should().Be("Bonjour");
        }
        finally
        {
            ShellStrings.Current = originalShellStrings;
            BackstageStrings.Current = originalBackstageStrings;
        }
    }

    [Fact]
    public void AppStartup_UsesOrderedLocalizationBootstrap()
    {
        var source = DialogSourceTestSupport.ReadHostSources("App.xaml.cs");
        var install = source.IndexOf(
            "AppLocalization.Bootstrap.InstallSharedSeams();",
            StringComparison.Ordinal);
        var apply = source.IndexOf(
            "AppLocalization.Bootstrap.ApplyAppLanguage(options.AppLanguage);",
            StringComparison.Ordinal);

        install.Should().BeGreaterThanOrEqualTo(0);
        apply.Should().BeGreaterThan(install);
        source.Should().NotContain("new ResourceShellStrings");
        source.Should().NotContain("new ResourceBackstageStrings");
    }

    [Fact]
    public void UiText_GetNeutralResourceKeys_ContainsInitialCommonAndStartupKeys()
    {
        var keys = UiText.GetNeutralResourceKeys();
        var expectedKeys = new[]
        {
            "Common_Cancel",
            "Common_ConfirmTitle",
            "Common_ErrorTitle",
            "Common_InformationTitle",
            "Common_Ok",
            "Common_WarningTitle",
            "Startup_CrashReportsConsentPrompt",
            "Startup_CrashReportsTitle",
        };

        foreach (var expectedKey in expectedKeys)
        {
            keys.Should().Contain(expectedKey);
        }
    }

    [Fact]
    public void LocalizedSelectorItems_DoNotExposeAccessKeyUnderscoresAsVisibleText()
    {
        var hostDirectory = DialogSourceTestSupport.FindHostSourceDirectory("OptionsDialog.xaml");
        var offenders = Directory
            .EnumerateFiles(hostDirectory, "*.xaml", SearchOption.AllDirectories)
            .SelectMany(file => LocalizedSelectorItemPattern()
                .Matches(File.ReadAllText(file))
                .Select(match =>
                {
                    var key = match.Groups["key"].Value;
                    return new
                    {
                        File = Path.GetRelativePath(hostDirectory, file),
                        Control = match.Groups["control"].Value,
                        Key = key,
                        Value = UiText.Get(key)
                    };
                }))
            .Where(item => item.Value.StartsWith("_", StringComparison.Ordinal))
            .ToList();

        offenders.Should().BeEmpty("selector item text is rendered literally and should not carry access-key markers");
    }

    [GeneratedRegex("<(?<control>ListBoxItem|ComboBoxItem|TreeViewItem)\\b[^>]*(?:Content|Header)=\"\\{local:Loc Key=(?<key>[^}]+)\\}\"", RegexOptions.CultureInvariant)]
    private static partial Regex LocalizedSelectorItemPattern();

}

using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed partial class LocalizationResourceTests
{
    [Fact]
    public void UiText_CommonProperties_ReturnNeutralStrings()
    {
        using var cultureScope = new CultureScope(currentCulture: "fr-FR", currentUICulture: "fr-FR");

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
    public void UiText_Format_UsesCurrentCultureForArguments()
    {
        using var cultureScope = new CultureScope(currentCulture: "fr-FR", currentUICulture: "en-US");
        const string key = "Missing_Format_{0:N2}";

        var expected = string.Format(CultureInfo.CurrentCulture, "[[Missing_Format_{0:N2}]]", 1234.5);

        UiText.Format(key, 1234.5).Should().Be(expected);
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
        using var cultureScope = new CultureScope(currentCulture: "fr-FR", currentUICulture: "fr-FR");
        var expectedDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;

        AppLocalization.ApplyAppLanguage("uk-UA");

        CultureInfo.CurrentUICulture.Name.Should().Be("uk-UA");
        CultureInfo.DefaultThreadCurrentUICulture?.Name.Should().Be("uk-UA");
        CultureInfo.CurrentCulture.Name.Should().Be("fr-FR");
        CultureInfo.DefaultThreadCurrentCulture.Should().BeSameAs(expectedDefaultCulture);
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
        var hostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "OptionsDialog.xaml"))!;
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

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previousCulture = CultureInfo.CurrentCulture;
        private readonly CultureInfo _previousUICulture = CultureInfo.CurrentUICulture;
        private readonly CultureInfo? _previousDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        private readonly CultureInfo? _previousDefaultUICulture = CultureInfo.DefaultThreadCurrentUICulture;

        public CultureScope(string currentCulture, string currentUICulture)
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(currentCulture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(currentUICulture);
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _previousCulture;
            CultureInfo.CurrentUICulture = _previousUICulture;
            CultureInfo.DefaultThreadCurrentCulture = _previousDefaultCulture;
            CultureInfo.DefaultThreadCurrentUICulture = _previousDefaultUICulture;
        }
    }
}

using System.Globalization;
using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed class LocalizationResourceTests
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

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previousCulture = CultureInfo.CurrentCulture;
        private readonly CultureInfo _previousUICulture = CultureInfo.CurrentUICulture;

        public CultureScope(string currentCulture, string currentUICulture)
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(currentCulture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(currentUICulture);
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _previousCulture;
            CultureInfo.CurrentUICulture = _previousUICulture;
        }
    }
}

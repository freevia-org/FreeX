using System.Globalization;
using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed class BulgarianLocalizationTests
{
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
    public void BulgarianSatelliteResource_FallsBackToNeutralForUntranslatedKeys()
    {
        using var cultureScope = new CultureScope("bg-BG");

        UiText.Get("Options_DefaultFont").Should().Be("Default _font:");
    }

    [Fact]
    public void AppLanguageCatalog_DiscoversBulgarianSatelliteAfterBuild()
    {
        AppLanguageCatalog.GetAvailableLanguages()
            .Select(option => option.CultureName)
            .Should()
            .Contain("bg-BG");
    }

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

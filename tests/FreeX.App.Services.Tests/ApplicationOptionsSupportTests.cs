using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class ApplicationOptionsSupportTests
{
    [Fact]
    public void Normalizer_TryParseRecentFilesCap_AcceptsOnlySharedRange()
    {
        ApplicationOptionsNormalizer.TryParseRecentFilesCap(" 7 ", out var cap).Should().BeTrue();
        cap.Should().Be(7);

        ApplicationOptionsNormalizer.TryParseRecentFilesCap("-1", out _).Should().BeFalse();
        ApplicationOptionsNormalizer.TryParseRecentFilesCap("9999", out _).Should().BeFalse();
        ApplicationOptionsNormalizer.TryParseRecentFilesCap("many", out _).Should().BeFalse();
    }

    [Fact]
    public void SummaryPlanner_BuildsCommonSisterAppOptionRows()
    {
        var options = new DummyOptions
        {
            RecentFilesCap = 7,
            DefaultSaveFormat = ".fxp",
            UiLanguage = ApplicationOptionsNormalizer.SystemDefaultLanguage
        };

        var plan = ApplicationOptionsSummaryPlanner.Build(options, @"C:\Users\Ada\AppData\Local\FreeP");

        plan.Rows.Should().Equal(
            new ApplicationOptionsSummaryRow(ApplicationOptionsSummaryPlanner.RecentFilesKeptLabel, "7"),
            new ApplicationOptionsSummaryRow(ApplicationOptionsSummaryPlanner.DefaultSaveFormatLabel, ".fxp"),
            new ApplicationOptionsSummaryRow(ApplicationOptionsSummaryPlanner.UiLanguageLabel, ApplicationOptionsSummaryPlanner.SystemDefaultLanguageLabel),
            new ApplicationOptionsSummaryRow(ApplicationOptionsSummaryPlanner.DataFolderLabel, @"C:\Users\Ada\AppData\Local\FreeP"));
    }

    [Fact]
    public void SummaryPlanner_FormatUiLanguage_PreservesExplicitLanguage()
    {
        ApplicationOptionsSummaryPlanner.FormatUiLanguage("uk-UA").Should().Be("uk-UA");
        ApplicationOptionsSummaryPlanner.FormatUiLanguage(null)
            .Should()
            .Be(ApplicationOptionsSummaryPlanner.SystemDefaultLanguageLabel);
    }

    [Fact]
    public void Load_WhenFileMissing_ReturnsNormalizedDefaults()
    {
        using var temp = new TestTemporaryDirectory();
        var store = NormalizingJsonSettingsStore<DummyOptions>.ForPath(Path.Combine(temp.Path, "missing.json"));

        var options = store.Load();

        options.RecentFilesCap.Should().Be(ApplicationOptionsNormalizer.DefaultRecentFilesCap);
        options.DefaultSaveFormat.Should().Be(DummyOptions.DefaultFormat);
        options.UiLanguage.Should().BeEmpty();
        store.LastError.Should().BeNull();
    }

    [Fact]
    public void Load_WhenCorrupt_ReturnsNormalizedDefaultsWithObservableError()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "settings.json");
        File.WriteAllText(path, "{ nope");
        var store = NormalizingJsonSettingsStore<DummyOptions>.ForPath(path);

        var options = store.Load();

        options.RecentFilesCap.Should().Be(ApplicationOptionsNormalizer.DefaultRecentFilesCap);
        options.DefaultSaveFormat.Should().Be(DummyOptions.DefaultFormat);
        store.LastError.Should().NotBeNull();
        store.LastError.Should().Contain("Failed to load settings");
    }

    [Fact]
    public void Save_NormalizesBeforeWriting()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "nested", "settings.json");
        var store = NormalizingJsonSettingsStore<DummyOptions>.ForPath(path);

        store.Save(new DummyOptions
        {
            RecentFilesCap = 9999,
            DefaultSaveFormat = "  ",
            UiLanguage = "  uk-UA  "
        }).Should().BeTrue();

        var reloaded = store.Load();
        reloaded.RecentFilesCap.Should().Be(ApplicationOptionsNormalizer.MaxRecentFilesCap);
        reloaded.DefaultSaveFormat.Should().Be(DummyOptions.DefaultFormat);
        reloaded.UiLanguage.Should().Be("uk-UA");
    }

    [Fact]
    public void ForProductFile_ResolvesUnderAmbientProductFolder()
    {
        using var temp = new TestTemporaryDirectory();
        var provider = new TestApplicationDataPathProvider(temp.Path);

        var store = NormalizingJsonSettingsStore<DummyOptions>.ForProductFile("settings.json", provider);

        store.StorePath.Should().Be(Path.Combine(temp.Path, "FreeX", "settings.json"));
    }

    private sealed class DummyOptions : INormalizableApplicationOptions, IApplicationOptionsSummarySource
    {
        public const string DefaultFormat = ".dummy";

        public int RecentFilesCap { get; set; } = ApplicationOptionsNormalizer.DefaultRecentFilesCap;

        public string DefaultSaveFormat { get; set; } = DefaultFormat;

        public string UiLanguage { get; set; } = ApplicationOptionsNormalizer.SystemDefaultLanguage;

        public void Normalize()
        {
            RecentFilesCap = ApplicationOptionsNormalizer.NormalizeRecentFilesCap(RecentFilesCap);
            DefaultSaveFormat = ApplicationOptionsNormalizer.NormalizeDefaultSaveFormat(DefaultSaveFormat, DefaultFormat);
            UiLanguage = ApplicationOptionsNormalizer.NormalizeUiLanguage(UiLanguage);
        }
    }

    private sealed class TestApplicationDataPathProvider(string path) : IApplicationDataPathProvider
    {
        public string GetApplicationDataDirectory() => path;
    }
}

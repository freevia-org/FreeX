using System;
using System.IO;
using FluentAssertions;
using Free.Shared.AppServices;
using FreeW.App.Host;
using FreeW.App.Presentation.Options;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Coverage for FreeW's P4 options adoption: <see cref="ApplicationOptionsStore{T}"/> persists
/// <see cref="FreeWOptions"/> through the shared, neutral options store under
/// FreeW's own product folder. These run headless (no WPF) — pure model + store round-trips.
/// </summary>
public sealed class FreeWOptionsTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "FreeW.OptionsTests", Guid.NewGuid().ToString("N"));

    public FreeWOptionsTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public void Defaults_AreSensible()
    {
        var options = new FreeWOptions();

        options.RecentFilesCap.Should().Be(FreeWOptions.DefaultRecentFilesCap);
        options.DefaultSaveFormat.Should().Be(FreeWOptions.DocxDefaultFormat);
        options.UiLanguage.Should().BeEmpty();
    }

    [Fact]
    public void Load_WhenFileMissing_ReturnsNormalizedDefaults()
    {
        var store = ApplicationOptionsStore<FreeWOptions>.ForPath(Path.Combine(_tempDir, "missing.json"));

        var options = store.Load();

        options.RecentFilesCap.Should().Be(FreeWOptions.DefaultRecentFilesCap);
        options.DefaultSaveFormat.Should().Be(FreeWOptions.DocxDefaultFormat);
        store.LastError.Should().BeNull();
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var path = Path.Combine(_tempDir, "nested", "settings.json");
        var store = ApplicationOptionsStore<FreeWOptions>.ForPath(path);

        store.Save(new FreeWOptions
        {
            RecentFilesCap = 5,
            DefaultSaveFormat = ".docx",
            UiLanguage = "uk-UA"
        }).Should().BeTrue();

        var reloaded = store.Load();
        reloaded.RecentFilesCap.Should().Be(5);
        reloaded.UiLanguage.Should().Be("uk-UA");
        store.LastError.Should().BeNull();
    }

    [Fact]
    public void Load_WhenCorrupt_ReturnsDefaultsWithObservableError()
    {
        var path = Path.Combine(_tempDir, "settings.json");
        File.WriteAllText(path, "{ not json");
        var store = ApplicationOptionsStore<FreeWOptions>.ForPath(path);

        var options = store.Load();

        options.RecentFilesCap.Should().Be(FreeWOptions.DefaultRecentFilesCap);
        store.LastError.Should().NotBeNull();
        store.LastError.Should().Contain("Failed to load settings");
    }

    [Fact]
    public void Load_NormalizesOutOfRangeValues()
    {
        var path = Path.Combine(_tempDir, "settings.json");
        File.WriteAllText(
            path,
            """
            { "RecentFilesCap": 9999, "DefaultSaveFormat": "  ", "UiLanguage": "  en-GB  " }
            """);
        var store = ApplicationOptionsStore<FreeWOptions>.ForPath(path);

        var options = store.Load();

        options.RecentFilesCap.Should().Be(FreeWOptions.MaxRecentFilesCap);
        options.DefaultSaveFormat.Should().Be(FreeWOptions.DocxDefaultFormat);
        options.UiLanguage.Should().Be("en-GB");
    }

    [Theory]
    [InlineData("  en-us  ", "en-US")]
    [InlineData(" QPS-PLOC ", "qps-ploc")]
    [InlineData("not-a-culture", "")]
    public void Normalize_CanonicalizesUiLanguage(string input, string expected)
    {
        var options = new FreeWOptions { UiLanguage = input };

        options.Normalize();

        options.UiLanguage.Should().Be(expected);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("15", 15)]
    [InlineData("  7  ", 7)]
    public void Planner_TryParseRecentFilesCap_AcceptsInRange(string text, int expected)
    {
        OptionsDialogPlanner.TryParseRecentFilesCap(text, out var cap).Should().BeTrue();
        cap.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("99999")]
    [InlineData("3.5")]
    public void Planner_TryParseRecentFilesCap_RejectsInvalidOrOutOfRange(string text)
    {
        OptionsDialogPlanner.TryParseRecentFilesCap(text, out _).Should().BeFalse();
    }

    [Fact]
    public void Planner_BuildResult_NormalizesAndDefaults()
    {
        var result = OptionsDialogPlanner.BuildResult(
            recentFilesCap: 4, format: "  ", uiLanguage: "  en-GB  ",
            autoCorrectEnabled: true, autoFormat: AutoFormatOptions.Default,
            autoCorrect: AutoCorrectOptions.Default);

        result.RecentFilesCap.Should().Be(4);
        // Blank format → the single shipped .docx default; language is trimmed by Normalize().
        result.DefaultSaveFormat.Should().Be(FreeWOptions.DocxDefaultFormat);
        result.UiLanguage.Should().Be("en-GB");
        result.AutoCorrectEnabled.Should().BeTrue();
        result.AutoFormat.Should().NotBeNull();
    }

    [Fact]
    public void AutoFormatToggles_RoundTripThroughStore()
    {
        // The per-rule AutoFormat-As-You-Type toggles persist + reload through the shared JSON store.
        var path = Path.Combine(_tempDir, "settings.json");
        var options = new FreeWOptions
        {
            AutoCorrectEnabled = false,
            AutoFormat = AutoFormatOptions.Default with { Hyperlinks = false, Fractions = false },
        };
        ApplicationOptionsStore<FreeWOptions>.ForPath(path).Save(options).Should().BeTrue();

        var reloaded = ApplicationOptionsStore<FreeWOptions>.ForPath(path).Load();
        reloaded.AutoCorrectEnabled.Should().BeFalse();
        reloaded.AutoFormat.Hyperlinks.Should().BeFalse();
        reloaded.AutoFormat.Fractions.Should().BeFalse();
        reloaded.AutoFormat.SmartQuotes.Should().BeTrue(); // untouched rule stays on
    }

    [Fact]
    public void EditFlow_AppliesLiveAndPersists()
    {
        // Mirrors MainWindow.OpenOptions: the dialog produces a normalized result, the host copies it onto
        // the live options instance (so FileCommands sees the new cap immediately) and saves via the store.
        var path = Path.Combine(_tempDir, "settings.json");
        var store = ApplicationOptionsStore<FreeWOptions>.ForPath(path);
        var live = new FreeWOptions { RecentFilesCap = FreeWOptions.DefaultRecentFilesCap };

        var edited = OptionsDialogPlanner.BuildResult(
            recentFilesCap: 3, format: null, uiLanguage: "uk-UA",
            autoCorrectEnabled: true, autoFormat: AutoFormatOptions.Default,
            autoCorrect: AutoCorrectOptions.Default);
        live.RecentFilesCap = edited.RecentFilesCap;
        live.DefaultSaveFormat = edited.DefaultSaveFormat;
        live.UiLanguage = edited.UiLanguage;
        live.Normalize();
        store.Save(live).Should().BeTrue();

        // Applied live on the shared instance...
        live.RecentFilesCap.Should().Be(3);
        // ...and survives a restart (fresh store load).
        var reloaded = ApplicationOptionsStore<FreeWOptions>.ForPath(path).Load();
        reloaded.RecentFilesCap.Should().Be(3);
        reloaded.UiLanguage.Should().Be("uk-UA");
        reloaded.DefaultSaveFormat.Should().Be(FreeWOptions.DocxDefaultFormat);
    }

    [Fact]
    public void Create_ResolvesUnderFreeWProductFolder()
    {
        var provider = new TestApplicationDataPathProvider(_tempDir);

        var store = ApplicationOptionsStore<FreeWOptions>.Create(provider);

        // The test assembly installs AppProduct = "FreeW" (AppProductTestDefaults).
        store.StorePath.Should().Be(Path.Combine(_tempDir, "FreeW", ApplicationOptionsStore<FreeWOptions>.DefaultFileName));
    }

    [Fact]
    public void WpfOptionsDialog_UsesPresentationOptionsPolicy()
    {
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var dialogSource = File.ReadAllText(Path.Combine(repoRoot, "freew", "FreeW.App.Host", "OptionsDialog.cs"));

        dialogSource.Should().Contain("using FreeW.App.Presentation.Options;");
        dialogSource.Should().Contain("new OptionsDialogSession(");
        dialogSource.Should().Contain("_session.PlanAcceptance(");
        dialogSource.Should().Contain("_session.PlanEnabledState(");
        dialogSource.Should().NotContain("OptionsDialogWorkflowPlanner.TryBuildResult(");
        dialogSource.Should().NotContain("OptionsDialogWorkflowPlanner.PlanEnabledState(");
        dialogSource.Should().NotContain("OptionsDialogPlanner.TryParseRecentFilesCap(");
        dialogSource.Should().NotContain("OptionsDialogPlanner.BuildResult(");
        dialogSource.Should().NotContain("new AutoFormatOptions");
        dialogSource.Should().NotContain("new AutoCorrectOptions");
        File.Exists(Path.Combine(repoRoot, "freew", "FreeW.App.Host", "OptionsDialogPlanner.cs"))
            .Should().BeFalse();
        File.Exists(Path.Combine(repoRoot, "freew", "FreeW.App.Host", "FreeWOptions.cs"))
            .Should().BeFalse();
    }

    private sealed class TestApplicationDataPathProvider(string path) : IApplicationDataPathProvider
    {
        public string GetApplicationDataDirectory() => path;
    }

}

using System;
using System.IO;
using FluentAssertions;
using Free.Shared.AppServices;
using FreeW.App.Host;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Coverage for FreeW's P4 options adoption: <see cref="FreeWOptionsStore"/> persists
/// <see cref="FreeWOptions"/> through the shared, neutral <see cref="JsonSettingsStore{T}"/> under
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
        var store = FreeWOptionsStore.ForPath(Path.Combine(_tempDir, "missing.json"));

        var options = store.Load();

        options.RecentFilesCap.Should().Be(FreeWOptions.DefaultRecentFilesCap);
        options.DefaultSaveFormat.Should().Be(FreeWOptions.DocxDefaultFormat);
        store.LastError.Should().BeNull();
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var path = Path.Combine(_tempDir, "nested", "settings.json");
        var store = FreeWOptionsStore.ForPath(path);

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
        var store = FreeWOptionsStore.ForPath(path);

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
        var store = FreeWOptionsStore.ForPath(path);

        var options = store.Load();

        options.RecentFilesCap.Should().Be(FreeWOptions.MaxRecentFilesCap);
        options.DefaultSaveFormat.Should().Be(FreeWOptions.DocxDefaultFormat);
        options.UiLanguage.Should().Be("en-GB");
    }

    [Fact]
    public void Create_ResolvesUnderFreeWProductFolder()
    {
        var provider = new TestApplicationDataPathProvider(_tempDir);

        var store = FreeWOptionsStore.Create(provider);

        // The test assembly installs AppProduct = "FreeW" (AppProductTestDefaults).
        store.StorePath.Should().Be(Path.Combine(_tempDir, "FreeW", FreeWOptionsStore.FileName));
    }

    private sealed class TestApplicationDataPathProvider(string path) : IApplicationDataPathProvider
    {
        public string GetApplicationDataDirectory() => path;
    }
}

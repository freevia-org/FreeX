using System.IO;
using Free.Shared.AppServices;
using FreeP.App.Host;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Coverage for FreeP's options adoption: <see cref="FreePOptionsStore"/> persists <see cref="FreePOptions"/>
/// through the shared, neutral <see cref="JsonSettingsStore{T}"/> under FreeP's own product folder. Headless
/// (no WPF) — pure model + store round-trips. Mirrors FreeWOptionsTests.
/// </summary>
public sealed class FreePOptionsTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "FreeP.OptionsTests", Guid.NewGuid().ToString("N"));

    public FreePOptionsTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public void Defaults_AreSensible()
    {
        var options = new FreePOptions();

        options.RecentFilesCap.Should().Be(FreePOptions.DefaultRecentFilesCap);
        options.DefaultSaveFormat.Should().Be(FreePOptions.FxpDefaultFormat);
        options.UiLanguage.Should().BeEmpty();
    }

    [Fact]
    public void Load_WhenFileMissing_ReturnsNormalizedDefaults()
    {
        var store = FreePOptionsStore.ForPath(Path.Combine(_tempDir, "missing.json"));

        var options = store.Load();

        options.RecentFilesCap.Should().Be(FreePOptions.DefaultRecentFilesCap);
        options.DefaultSaveFormat.Should().Be(FreePOptions.FxpDefaultFormat);
        store.LastError.Should().BeNull();
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var path = Path.Combine(_tempDir, "nested", "settings.json");
        var store = FreePOptionsStore.ForPath(path);

        store.Save(new FreePOptions
        {
            RecentFilesCap = 5,
            DefaultSaveFormat = ".fxp",
            UiLanguage = "uk-UA"
        }).Should().BeTrue();

        var reloaded = store.Load();
        reloaded.RecentFilesCap.Should().Be(5);
        reloaded.UiLanguage.Should().Be("uk-UA");
        store.LastError.Should().BeNull();
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
        var store = FreePOptionsStore.ForPath(path);

        var options = store.Load();

        options.RecentFilesCap.Should().Be(FreePOptions.MaxRecentFilesCap);
        options.DefaultSaveFormat.Should().Be(FreePOptions.FxpDefaultFormat);
        options.UiLanguage.Should().Be("en-GB");
    }

    [Fact]
    public void Create_ResolvesUnderFreePProductFolder()
    {
        var provider = new TestApplicationDataPathProvider(_tempDir);

        var store = FreePOptionsStore.Create(provider);

        // The test assembly installs AppProduct = "FreeP" (AppProductTestDefaults).
        store.StorePath.Should().Be(Path.Combine(_tempDir, "FreeP", FreePOptionsStore.FileName));
    }

    private sealed class TestApplicationDataPathProvider(string path) : IApplicationDataPathProvider
    {
        public string GetApplicationDataDirectory() => path;
    }
}

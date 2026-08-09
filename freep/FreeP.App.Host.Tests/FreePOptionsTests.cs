using System.IO;
using Free.Shared.AppServices;
using FreeP.App.Host;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Coverage for FreeP's options adoption: <see cref="ApplicationOptionsStore{T}"/> persists <see cref="FreePOptions"/>
/// through the shared, neutral options store under FreeP's own product folder. Headless
/// (no WPF) — pure model + store round-trips. Mirrors FreeWOptionsTests.
/// </summary>
public sealed class FreePOptionsTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeP.OptionsTests-");
    private string _tempDir => _temporaryDirectory.Path;

    public void Dispose() => _temporaryDirectory.Dispose();

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
        var store = ApplicationOptionsStore<FreePOptions>.ForPath(Path.Combine(_tempDir, "missing.json"));

        var options = store.Load();

        options.RecentFilesCap.Should().Be(FreePOptions.DefaultRecentFilesCap);
        options.DefaultSaveFormat.Should().Be(FreePOptions.FxpDefaultFormat);
        store.LastError.Should().BeNull();
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var path = Path.Combine(_tempDir, "nested", "settings.json");
        var store = ApplicationOptionsStore<FreePOptions>.ForPath(path);

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
        var store = ApplicationOptionsStore<FreePOptions>.ForPath(path);

        var options = store.Load();

        options.RecentFilesCap.Should().Be(FreePOptions.MaxRecentFilesCap);
        options.DefaultSaveFormat.Should().Be(FreePOptions.FxpDefaultFormat);
        options.UiLanguage.Should().Be("en-GB");
    }

    [Theory]
    [InlineData("  en-us  ", "en-US")]
    [InlineData(" QPS-PLOC ", "qps-ploc")]
    [InlineData("not-a-culture", "")]
    public void Normalize_CanonicalizesUiLanguage(string input, string expected)
    {
        var options = new FreePOptions { UiLanguage = input };

        options.Normalize();

        options.UiLanguage.Should().Be(expected);
    }

    [Fact]
    public void Create_ResolvesUnderFreePProductFolder()
    {
        var provider = new TestApplicationDataPathProvider(_tempDir);

        var store = ApplicationOptionsStore<FreePOptions>.Create(provider);

        // The test assembly installs AppProduct = "FreeP" (AppProductTestDefaults).
        store.StorePath.Should().Be(Path.Combine(_tempDir, "FreeP", ApplicationOptionsStore<FreePOptions>.DefaultFileName));
    }

    private sealed class TestApplicationDataPathProvider(string path) : IApplicationDataPathProvider
    {
        public string GetApplicationDataDirectory() => path;
    }
}

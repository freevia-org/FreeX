using FluentAssertions;
using Free.Shared.AppServices;

namespace FreeX.App.Services.Tests;

public sealed class JsonSettingsStoreTests
{
    private sealed class SampleSettings
    {
        public int RecentFilesCap { get; set; } = 10;
        public string DefaultFormat { get; set; } = ".docx";
        public string Language { get; set; } = "";
    }

    [Fact]
    public void GetProductFilePath_UsesProductDirectoryUnderApplicationData()
    {
        using var temp = new TestTemporaryDirectory();
        var provider = new TestApplicationDataPathProvider(temp.Path);

        var path = JsonSettingsStore<SampleSettings>.GetProductFilePath("settings.json", provider);

        // The test assembly installs AppProduct = "FreeX" (AppProductTestDefaults).
        path.Should().Be(Path.Combine(temp.Path, "FreeX", "settings.json"));
    }

    [Fact]
    public void ForProductFile_PrefersOverridePathWhenProvided()
    {
        using var temp = new TestTemporaryDirectory();
        var provider = new TestApplicationDataPathProvider(Path.Combine(temp.Path, "ignored"));
        var overridePath = Path.Combine(temp.Path, "custom-settings.json");

        var store = JsonSettingsStore<SampleSettings>.ForProductFile(
            "settings.json", provider, overridePath);

        store.StorePath.Should().Be(overridePath);
    }

    [Fact]
    public void Load_WhenFileMissing_ReturnsFreshDefaultsWithNoError()
    {
        using var temp = new TestTemporaryDirectory();
        var store = JsonSettingsStore<SampleSettings>.ForPath(
            Path.Combine(temp.Path, "does-not-exist.json"));

        var loaded = store.Load();

        loaded.RecentFilesCap.Should().Be(10);
        loaded.DefaultFormat.Should().Be(".docx");
        loaded.Language.Should().BeEmpty();
        store.LastError.Should().BeNull();
    }

    [Fact]
    public void SaveThenLoad_RoundTripsValuesAtomically()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "nested", "settings.json");
        var store = JsonSettingsStore<SampleSettings>.ForPath(path);

        var saved = store.Save(new SampleSettings
        {
            RecentFilesCap = 7,
            DefaultFormat = ".odt",
            Language = "uk-UA"
        });

        saved.Should().BeTrue();
        store.LastError.Should().BeNull();

        // Atomic write leaves exactly the target file behind (no stray temp files).
        Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(path)!)
            .Should().ContainSingle().Which.Should().Be(path);

        var reloaded = store.Load();
        reloaded.RecentFilesCap.Should().Be(7);
        reloaded.DefaultFormat.Should().Be(".odt");
        reloaded.Language.Should().Be("uk-UA");
        store.LastError.Should().BeNull();
    }

    [Fact]
    public void Load_WhenJsonIsCorrupt_ReturnsDefaultsWithObservableError()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "settings.json");
        File.WriteAllText(path, "{ not json at all");
        var store = JsonSettingsStore<SampleSettings>.ForPath(path);

        var loaded = store.Load();

        loaded.RecentFilesCap.Should().Be(10);
        loaded.DefaultFormat.Should().Be(".docx");
        store.LastError.Should().NotBeNull();
        store.LastError.Should().Contain("Failed to load settings");
        store.LastError.Should().Contain(path);
    }

    [Fact]
    public void Save_WhenTargetCannotBeWritten_ReturnsFalseWithObservableError()
    {
        using var temp = new TestTemporaryDirectory();
        // A directory at the target path makes the file write fail.
        var blockedPath = Path.Combine(temp.Path, "blocked");
        Directory.CreateDirectory(blockedPath);
        var store = JsonSettingsStore<SampleSettings>.ForPath(blockedPath);

        var saved = store.Save(new SampleSettings());

        saved.Should().BeFalse();
        store.LastError.Should().NotBeNull();
        store.LastError.Should().Contain("Failed to save settings");
        store.LastError.Should().Contain(blockedPath);
        // The failed atomic write must not leave a temp artifact behind.
        Directory.EnumerateFileSystemEntries(temp.Path, ".blocked.*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void Save_ClearsPreviousErrorOnSuccess()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "settings.json");
        var store = JsonSettingsStore<SampleSettings>.ForPath(path);

        File.WriteAllText(path, "{ broken");
        store.Load();
        store.LastError.Should().NotBeNull();

        store.Save(new SampleSettings { RecentFilesCap = 3 }).Should().BeTrue();
        store.LastError.Should().BeNull();
    }

    private sealed class TestApplicationDataPathProvider(string path) : IApplicationDataPathProvider
    {
        public string GetApplicationDataDirectory() => path;
    }
}

using System.Text.Json;
using FluentAssertions;
using FreeX.App.Services;

namespace FreeX.App.Services.Tests;

public sealed class RecentFilesStoreTests
{
    [Fact]
    public void AtomicWriteAllText_CreatesFileWithContentIncludingMissingDirectories()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "nested", "recent.json");

        AtomicFileWriter.WriteAllText(path, "payload");

        File.ReadAllText(path).Should().Be("payload");
    }

    [Fact]
    public void AtomicWriteAllText_OverwritesExistingFileAndLeavesNoTempArtifact()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "recent.json");

        AtomicFileWriter.WriteAllText(path, "first");
        AtomicFileWriter.WriteAllText(path, "second");

        File.ReadAllText(path).Should().Be("second");
        Directory.GetFiles(temp.Path).Should().ContainSingle().Which.Should().Be(path);
    }

    [Fact]
    public void AddOrUpdate_PersistsNewestFirstAndPreservesPinnedState()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "recent.json");
        var now = new DateTimeOffset(2026, 6, 7, 12, 0, 0, TimeSpan.Zero);
        var clockTicks = 0;
        var store = RecentFilesStore.Load(path, () => now.AddMinutes(clockTicks++));

        store.AddOrUpdate(@"C:\Work\Budget.xlsx");
        store.Pin(@"C:\Work\Budget.xlsx");
        store.AddOrUpdate(@"C:\Work\Budget.xlsx");

        var reloaded = RecentFilesStore.Load(path);
        reloaded.Entries.Should().ContainSingle();
        reloaded.Entries[0].Path.Should().Be(@"C:\Work\Budget.xlsx");
        reloaded.Entries[0].IsPinned.Should().BeTrue();
        reloaded.Entries[0].LastOpened.Should().Be(now.AddMinutes(1));
    }

    [Fact]
    public void AddOrUpdate_WithCustomCap_RetainsOnlyCappedUnpinnedEntries()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "recent.json");
        var now = new DateTimeOffset(2026, 6, 18, 9, 0, 0, TimeSpan.Zero);
        var clockTicks = 0;
        var store = RecentFilesStore.Load(path, () => now.AddMinutes(clockTicks++));

        // Register five files but cap retention at two unpinned entries (an app-configured cap).
        for (var i = 0; i < 5; i++)
            store.AddOrUpdate(Path.Combine(temp.Path, $"file{i}.docx"), maxRecentEntries: 2);

        var reloaded = RecentFilesStore.Load(path);
        reloaded.Entries.Should().HaveCount(2);
        // Newest-first: the two most recently added survive.
        reloaded.Entries[0].Path.Should().EndWith("file4.docx");
        reloaded.Entries[1].Path.Should().EndWith("file3.docx");
    }

    [Fact]
    public void AddOrUpdate_PersistsBookmarkedFileAccessIdentityAndPreservesPinnedState()
    {
        using var temp = new TestTemporaryDirectory();
        var storePath = Path.Combine(temp.Path, "recent.json");
        var workbookPath = Path.Combine(temp.Path, "Budget.fxl");
        var now = new DateTimeOffset(2026, 6, 8, 8, 30, 0, TimeSpan.Zero);
        var clockTicks = 0;
        var store = RecentFilesStore.Load(storePath, () => now.AddMinutes(clockTicks++));
        var firstIdentity = new WorkbookFileAccessIdentity(
            workbookPath,
            "macos-security-scoped-bookmark",
            "first-token");
        var newestIdentity = new WorkbookFileAccessIdentity(
            workbookPath,
            "macos-security-scoped-bookmark",
            "newest-token");

        store.AddOrUpdate(workbookPath, firstIdentity);
        store.Pin(workbookPath);
        store.AddOrUpdate(workbookPath, newestIdentity);

        var reloaded = RecentFilesStore.Load(storePath);
        reloaded.Entries.Should().ContainSingle();
        reloaded.Entries[0].Path.Should().Be(workbookPath);
        reloaded.Entries[0].IsPinned.Should().BeTrue();
        reloaded.Entries[0].LastOpened.Should().Be(now.AddMinutes(1));
        var reloadedIdentity = reloaded.Entries[0].FileAccessIdentity;
        reloadedIdentity.Should().NotBeNull();
        reloadedIdentity!.LocalPath.Should().Be(workbookPath);
        reloadedIdentity.BookmarkKind.Should().Be("macos-security-scoped-bookmark");
        reloadedIdentity.BookmarkPayload.Should().Be("newest-token");
    }

    [Fact]
    public void AddOrUpdate_PreservesExistingBookmarkedIdentityWhenIdentityIsNotProvided()
    {
        using var temp = new TestTemporaryDirectory();
        var storePath = Path.Combine(temp.Path, "recent.json");
        var workbookPath = Path.Combine(temp.Path, "Budget.fxl");
        var store = RecentFilesStore.Load(storePath);
        var identity = new WorkbookFileAccessIdentity(
            workbookPath,
            "macos-security-scoped-bookmark",
            "persisted-token");

        store.AddOrUpdate(workbookPath, identity);
        store.AddOrUpdate(workbookPath);

        var reloaded = RecentFilesStore.Load(storePath);
        reloaded.Entries.Should().ContainSingle();
        var reloadedIdentity = reloaded.Entries[0].FileAccessIdentity;
        reloadedIdentity.Should().NotBeNull();
        reloadedIdentity!.LocalPath.Should().Be(workbookPath);
        reloadedIdentity.BookmarkPayload.Should().Be("persisted-token");
    }

    [Fact]
    public void AddOrUpdate_OmitsPathOnlyIdentityFromRecentJson()
    {
        using var temp = new TestTemporaryDirectory();
        var storePath = Path.Combine(temp.Path, "recent.json");
        var workbookPath = Path.Combine(temp.Path, "Budget.fxl");
        var store = RecentFilesStore.Load(storePath);

        store.AddOrUpdate(workbookPath, WorkbookFileAccessIdentity.FromLocalPath(workbookPath));

        File.ReadAllText(storePath).Should().NotContain("FileAccessIdentity");
        RecentFilesStore.Load(storePath).Entries.Should().ContainSingle()
            .Which.FileAccessIdentity.Should().BeNull();
    }

    [Fact]
    public void AddOrUpdate_WithWindowsPathIdentityKeepsCaseInsensitiveBehavior()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "recent.json");
        var now = new DateTimeOffset(2026, 6, 8, 8, 0, 0, TimeSpan.Zero);
        var clockTicks = 0;
        var store = RecentFilesStore.Load(
            path,
            PlatformPathIdentityComparer.Windows,
            () => now.AddMinutes(clockTicks++));

        store.AddOrUpdate(@"C:\Work\Budget.xlsx");
        store.Pin(@"c:\work\budget.xlsx");
        store.AddOrUpdate("C:/WORK/BUDGET.xlsx");

        var reloaded = RecentFilesStore.Load(path, PlatformPathIdentityComparer.Windows);
        reloaded.Entries.Should().ContainSingle();
        reloaded.Entries[0].Path.Should().Be("C:/WORK/BUDGET.xlsx");
        reloaded.Entries[0].IsPinned.Should().BeTrue();
        reloaded.Entries[0].LastOpened.Should().Be(now.AddMinutes(1));
    }

    [Fact]
    public void AddOrUpdate_WithUnixPathIdentityPreservesCaseSensitiveDistinctPaths()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "recent.json");
        var store = RecentFilesStore.Load(
            path,
            PlatformPathIdentityComparer.Unix);

        store.AddOrUpdate("/Users/quinn/Work/Budget.xlsx");
        store.Pin("/Users/quinn/Work/Budget.xlsx");
        store.AddOrUpdate("/Users/quinn/Work/budget.xlsx");

        var reloaded = RecentFilesStore.Load(path, PlatformPathIdentityComparer.Unix);
        reloaded.Entries.Should().HaveCount(2);
        reloaded.Entries[0].Path.Should().Be("/Users/quinn/Work/budget.xlsx");
        reloaded.Entries[0].IsPinned.Should().BeFalse();
        reloaded.Entries[1].Path.Should().Be("/Users/quinn/Work/Budget.xlsx");
        reloaded.Entries[1].IsPinned.Should().BeTrue();
    }

    [Fact]
    public void AddOrUpdate_TrimsToRecentFileLimit()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "recent.json");
        var store = RecentFilesStore.Load(path);

        for (var index = 0; index < 30; index++)
            store.AddOrUpdate(Path.Combine(temp.Path, $"Book{index}.fxl"));

        var reloaded = RecentFilesStore.Load(path);
        reloaded.Entries.Should().HaveCount(25);
        reloaded.Entries.Select(entry => Path.GetFileName(entry.Path))
            .Should()
            .StartWith("Book29.fxl");
    }

    [Fact]
    public void GetDefaultStorePath_UsesApplicationDataPathProvider()
    {
        using var temp = new TestTemporaryDirectory();
        var provider = new TestApplicationDataPathProvider(temp.Path);

        var path = RecentFilesStore.GetDefaultStorePath(provider);

        path.Should().Be(Path.Combine(temp.Path, "FreeX", "recent.json"));
    }

    [Fact]
    public void Load_WithApplicationDataPathProviderPersistsUnderProviderRoot()
    {
        using var temp = new TestTemporaryDirectory();
        var provider = new TestApplicationDataPathProvider(temp.Path);
        var now = new DateTimeOffset(2026, 6, 7, 13, 0, 0, TimeSpan.Zero);
        var store = RecentFilesStore.Load(provider, () => now);

        store.AddOrUpdate(Path.Combine(temp.Path, "Book.fxl"));

        var storePath = Path.Combine(temp.Path, "FreeX", "recent.json");
        File.Exists(storePath).Should().BeTrue();
        var reloaded = RecentFilesStore.Load(provider);
        reloaded.Entries.Should().ContainSingle();
        reloaded.Entries[0].LastOpened.Should().Be(now);
    }

    [Fact]
    public void PlatformApplicationDataPathProvider_UsesMacOsApplicationSupportDirectory()
    {
        var home = Path.Combine("Users", "quinn");
        var provider = new PlatformApplicationDataPathProvider(
            isMacOsProvider: () => true,
            userProfilePathProvider: () => home,
            applicationDataPathProvider: () => "ignored");

        provider.GetApplicationDataDirectory()
            .Should()
            .Be(Path.Combine(home, "Library", "Application Support"));
    }

    [Fact]
    public void PlatformApplicationDataPathProvider_UsesApplicationDataDirectoryOutsideMacOs()
    {
        using var temp = new TestTemporaryDirectory();
        var provider = new PlatformApplicationDataPathProvider(
            isMacOsProvider: () => false,
            userProfilePathProvider: () => "ignored",
            applicationDataPathProvider: () => temp.Path);

        provider.GetApplicationDataDirectory().Should().Be(temp.Path);
    }

    [Fact]
    public void RecentFileEntry_SerializesLastOpenedWithUtcOffset()
    {
        var entry = new RecentFileEntry
        {
            Path = @"C:\Work\Budget.xlsx",
            LastOpened = new DateTimeOffset(2026, 5, 28, 12, 34, 56, TimeSpan.Zero),
        };

        var json = JsonSerializer.Serialize(entry);

        json.Should().Contain(@"""LastOpened"":""2026-05-28T12:34:56+00:00""");
    }

    [Fact]
    public void Load_WithLegacyRecentJsonWithoutFileAccessIdentity_RemainsValid()
    {
        using var temp = new TestTemporaryDirectory();
        var storePath = Path.Combine(temp.Path, "recent.json");
        var workbookPath = Path.Combine(temp.Path, "Legacy.fxl");
        var lastOpened = new DateTimeOffset(2026, 6, 8, 9, 15, 0, TimeSpan.Zero);
        File.WriteAllText(
            storePath,
            JsonSerializer.Serialize(new[]
            {
                new
                {
                    Path = workbookPath,
                    LastOpened = lastOpened,
                    IsPinned = true,
                }
            }));

        var store = RecentFilesStore.Load(storePath);

        store.Entries.Should().ContainSingle();
        store.Entries[0].Path.Should().Be(workbookPath);
        store.Entries[0].LastOpened.Should().Be(lastOpened);
        store.Entries[0].IsPinned.Should().BeTrue();
        store.Entries[0].FileAccessIdentity.Should().BeNull();
    }

    private sealed class TestApplicationDataPathProvider(string path) : IApplicationDataPathProvider
    {
        public string GetApplicationDataDirectory() => path;
    }
}

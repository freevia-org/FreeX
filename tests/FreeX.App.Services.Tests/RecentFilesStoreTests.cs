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
}

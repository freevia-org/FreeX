using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class RecentColorsStoreTests
{
    [Fact]
    public void NewStore_WithNoFile_StartsEmpty()
    {
        using var temp = new TestTemporaryDirectory();
        var store = new RecentColorsStore(StorePath(temp));

        store.Colors.Should().BeEmpty();
        store.Swatches.Should().BeEmpty();
    }

    [Fact]
    public void Remember_MovesColorToFrontDedupesAndPersists()
    {
        using var temp = new TestTemporaryDirectory();
        var path = StorePath(temp);
        var store = new RecentColorsStore(path);

        store.Remember(new CellColor(0x10, 0x20, 0x30));
        store.Remember(new CellColor(0xAA, 0xBB, 0xCC));
        store.Remember(new CellColor(0x10, 0x20, 0x30));

        store.Colors.Should().Equal(
            new CellColor(0x10, 0x20, 0x30),
            new CellColor(0xAA, 0xBB, 0xCC));

        // A fresh store reading the same path sees the persisted, deduped list.
        var reloaded = new RecentColorsStore(path);
        reloaded.Colors.Should().Equal(
            new CellColor(0x10, 0x20, 0x30),
            new CellColor(0xAA, 0xBB, 0xCC));
        reloaded.Swatches.Select(swatch => swatch.Hex).Should().Equal("#102030", "#AABBCC");
    }

    [Fact]
    public void Remember_HonorsCapacity()
    {
        using var temp = new TestTemporaryDirectory();
        var store = new RecentColorsStore(StorePath(temp), capacity: 2);

        store.Remember(new CellColor(1, 1, 1));
        store.Remember(new CellColor(2, 2, 2));
        store.Remember(new CellColor(3, 3, 3));

        store.Capacity.Should().Be(2);
        store.Colors.Should().Equal(
            new CellColor(3, 3, 3),
            new CellColor(2, 2, 2));
    }

    [Fact]
    public void NewStore_LoadsAndCapsExistingFile()
    {
        using var temp = new TestTemporaryDirectory();
        var path = StorePath(temp);
        File.WriteAllText(path, "[\"#010203\",\"#AABBCC\",\"#010203\",\"#112233\"]");

        var store = new RecentColorsStore(path, capacity: 2);

        store.Colors.Should().Equal(
            new CellColor(0x01, 0x02, 0x03),
            new CellColor(0xAA, 0xBB, 0xCC));
    }

    [Fact]
    public void NewStore_WithCorruptFile_StartsEmptyWithoutThrowing()
    {
        using var temp = new TestTemporaryDirectory();
        var path = StorePath(temp);
        File.WriteAllText(path, "{ not json ]");

        var store = new RecentColorsStore(path);

        store.Colors.Should().BeEmpty();
    }

    [Fact]
    public void NewStore_NonPositiveCapacity_FallsBackToDefault()
    {
        using var temp = new TestTemporaryDirectory();
        var store = new RecentColorsStore(StorePath(temp), capacity: 0);

        store.Capacity.Should().Be(CellColorPalettePlanner.DefaultRecentColorCapacity);
    }

    [Fact]
    public void Persistence_DelegatesJsonAndAtomicWriteCeremonyToSharedStore()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find(
            "src", "FreeX.App.Services", "RecentColorsStore.cs"));

        source.Should().Contain("JsonSettingsStore<List<string>>")
            .And.NotContain("JsonSerializer")
            .And.NotContain("File.ReadAllText")
            .And.NotContain("AtomicFileWriter");
    }

    [Fact]
    public void Remember_WhenPersistenceFails_RemainsBestEffortAndKeepsInMemoryColor()
    {
        using var temp = new TestTemporaryDirectory();
        var blockedPath = Path.Combine(temp.Path, "blocked");
        Directory.CreateDirectory(blockedPath);
        var store = new RecentColorsStore(blockedPath);

        var remember = () => store.Remember(new CellColor(0x12, 0x34, 0x56));

        remember.Should().NotThrow();
        store.Colors.Should().Equal(new CellColor(0x12, 0x34, 0x56));
    }

    [Fact]
    public void Remember_ReloadsFromDiskFirst_SoASecondWindowsColorSurvives()
    {
        // Simulates two FreeX windows (e.g. Avalonia "View > New Window"), each owning its own
        // RecentColorsStore instance loaded from the same file at construction time.
        using var temp = new TestTemporaryDirectory();
        var path = StorePath(temp);
        var windowA = new RecentColorsStore(path);
        var windowB = new RecentColorsStore(path);

        // Window A picks a custom color and persists it.
        windowA.Remember(new CellColor(0x10, 0x20, 0x30));

        // Window B, still holding its stale in-memory (empty) list from construction time, picks a
        // different custom color.
        windowB.Remember(new CellColor(0xAA, 0xBB, 0xCC));

        // Window A's color must not be silently discarded by window B's write.
        var reloaded = new RecentColorsStore(path);
        reloaded.Colors.Should().Equal(
            new CellColor(0xAA, 0xBB, 0xCC),
            new CellColor(0x10, 0x20, 0x30));
    }

    [Fact]
    public void Remember_SameInstanceCalledTwice_StillDedupesAndOrdersCorrectly()
    {
        // Sibling/no-regression case: a single window remembering colors in sequence (the common,
        // non-multi-window path) must keep behaving exactly as before the reload-before-write fix.
        using var temp = new TestTemporaryDirectory();
        var path = StorePath(temp);
        var store = new RecentColorsStore(path);

        store.Remember(new CellColor(0x10, 0x20, 0x30));
        store.Remember(new CellColor(0xAA, 0xBB, 0xCC));
        store.Remember(new CellColor(0x10, 0x20, 0x30));

        store.Colors.Should().Equal(
            new CellColor(0x10, 0x20, 0x30),
            new CellColor(0xAA, 0xBB, 0xCC));

        var reloaded = new RecentColorsStore(path);
        reloaded.Colors.Should().Equal(
            new CellColor(0x10, 0x20, 0x30),
            new CellColor(0xAA, 0xBB, 0xCC));
    }

    private static string StorePath(TestTemporaryDirectory temp) =>
        Path.Combine(temp.Path, "recent-colors.json");
}

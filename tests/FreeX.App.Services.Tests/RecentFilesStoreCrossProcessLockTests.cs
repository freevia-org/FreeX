using System.Text.Json;
using FluentAssertions;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R115: FreeX has no single-instance enforcement (no Mutex/named-pipe activation redirect
/// anywhere under src/), so a user can have two separate FreeX.exe processes running concurrently
/// against the same recent.json (launching FreeX twice, double-clicking two workbook files that
/// each spawn a new process, etc.). Before this fix, <see cref="RecentFilesStore"/>'s mutators
/// (AddOrUpdate/Pin/Unpin/Remove) did a plain load-modify-write with no re-read immediately before
/// the write and no cross-process coordination: if process B's own mutation raced in between
/// process A's earlier load and A's save, B's save silently discarded A's just-written entry (a
/// classic lost-update/TOCTOU race), because B's Save() rewrote the whole file from B's stale
/// in-memory snapshot.
///
/// The fix adds (a) a fresh-from-disk reload immediately before every mutator applies its change
/// (<c>ReloadEntriesLocked</c>) and (b) a cross-process exclusive lock around the whole
/// reload-mutate-save sequence (<c>AcquireCrossProcessLock</c>, an exclusively-opened sibling
/// ".lock" file — FileShare.None is honored across separate OS processes, unlike the
/// in-process-only <c>_sync</c> monitor). Together these mean a concurrent writer's save is
/// merged instead of clobbered, whether the two writers are two windows in one process (already
/// covered by <see cref="RecentFilesStoreMultiWindowReloadTests"/> for the H58 caller-side fix)
/// or two wholly separate processes (this class).
///
/// These tests exercise two independently-<see cref="RecentFilesStore.Load"/>-ed instances against
/// the same backing file — the same emulation technique the existing H58 multi-window tests use —
/// which is exactly the shape a second FreeX.exe process reduces to at the store/service layer.
/// </summary>
public sealed class RecentFilesStoreCrossProcessLockTests
{
    [Fact]
    public void R115_AddOrUpdate_ReloadsFromDiskBeforeApplying_PreservesSiblingWrite()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "recent.json");

        // "Process A" and "process B" each load their own snapshot up front.
        var processA = RecentFilesStore.Load(path);
        var processB = RecentFilesStore.Load(path); // Stale: loaded before Z exists.

        processA.AddOrUpdate(@"C:\Docs\Z.xlsx");

        // "Process B" mutates through its stale, never-explicitly-reloaded instance. Before the
        // fix this clobbered recent.json with a list that never contained Z, silently losing
        // process A's write the instant process B saved.
        processB.AddOrUpdate(@"C:\Docs\W.xlsx");

        var final = RecentFilesStore.Load(path);
        final.Entries.Select(e => e.Path).Should().Contain(@"C:\Docs\Z.xlsx",
            "AddOrUpdate must reload from disk immediately before applying its change so a sibling process's earlier write survives");
        final.Entries.Select(e => e.Path).Should().Contain(@"C:\Docs\W.xlsx");
    }

    [Fact]
    public void R115_Pin_ReloadsFromDiskBeforeApplying_PreservesSiblingAddOrUpdateWrite()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "recent.json");

        RecentFilesStore.Load(path).AddOrUpdate(@"C:\Docs\Existing.xlsx");

        // "Process A" and "process B" each load a snapshot that already contains Existing.xlsx.
        var processA = RecentFilesStore.Load(path);
        var processB = RecentFilesStore.Load(path);

        processA.AddOrUpdate(@"C:\Docs\New.xlsx");

        // "Process B" pins through its stale instance (loaded before New.xlsx was added).
        processB.Pin(@"C:\Docs\Existing.xlsx");

        var final = RecentFilesStore.Load(path);
        final.Entries.Should().ContainSingle(e => e.Path == @"C:\Docs\Existing.xlsx" && e.IsPinned);
        final.Entries.Select(e => e.Path).Should().Contain(@"C:\Docs\New.xlsx",
            "Pin must reload from disk immediately before applying its change so a sibling process's earlier AddOrUpdate survives");
    }

    [Fact]
    public void R115_Unpin_ReloadsFromDiskBeforeApplying_PreservesSiblingWrite()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "recent.json");

        var seed = RecentFilesStore.Load(path);
        seed.AddOrUpdate(@"C:\Docs\Existing.xlsx");
        seed.Pin(@"C:\Docs\Existing.xlsx");

        var processA = RecentFilesStore.Load(path);
        var processB = RecentFilesStore.Load(path);

        processA.AddOrUpdate(@"C:\Docs\New.xlsx");

        processB.Unpin(@"C:\Docs\Existing.xlsx");

        var final = RecentFilesStore.Load(path);
        final.Entries.Should().ContainSingle(e => e.Path == @"C:\Docs\Existing.xlsx" && !e.IsPinned);
        final.Entries.Select(e => e.Path).Should().Contain(@"C:\Docs\New.xlsx",
            "Unpin must reload from disk immediately before applying its change so a sibling process's earlier AddOrUpdate survives");
    }

    [Fact]
    public void R115_Remove_ReloadsFromDiskBeforeApplying_PreservesSiblingWrite()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "recent.json");

        var seed = RecentFilesStore.Load(path);
        seed.AddOrUpdate(@"C:\Docs\Existing.xlsx");
        seed.AddOrUpdate(@"C:\Docs\ToRemove.xlsx");

        var processA = RecentFilesStore.Load(path);
        var processB = RecentFilesStore.Load(path);

        processA.AddOrUpdate(@"C:\Docs\New.xlsx");

        processB.Remove(@"C:\Docs\ToRemove.xlsx");

        var final = RecentFilesStore.Load(path);
        final.Entries.Select(e => e.Path).Should().NotContain(@"C:\Docs\ToRemove.xlsx");
        final.Entries.Select(e => e.Path).Should().Contain(@"C:\Docs\Existing.xlsx");
        final.Entries.Select(e => e.Path).Should().Contain(@"C:\Docs\New.xlsx",
            "Remove must reload from disk immediately before applying its change so a sibling process's earlier AddOrUpdate survives");
    }

    [Fact]
    public async Task R115_ConcurrentAddOrUpdate_AcrossSeparateProcessLikeInstances_NoLostUpdates()
    {
        // A genuinely concurrent variant (real threads, not just interleaved sequential calls) of
        // the scenarios above, exercising the cross-process file lock itself rather than only the
        // reload-before-mutate merge: two independently-loaded store instances (standing in for two
        // separate FreeX.exe processes; each has its own in-process _sync monitor, so only the
        // cross-process file lock can serialize them) hammer the same recent.json at the same time.
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "recent.json");

        const int countPerWriter = 15;
        using var barrier = new Barrier(2);

        void RunWriter(string prefix)
        {
            var store = RecentFilesStore.Load(path);
            barrier.SignalAndWait();
            for (var i = 0; i < countPerWriter; i++)
                store.AddOrUpdate($@"C:\Docs\{prefix}{i}.xlsx", maxRecentEntries: 100);
        }

        var t1 = Task.Run(() => RunWriter("A"));
        var t2 = Task.Run(() => RunWriter("B"));
        await Task.WhenAll(t1, t2);

        // Read the raw persisted file rather than through RecentFilesStore.Load(path): Load()'s
        // public contract always re-applies the *default* 25-entry cap (LimitForPersistence with no
        // override), regardless of the 100 this test's writers used — that default-cap-on-load
        // behavior is pre-existing and orthogonal to what's under test here (no lost updates across
        // concurrent writers), so asserting through the raw file avoids conflating the two.
        var rawEntries = JsonSerializer.Deserialize<List<RecentFileEntry>>(File.ReadAllText(path))!;
        var paths = rawEntries.Select(e => e.Path).ToHashSet();

        for (var i = 0; i < countPerWriter; i++)
        {
            paths.Should().Contain($@"C:\Docs\A{i}.xlsx");
            paths.Should().Contain($@"C:\Docs\B{i}.xlsx");
        }
    }
}

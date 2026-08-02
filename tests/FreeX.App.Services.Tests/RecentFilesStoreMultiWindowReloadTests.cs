using FluentAssertions;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Regression coverage for H58: with multiple windows sharing one process (Excel-style
/// "View &gt; New Window"), each window historically cached its own long-lived
/// <see cref="RecentFilesStore"/> instance loaded once at construction. Writing straight through
/// that stale cache (instead of reloading from disk immediately before every mutation, as
/// <see cref="RecentFilesStore"/>'s own load-then-mutate contract requires) let a second window's
/// write silently clobber a first window's earlier write to the shared recent.json (lost update).
///
/// These tests exercise the store/service contract directly: two independently-loaded
/// <see cref="RecentFilesStore"/> instances against the same backing file emulate "window A" and
/// "window B". The fix in <c>MainWindow.Backstage.cs</c> (<c>ReloadRecentFilesStore()</c>) reloads
/// a fresh instance immediately before every read/Pin/Unpin/Remove/registration, which is exactly
/// the pattern asserted here: reloading before the second write must observe the first write.
/// </summary>
public sealed class RecentFilesStoreMultiWindowReloadTests
{
    [Fact]
    public void ReloadBeforeSecondWrite_PreservesFirstWindowsAddOrUpdate()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "recent.json");

        // Window A and Window B each load their own snapshot at "construction" time.
        var windowA = RecentFilesStore.Load(path);
        var windowB = RecentFilesStore.Load(path);

        // Window A opens file Z, which registers it as a recent file and saves.
        windowA.AddOrUpdate(@"C:\Docs\Z.xlsx");

        // Window B now pins an unrelated (nonexistent) entry. If Window B mutated through its
        // stale cached instance (loaded before Z existed), Save() would overwrite recent.json
        // with a list that never contained Z, silently losing Window A's write.
        // The fix reloads fresh from disk immediately before mutating, so it must see Z.
        var reloadedForWindowB = RecentFilesStore.Load(path);
        reloadedForWindowB.Entries.Should().ContainSingle(e => e.Path == @"C:\Docs\Z.xlsx");
        reloadedForWindowB.Pin(@"C:\Docs\Z.xlsx");

        var final = RecentFilesStore.Load(path);
        final.Entries.Should().ContainSingle(e => e.Path == @"C:\Docs\Z.xlsx" && e.IsPinned);
    }

    [Fact]
    public void StaleCachedInstance_WithoutExplicitReload_NoLongerLosesSiblingWindowsEntry()
    {
        // Historically this demonstrated the bug that made the MainWindow.Backstage.cs-level
        // ReloadRecentFilesStore() fix (H58) necessary: mutating through a stale, un-reloaded
        // instance clobbered a sibling window's later write, because the mutator rewrote the whole
        // file from its own stale in-memory snapshot. RecentFilesStore's mutators now reload from
        // disk immediately before applying their change (see ReloadEntriesLocked/R115 cross-process
        // lock fix), so even a caller that never explicitly reloads no longer loses a sibling's
        // write — this is now a belt-and-suspenders guarantee at the store level, not just at the
        // MainWindow call-site level.
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "recent.json");

        var windowA = RecentFilesStore.Load(path);
        var windowB = RecentFilesStore.Load(path); // Loaded before Z exists — the "stale" cache.

        windowA.AddOrUpdate(@"C:\Docs\Z.xlsx");

        // Window B mutates through its stale, never-reloaded instance. The store itself now
        // reloads from disk before applying the mutation, so Z survives.
        windowB.AddOrUpdate(@"C:\Docs\W.xlsx");

        var final = RecentFilesStore.Load(path);
        final.Entries.Select(e => e.Path).Should().Contain(@"C:\Docs\Z.xlsx",
            "the store reloads from disk immediately before every mutation, so a sibling's earlier write survives");
        final.Entries.Select(e => e.Path).Should().Contain(@"C:\Docs\W.xlsx");
    }

    [Fact]
    public void RegisterIfNeeded_WithLoadStoreDelegate_ObservesPriorSiblingWrite()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "recent.json");

        RecentFilesStore.Load(path).AddOrUpdate(@"C:\Docs\Existing.xlsx");

        // Mirrors MainWindow.Backstage.cs's fixed call sites: pass a Func<RecentFilesStore> that
        // reloads from disk immediately before writing, rather than a cached instance.
        var result = RecentFileRegistrationService.RegisterIfNeeded(
            () => RecentFilesStore.Load(path),
            new RecentFileRegistrationRequest(@"C:\Docs\New.xlsx"));

        result.Registered.Should().BeTrue();

        var final = RecentFilesStore.Load(path);
        final.Entries.Select(e => e.Path).Should().Contain(@"C:\Docs\Existing.xlsx");
        final.Entries.Select(e => e.Path).Should().Contain(@"C:\Docs\New.xlsx");
    }
}

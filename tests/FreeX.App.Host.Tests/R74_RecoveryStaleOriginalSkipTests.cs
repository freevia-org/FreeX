using System.IO;
using System.IO.Compression;
using System.Reflection;
using FluentAssertions;
using Free.Shared.AppServices;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R74-services-autosave-recovery-4-1: startup recovery must not offer a
/// candidate whose ORIGINAL on-disk file was saved MORE RECENTLY than the crash snapshot itself.
/// Accepting such a candidate would silently overwrite a newer manual save with stale recovered
/// content. App.FilterCandidatesWithNewerOriginal drops (and deletes) any such candidate before
/// OfferStartupRecovery ever offers it, while still offering a candidate whose original is older
/// than the snapshot or missing entirely.
/// </summary>
public sealed class R74_RecoveryStaleOriginalSkipTests
{
    // Real snapshots are OPC/ZIP packages; EnumerateCandidates validates that, so test snapshots
    // must be readable archives (matching AutosaveSnapshotStoreTests' WriteSnapshotZip pattern).
    private static void WriteSnapshotZip(string path)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        zip.CreateEntry("[Content_Types].xml");
    }

    private static AutosaveRecoveryCandidate WriteCandidate(
        AutosaveSnapshotStore store,
        string snapshotId,
        string? originalFilePath,
        string? displayName,
        DateTimeOffset timestamp)
    {
        var snapshotPath = store.GetSnapshotPath(snapshotId);
        var sidecarPath = store.GetSidecarPath(snapshotId);
        WriteSnapshotZip(snapshotPath);
        var sidecar = new AutosaveSidecar
        {
            OriginalFilePath = originalFilePath,
            DisplayName = displayName,
            TimestampUtc = timestamp.ToString("O"),
            SnapshotId = snapshotId
        };
        File.WriteAllText(sidecarPath, AutosaveSnapshotStore.SerializeSidecar(sidecar));
        return new AutosaveRecoveryCandidate(snapshotPath, sidecarPath, sidecar);
    }

    private static IReadOnlyList<AutosaveRecoveryCandidate> InvokeFilter(
        IReadOnlyList<AutosaveRecoveryCandidate> candidates)
    {
        var method = typeof(App).GetMethod(
            "FilterCandidatesWithNewerOriginal",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        return (IReadOnlyList<AutosaveRecoveryCandidate>)method!.Invoke(null, [candidates])!;
    }

    [Fact]
    public void Filter_DropsCandidateWhoseOriginalWasSavedAfterTheSnapshot()
    {
        using var temp = new TestTemporaryDirectory("FreeX.R74.Recovery-");
        var store = new AutosaveSnapshotStore(temp.Path);
        var originalPath = System.IO.Path.Combine(temp.Path, "Book1.fxl");
        File.WriteAllText(originalPath, "newer-manual-save");

        var snapshotTimeUtc = DateTimeOffset.UtcNow.AddMinutes(-10);
        // The on-disk original was saved AFTER the crash snapshot was taken.
        File.SetLastWriteTimeUtc(originalPath, snapshotTimeUtc.AddMinutes(5).UtcDateTime);

        var stale = WriteCandidate(store, "recovery-1-w0", originalPath, "Book1", snapshotTimeUtc);

        var filtered = InvokeFilter([stale]);

        filtered.Should().BeEmpty(
            "the on-disk original is newer than the crash snapshot, so recovering it would clobber the newer manual save");
        File.Exists(stale.SnapshotPath).Should().BeFalse("a superseded candidate is deleted, not left to be silently re-skipped forever");
        File.Exists(stale.SidecarPath).Should().BeFalse();
    }

    [Fact]
    public void Filter_KeepsCandidateWhoseOriginalIsOlderThanTheSnapshot()
    {
        using var temp = new TestTemporaryDirectory("FreeX.R74.Recovery-");
        var store = new AutosaveSnapshotStore(temp.Path);
        var originalPath = System.IO.Path.Combine(temp.Path, "Book2.fxl");
        File.WriteAllText(originalPath, "stale-on-disk-copy");

        var snapshotTimeUtc = DateTimeOffset.UtcNow;
        // The on-disk original predates the crash snapshot — the snapshot is genuinely newer.
        File.SetLastWriteTimeUtc(originalPath, snapshotTimeUtc.AddMinutes(-30).UtcDateTime);

        var candidate = WriteCandidate(store, "recovery-2-w0", originalPath, "Book2", snapshotTimeUtc);

        var filtered = InvokeFilter([candidate]);

        filtered.Should().ContainSingle("the snapshot is newer than what is on disk, so it must still be offered");
        filtered[0].SnapshotPath.Should().Be(candidate.SnapshotPath);
        File.Exists(candidate.SnapshotPath).Should().BeTrue();
    }

    [Fact]
    public void Filter_KeepsCandidateWhoseOriginalFileIsMissing()
    {
        using var temp = new TestTemporaryDirectory("FreeX.R74.Recovery-");
        var store = new AutosaveSnapshotStore(temp.Path);
        var missingOriginalPath = System.IO.Path.Combine(temp.Path, "DoesNotExist.fxl");

        var candidate = WriteCandidate(store, "recovery-3-w0", missingOriginalPath, "Book3", DateTimeOffset.UtcNow);

        var filtered = InvokeFilter([candidate]);

        filtered.Should().ContainSingle("a missing original (never saved / moved / deleted) must not block recovery");
        File.Exists(candidate.SnapshotPath).Should().BeTrue();
    }

    [Fact]
    public void Filter_MixedList_OnlyDropsTheStaleOne()
    {
        using var temp = new TestTemporaryDirectory("FreeX.R74.Recovery-");
        var store = new AutosaveSnapshotStore(temp.Path);

        var okOriginalPath = System.IO.Path.Combine(temp.Path, "Ok.fxl");
        File.WriteAllText(okOriginalPath, "ok");
        var now = DateTimeOffset.UtcNow;
        File.SetLastWriteTimeUtc(okOriginalPath, now.AddMinutes(-30).UtcDateTime);
        var ok = WriteCandidate(store, "recovery-4-w0", okOriginalPath, "Ok", now);

        var staleOriginalPath = System.IO.Path.Combine(temp.Path, "Stale.fxl");
        File.WriteAllText(staleOriginalPath, "newer");
        File.SetLastWriteTimeUtc(staleOriginalPath, now.AddMinutes(30).UtcDateTime);
        var stale = WriteCandidate(store, "recovery-4-w1", staleOriginalPath, "Stale", now);

        var filtered = InvokeFilter([ok, stale]);

        filtered.Should().ContainSingle();
        filtered[0].SnapshotPath.Should().Be(ok.SnapshotPath);
        File.Exists(ok.SnapshotPath).Should().BeTrue();
        File.Exists(stale.SnapshotPath).Should().BeFalse();
    }
}

using System.IO.Compression;
using FluentAssertions;
using FreeX.App.Services;

namespace FreeX.App.Services.Tests;

public sealed class AutosaveSnapshotStoreTests
{
    // Real snapshots are OPC/ZIP packages and EnumerateCandidates now validates that, so test
    // snapshots must be readable archives (not plain text) to be enumerated as valid candidates.
    private static void WriteSnapshotZip(string path)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        zip.CreateEntry("[Content_Types].xml");
    }

    // ── ShouldSnapshot ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false, 0, -1, false)]   // not dirty → no snapshot
    [InlineData(true, 0, 0, false)]    // dirty but generation unchanged → no snapshot
    [InlineData(true, 1, 0, true)]     // dirty + new generation → snapshot
    [InlineData(true, 5, 3, true)]     // dirty + higher generation → snapshot
    [InlineData(false, 5, 3, false)]   // not dirty even if generation changed → no snapshot
    public void ShouldSnapshot_ReturnsExpected(bool dirty, int current, int lastSnapshot, bool expected)
    {
        AutosaveSnapshotStore.ShouldSnapshot(dirty, current, lastSnapshot)
            .Should().Be(expected);
    }

    // ── Path resolution ───────────────────────────────────────────────────────

    [Fact]
    public void GetSnapshotPath_ContainsRecoveryDirectoryAndId()
    {
        var store = new AutosaveSnapshotStore(@"C:\FreeX\Recovery");

        store.GetSnapshotPath("recovery-1234-w0")
            .Should().Be(@"C:\FreeX\Recovery\recovery-1234-w0.fxl");
    }

    [Fact]
    public void GetSidecarPath_ContainsRecoveryDirectoryAndId()
    {
        var store = new AutosaveSnapshotStore(@"C:\FreeX\Recovery");

        store.GetSidecarPath("recovery-1234-w0")
            .Should().Be(@"C:\FreeX\Recovery\recovery-1234-w0.sidecar.json");
    }

    [Fact]
    public void CreateDefault_UsesProvidedApplicationDataPathProvider()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var provider = new TestApplicationDataPathProvider(root);

        var store = AutosaveSnapshotStore.CreateDefault(provider);

        store.GetSnapshotPath("recovery-1234-w0").Should().Be(Path.Combine(
            root,
            AppStoragePathPlanner.ProductDirectoryName,
            AutosaveSnapshotStore.RecoveryDirectoryName,
            "recovery-1234-w0.fxl"));
    }

    // ── Sidecar serialization round-trip ──────────────────────────────────────

    [Fact]
    public void SerializeSidecar_ThenDeserialize_RoundTrips()
    {
        var sidecar = new AutosaveSidecar
        {
            OriginalFilePath = @"C:\Users\alice\budget.xlsx",
            DisplayName = "budget",
            TimestampUtc = DateTimeOffset.UtcNow.ToString("O"),
            SnapshotId = "recovery-42-w0"
        };

        var json = AutosaveSnapshotStore.SerializeSidecar(sidecar);
        var restored = AutosaveSnapshotStore.TryDeserializeSidecar(json);

        restored.Should().NotBeNull();
        restored!.OriginalFilePath.Should().Be(sidecar.OriginalFilePath);
        restored.DisplayName.Should().Be(sidecar.DisplayName);
        restored.SnapshotId.Should().Be(sidecar.SnapshotId);
    }

    [Fact]
    public void TryDeserializeSidecar_ReturnsNullForEmpty()
    {
        AutosaveSnapshotStore.TryDeserializeSidecar("").Should().BeNull();
        AutosaveSnapshotStore.TryDeserializeSidecar("   ").Should().BeNull();
        AutosaveSnapshotStore.TryDeserializeSidecar(null!).Should().BeNull();
    }

    [Fact]
    public void TryDeserializeSidecar_ReturnsNullForCorruptJson()
    {
        AutosaveSnapshotStore.TryDeserializeSidecar("not-json{{{{").Should().BeNull();
    }

    [Fact]
    public void TryDeserializeSidecar_ToleratesMissingFields()
    {
        // Minimal valid JSON object — extra fields and missing optional fields should not throw.
        var json = """{"snapshotId":"x"}""";
        var sidecar = AutosaveSnapshotStore.TryDeserializeSidecar(json);

        sidecar.Should().NotBeNull();
        sidecar!.SnapshotId.Should().Be("x");
        sidecar.OriginalFilePath.Should().BeNull();
    }

    // ── Candidate enumeration ─────────────────────────────────────────────────

    [Fact]
    public void EnumerateCandidates_EmptyWhenDirectoryAbsent()
    {
        var store = new AutosaveSnapshotStore(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        store.EnumerateCandidates().Should().BeEmpty();
    }

    [Fact]
    public void EnumerateCandidates_ReturnsCandidateWithValidSidecar()
    {
        using var dir = new TestTemporaryDirectory();
        var store = new AutosaveSnapshotStore(dir.Path);

        const string snapshotId = "recovery-99-w0";
        var snapshotPath = store.GetSnapshotPath(snapshotId);
        var sidecarPath = store.GetSidecarPath(snapshotId);

        // Write a minimal .fxl placeholder and a sidecar.
        WriteSnapshotZip(snapshotPath);
        var sidecar = new AutosaveSidecar
        {
            OriginalFilePath = @"C:\test.xlsx",
            DisplayName = "test",
            SnapshotId = snapshotId
        };
        File.WriteAllText(sidecarPath, AutosaveSnapshotStore.SerializeSidecar(sidecar));

        var candidates = store.EnumerateCandidates();

        candidates.Should().HaveCount(1);
        candidates[0].Sidecar.OriginalFilePath.Should().Be(@"C:\test.xlsx");
        candidates[0].Sidecar.DisplayName.Should().Be("test");
    }

    [Fact]
    public void EnumerateCandidates_SkipsSnapshotWithMissingSidecar()
    {
        using var dir = new TestTemporaryDirectory();
        var store = new AutosaveSnapshotStore(dir.Path);

        WriteSnapshotZip(store.GetSnapshotPath("recovery-1-w0"));
        // No sidecar written.

        store.EnumerateCandidates().Should().BeEmpty();
    }

    // ── Write-order regression: sidecar before snapshot ───────────────────────
    // Regression for: TryWriteSnapshot previously moved the snapshot into place BEFORE
    // writing the sidecar.  If the process died between those two steps the snapshot was
    // invisible to recovery (EnumerateCandidates requires both files).  The fix writes the
    // sidecar first, so a mid-write crash leaves a sidecar-only entry (which is safely
    // skipped by EnumerateCandidates) rather than a sidecar-less snapshot (which is lost).

    [Fact]
    public void EnumerateCandidates_SidecarWithoutSnapshot_IsSkippedSafely()
    {
        // Simulate a crash after the sidecar was written but before the snapshot was moved
        // into place (the correct write order).  Only the sidecar exists.
        // EnumerateCandidates should return no candidates — no data is lost because the
        // crash happened before the snapshot was committed.
        using var dir = new TestTemporaryDirectory();
        var store = new AutosaveSnapshotStore(dir.Path);

        const string snapshotId = "recovery-crash-w0";
        // Write only the sidecar — the snapshot file is absent (simulates mid-write crash
        // using the sidecar-first ordering).
        var sidecar = new AutosaveSidecar
        {
            OriginalFilePath = @"C:\test.xlsx",
            DisplayName = "test",
            SnapshotId = snapshotId
        };
        File.WriteAllText(store.GetSidecarPath(snapshotId), AutosaveSnapshotStore.SerializeSidecar(sidecar));
        // No snapshot file written.

        // Should not surface a candidate — the snapshot data was never committed.
        store.EnumerateCandidates().Should().BeEmpty(
            "a sidecar without a snapshot means the write was interrupted before the snapshot was moved into place; no recoverable data exists");
    }

    [Fact]
    public void EnumerateCandidates_SnapshotWithoutSidecar_IsSkipped_NotLostData()
    {
        // This is the BAD write order (snapshot moved first, sidecar not yet written).
        // EnumerateCandidates skips it — the snapshot data is invisible to recovery.
        // This test documents the contract that must be avoided by always writing the
        // sidecar before moving the snapshot into place.
        using var dir = new TestTemporaryDirectory();
        var store = new AutosaveSnapshotStore(dir.Path);

        const string snapshotId = "recovery-badorder-w0";
        WriteSnapshotZip(store.GetSnapshotPath(snapshotId));
        // Sidecar not yet written (simulates the OLD broken ordering after crash).

        store.EnumerateCandidates().Should().BeEmpty(
            "a snapshot without a sidecar is skipped — demonstrating why sidecar must be written first");
    }

    [Fact]
    public void EnumerateCandidates_SkipsCorruptSidecar()
    {
        using var dir = new TestTemporaryDirectory();
        var store = new AutosaveSnapshotStore(dir.Path);

        const string snapshotId = "recovery-2-w0";
        WriteSnapshotZip(store.GetSnapshotPath(snapshotId));
        File.WriteAllText(store.GetSidecarPath(snapshotId), "corrupt{{json");

        store.EnumerateCandidates().Should().BeEmpty();
    }

    // ── Delete operations ─────────────────────────────────────────────────────

    [Fact]
    public void DeleteSnapshot_RemovesBothFiles()
    {
        using var dir = new TestTemporaryDirectory();
        var store = new AutosaveSnapshotStore(dir.Path);

        const string snapshotId = "recovery-3-w0";
        WriteSnapshotZip(store.GetSnapshotPath(snapshotId));
        File.WriteAllText(store.GetSidecarPath(snapshotId), "{}");

        store.DeleteSnapshot(snapshotId);

        File.Exists(store.GetSnapshotPath(snapshotId)).Should().BeFalse();
        File.Exists(store.GetSidecarPath(snapshotId)).Should().BeFalse();
    }

    // error-recovery-paths F2: if the snapshot's own delete fails (e.g. a transient AV-scan or
    // indexer lock on Windows), the sidecar must NOT be deleted anyway -- otherwise the payload
    // survives with no sidecar, which is invisible to EnumerateCandidates forever (it requires a
    // matching sidecar) and leaks in the recovery directory with no cleanup path.
    [Fact]
    public void DeleteSnapshot_WhenSnapshotDeleteFails_LeavesSidecarInPlaceInsteadOfOrphaningPayload()
    {
        using var dir = new TestTemporaryDirectory();
        var store = new AutosaveSnapshotStore(dir.Path);

        const string snapshotId = "recovery-lockedsnapshot-w0";
        var snapshotPath = store.GetSnapshotPath(snapshotId);
        var sidecarPath = store.GetSidecarPath(snapshotId);
        WriteSnapshotZip(snapshotPath);
        File.WriteAllText(sidecarPath, "{}");

        // Hold the snapshot open without FileShare.Delete so Windows refuses File.Delete on it,
        // simulating the transient AV/indexer lock the finding describes.
        using (new FileStream(snapshotPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            store.DeleteSnapshot(snapshotId);
        }

        File.Exists(snapshotPath).Should().BeTrue("the snapshot delete failed because the file was locked");
        File.Exists(sidecarPath).Should().BeTrue(
            "the sidecar must be preserved when the snapshot delete fails, so the pair stays " +
            "intact for a later recovery scan instead of leaking an invisible orphaned payload");
    }

    // Sibling/no-regression: once the lock clears, a retried DeleteSnapshot call still cleans up
    // the pair completely -- the fix only changes behavior on the failure path, not the happy path.
    [Fact]
    public void DeleteSnapshot_AfterLockClears_StillRemovesBothFilesOnRetry()
    {
        using var dir = new TestTemporaryDirectory();
        var store = new AutosaveSnapshotStore(dir.Path);

        const string snapshotId = "recovery-lockthenretry-w0";
        var snapshotPath = store.GetSnapshotPath(snapshotId);
        var sidecarPath = store.GetSidecarPath(snapshotId);
        WriteSnapshotZip(snapshotPath);
        File.WriteAllText(sidecarPath, "{}");

        using (new FileStream(snapshotPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            store.DeleteSnapshot(snapshotId);
        }

        // Lock released -- a subsequent retry (e.g. next launch's cleanup or a later close) sees
        // both files still paired and removes them cleanly.
        store.DeleteSnapshot(snapshotId);

        File.Exists(snapshotPath).Should().BeFalse();
        File.Exists(sidecarPath).Should().BeFalse();
    }

    [Fact]
    public void DeleteCandidate_RemovesBothFiles()
    {
        using var dir = new TestTemporaryDirectory();
        var store = new AutosaveSnapshotStore(dir.Path);

        const string snapshotId = "recovery-4-w0";
        var snapshotPath = store.GetSnapshotPath(snapshotId);
        var sidecarPath = store.GetSidecarPath(snapshotId);

        WriteSnapshotZip(snapshotPath);
        var sidecar = new AutosaveSidecar { SnapshotId = snapshotId };
        File.WriteAllText(sidecarPath, AutosaveSnapshotStore.SerializeSidecar(sidecar));

        var candidate = store.EnumerateCandidates().Single();
        AutosaveSnapshotStore.DeleteCandidate(candidate);

        File.Exists(snapshotPath).Should().BeFalse();
        File.Exists(sidecarPath).Should().BeFalse();
    }

    [Fact]
    public void DeleteSnapshot_DoesNotThrowWhenFilesAbsent()
    {
        var store = new AutosaveSnapshotStore(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        var act = () => store.DeleteSnapshot("recovery-nonexistent-w0");
        act.Should().NotThrow();
    }

    private sealed class TestApplicationDataPathProvider(string path) : IApplicationDataPathProvider
    {
        public string GetApplicationDataDirectory() => path;
    }
}

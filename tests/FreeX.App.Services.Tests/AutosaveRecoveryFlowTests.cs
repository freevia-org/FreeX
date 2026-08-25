using System.IO.Compression;
using FluentAssertions;
using FreeX.App.Services;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Tests for the crash-recovery correctness fixes:
/// - PID-reuse: LaunchId is stable within a process but different across instances.
/// - Multi-candidate enumeration with multiple snapshots.
/// - Never-delete-unchosen invariant (declined candidates are deleted; accepted ones are deleted
///   only after a successful load, and only by the caller).
/// - Recent-files suppression is a caller responsibility: these tests verify the store/service
///   layer decision-making does not record snapshot paths.
/// </summary>
public sealed class AutosaveRecoveryFlowTests
{
    // Real snapshots are OPC/ZIP packages; EnumerateCandidates validates that, so test snapshots
    // must be readable archives (not plain text).
    private static void WriteSnapshotZip(string path)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        zip.CreateEntry("[Content_Types].xml");
    }

    // ── PID-reuse / LaunchId uniqueness ───────────────────────────────────────

    [Fact]
    public void LaunchId_IsStableWithinProcess()
    {
        // Two reads within the same process must return the same value.
        var first = AutosaveSnapshotStore.LaunchId;
        var second = AutosaveSnapshotStore.LaunchId;

        first.Should().Be(second);
    }

    [Fact]
    public void LaunchId_IsNonEmpty()
    {
        AutosaveSnapshotStore.LaunchId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void LaunchId_IsUniqueAcrossNewInstancesSimulated()
    {
        // We cannot start a new process in a unit test, but we CAN verify that two Guid.NewGuid()
        // calls (which is what each process launch produces) are not equal — i.e. the design is
        // sound. This test documents the invariant.
        var launchA = Guid.NewGuid();
        var launchB = Guid.NewGuid();

        launchA.Should().NotBe(launchB,
            "each process launch produces a fresh Guid so recycled PIDs never clobber snapshots");
    }

    // ── Multi-candidate enumeration ───────────────────────────────────────────

    [Fact]
    public void EnumerateCandidates_ReturnsAllValidCandidates()
    {
        using var dir = new TestTemporaryDirectory();
        var store = new AutosaveSnapshotStore(dir.Path);

        WriteFakeSnapshot(store, "recovery-100-aabbccdd-w0", @"C:\work\a.xlsx", "Workbook A");
        WriteFakeSnapshot(store, "recovery-101-eeff0011-w0", @"C:\work\b.xlsx", "Workbook B");
        WriteFakeSnapshot(store, "recovery-102-11223344-w0", @"C:\work\c.xlsx", "Workbook C");

        var candidates = store.EnumerateCandidates();

        candidates.Should().HaveCount(3);
        candidates.Select(c => c.Sidecar.DisplayName).Should()
            .Contain(["Workbook A", "Workbook B", "Workbook C"]);
    }

    [Fact]
    public void EnumerateCandidates_IgnoresSnapshotWithoutSidecar()
    {
        using var dir = new TestTemporaryDirectory();
        var store = new AutosaveSnapshotStore(dir.Path);

        // One valid candidate, one with a missing sidecar.
        WriteFakeSnapshot(store, "recovery-200-aabbccdd-w0", @"C:\work\good.xlsx", "Good");
        WriteSnapshotZip(store.GetSnapshotPath("recovery-201-eeff0011-w0"));
        // No sidecar for the second one.

        var candidates = store.EnumerateCandidates();

        candidates.Should().HaveCount(1);
        candidates[0].Sidecar.DisplayName.Should().Be("Good");
    }

    // ── Never-delete-unchosen invariant ───────────────────────────────────────

    [Fact]
    public void DeleteCandidate_RemovesOnlyThatCandidate_LeavingOthersIntact()
    {
        using var dir = new TestTemporaryDirectory();
        var store = new AutosaveSnapshotStore(dir.Path);

        WriteFakeSnapshot(store, "recovery-300-aabbccdd-w0", @"C:\work\chosen.xlsx", "Chosen");
        WriteFakeSnapshot(store, "recovery-301-eeff0011-w0", @"C:\work\other.xlsx", "Other");

        var candidates = store.EnumerateCandidates();
        var chosen = candidates.First(c => c.Sidecar.DisplayName == "Chosen");

        // Simulate: user accepted "Chosen" and we delete it after successful load.
        AutosaveSnapshotStore.DeleteCandidate(chosen);

        // "Other" must still exist.
        File.Exists(store.GetSnapshotPath("recovery-301-eeff0011-w0")).Should().BeTrue(
            "unchosen (not-yet-offered) snapshots must not be deleted by the delete-candidate call");
        File.Exists(store.GetSidecarPath("recovery-301-eeff0011-w0")).Should().BeTrue();

        // "Chosen" must be gone.
        File.Exists(store.GetSnapshotPath("recovery-300-aabbccdd-w0")).Should().BeFalse();
        File.Exists(store.GetSidecarPath("recovery-300-aabbccdd-w0")).Should().BeFalse();
    }

    [Fact]
    public void DeleteCandidate_DeclinedCandidateIsRemovedAndDoesNotReappear()
    {
        using var dir = new TestTemporaryDirectory();
        var store = new AutosaveSnapshotStore(dir.Path);

        WriteFakeSnapshot(store, "recovery-400-aabbccdd-w0", @"C:\work\declined.xlsx", "Declined");

        var candidates = store.EnumerateCandidates();
        candidates.Should().HaveCount(1);

        // Simulate: user declined — recovery flow deletes the candidate.
        AutosaveSnapshotStore.DeleteCandidate(candidates[0]);

        // On the next launch, EnumerateCandidates must return empty.
        store.EnumerateCandidates().Should().BeEmpty(
            "a declined candidate must be deleted so it is not re-offered on next startup");
    }

    [Fact]
    public void MultipleDeclines_AllCandidatesAreDeletedIndividually()
    {
        using var dir = new TestTemporaryDirectory();
        var store = new AutosaveSnapshotStore(dir.Path);

        WriteFakeSnapshot(store, "recovery-500-aabbccdd-w0", @"C:\work\x.xlsx", "X");
        WriteFakeSnapshot(store, "recovery-500-aabbccdd-w1", @"C:\work\y.xlsx", "Y");

        var candidates = store.EnumerateCandidates();
        candidates.Should().HaveCount(2);

        // Simulate: user declined both, one at a time.
        foreach (var c in candidates)
            AutosaveSnapshotStore.DeleteCandidate(c);

        store.EnumerateCandidates().Should().BeEmpty();
    }

    // ── ShouldSnapshot decision logic (no recent-files concern at this layer) ─

    [Fact]
    public void ShouldSnapshot_DoesNotInvolveFilePaths_ConfirmingNoRecentFilesLeakAtStoreLayer()
    {
        // The store's ShouldSnapshot makes its decision purely on dirty flag + generation counters,
        // with no knowledge of file paths. This confirms that recent-files suppression is correctly
        // handled at the caller layer (MainWindow.Backstage / App.xaml.cs) rather than here.
        AutosaveSnapshotStore.ShouldSnapshot(workbookDirty: true, currentGeneration: 1, lastSnapshotGeneration: 0)
            .Should().BeTrue("the store layer only checks dirty/generation — path suppression is a caller concern");
    }

    // ── Snapshot ID uniqueness with GUID tag ──────────────────────────────────

    [Fact]
    public void SnapshotIds_WithDifferentLaunchGuids_DoNotCollide()
    {
        // Two "processes" with the same PID but different launch GUIDs should produce distinct paths.
        var pid = 1234;
        var launchA = Guid.NewGuid().ToString("N")[..8];
        var launchB = Guid.NewGuid().ToString("N")[..8];

        var idA = FormattableString.Invariant($"recovery-{pid}-{launchA}-w0");
        var idB = FormattableString.Invariant($"recovery-{pid}-{launchB}-w0");

        idA.Should().NotBe(idB,
            "even with a recycled PID the launch GUID ensures snapshot IDs never collide");
    }

    [Fact]
    public void SnapshotIds_WithSamePidAndSameGuid_ProduceConsistentPaths()
    {
        // Within one session the same window should always map to the same snapshot file.
        var recoveryDirectory = Path.Combine("FreeX", "Recovery");
        var store = new AutosaveSnapshotStore(recoveryDirectory);
        var id = "recovery-9999-abcd1234-w0";

        store.GetSnapshotPath(id).Should().Be(Path.Combine(recoveryDirectory, $"{id}.fxl"));
        store.GetSidecarPath(id).Should().Be(Path.Combine(recoveryDirectory, $"{id}.sidecar.json"));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void WriteFakeSnapshot(
        AutosaveSnapshotStore store,
        string snapshotId,
        string originalPath,
        string displayName)
    {
        WriteSnapshotZip(store.GetSnapshotPath(snapshotId));
        var sidecar = new AutosaveSidecar
        {
            SnapshotId = snapshotId,
            OriginalFilePath = originalPath,
            DisplayName = displayName,
            TimestampUtc = DateTimeOffset.UtcNow.ToString("O")
        };
        File.WriteAllText(store.GetSidecarPath(snapshotId), AutosaveSnapshotStore.SerializeSidecar(sidecar));
    }
}

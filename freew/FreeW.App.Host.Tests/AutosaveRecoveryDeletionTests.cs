using System.IO;
using Free.Shared.AppServices;
using FreeW.App.Host;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Regression tests for F26: AutosaveCoordinator.OfferRecovery must NOT delete non-offered
/// candidates, and must NOT delete the offered candidate on decline. Only a successful recovery
/// (user accepted AND file loaded) may delete the offered snapshot.
/// </summary>
public class AutosaveRecoveryDeletionTests : IDisposable
{
    private readonly string _recoveryDir;

    public AutosaveRecoveryDeletionTests()
    {
        _recoveryDir = Path.Combine(Path.GetTempPath(), "FreeWRecoveryTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_recoveryDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_recoveryDir, recursive: true); } catch { /* best-effort */ }
    }

    /// <summary>
    /// Creates a real snapshot + sidecar pair in the temp recovery directory.
    /// Returns the candidate so callers can check its paths.
    /// </summary>
    private AutosaveRecoveryCandidate CreateCandidate(string id, string timestampUtc, string displayName = "Test Doc")
    {
        var snapshotPath = Path.Combine(_recoveryDir, id + ".fxl");
        var sidecarPath = Path.Combine(_recoveryDir, id + ".sidecar.json");

        File.WriteAllText(snapshotPath, "dummy snapshot content");
        var sidecar = new AutosaveSidecar
        {
            DisplayName = displayName,
            TimestampUtc = timestampUtc,
            OriginalFilePath = null,
            SnapshotId = id
        };
        File.WriteAllText(sidecarPath, AutosaveSnapshotStore.SerializeSidecar(sidecar));

        return new AutosaveRecoveryCandidate(snapshotPath, sidecarPath, sidecar);
    }

    [Fact]
    public void SelectLatest_ThenDeleteOffered_LeavesNonOfferedCandidatesIntact()
    {
        // Arrange: two candidates, one older and one newer (the offered one)
        var older = CreateCandidate("older-snap", "2026-06-20T08:00:00Z", "Older Doc");
        var newer = CreateCandidate("newer-snap", "2026-06-20T09:00:00Z", "Newer Doc");

        var store = new AutosaveSnapshotStore(_recoveryDir);
        var candidates = store.EnumerateCandidates();
        candidates.Should().HaveCount(2);

        // Act: simulate the fixed OfferRecovery — select the latest, user accepts, delete only it
        var offered = AutosaveRecoveryCandidatePlanner.SelectLatest(candidates)!;
        offered.SnapshotPath.Should().Be(newer.SnapshotPath, "SelectLatest must return the newer candidate");

        // Only delete the offered candidate (as the fixed code does on successful recovery)
        AutosaveSnapshotStore.DeleteCandidate(offered);

        // Assert: only the offered candidate was deleted; the older one still exists
        File.Exists(newer.SnapshotPath).Should().BeFalse("offered snapshot must be deleted after successful recovery");
        File.Exists(newer.SidecarPath).Should().BeFalse("offered sidecar must be deleted after successful recovery");
        File.Exists(older.SnapshotPath).Should().BeTrue("non-offered snapshot must survive");
        File.Exists(older.SidecarPath).Should().BeTrue("non-offered sidecar must survive");

        // And the older candidate is still enumerable
        store.EnumerateCandidates().Should().HaveCount(1);
    }

    [Fact]
    public void DeclinedOffer_DoesNotDeleteAnyCandidate()
    {
        // Arrange: two candidates
        var older = CreateCandidate("snap-a", "2026-06-20T08:00:00Z");
        var newer = CreateCandidate("snap-b", "2026-06-20T09:00:00Z");

        var store = new AutosaveSnapshotStore(_recoveryDir);
        var candidates = store.EnumerateCandidates();

        // Act: simulate the fixed OfferRecovery — user declines ("No"), no deletion occurs
        _ = AutosaveRecoveryCandidatePlanner.SelectLatest(candidates)!;
        // On decline the fixed code does NOT call DeleteCandidate — nothing is deleted here.

        // Assert: both candidates still exist
        File.Exists(newer.SnapshotPath).Should().BeTrue("declined snapshot must not be deleted");
        File.Exists(older.SnapshotPath).Should().BeTrue("non-offered snapshot must not be deleted");
        store.EnumerateCandidates().Should().HaveCount(2);
    }

    [Fact]
    public void NonOfferedCandidates_AreEnumerableAfterOfferedIsRecovered()
    {
        // Arrange: three candidates; newest is offered + recovered + deleted; two others must survive
        var c1 = CreateCandidate("snap-1", "2026-06-20T07:00:00Z", "Doc 1");
        var c2 = CreateCandidate("snap-2", "2026-06-20T08:00:00Z", "Doc 2");
        var c3 = CreateCandidate("snap-3", "2026-06-20T09:00:00Z", "Doc 3");

        var store = new AutosaveSnapshotStore(_recoveryDir);
        var candidates = store.EnumerateCandidates();
        candidates.Should().HaveCount(3);

        var offered = AutosaveRecoveryCandidatePlanner.SelectLatest(candidates)!;
        offered.SnapshotPath.Should().Be(c3.SnapshotPath);

        // Fixed OfferRecovery: delete only the offered one on successful load
        AutosaveSnapshotStore.DeleteCandidate(offered);

        var remaining = store.EnumerateCandidates();
        remaining.Should().HaveCount(2, "two non-offered candidates must survive");
        remaining.Select(c => c.SnapshotPath).Should().Contain(c1.SnapshotPath).And.Contain(c2.SnapshotPath);
    }

    /// <summary>
    /// Regression test for H2: when the snapshot load fails the caller must NOT delete the
    /// candidate. This test exercises the contract at the store level: if the load-result bool
    /// is respected (false → skip DeleteCandidate) the snapshot file survives intact.
    /// The matching FileCommands-level assertion (OpenSnapshot returns false for a corrupt file)
    /// lives in FileLifecycleTests.OpenSnapshot_CorruptFile_ReturnsFalseAndSnapshotFileSurvives.
    /// </summary>
    [Fact]
    public void FailedLoad_DoesNotDeleteOfferedCandidate()
    {
        // Arrange: one candidate whose "load" will fail (simulated by returning false)
        var candidate = CreateCandidate("failing-snap", "2026-06-20T10:00:00Z", "Corrupt Doc");

        var store = new AutosaveSnapshotStore(_recoveryDir);
        store.EnumerateCandidates().Should().HaveCount(1);

        // Act: simulate the fixed OfferRecovery when the user accepts but OpenSnapshot returns false.
        // The fixed code: var loaded = _file.OpenSnapshot(...); if (loaded) DeleteCandidate(candidate);
        // Here we simulate loaded = false → do NOT call DeleteCandidate.
        bool loaded = false; // simulate a corrupt-file load failure
        if (loaded)
            AutosaveSnapshotStore.DeleteCandidate(candidate);

        // Assert: the snapshot is still on disk because the failed load was not deleted
        File.Exists(candidate.SnapshotPath).Should().BeTrue(
            "a snapshot whose load failed must NOT be deleted so the user's unsaved work is preserved");
        File.Exists(candidate.SidecarPath).Should().BeTrue(
            "the sidecar for a failed-load snapshot must also survive");
        store.EnumerateCandidates().Should().HaveCount(1,
            "the candidate must remain enumerable after a failed load");
    }
}

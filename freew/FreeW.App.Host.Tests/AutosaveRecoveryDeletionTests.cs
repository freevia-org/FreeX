using System.IO;
using System.IO.Compression;
using Free.Shared.AppServices;
using FreeW.App.Host;
using FreeW.App.Presentation.Shell;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Regression tests for F26 (AutosaveCoordinator.OfferRecovery must NOT delete non-offered
/// candidates) and its r141 revision (a declined STARTUP offer must be discarded, matching FreeX's
/// own startup recovery workflow, so the same stale snapshot does not nag on every later launch --
/// see <see cref="DeclinedOffer_AtStartup_DeletesOnlyTheDeclinedCandidate"/>). The manual "Recover
/// Unsaved Documents" command stays F26-safe: a decline there leaves the candidate in place for the
/// user to revisit (<see cref="DeclinedOffer_ViaManualCommand_LeavesTheCandidateInPlace"/>).
/// </summary>
public class AutosaveRecoveryDeletionTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeWRecoveryTests-");
    private string _recoveryDir => _temporaryDirectory.Path;

    public void Dispose() => _temporaryDirectory.Dispose();

    /// <summary>
    /// Creates a real snapshot + sidecar pair in the temp recovery directory.
    /// Returns the candidate so callers can check its paths.
    /// </summary>
    private AutosaveRecoveryCandidate CreateCandidate(string id, string timestampUtc, string displayName = "Test Doc")
    {
        var snapshotPath = Path.Combine(_recoveryDir, id + ".fxl");
        var sidecarPath = Path.Combine(_recoveryDir, id + ".sidecar.json");

        // A real snapshot is an OPC/ZIP package; EnumerateCandidates now validates that, so the test
        // snapshot must be a readable archive (not plain text) to be enumerated as a valid candidate.
        WriteMinimalZip(snapshotPath);
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

    /// <summary>Writes a minimal but valid ZIP/OPC package so the snapshot passes the readable-archive check.</summary>
    private static void WriteMinimalZip(string path)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using var entry = zip.CreateEntry("[Content_Types].xml").Open();
        var bytes = System.Text.Encoding.UTF8.GetBytes("<Types/>");
        entry.Write(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// Regression for the "Could not recover the document: End of Central Directory record could not be
    /// found" error: a corrupt/truncated snapshot (not a readable ZIP) must be skipped by
    /// EnumerateCandidates entirely — so it is NEVER offered for recovery and no modal error fires —
    /// and quarantined off to the side (bytes preserved, removed from the recovery dir).
    /// </summary>
    [Fact]
    public void CorruptSnapshot_IsSkippedAndQuarantined_NeverEnumerated()
    {
        // A valid candidate and a corrupt one (plain text, not a ZIP) sharing the recovery dir.
        var good = CreateCandidate("good-snap", "2026-06-25T09:00:00Z", "Good Doc");
        var badSnapshot = Path.Combine(_recoveryDir, "bad-snap.fxl");
        File.WriteAllText(badSnapshot, "not a zip — truncated mid-write");
        File.WriteAllText(Path.Combine(_recoveryDir, "bad-snap.sidecar.json"),
            AutosaveSnapshotStore.SerializeSidecar(new AutosaveSidecar
            { DisplayName = "Corrupt Doc", TimestampUtc = "2026-06-25T10:00:00Z", SnapshotId = "bad-snap" }));

        var store = new AutosaveSnapshotStore(_recoveryDir);
        var candidates = store.EnumerateCandidates();

        // Only the good candidate is enumerated; the corrupt one is silently skipped.
        candidates.Should().ContainSingle(c => c.SnapshotPath == good.SnapshotPath);
        candidates.Should().NotContain(c => c.SnapshotPath == badSnapshot);
        // The corrupt snapshot is moved out of the recovery dir (quarantined), not left to retry.
        File.Exists(badSnapshot).Should().BeFalse();
        Directory.GetFiles(Path.Combine(_recoveryDir, "Quarantine")).Should().Contain(p => p.EndsWith(".fxl"));
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
        var offered = AutosaveRecoveryPolicy.SelectLatest(candidates)!;
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

    /// <summary>
    /// r141: declining the unprompted STARTUP offer must discard the offered snapshot -- through the
    /// real <see cref="FreeWRecoveryWorkflow.RunAsync"/> production path, not a hand-simulated
    /// stand-in -- so the same stale document does not keep nagging on every later launch (matches
    /// FreeX's <c>StartupRecoveryWorkflow</c>, which discards a declined snapshot the same way). A
    /// non-offered candidate sitting in the same recovery directory must survive untouched.
    /// </summary>
    [Fact]
    public async Task DeclinedOffer_AtStartup_DeletesOnlyTheDeclinedCandidate()
    {
        // Arrange: two candidates in the same recovery directory; only the newer one is offered
        // (mirrors AutosaveCoordinator.OfferRecovery, which offers the whole PlanAll list -- here we
        // use PlanLatest to model offering just the single most-recent one, leaving the older
        // candidate un-offered, exactly like the pre-existing "non-offered survives" tests below).
        var older = CreateCandidate("snap-a", "2026-06-20T08:00:00Z");
        var newer = CreateCandidate("snap-b", "2026-06-20T09:00:00Z");

        var store = new AutosaveSnapshotStore(_recoveryDir);
        var offered = AutosaveRecoveryPlanner.PlanLatest(store)!;
        offered.Candidate.SnapshotPath.Should().Be(newer.SnapshotPath);

        // Act: run the real startup workflow and decline the one offer.
        var result = await FreeWRecoveryWorkflow.RunAsync(
            [offered],
            FreeWRecoveryPromptMode.Startup,
            _ => new ValueTask<bool>(false),
            (_, _) => new ValueTask<bool>(false));

        // Assert: the offer was declined, the declined snapshot is gone, the other one survives.
        result.AnyAccepted.Should().BeFalse();
        File.Exists(newer.SnapshotPath).Should().BeFalse(
            "a declined startup offer must be discarded so it does not nag on the next launch");
        File.Exists(newer.SidecarPath).Should().BeFalse();
        File.Exists(older.SnapshotPath).Should().BeTrue("a candidate that was never offered must survive");
        File.Exists(older.SidecarPath).Should().BeTrue();
    }

    /// <summary>
    /// Sibling of <see cref="DeclinedOffer_AtStartup_DeletesOnlyTheDeclinedCandidate"/>: the
    /// user-invoked "Recover Unsaved Documents" command is opt-in and browsable, so declining one
    /// candidate there must NOT delete it -- the user can still reach it again later (F26's original
    /// intent, preserved for the manual entry point).
    /// </summary>
    [Fact]
    public async Task DeclinedOffer_ViaManualCommand_LeavesTheCandidateInPlace()
    {
        var older = CreateCandidate("snap-a", "2026-06-20T08:00:00Z");
        var newer = CreateCandidate("snap-b", "2026-06-20T09:00:00Z");

        var store = new AutosaveSnapshotStore(_recoveryDir);
        var recoveries = AutosaveRecoveryPlanner.PlanAll(store);

        var result = await FreeWRecoveryWorkflow.RunAsync(
            recoveries,
            FreeWRecoveryPromptMode.Manual,
            _ => new ValueTask<bool>(false),
            (_, _) => new ValueTask<bool>(false));

        result.AnyAccepted.Should().BeFalse();
        File.Exists(newer.SnapshotPath).Should().BeTrue(
            "declining the manual recovery command must leave the candidate for the user to revisit");
        File.Exists(older.SnapshotPath).Should().BeTrue();
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

        var offered = AutosaveRecoveryPolicy.SelectLatest(candidates)!;
        offered.SnapshotPath.Should().Be(c3.SnapshotPath);

        // Fixed OfferRecovery: delete only the offered one on successful load
        AutosaveSnapshotStore.DeleteCandidate(offered);

        var remaining = store.EnumerateCandidates();
        remaining.Should().HaveCount(2, "two non-offered candidates must survive");
        remaining.Select(c => c.SnapshotPath).Should().Contain(c1.SnapshotPath).And.Contain(c2.SnapshotPath);
    }

    /// <summary>
    /// Regression test for the "Could not recover the document" error loop: when a snapshot fails to
    /// load (structurally corrupt — e.g. a truncated ZIP from a crashed write), the fixed
    /// OfferRecovery/RecoverUnsavedDocuments QUARANTINE the candidate. The bytes are preserved (moved
    /// into a Quarantine subfolder, not deleted), but the candidate is no longer enumerable, so it is
    /// not re-offered on the next launch and the error cannot loop.
    /// </summary>
    [Fact]
    public void FailedLoad_QuarantinesCandidate_PreservesBytes_NotReEnumerated()
    {
        // Arrange: one candidate whose load fails.
        var candidate = CreateCandidate("failing-snap", "2026-06-20T10:00:00Z", "Corrupt Doc");

        var store = new AutosaveSnapshotStore(_recoveryDir);
        store.EnumerateCandidates().Should().HaveCount(1);

        // Act: simulate the fixed recovery flow when OpenSnapshot returns false (corrupt file).
        bool loaded = false;
        if (loaded)
            AutosaveSnapshotStore.DeleteCandidate(candidate);
        else
            AutosaveSnapshotStore.QuarantineCandidate(candidate);

        // Assert: original snapshot+sidecar moved out of the recovery dir (not deleted) ...
        File.Exists(candidate.SnapshotPath).Should().BeFalse("the corrupt snapshot is moved aside, not left to re-prompt");
        File.Exists(candidate.SidecarPath).Should().BeFalse("the sidecar is moved aside with its snapshot");
        var quarantine = Path.Combine(_recoveryDir, "Quarantine");
        Directory.Exists(quarantine).Should().BeTrue();
        Directory.GetFiles(quarantine).Should().Contain(p => p.EndsWith(".fxl"),
            "the snapshot bytes are preserved in the Quarantine subfolder for diagnostics");
        // ... and the candidate is no longer enumerable, so it cannot loop the recovery error.
        store.EnumerateCandidates().Should().BeEmpty(
            "a quarantined corrupt snapshot must not be re-offered on the next launch");
    }
}

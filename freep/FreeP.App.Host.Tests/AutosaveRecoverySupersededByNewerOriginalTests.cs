using System.IO;
using System.IO.Compression;
using Free.Shared.AppServices;
using FreeP.App.Compositor;

namespace FreeP.App.Host.Tests;

/// <summary>
/// r141: FreeP's recovery planner must apply the same "supersede by newer original" filter FreeX's
/// <c>AutosaveRecoveryOfferPlanner</c> already applies (via
/// <see cref="AutosaveRecoveryCandidateProcessor.FilterSupersededByNewerOriginal"/>) -- a snapshot
/// whose original file was saved AFTER the snapshot was written must never be offered, because
/// recovering it would silently overwrite newer on-disk work with stale autosaved content. Mirrors
/// FreeW's <c>AutosaveRecoverySupersededByNewerOriginalTests</c>.
/// </summary>
public sealed class AutosaveRecoverySupersededByNewerOriginalTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreePSupersededRecoveryTests-");
    private string RecoveryDir => _temporaryDirectory.Path;

    public void Dispose() => _temporaryDirectory.Dispose();

    private static void WriteMinimalZip(string path)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using var entry = zip.CreateEntry("[Content_Types].xml").Open();
        var bytes = System.Text.Encoding.UTF8.GetBytes("<Types/>");
        entry.Write(bytes, 0, bytes.Length);
    }

    private AutosaveRecoveryCandidate CreateCandidate(
        string id,
        DateTimeOffset snapshotTimestampUtc,
        string? originalFilePath,
        string displayName = "Test Presentation")
    {
        var snapshotPath = Path.Combine(RecoveryDir, id + ".fxl");
        var sidecarPath = Path.Combine(RecoveryDir, id + ".sidecar.json");

        WriteMinimalZip(snapshotPath);
        var sidecar = new AutosaveSidecar
        {
            DisplayName = displayName,
            TimestampUtc = snapshotTimestampUtc.ToString("O"),
            OriginalFilePath = originalFilePath,
            SnapshotId = id
        };
        File.WriteAllText(sidecarPath, AutosaveSnapshotStore.SerializeSidecar(sidecar));

        return new AutosaveRecoveryCandidate(snapshotPath, sidecarPath, sidecar);
    }

    /// <summary>
    /// A crash leaves a snapshot at 10:00, the user later reopens the original and saves normally at
    /// 10:15 (the original file's mtime is now newer than the snapshot). Neither
    /// <see cref="AutosaveRecoveryPlanner.PlanAll"/> nor
    /// <see cref="AutosaveRecoveryPlanner.PlanLatest(AutosaveSnapshotStore)"/> may still offer it.
    /// </summary>
    [Fact]
    public void PlanAll_And_PlanLatest_ExcludeASnapshotSupersededByANewerOriginalSave()
    {
        var originalPath = Path.Combine(RecoveryDir, "deck.pptx");
        File.WriteAllText(originalPath, "the user's current, newer content");
        var originalSavedAt = new DateTimeOffset(2026, 8, 18, 10, 15, 0, TimeSpan.Zero);
        File.SetLastWriteTimeUtc(originalPath, originalSavedAt.UtcDateTime);

        var snapshotTakenAt = new DateTimeOffset(2026, 8, 18, 10, 0, 0, TimeSpan.Zero);
        var stale = CreateCandidate("stale-snap", snapshotTakenAt, originalPath, "Stale Deck");

        var store = new AutosaveSnapshotStore(RecoveryDir);
        store.EnumerateCandidates().Should().ContainSingle(c => c.SnapshotPath == stale.SnapshotPath,
            "the raw store must still see the file -- filtering happens in the planner, not enumeration");

        AutosaveRecoveryPlanner.PlanAll(store).Should().BeEmpty(
            "a snapshot older than the original it would overwrite must never be offered");
        AutosaveRecoveryPlanner.PlanLatest(store).Should().BeNull(
            "PlanLatest must apply the same supersede filter as PlanAll");
    }

    /// <summary>
    /// Sibling of the superseded case: a genuine crash-recovery snapshot (the original is older than
    /// the snapshot, exactly what a real crash produces) must still be offered normally.
    /// </summary>
    [Fact]
    public void PlanAll_StillOffersASnapshotNewerThanItsOriginal()
    {
        var originalPath = Path.Combine(RecoveryDir, "deck.pptx");
        File.WriteAllText(originalPath, "the last content the user manually saved");
        var originalSavedAt = new DateTimeOffset(2026, 8, 18, 9, 0, 0, TimeSpan.Zero);
        File.SetLastWriteTimeUtc(originalPath, originalSavedAt.UtcDateTime);

        var snapshotTakenAt = new DateTimeOffset(2026, 8, 18, 9, 30, 0, TimeSpan.Zero);
        var fresh = CreateCandidate("fresh-snap", snapshotTakenAt, originalPath, "Fresh Deck");

        var store = new AutosaveSnapshotStore(RecoveryDir);

        var all = AutosaveRecoveryPlanner.PlanAll(store);
        all.Should().ContainSingle(r => r.Candidate.SnapshotPath == fresh.SnapshotPath,
            "a snapshot newer than its original is a genuine crash recovery and must still be offered");
        AutosaveRecoveryPlanner.PlanLatest(store)!.Candidate.SnapshotPath.Should().Be(fresh.SnapshotPath);
    }
}

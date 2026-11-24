using System.IO.Compression;
using FreeP.App.Compositor;
using FreeW.App.Presentation.Shell;

namespace Free.Shared.AppServices.Tests;

/// <summary>
/// r141: each sister app's recovery profile must apply the same "supersede by newer original" filter
/// FreeX's <c>AutosaveRecoveryOfferPlanner</c> applies (via
/// <see cref="AutosaveRecoveryCandidateProcessor.FilterSupersededByNewerOriginal"/>) -- a snapshot whose
/// original file was saved AFTER the snapshot was written must never be offered, because recovering it
/// would silently overwrite newer on-disk work with stale autosaved content. One parameterized test
/// covers both apps; the only per-app input is the profile and a document extension.
/// </summary>
public sealed class SisterAutosaveSupersededByNewerOriginalTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new("SisterSupersededRecoveryTests-");

    private string RecoveryDir => _temporaryDirectory.Path;

    public void Dispose() => _temporaryDirectory.Dispose();

    public static TheoryData<string, string> Apps => new()
    {
        { "FreeW", "document.docx" },
        { "FreeP", "deck.pptx" },
    };

    private static AutosaveRecoveryProfile ProfileFor(string app) => app switch
    {
        "FreeW" => FreeWAutosaveProfile.Recovery,
        "FreeP" => FreePAutosaveProfile.Recovery,
        _ => throw new ArgumentOutOfRangeException(nameof(app), app, null),
    };

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
        string displayName)
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
    /// <see cref="AutosaveRecoveryProfile.PlanAll"/> nor
    /// <see cref="AutosaveRecoveryProfile.PlanLatest(AutosaveSnapshotStore)"/> may still offer it.
    /// </summary>
    [Theory]
    [MemberData(nameof(Apps))]
    public void PlanAll_And_PlanLatest_ExcludeASnapshotSupersededByANewerOriginalSave(
        string app,
        string originalName)
    {
        var profile = ProfileFor(app);
        var originalPath = Path.Combine(RecoveryDir, originalName);
        File.WriteAllText(originalPath, "the user's current, newer content");
        var originalSavedAt = new DateTimeOffset(2026, 8, 18, 10, 15, 0, TimeSpan.Zero);
        File.SetLastWriteTimeUtc(originalPath, originalSavedAt.UtcDateTime);

        var snapshotTakenAt = new DateTimeOffset(2026, 8, 18, 10, 0, 0, TimeSpan.Zero);
        var stale = CreateCandidate("stale-snap", snapshotTakenAt, originalPath, "Stale");

        var store = new AutosaveSnapshotStore(RecoveryDir);
        store.EnumerateCandidates().Should().ContainSingle(c => c.SnapshotPath == stale.SnapshotPath,
            "the raw store must still see the file -- filtering happens in the planner, not enumeration");

        profile.PlanAll(store).Should().BeEmpty(
            "a snapshot older than the original it would overwrite must never be offered");
        profile.PlanLatest(store).Should().BeNull(
            "PlanLatest must apply the same supersede filter as PlanAll");
    }

    /// <summary>
    /// Sibling of the superseded case: a genuine crash-recovery snapshot (the original is older than
    /// the snapshot, exactly what a real crash produces) must still be offered normally. Proves the
    /// filter targets only the superseded case, not every candidate with an OriginalFilePath.
    /// </summary>
    [Theory]
    [MemberData(nameof(Apps))]
    public void PlanAll_StillOffersASnapshotNewerThanItsOriginal(string app, string originalName)
    {
        var profile = ProfileFor(app);
        var originalPath = Path.Combine(RecoveryDir, originalName);
        File.WriteAllText(originalPath, "the last content the user manually saved");
        var originalSavedAt = new DateTimeOffset(2026, 8, 18, 9, 0, 0, TimeSpan.Zero);
        File.SetLastWriteTimeUtc(originalPath, originalSavedAt.UtcDateTime);

        var snapshotTakenAt = new DateTimeOffset(2026, 8, 18, 9, 30, 0, TimeSpan.Zero);
        var fresh = CreateCandidate("fresh-snap", snapshotTakenAt, originalPath, "Fresh");

        var store = new AutosaveSnapshotStore(RecoveryDir);

        var all = profile.PlanAll(store);
        all.Should().ContainSingle(r => r.Candidate.SnapshotPath == fresh.SnapshotPath,
            "a snapshot newer than its original is a genuine crash recovery and must still be offered");
        profile.PlanLatest(store)!.Candidate.SnapshotPath.Should().Be(fresh.SnapshotPath);
    }
}

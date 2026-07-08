using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

// ── Round-13 fix bucket S8 ────────────────────────────────────────────────────
//
// R13-autosave-recovery-1 [HIGH]: AutosaveSnapshotStore.EnumerateCandidates validated every
// recovery snapshot as a ZIP/OPC archive (ZipFile.OpenRead), but FreeX's real autosave snapshots
// are plain JSON written by NativeJsonAdapter.Save (AutosaveService.WriteSnapshot -> _adapter.Save).
// A JSON file always fails ZipFile.OpenRead, so every genuine FreeX snapshot was quarantined and
// silently discarded -- crash recovery never worked for FreeX. This test writes a REAL JSON
// snapshot the same way AutosaveService does (via NativeJsonAdapter, not a fake ZIP like the
// pre-existing AutosaveSnapshotStoreTests helpers) and asserts it survives EnumerateCandidates
// instead of being quarantined.
public sealed class FreeXR13S8Tests
{
    [Fact]
    public void EnumerateCandidates_RealFreeXJsonSnapshot_IsRecoveredNotQuarantined()
    {
        using var dir = new TestTemporaryDirectory();
        var store = new AutosaveSnapshotStore(dir.Path);

        var workbook = new Workbook("R13S8");
        workbook.AddSheet("Sheet1");
        var sheet = workbook.Sheets[0];
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(42)));

        const string snapshotId = "recovery-r13s8-w0";
        var snapshotPath = store.GetSnapshotPath(snapshotId);
        var sidecarPath = store.GetSidecarPath(snapshotId);

        // Write the snapshot exactly the way AutosaveService/AvaloniaAutosaveCoordinator do:
        // plain JSON via NativeJsonAdapter.Save, NOT a ZIP archive.
        using (var fs = new FileStream(snapshotPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            new NativeJsonAdapter().Save(workbook, fs);
        }

        var sidecar = new AutosaveSidecar
        {
            OriginalFilePath = @"C:\work\r13s8.fxl",
            DisplayName = "R13S8",
            SnapshotId = snapshotId,
            TimestampUtc = DateTimeOffset.UtcNow.ToString("O")
        };
        File.WriteAllText(sidecarPath, AutosaveSnapshotStore.SerializeSidecar(sidecar));

        var candidates = store.EnumerateCandidates();

        candidates.Should().HaveCount(1,
            "a real FreeX JSON snapshot must be recognized as readable, not quarantined as an invalid ZIP");
        candidates[0].Sidecar.DisplayName.Should().Be("R13S8");

        // The bug quarantined the snapshot (moved it into a Quarantine subfolder) instead of
        // surfacing it, so also verify the original files are untouched in place.
        File.Exists(snapshotPath).Should().BeTrue("the snapshot must be left in place, not quarantined");
        File.Exists(sidecarPath).Should().BeTrue("the sidecar must be left in place, not quarantined");
        Directory.Exists(Path.Combine(dir.Path, "Quarantine")).Should().BeFalse(
            "a valid JSON snapshot must never be moved into Quarantine");
    }
}

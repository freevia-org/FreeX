using System.IO;
using System.IO.Compression;
using System.Reflection;
using FluentAssertions;
using Free.Shared.AppServices;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for K4: crash-recovery must not offer/restore a workbook that was shared
/// across multiple "New Window" views (see MainWindow.MultiWindow.cs's ViewNewWindowBtn_Click,
/// which gives each sibling window its own autosave snapshot per J25) as several independent
/// recovery candidates. Accepting more than one such candidate previously loaded the same document
/// into two disconnected MainWindow/WorkbookRef instances, silently forking what was one shared,
/// dirtied workbook. App.DeduplicateCandidatesByDocument collapses same-document candidates down
/// to the single newest snapshot before OfferStartupRecovery ever offers them.
///
/// Since R82-services-autosave-recovery-5-1, "same document" additionally requires a matching
/// <see cref="AutosaveSidecar.DocumentId"/> (not just launch scope + path/name) — two ordinary,
/// independent windows opened on the same path from the same process launch are NOT siblings and
/// must not be merged, so the fixtures below that represent genuine "New Window" siblings supply
/// a shared DocumentId to prove that relationship.
/// </summary>
public sealed class CrashRecoverySharedWorkbookDedupTests
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
        DateTimeOffset timestamp,
        string? documentId = null)
    {
        var snapshotPath = store.GetSnapshotPath(snapshotId);
        var sidecarPath = store.GetSidecarPath(snapshotId);
        WriteSnapshotZip(snapshotPath);
        var sidecar = new AutosaveSidecar
        {
            OriginalFilePath = originalFilePath,
            DisplayName = displayName,
            TimestampUtc = timestamp.ToString("O"),
            SnapshotId = snapshotId,
            DocumentId = documentId
        };
        File.WriteAllText(sidecarPath, AutosaveSnapshotStore.SerializeSidecar(sidecar));
        return new AutosaveRecoveryCandidate(snapshotPath, sidecarPath, sidecar);
    }

    private static IReadOnlyList<AutosaveRecoveryCandidate> InvokeDeduplicate(
        IReadOnlyList<AutosaveRecoveryCandidate> candidates)
    {
        var method = typeof(App).GetMethod(
            "DeduplicateCandidatesByDocument",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        return (IReadOnlyList<AutosaveRecoveryCandidate>)method!.Invoke(null, [candidates])!;
    }

    [Fact]
    public void Deduplicate_CollapsesTwoSnapshotsOfSharedWorkbookIntoOne()
    {
        using var temp = new TestTemporaryDirectory("FreeX.CrashRecoveryDedup-");
        var store = new AutosaveSnapshotStore(temp.Path);
        var now = DateTimeOffset.UtcNow;

        // Two "New Window" siblings viewing the same saved document produce two snapshots that
        // share OriginalFilePath/DisplayName (per AutosaveService.WorkbookSnapshotSource) but have
        // distinct per-window snapshot ids/files. They DO share the same DocumentId, though — both
        // windows wrap the SAME Workbook instance (see IAutosaveWorkbookSource.DocumentId) — which
        // is what makes them provably the same document (R82-services-autosave-recovery-5-1).
        var older = WriteCandidate(store, "recovery-1-w0", @"C:\Users\alice\Book1.fxl", "Book1", now.AddMinutes(-1), documentId: "shared-book1-workbook-id");
        var newer = WriteCandidate(store, "recovery-1-w1", @"C:\Users\alice\Book1.fxl", "Book1", now, documentId: "shared-book1-workbook-id");

        var deduped = InvokeDeduplicate([older, newer]);

        deduped.Should().ContainSingle("the two snapshots represent one shared, still-dirty document");
        deduped[0].SnapshotPath.Should().Be(newer.SnapshotPath, "the newest snapshot of the shared document should win");
        File.Exists(newer.SnapshotPath).Should().BeTrue();
        File.Exists(newer.SidecarPath).Should().BeTrue();
        File.Exists(older.SnapshotPath).Should().BeFalse("the older duplicate snapshot must be deleted, not silently offered");
        File.Exists(older.SidecarPath).Should().BeFalse();
    }

    [Fact]
    public void Deduplicate_KeepsDistinctDocumentsSeparate()
    {
        using var temp = new TestTemporaryDirectory("FreeX.CrashRecoveryDedup-");
        var store = new AutosaveSnapshotStore(temp.Path);
        var now = DateTimeOffset.UtcNow;

        var book1 = WriteCandidate(store, "recovery-1-w0", @"C:\Users\alice\Book1.fxl", "Book1", now);
        var book2 = WriteCandidate(store, "recovery-2-w0", @"C:\Users\alice\Book2.fxl", "Book2", now);

        var deduped = InvokeDeduplicate([book1, book2]);

        deduped.Should().HaveCount(2, "unrelated documents must each still be offered");
        deduped.Select(c => c.SnapshotPath).Should().BeEquivalentTo([book1.SnapshotPath, book2.SnapshotPath]);
        File.Exists(book1.SnapshotPath).Should().BeTrue();
        File.Exists(book2.SnapshotPath).Should().BeTrue();
    }

    [Fact]
    public void Deduplicate_GroupsUnsavedWorkbooksByDisplayNameWhenNoFilePath()
    {
        using var temp = new TestTemporaryDirectory("FreeX.CrashRecoveryDedup-");
        var store = new AutosaveSnapshotStore(temp.Path);
        var now = DateTimeOffset.UtcNow;

        // An unsaved shared workbook has no OriginalFilePath yet; DisplayName is the fallback
        // identity signal for "New Window" siblings of the same never-saved document, and they
        // share the same DocumentId for the same reason as the saved-path case above.
        var older = WriteCandidate(store, "recovery-3-w0", originalFilePath: null, displayName: "Book2", now.AddMinutes(-1), documentId: "shared-book2-workbook-id");
        var newer = WriteCandidate(store, "recovery-3-w1", originalFilePath: null, displayName: "Book2", now, documentId: "shared-book2-workbook-id");

        var deduped = InvokeDeduplicate([older, newer]);

        deduped.Should().ContainSingle();
        deduped[0].SnapshotPath.Should().Be(newer.SnapshotPath);
        File.Exists(older.SnapshotPath).Should().BeFalse();
    }

    [Fact]
    public void Deduplicate_IsCaseInsensitiveForFilePaths()
    {
        using var temp = new TestTemporaryDirectory("FreeX.CrashRecoveryDedup-");
        var store = new AutosaveSnapshotStore(temp.Path);
        var now = DateTimeOffset.UtcNow;

        var older = WriteCandidate(store, "recovery-4-w0", @"C:\Users\alice\Book1.fxl", "Book1", now.AddMinutes(-1), documentId: "shared-book1-workbook-id");
        var newer = WriteCandidate(store, "recovery-4-w1", @"c:\users\alice\book1.fxl", "Book1", now, documentId: "shared-book1-workbook-id");

        var deduped = InvokeDeduplicate([older, newer]);

        deduped.Should().ContainSingle("Windows file paths are case-insensitive, so these are the same document");
        deduped[0].SnapshotPath.Should().Be(newer.SnapshotPath);
    }

    [Fact]
    public void Deduplicate_SingleCandidateIsUnaffected()
    {
        using var temp = new TestTemporaryDirectory("FreeX.CrashRecoveryDedup-");
        var store = new AutosaveSnapshotStore(temp.Path);

        var only = WriteCandidate(store, "recovery-5-w0", @"C:\Users\alice\Book1.fxl", "Book1", DateTimeOffset.UtcNow);

        var deduped = InvokeDeduplicate([only]);

        deduped.Should().ContainSingle();
        deduped[0].SnapshotPath.Should().Be(only.SnapshotPath);
        File.Exists(only.SnapshotPath).Should().BeTrue();
    }
}

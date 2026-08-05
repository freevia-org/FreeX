using FluentAssertions;
using Free.Shared.AppServices;

namespace FreeX.App.Services.Tests;

public sealed class AutosaveRecoveryCandidateProcessorTests
{
    [Fact]
    public void PrepareForRecovery_KeepsNewestSnapshotForProvableDocumentSiblings()
    {
        using var temp = new TempDirectory();
        var store = new AutosaveSnapshotStore(temp.Path);
        var now = DateTimeOffset.UtcNow;
        var older = WriteCandidate(
            store,
            "recovery-42-launch-window1",
            "document-1",
            "Book1",
            now.AddMinutes(-1));
        var newer = WriteCandidate(
            store,
            "recovery-42-launch-window2",
            "document-1",
            "Book1",
            now);

        var prepared = AutosaveRecoveryCandidateProcessor.PrepareForRecovery([older, newer]);

        prepared.Should().ContainSingle().Which.Should().BeSameAs(newer);
        File.Exists(older.SnapshotPath).Should().BeFalse();
        File.Exists(older.SidecarPath).Should().BeFalse();
        File.Exists(newer.SnapshotPath).Should().BeTrue();
    }

    [Fact]
    public void PrepareForRecovery_KeepsIndependentDocumentsWithSameNameAndLaunchScope()
    {
        using var temp = new TempDirectory();
        var store = new AutosaveSnapshotStore(temp.Path);
        var now = DateTimeOffset.UtcNow;
        var first = WriteCandidate(store, "recovery-42-launch-window1", "document-1", "Book1", now);
        var second = WriteCandidate(store, "recovery-42-launch-window2", "document-2", "Book1", now);

        var prepared = AutosaveRecoveryCandidateProcessor.PrepareForRecovery([first, second]);

        prepared.Should().Equal(first, second);
        File.Exists(first.SnapshotPath).Should().BeTrue();
        File.Exists(second.SnapshotPath).Should().BeTrue();
    }

    [Fact]
    public void PrepareForRecovery_DeletesSnapshotSupersededByNewerOriginal()
    {
        using var temp = new TempDirectory();
        var store = new AutosaveSnapshotStore(temp.Path);
        var snapshotTime = DateTimeOffset.UtcNow.AddMinutes(-10);
        var originalPath = System.IO.Path.Combine(temp.Path, "Book1.fxl");
        File.WriteAllText(originalPath, "newer manual save");
        File.SetLastWriteTimeUtc(originalPath, snapshotTime.AddMinutes(5).UtcDateTime);
        var candidate = WriteCandidate(
            store,
            "recovery-42-launch-window1",
            "document-1",
            "Book1",
            snapshotTime,
            originalPath);

        var prepared = AutosaveRecoveryCandidateProcessor.PrepareForRecovery([candidate]);

        prepared.Should().BeEmpty();
        File.Exists(candidate.SnapshotPath).Should().BeFalse();
        File.Exists(candidate.SidecarPath).Should().BeFalse();
    }

    [Fact]
    public void DeduplicateByDocument_CleanupFailureDoesNotAbortPreparation()
    {
        using var temp = new TempDirectory();
        var store = new AutosaveSnapshotStore(temp.Path);
        var now = DateTimeOffset.UtcNow;
        var older = WriteCandidate(
            store,
            "recovery-42-launch-window1",
            "document-1",
            "Book1",
            now.AddMinutes(-1));
        var newer = WriteCandidate(
            store,
            "recovery-42-launch-window2",
            "document-1",
            "Book1",
            now);

        var prepared = AutosaveRecoveryCandidateProcessor.DeduplicateByDocument(
            [older, newer],
            _ => throw new IOException("Cleanup failed."));

        prepared.Should().ContainSingle().Which.Should().BeSameAs(newer);
    }

    [Fact]
    public void FilterSupersededByNewerOriginal_CleanupFailureDoesNotAbortRemainingCandidates()
    {
        using var temp = new TempDirectory();
        var store = new AutosaveSnapshotStore(temp.Path);
        var snapshotTime = DateTimeOffset.UtcNow.AddMinutes(-10);
        var originalPath = System.IO.Path.Combine(temp.Path, "Book1.fxl");
        File.WriteAllText(originalPath, "newer manual save");
        File.SetLastWriteTimeUtc(originalPath, snapshotTime.AddMinutes(5).UtcDateTime);
        var superseded = WriteCandidate(
            store,
            "recovery-42-launch-window1",
            "document-1",
            "Book1",
            snapshotTime,
            originalPath);
        var valid = WriteCandidate(
            store,
            "recovery-42-launch-window2",
            "document-2",
            "Book2",
            snapshotTime);

        var prepared = AutosaveRecoveryCandidateProcessor.FilterSupersededByNewerOriginal(
            [superseded, valid],
            _ => throw new IOException("Cleanup failed."));

        prepared.Should().ContainSingle().Which.Should().BeSameAs(valid);
    }

    [Fact]
    public void FreeXRenderers_DelegateRecoveryCandidatePolicyToSharedProcessor()
    {
        var hostSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Host", "App.xaml.cs"));
        var avaloniaSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "App.cs"));
        var processorSource = File.ReadAllText(RepositoryFileLocator.Find(
            "shared", "Free.Shared.AppServices", "AutosaveRecoveryCandidateProcessor.cs"));

        hostSource.Should().Contain("AutosaveRecoveryCandidateProcessor.PrepareForRecovery(");
        avaloniaSource.Should().Contain("AutosaveRecoveryCandidateProcessor.PrepareForRecovery(");
        hostSource.Should().NotContain("new Dictionary<string, AutosaveRecoveryCandidate>");
        avaloniaSource.Should().NotContain("new Dictionary<string, AutosaveRecoveryCandidate>");
        hostSource.Should().NotContain("DateTimeOffset.TryParse(candidate.Sidecar.TimestampUtc");
        avaloniaSource.Should().NotContain("DateTimeOffset.TryParse(candidate.Sidecar.TimestampUtc");
        processorSource.Should().Contain("public static IReadOnlyList<AutosaveRecoveryCandidate> PrepareForRecovery(");
        processorSource.Should().Contain("FilterSupersededByNewerOriginal(DeduplicateByDocument(candidates))");
    }

    private static AutosaveRecoveryCandidate WriteCandidate(
        AutosaveSnapshotStore store,
        string snapshotId,
        string? documentId,
        string? displayName,
        DateTimeOffset timestamp,
        string? originalFilePath = null)
    {
        var snapshotPath = store.GetSnapshotPath(snapshotId);
        var sidecarPath = store.GetSidecarPath(snapshotId);
        File.WriteAllText(snapshotPath, "{}");
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

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "FreeX.RecoveryCandidateProcessor." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Best-effort test cleanup.
            }
        }
    }
}

using System.IO.Compression;
using Free.Shared.AppServices;
using FreeW.App.Presentation.Shell;
using FreeW.Core.IO;

namespace FreeW.App.Presentation.Tests;

public sealed class FreeWAutosaveSessionTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new(nameof(FreeWAutosaveSessionTests));

    [Fact]
    public void DefaultInterval_PreservesThirtySecondProductPolicy()
    {
        FreeWAutosaveSession.DefaultInterval.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Snapshot_UsesPortableSourcePortsAndGenerationGate()
    {
        var generation = 4;
        var writes = 0;
        var store = CreateStore();
        using var session = new FreeWAutosaveSession(
            new FreeWAutosavePorts(
                GetOriginalFilePath: () => "C:\\Docs\\Draft.docx",
                GetDisplayName: () => "Draft.docx",
                GetIsDirty: () => true,
                GetDirtyGeneration: () => generation,
                ExecuteWithDocument: writeDocument =>
                {
                    writes++;
                    writeDocument(TextDocument.CreateEmpty());
                }),
            store,
            "current-session");

        session.Snapshot();
        session.Snapshot();

        writes.Should().Be(1);
        var sidecar = AutosaveSnapshotStore.TryDeserializeSidecar(
            File.ReadAllText(store.GetSidecarPath("current-session")));
        sidecar.Should().NotBeNull();
        sidecar!.OriginalFilePath.Should().Be("C:\\Docs\\Draft.docx");
        sidecar.DisplayName.Should().Be("Draft.docx");

        generation++;
        session.Snapshot();
        writes.Should().Be(2);
    }

    [Fact]
    public void PlanAndCompleteRecovery_RestoresLatestAndDeletesOnlyOfferedCandidate()
    {
        var store = CreateStore();
        var older = CreateCandidate(store, "older", "2026-08-10T08:00:00Z", "Older");
        var newer = CreateCandidate(store, "newer", "2026-08-10T09:00:00Z", "Newer", "C:\\Docs\\Newer.docx");
        using var session = CreateSession(store);
        string? restoredPath = null;
        string? restoredOriginalPath = null;

        var plan = session.PlanLatestRecovery();
        var recovered = session.CompleteRecovery(
            plan!,
            accepted: true,
            restoreSnapshot: (path, originalPath) =>
            {
                restoredPath = path;
                restoredOriginalPath = originalPath;
                return true;
            });

        recovered.Should().BeTrue();
        plan!.Candidate.SnapshotPath.Should().Be(newer.SnapshotPath);
        restoredPath.Should().Be(newer.SnapshotPath);
        restoredOriginalPath.Should().Be("C:\\Docs\\Newer.docx");
        File.Exists(newer.SnapshotPath).Should().BeFalse();
        File.Exists(newer.SidecarPath).Should().BeFalse();
        File.Exists(older.SnapshotPath).Should().BeTrue();
        File.Exists(older.SidecarPath).Should().BeTrue();
    }

    [Fact]
    public void CompleteRecovery_DeclineKeepsCandidateAndSkipsRestore()
    {
        var store = CreateStore();
        var candidate = CreateCandidate(store, "declined", "2026-08-10T09:00:00Z", "Draft");
        using var session = CreateSession(store);
        var restoreCalled = false;

        var recovered = session.CompleteRecovery(
            session.PlanLatestRecovery()!,
            accepted: false,
            restoreSnapshot: (_, _) =>
            {
                restoreCalled = true;
                return true;
            });

        recovered.Should().BeFalse();
        restoreCalled.Should().BeFalse();
        File.Exists(candidate.SnapshotPath).Should().BeTrue();
        File.Exists(candidate.SidecarPath).Should().BeTrue();
    }

    [Fact]
    public void CompleteRecovery_FailedRestoreQuarantinesCandidate()
    {
        var store = CreateStore();
        var candidate = CreateCandidate(store, "failed", "2026-08-10T09:00:00Z", "Draft");
        using var session = CreateSession(store);

        var recovered = session.CompleteRecovery(
            session.PlanLatestRecovery()!,
            accepted: true,
            restoreSnapshot: (_, _) => false);

        recovered.Should().BeFalse();
        File.Exists(candidate.SnapshotPath).Should().BeFalse();
        File.Exists(candidate.SidecarPath).Should().BeFalse();
        Directory.GetFiles(Path.Combine(_temporaryDirectory.Path, "Quarantine"))
            .Should().HaveCount(2);
    }

    [Fact]
    public void CompleteRecovery_DefaultExceptionPolicyPropagatesAndKeepsCandidate()
    {
        var store = CreateStore();
        var candidate = CreateCandidate(store, "preserved", "2026-08-10T09:00:00Z", "Draft");
        using var session = CreateSession(store);

        Action act = () => session.CompleteRecovery(
            session.PlanLatestRecovery()!,
            accepted: true,
            restoreSnapshot: (_, _) => throw new InvalidOperationException("restore failed"));

        act.Should().Throw<InvalidOperationException>();
        File.Exists(candidate.SnapshotPath).Should().BeTrue();
        File.Exists(candidate.SidecarPath).Should().BeTrue();
    }

    [Fact]
    public void CompleteRecovery_QuarantineExceptionPolicyMovesCandidateAside()
    {
        var store = CreateStore();
        var candidate = CreateCandidate(store, "quarantined", "2026-08-10T09:00:00Z", "Draft");
        using var session = CreateSession(store);

        var recovered = session.CompleteRecovery(
            session.PlanLatestRecovery()!,
            accepted: true,
            restoreSnapshot: (_, _) => throw new InvalidOperationException("restore failed"),
            exceptionPolicy: FreeWRecoveryRestoreExceptionPolicy.QuarantineCandidate);

        recovered.Should().BeFalse();
        File.Exists(candidate.SnapshotPath).Should().BeFalse();
        File.Exists(candidate.SidecarPath).Should().BeFalse();
        Directory.GetFiles(Path.Combine(_temporaryDirectory.Path, "Quarantine"))
            .Should().HaveCount(2);
    }

    [Fact]
    public void CompleteDocumentRecovery_ReadsDocxAndAppliesRecoveredDocument()
    {
        var store = CreateStore();
        var candidate = CreateDocumentCandidate(
            store,
            "document",
            "2026-08-10T09:00:00Z",
            "C:\\Docs\\Draft.docx");
        using var session = CreateSession(store);
        TextDocument? restoredDocument = null;
        string? restoredOriginalPath = null;

        var recovered = session.CompleteDocumentRecovery(
            session.PlanLatestRecovery()!,
            accepted: true,
            applyRecoveredDocument: (document, originalPath) =>
            {
                restoredDocument = document;
                restoredOriginalPath = originalPath;
            });

        recovered.Should().BeTrue();
        restoredDocument.Should().NotBeNull();
        restoredOriginalPath.Should().Be("C:\\Docs\\Draft.docx");
        File.Exists(candidate.SnapshotPath).Should().BeFalse();
        File.Exists(candidate.SidecarPath).Should().BeFalse();
    }

    [Fact]
    public void CompleteCleanExit_DeletesCurrentSessionSnapshot()
    {
        var store = CreateStore();
        using var session = new FreeWAutosaveSession(
            new FreeWAutosavePorts(
                GetOriginalFilePath: () => null,
                GetDisplayName: () => "Draft",
                GetIsDirty: () => true,
                GetDirtyGeneration: () => 1,
                ExecuteWithDocument: writeDocument => writeDocument(TextDocument.CreateEmpty())),
            store,
            "current-session");
        session.Snapshot();

        session.CompleteCleanExit();

        File.Exists(store.GetSnapshotPath("current-session")).Should().BeFalse();
        File.Exists(store.GetSidecarPath("current-session")).Should().BeFalse();
    }

    public void Dispose() => _temporaryDirectory.Dispose();

    private AutosaveSnapshotStore CreateStore() => new(_temporaryDirectory.Path);

    private static FreeWAutosaveSession CreateSession(AutosaveSnapshotStore store) =>
        new(
            new FreeWAutosavePorts(
                GetOriginalFilePath: () => null,
                GetDisplayName: () => "Current",
                GetIsDirty: () => false,
                GetDirtyGeneration: () => 0,
                ExecuteWithDocument: writeDocument => writeDocument(TextDocument.CreateEmpty())),
            store,
            "current-session");

    private static AutosaveRecoveryCandidate CreateCandidate(
        AutosaveSnapshotStore store,
        string snapshotId,
        string timestampUtc,
        string displayName,
        string? originalPath = null)
    {
        var snapshotPath = store.GetSnapshotPath(snapshotId);
        WriteValidSnapshot(snapshotPath);

        var sidecarPath = store.GetSidecarPath(snapshotId);
        var sidecar = new AutosaveSidecar
        {
            DisplayName = displayName,
            OriginalFilePath = originalPath,
            SnapshotId = snapshotId,
            TimestampUtc = timestampUtc
        };
        File.WriteAllText(sidecarPath, AutosaveSnapshotStore.SerializeSidecar(sidecar));

        return new AutosaveRecoveryCandidate(snapshotPath, sidecarPath, sidecar);
    }

    private static AutosaveRecoveryCandidate CreateDocumentCandidate(
        AutosaveSnapshotStore store,
        string snapshotId,
        string timestampUtc,
        string originalPath)
    {
        var snapshotPath = store.GetSnapshotPath(snapshotId);
        DocxWriter.Write(TextDocument.CreateEmpty(), snapshotPath);

        var sidecarPath = store.GetSidecarPath(snapshotId);
        var sidecar = new AutosaveSidecar
        {
            DisplayName = "Draft",
            OriginalFilePath = originalPath,
            SnapshotId = snapshotId,
            TimestampUtc = timestampUtc
        };
        File.WriteAllText(sidecarPath, AutosaveSnapshotStore.SerializeSidecar(sidecar));

        return new AutosaveRecoveryCandidate(snapshotPath, sidecarPath, sidecar);
    }

    private static void WriteValidSnapshot(string path)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        archive.CreateEntry("[Content_Types].xml");
    }
}

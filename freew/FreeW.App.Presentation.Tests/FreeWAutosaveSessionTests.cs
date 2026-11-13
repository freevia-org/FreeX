using Free.Shared.AppServices;
using FreeW.App.Presentation.Shell;
using FreeW.Core.IO;

namespace FreeW.App.Presentation.Tests;

/// <summary>
/// FreeW's binding of the shared autosave session: the 30 s cadence, the DOCX serializer pair, and
/// the FreeW recovery profile. Lifecycle, planning, and recovery-completion behaviour is tested once
/// in <c>Free.Shared.AppServices.Tests.AutosaveApplicationProfileTests</c>.
/// </summary>
public sealed class FreeWAutosaveSessionTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new(nameof(FreeWAutosaveSessionTests));

    [Fact]
    public void DefaultInterval_PreservesThirtySecondProductPolicy()
    {
        FreeWAutosaveSession.DefaultInterval.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Profile_CarriesFreeWProductTextAndDocxSerializers()
    {
        var text = FreeWAutosaveProfile.Recovery.ResolveText();

        FreeWAutosaveProfile.Recovery.PromptText.Should().Be(
            new AutosaveRecoveryPromptText("FreeW", "documents"));
        FreeWAutosaveProfile.Recovery.UnnamedItemFallback.Should().Be("a document");
        text.Title.Should().Be("FreeW - Recover");
        text.NoDocumentsMessage.Should().Be("No unsaved documents were found.");
        FreeWAutosaveProfile.Session.Recovery.Should().BeSameAs(FreeWAutosaveProfile.Recovery);
    }

    [Fact]
    public void Snapshot_WritesAReadableDocxThroughThePortableSourcePorts()
    {
        var generation = 4;
        var writes = 0;
        var store = CreateStore();
        using var session = new FreeWAutosaveSession(
            new AutosaveDocumentPorts<TextDocument>(
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
        DocxReader.Read(store.GetSnapshotPath("current-session")).Should().NotBeNull();
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

    public void Dispose() => _temporaryDirectory.Dispose();

    private AutosaveSnapshotStore CreateStore() => new(_temporaryDirectory.Path);

    private static FreeWAutosaveSession CreateSession(AutosaveSnapshotStore store) =>
        new(
            new AutosaveDocumentPorts<TextDocument>(
                GetOriginalFilePath: () => null,
                GetDisplayName: () => "Current",
                GetIsDirty: () => false,
                GetDirtyGeneration: () => 0,
                ExecuteWithDocument: writeDocument => writeDocument(TextDocument.CreateEmpty())),
            store,
            "current-session");

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
}

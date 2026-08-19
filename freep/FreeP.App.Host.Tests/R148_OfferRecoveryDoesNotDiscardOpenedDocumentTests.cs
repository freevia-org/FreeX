using System.IO;
using System.Windows;
using Free.Shared.AppServices;
using Free.Shared.Shell;
using FreeP.App.Compositor;
using FreeP.App.Recording;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Round 148 REMEDIATION (startup-fileopen F2, WPF host sibling): <see
/// cref="AutosaveCoordinator.OfferRecovery"/>'s "useCurrentWindow" branch used to restore an
/// accepted recovery candidate straight into the caller's window with no check at all -- the exact
/// bug already fixed in FreeP's Avalonia <c>AutosaveAdapter.OfferRecoveryAsync</c>
/// (<c>R148_StartupRecoveryDoesNotDiscardOpenedDocumentTests</c>), left standing here.
/// <c>MainWindow</c> opens a command-line/file-association startup file into itself SYNCHRONOUSLY,
/// before <c>OfferRecovery</c> runs, so the window may already hold a document the user explicitly
/// asked for. That document is not dirty (it was just opened, not edited), so the silent startup
/// offer must ask whether the window already has an explicitly opened document (a non-null
/// <c>CurrentPath</c>) BEFORE any candidate is applied, and route every accepted candidate through
/// the new-window path when it does, exactly like every candidate after the first.
/// </summary>
public sealed class R148_OfferRecoveryDoesNotDiscardOpenedDocumentTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeP.R148OfferRecoveryTests-");
    private string TempDir => _temporaryDirectory.Path;

    public void Dispose() => _temporaryDirectory.Dispose();

    private (AutosaveCoordinator Coordinator, PresentationFileCommandSession File, Window Owner) NewWindowHarness(
        AutosaveSnapshotStore store,
        Action<AutosaveRecoveryCandidate>? recoverInNewWindow = null)
    {
        var window = new Window { Width = 100, Height = 100, ShowInTaskbar = false, Left = -10000, Top = -10000 };
        var model = Presentation.CreateEmpty();
        var file = WpfPresentationFileCommandSessionFactory.Create(
            window,
            () => model,
            loaded => model = loaded,
            onChanged: () => { },
            loadRecentFilesStore: () => RecentFilesStore.Load(
                Path.Combine(TempDir, Guid.NewGuid().ToString("N") + ".json")),
            videoEncoderCapability: LinuxVideoEncoderCapability.Unavailable("Test encoder handoff deferred."),
            nativePrintCapability: PresentationNativePrintHandoffHostCapabilities.Deferred(
                "WPF print host",
                "Test printer handoff deferred."));
        var coordinator = new AutosaveCoordinator(
            () => model,
            file,
            ports => new FreePAutosaveSession(ports, store),
            recoverInNewWindow: recoverInNewWindow is null
                ? null
                : candidate =>
                {
                    recoverInNewWindow(candidate);
                    return true;
                });
        return (coordinator, file, window);
    }

    private static void WriteCandidate(AutosaveSnapshotStore store, string id, string timestampUtc, string displayName)
    {
        var snapshotPath = store.GetSnapshotPath(id);
        var sidecarPath = store.GetSidecarPath(id);
        PptxPackageWriter.Write(Presentation.CreateEmpty(), snapshotPath);
        var sidecar = new AutosaveSidecar
        {
            DisplayName = displayName,
            TimestampUtc = timestampUtc,
            SnapshotId = id
        };
        File.WriteAllText(sidecarPath, AutosaveSnapshotStore.SerializeSidecar(sidecar));
    }

    /// <summary>
    /// THE FIX: a window that already has an explicitly opened document (simulated the same way
    /// <c>MainWindow</c>'s constructor leaves it after a successful startup open -- opened via
    /// <c>OpenPathAsync</c>, so <c>CurrentPath</c> is set and the document is NOT dirty) must not
    /// have that document silently replaced by an unrelated recovered snapshot.
    /// </summary>
    [StaFact]
    public void OfferRecovery_RoutesToANewWindowWhenTheCurrentWindowAlreadyHasAnOpenedDocument()
    {
        var store = new AutosaveSnapshotStore(TempDir);
        WriteCandidate(store, "snap-unrelated", "2026-06-20T07:00:00Z", "Unrelated Deck");

        var recoveredInNewWindow = false;
        var (coordinator, file, owner) = NewWindowHarness(store, _ => recoveredInNewWindow = true);

        // Mirrors what MainWindow does for a command-line/file-association startup file: a real
        // Open, which leaves CurrentPath non-null and the document clean (not dirty).
        var openedPath = Path.Combine(TempDir, "B.pptx");
        PptxPackageWriter.Write(Presentation.CreateEmpty(), openedPath);
        file.OpenPathAsync(openedPath).GetAwaiter().GetResult().Succeeded.Should().BeTrue();
        file.IsDirty.Should().BeFalse("a freshly opened document has nothing unsaved yet");
        var pathBeforeRecovery = file.CurrentPath;

        HeadlessMessageBox.Handler = (_, _) => UserMessageResult.Yes; // accept the "recover this?" offer
        try
        {
            var anyAccepted = coordinator.OfferRecovery(owner);

            anyAccepted.Should().BeTrue("the candidate is still offered and accepted");
            recoveredInNewWindow.Should().BeTrue(
                "an already-opened document must route the recovery into a NEW window, not overwrite this one");
            file.CurrentPath.Should().Be(pathBeforeRecovery,
                "the just-opened document in the current window must never be silently replaced");
            file.IsDirty.Should().BeFalse(
                "the current window's clean, just-opened document must be untouched by the recovery");
        }
        finally
        {
            HeadlessMessageBox.Handler = null;
        }
    }

    /// <summary>
    /// Sibling no-regression (rule 10's adjacent case): a genuinely fresh window -- no startup file
    /// opened, <c>CurrentPath</c> still null -- must keep the prior behaviour exactly: the silent
    /// startup offer recovers straight into the current window, unconditionally and ungated.
    /// </summary>
    [StaFact]
    public void OfferRecovery_StillRecoversIntoTheCurrentWindowWhenItHasNoOpenedDocument()
    {
        var store = new AutosaveSnapshotStore(TempDir);
        WriteCandidate(store, "snap-fresh-window", "2026-06-20T07:00:00Z", "Recovered Deck");

        var recoveredInNewWindow = false;
        var (coordinator, file, owner) = NewWindowHarness(store, _ => recoveredInNewWindow = true);
        // never opened -- CurrentPath stays null

        HeadlessMessageBox.Handler = (_, _) => UserMessageResult.Yes;
        try
        {
            var anyAccepted = coordinator.OfferRecovery(owner);

            anyAccepted.Should().BeTrue();
            recoveredInNewWindow.Should().BeFalse(
                "the fresh-window path must not be redirected to a new window -- it must recover straight into itself, exactly as before this fix");
            file.IsDirty.Should().BeTrue("a recovered presentation is unsaved work and must stay dirty");
            store.EnumerateCandidates().Should().BeEmpty();
        }
        finally
        {
            HeadlessMessageBox.Handler = null;
        }
    }
}

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
/// The manual "Recover Unsaved Presentations" Backstage command --
/// <see cref="AutosaveCoordinator.RecoverUnsavedPresentations"/>. Unlike the best-effort, silent
/// <see cref="AutosaveCoordinator.OfferRecovery"/> used at startup, this is user-invoked: it must
/// tell the user when there is nothing to recover, and it must ask before restoring (not just
/// silently accept). Ported from FreeW's <c>R133_AutosaveCoordinatorRecoverUnsavedDocumentsTests</c>
/// -style coverage; mirrors <c>AutosaveCoordinatorEmergencySnapshotTests.NewWindowHarness</c>.
/// </summary>
public sealed class AutosaveCoordinatorRecoverUnsavedTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeP.AutosaveRecoverUnsavedTests-");
    private string TempDir => _temporaryDirectory.Path;

    public void Dispose() => _temporaryDirectory.Dispose();

    private (AutosaveCoordinator Coordinator, PresentationFileCommandSession File, Window Owner) NewWindowHarness(
        AutosaveSnapshotStore store)
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
            ports => new FreePAutosaveSession(ports, store));
        return (coordinator, file, window);
    }

    /// <summary>
    /// Unlike the silent startup offer, the manual command must tell the user there was nothing to
    /// recover rather than doing nothing visibly.
    /// </summary>
    [StaFact]
    public void RecoverUnsavedPresentations_TellsTheUserWhenThereIsNothingToRecover()
    {
        var store = new AutosaveSnapshotStore(TempDir);
        var (coordinator, _, owner) = NewWindowHarness(store);

        var infoMessages = new List<string?>();
        HeadlessMessageBox.Handler = (message, _) =>
        {
            infoMessages.Add(message);
            return UserMessageResult.Ok;
        };
        try
        {
            var recovered = coordinator.RecoverUnsavedPresentations(owner);

            recovered.Should().BeFalse();
            infoMessages.Should().ContainSingle()
                .Which.Should().Be("No unsaved presentations were found.");
        }
        finally
        {
            HeadlessMessageBox.Handler = null;
        }
    }

    /// <summary>
    /// Core happy path: a pending snapshot from a crashed window is offered, and accepting it
    /// restores it into the invoking (current) window, dirty, and deletes the recovered snapshot.
    /// </summary>
    [StaFact]
    public void RecoverUnsavedPresentations_RecoversAnAcceptedCandidateIntoTheCurrentWindow()
    {
        var store = new AutosaveSnapshotStore(TempDir);
        var (crashed, crashedFile, _) = NewWindowHarness(store);
        crashedFile.MarkDirty();
        crashed.TryEmergencySnapshot();
        var snapshotPath = store.GetSnapshotPath(crashed.SnapshotIdForTests);
        File.Exists(snapshotPath).Should().BeTrue("the crashed window must have left a snapshot behind");
        // A real crash releases the ownership lock automatically (the process just exits); simulate
        // that here so recovery does not filter this snapshot out as still "live owned" by `crashed`.
        crashed.SimulateCrashForTests();

        var (recovering, recoveringFile, owner) = NewWindowHarness(store);

        HeadlessMessageBox.Handler = (_, _) => UserMessageResult.Ok;
        try
        {
            var recovered = recovering.RecoverUnsavedPresentations(owner);

            recovered.Should().BeTrue();
            recoveringFile.IsDirty.Should().BeTrue(
                "a recovered presentation is unsaved work and must stay dirty");
            File.Exists(snapshotPath).Should().BeFalse(
                "a successfully recovered snapshot must be cleaned up so it is not offered again");
        }
        finally
        {
            HeadlessMessageBox.Handler = null;
        }
    }

    /// <summary>
    /// Declining the offer (Cancel) must leave the candidate on disk untouched and the invoking
    /// window unaffected -- the user gets to try again later.
    /// </summary>
    [StaFact]
    public void RecoverUnsavedPresentations_LeavesTheSnapshotWhenTheUserDeclines()
    {
        var store = new AutosaveSnapshotStore(TempDir);
        var (crashed, crashedFile, _) = NewWindowHarness(store);
        crashedFile.MarkDirty();
        crashed.TryEmergencySnapshot();
        var snapshotPath = store.GetSnapshotPath(crashed.SnapshotIdForTests);
        crashed.SimulateCrashForTests();

        var (recovering, recoveringFile, owner) = NewWindowHarness(store);

        HeadlessMessageBox.Handler = (_, _) => UserMessageResult.Cancel;
        try
        {
            var recovered = recovering.RecoverUnsavedPresentations(owner);

            recovered.Should().BeFalse();
            recoveringFile.IsDirty.Should().BeFalse();
            File.Exists(snapshotPath).Should().BeTrue(
                "declining recovery must not delete the user's only copy of the unsaved presentation");
        }
        finally
        {
            HeadlessMessageBox.Handler = null;
        }
    }

    /// <summary>
    /// r141: unlike the manual command above, <see cref="AutosaveCoordinator.OfferRecovery"/> is the
    /// unprompted STARTUP offer that would otherwise repeat on every relaunch. Declining it must
    /// discard the snapshot so the same stale presentation does not keep nagging (matches FreeX's own
    /// <c>StartupRecoveryWorkflow</c>, which discards a declined snapshot the same way).
    /// </summary>
    [StaFact]
    public void OfferRecovery_DeletesTheSnapshotWhenTheUserDeclines()
    {
        var store = new AutosaveSnapshotStore(TempDir);
        var (crashed, crashedFile, _) = NewWindowHarness(store);
        crashedFile.MarkDirty();
        crashed.TryEmergencySnapshot();
        var snapshotPath = store.GetSnapshotPath(crashed.SnapshotIdForTests);
        File.Exists(snapshotPath).Should().BeTrue("the crashed window must have left a snapshot behind");
        crashed.SimulateCrashForTests();

        var (recovering, recoveringFile, owner) = NewWindowHarness(store);

        HeadlessMessageBox.Handler = (_, _) => UserMessageResult.No;
        try
        {
            var anyAccepted = recovering.OfferRecovery(owner);

            anyAccepted.Should().BeFalse();
            recoveringFile.IsDirty.Should().BeFalse();
            File.Exists(snapshotPath).Should().BeFalse(
                "a declined startup offer must be discarded so it does not nag on the next launch");
            store.EnumerateCandidates().Should().BeEmpty();
        }
        finally
        {
            HeadlessMessageBox.Handler = null;
        }
    }

    /// <summary>
    /// Pins the Backstage wiring end-to-end: without this, the portable endpoint and pane exist but
    /// nothing in the real host ever reaches <see cref="AutosaveCoordinator.RecoverUnsavedPresentations"/>.
    /// </summary>
    [Fact]
    public void MainWindow_WiresRecoverUnsavedIntoTheBackstageEndpoints()
    {
        var source = TestWorkspaceFileLocator.ReadAllText("freep", "FreeP.App.Host", "MainWindow.cs");

        source.Should().Contain("RecoverUnsaved: () => _autosave.RecoverUnsavedPresentations(this),");
    }

    /// <summary>
    /// Like <see cref="NewWindowHarness"/>, but also counts how many times the presentation model
    /// was replaced (<c>loadPresentation</c> invocations), so tests can prove the CURRENT window's
    /// in-memory content was never touched when recovery is declined.
    /// </summary>
    private (AutosaveCoordinator Coordinator, PresentationFileCommandSession File, Window Owner, Func<int> LoadCount)
        NewWindowHarnessWithLoadTracking(AutosaveSnapshotStore store)
    {
        var window = new Window { Width = 100, Height = 100, ShowInTaskbar = false, Left = -10000, Top = -10000 };
        var model = Presentation.CreateEmpty();
        var loadCount = 0;
        var file = WpfPresentationFileCommandSessionFactory.Create(
            window,
            () => model,
            loaded =>
            {
                loadCount++;
                model = loaded;
            },
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
            ports => new FreePAutosaveSession(ports, store));
        return (coordinator, file, window, () => loadCount);
    }

    /// <summary>
    /// r146 F1: the manual Backstage command is reachable at any time, not just on a fresh startup
    /// window. If the CURRENT window already holds unsaved edits, accepting an old crash snapshot
    /// must not silently overwrite them -- the user must be asked to save/discard/cancel first, the
    /// same way New/Open/Close already gate destructive replacement. Before the fix,
    /// <c>RestoreAutosaveSnapshot</c> was invoked unconditionally and this test fails: the current
    /// presentation is replaced (LoadCount reaches 1), the method reports success, and the snapshot
    /// is deleted out from under the user's declined choice.
    /// </summary>
    [StaFact]
    public void RecoverUnsavedPresentations_DoesNotOverwriteTheCurrentWindowWhenTheUserDeclinesToDiscardItsUnsavedEdits()
    {
        var store = new AutosaveSnapshotStore(TempDir);
        var (crashed, crashedFile, _) = NewWindowHarness(store);
        crashedFile.MarkDirty();
        crashed.TryEmergencySnapshot();
        var snapshotPath = store.GetSnapshotPath(crashed.SnapshotIdForTests);
        File.Exists(snapshotPath).Should().BeTrue("the crashed window must have left a snapshot behind");
        crashed.SimulateCrashForTests();

        var (recovering, recoveringFile, owner, loadCount) = NewWindowHarnessWithLoadTracking(store);
        // Simulate the user having made their OWN unsaved edits in the current window before ever
        // touching the recovery command.
        recoveringFile.MarkDirty();

        HeadlessMessageBox.Handler = (_, buttons) => buttons switch
        {
            // "Recover unsaved changes to X?" -- accept the OLD candidate.
            UserMessageButtons.OkCancel => UserMessageResult.Ok,
            // "Save changes to <current> before recovering an unsaved presentation?" -- decline
            // (Cancel), protecting the current window's own unsaved work.
            UserMessageButtons.YesNoCancel => UserMessageResult.Cancel,
            _ => UserMessageResult.Cancel,
        };
        try
        {
            var recovered = recovering.RecoverUnsavedPresentations(owner);

            recovered.Should().BeFalse(
                "the current window's unsaved edits must not be silently discarded");
            loadCount().Should().Be(0,
                "declining the discard prompt must leave the current in-memory presentation untouched");
            recoveringFile.IsDirty.Should().BeTrue(
                "the current window's own unsaved edits must survive a declined recovery");
            File.Exists(snapshotPath).Should().BeTrue(
                "the recovery candidate must be preserved on disk so the user can revisit it later");
        }
        finally
        {
            HeadlessMessageBox.Handler = null;
        }
    }

    /// <summary>
    /// Sibling no-regression case: when the user affirmatively agrees to discard the current window's
    /// unsaved edits (answers "Don't Save" to the new prompt), recovery must still proceed into the
    /// current window exactly as it always has.
    /// </summary>
    [StaFact]
    public void RecoverUnsavedPresentations_ProceedsIntoTheCurrentWindowWhenTheUserApprovesDiscardingItsUnsavedEdits()
    {
        var store = new AutosaveSnapshotStore(TempDir);
        var (crashed, crashedFile, _) = NewWindowHarness(store);
        crashedFile.MarkDirty();
        crashed.TryEmergencySnapshot();
        var snapshotPath = store.GetSnapshotPath(crashed.SnapshotIdForTests);
        crashed.SimulateCrashForTests();

        var (recovering, recoveringFile, owner, loadCount) = NewWindowHarnessWithLoadTracking(store);
        recoveringFile.MarkDirty();

        HeadlessMessageBox.Handler = (_, buttons) => buttons switch
        {
            UserMessageButtons.OkCancel => UserMessageResult.Ok,
            // "Don't Save" -- the user explicitly agrees to discard their current unsaved edits.
            UserMessageButtons.YesNoCancel => UserMessageResult.No,
            _ => UserMessageResult.Cancel,
        };
        try
        {
            var recovered = recovering.RecoverUnsavedPresentations(owner);

            recovered.Should().BeTrue();
            loadCount().Should().Be(1);
            recoveringFile.IsDirty.Should().BeTrue(
                "a recovered presentation is unsaved work and must stay dirty");
            File.Exists(snapshotPath).Should().BeFalse(
                "a successfully recovered snapshot must be cleaned up so it is not offered again");
        }
        finally
        {
            HeadlessMessageBox.Handler = null;
        }
    }
}

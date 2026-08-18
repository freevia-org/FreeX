using System;
using System.IO;
using System.Windows;
using Free.Shared.AppServices;
using Free.Shared.Shell;
using FreeW.App.Host;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Shell;
using FreeW.Core.Model;

namespace FreeW.App.Host.Tests;

/// <summary>
/// r141: <see cref="AutosaveCoordinator.OfferRecovery"/> is the real, unprompted startup entry point
/// a relaunching user reaches after a crash. Declining its offer must discard the snapshot so the
/// same stale document does not keep nagging on every later launch (matches FreeX's own
/// <c>StartupRecoveryWorkflow</c>, which discards a declined snapshot the same way). This exercises
/// the production coordinator end to end -- real WPF window, real <see cref="AutosaveSnapshotStore"/>,
/// real files on disk -- rather than the lower-level workflow helper directly.
/// </summary>
public sealed class AutosaveCoordinatorOfferRecoveryDeclineTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "FreeW.OfferRecoveryDeclineTests", Guid.NewGuid().ToString("N"));

    public AutosaveCoordinatorOfferRecoveryDeclineTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    private (AutosaveCoordinator coordinator, FileCommands file) NewWindowHarness(AutosaveSnapshotStore store)
    {
        var window = new Window { Width = 100, Height = 100, ShowInTaskbar = false, Left = -10000, Top = -10000 };
        var editor = new DocumentView();
        editor.LoadModel(TextDocument.CreateEmpty());
        var file = new FileCommands(
            window,
            editor,
            onChanged: () => { },
            loadRecentFilesStore: () => RecentFilesStore.Load(Path.Combine(_tempDir, Guid.NewGuid().ToString("N") + ".json")));
        var coordinator = new AutosaveCoordinator(
            editor,
            file,
            ports => new FreeWAutosaveSession(ports, store));
        return (coordinator, file);
    }

    /// <summary>
    /// A crashed window left a snapshot; the relaunching window's <c>OfferRecovery</c> offers it and
    /// the user answers "No". The snapshot must be gone afterward so the next relaunch is silent.
    /// </summary>
    [StaFact]
    public void OfferRecovery_DeletesTheSnapshotWhenTheUserDeclines()
    {
        var store = new AutosaveSnapshotStore(_tempDir);

        var (crashed, crashedFile) = NewWindowHarness(store);
        crashedFile.MarkDirty();
        crashed.SnapshotNowForTests();
        var snapshotPath = store.GetSnapshotPath(crashed.SnapshotIdForTests);
        File.Exists(snapshotPath).Should().BeTrue("the crashed window must have left a snapshot behind");
        crashed.SimulateCrashForTests();

        var (recovering, recoveringFile) = NewWindowHarness(store);
        var owningWindow = new Window { Width = 100, Height = 100, ShowInTaskbar = false, Left = -10000, Top = -10000 };

        HeadlessMessageBox.Handler = (_, _) => UserMessageResult.No;
        try
        {
            var anyAccepted = recovering.OfferRecovery(owningWindow);

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
}

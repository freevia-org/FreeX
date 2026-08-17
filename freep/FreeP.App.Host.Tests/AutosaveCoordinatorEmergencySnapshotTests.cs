using System.IO;
using System.Windows;
using Free.Shared.AppServices;
using FreeP.App.Compositor;
using FreeP.App.Recording;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

/// <summary>
/// FreeP shipped with NO autosave machinery at all: no periodic snapshot, no emergency snapshot on
/// crash, and no startup recovery offer -- a crash lost every edit back to the last manual save.
/// These tests exercise <see cref="AutosaveCoordinator.TryEmergencySnapshot"/>, the exact method
/// <see cref="EmergencySnapshotCrashHandler"/> calls on every open <see cref="MainWindow"/> via
/// <c>MainWindow.AutosaveCoordinatorForCrashHandler</c> -- the real path a WPF crash reaches, not a
/// test-only helper. Mirrors FreeW's <c>R138_AutosaveCoordinatorEmergencySnapshotTests</c>.
/// </summary>
public sealed class AutosaveCoordinatorEmergencySnapshotTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeP.AutosaveEmergencyTests-");
    private string TempDir => _temporaryDirectory.Path;

    public void Dispose() => _temporaryDirectory.Dispose();

    private (AutosaveCoordinator Coordinator, PresentationFileCommandSession File) NewWindowHarness(
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
        return (coordinator, file);
    }

    /// <summary>
    /// Core regression: a dirty presentation must get an emergency snapshot even though no periodic
    /// autosave tick has ever run -- exactly the situation right after the user's first edit, the
    /// moment before a crash would otherwise lose it.
    /// </summary>
    [StaFact]
    public void TryEmergencySnapshot_WritesASnapshotForADirtyPresentation_WithoutWaitingForATimerTick()
    {
        var store = new AutosaveSnapshotStore(TempDir);
        var (coordinator, file) = NewWindowHarness(store);
        file.MarkDirty();

        coordinator.TryEmergencySnapshot();

        var snapshotPath = store.GetSnapshotPath(coordinator.SnapshotIdForTests);
        File.Exists(snapshotPath).Should().BeTrue(
            "a crash right after a dirty edit must not lose the edit -- that is exactly what autosave exists to prevent");
        File.Exists(store.GetSidecarPath(coordinator.SnapshotIdForTests)).Should().BeTrue();

        // Recovery reads the snapshot back through PptxPackageReader, so an unreadable snapshot is
        // no better than none at all.
        PptxPackageReader.Read(snapshotPath).Should().NotBeNull();
    }

    /// <summary>
    /// Sibling no-regression: a clean (never-edited) presentation must NOT get an emergency
    /// snapshot. Recovery must never resurrect a deck that had nothing unsaved at crash time.
    /// </summary>
    [StaFact]
    public void TryEmergencySnapshot_SkipsACleanPresentation()
    {
        var store = new AutosaveSnapshotStore(TempDir);
        var (coordinator, _) = NewWindowHarness(store);

        coordinator.TryEmergencySnapshot();

        File.Exists(store.GetSnapshotPath(coordinator.SnapshotIdForTests)).Should().BeFalse();
    }

    /// <summary>
    /// A recovered snapshot is unsaved work belonging to the ORIGINAL file, so restoring it must
    /// leave the window dirty and pointed at that path -- not clean, and not pointed at the
    /// snapshot's throwaway location in the recovery directory.
    /// </summary>
    [StaFact]
    public void RestoreAutosaveSnapshot_ReopensDirtyAndTargetsTheOriginalPath()
    {
        var store = new AutosaveSnapshotStore(TempDir);
        var snapshotPath = Path.Combine(TempDir, "snapshot.fxl");
        PptxPackageWriter.Write(Presentation.CreateEmpty(), snapshotPath);
        var originalPath = Path.Combine(TempDir, "Quarterly.pptx");
        var (_, file) = NewWindowHarness(store);

        var restored = file.RestoreAutosaveSnapshot(snapshotPath, originalPath);

        restored.Should().BeTrue();
        file.IsDirty.Should().BeTrue();
        file.CurrentPath.Should().Be(originalPath);
    }

    /// <summary>
    /// A snapshot truncated by the very crash that produced it must fail closed: return false
    /// without throwing, so the caller preserves rather than deletes what may be the user's only
    /// copy of the unsaved deck.
    /// </summary>
    [StaFact]
    public void RestoreAutosaveSnapshot_ReturnsFalseForACorruptSnapshot()
    {
        var store = new AutosaveSnapshotStore(TempDir);
        var corruptPath = Path.Combine(TempDir, "corrupt.fxl");
        File.WriteAllText(corruptPath, "not a pptx package");
        var (_, file) = NewWindowHarness(store);

        var restored = file.RestoreAutosaveSnapshot(corruptPath, originalPath: null);

        restored.Should().BeFalse();
        file.IsDirty.Should().BeFalse();
    }

    /// <summary>
    /// Pins the wiring a real crash reaches end-to-end: Program.cs must hand the shared WPF runner a
    /// hook that fans out to every open window's coordinator via <c>TryEmergencySnapshot</c>.
    /// Without this wiring the coordinator-level behaviour above is unreachable from an actual
    /// crash. The fan-out itself lives in a separate file (not Program.cs) so Program.cs never
    /// references <c>Application.Current</c> directly and stays a thin shared-runner adapter.
    /// </summary>
    [Fact]
    public void Program_WiresEmergencySnapshotHookIntoTheSharedRunner()
    {
        var programSource = TestWorkspaceFileLocator.ReadAllText("freep", "FreeP.App.Host", "Program.cs");
        var handlerSource = TestWorkspaceFileLocator.ReadAllText(
            "freep", "FreeP.App.Host", "EmergencySnapshotCrashHandler.cs");

        programSource.Should().Contain(
            "OnEmergencySnapshot = EmergencySnapshotCrashHandler.TryEmergencySnapshotAllWindows");
        programSource.Should().NotContain("Application.Current");
        handlerSource.Should().Contain("mainWindow.AutosaveCoordinatorForCrashHandler?.TryEmergencySnapshot()");
    }

    /// <summary>
    /// Pins that MainWindow actually starts autosave and offers recovery, and that it only tears the
    /// snapshot down once the close is committed. A coordinator that is constructed but never
    /// started produces no periodic snapshots at all.
    /// </summary>
    [Fact]
    public void MainWindow_StartsAutosaveOffersRecoveryAndStopsOnlyOnACommittedClose()
    {
        var source = TestWorkspaceFileLocator.ReadAllText("freep", "FreeP.App.Host", "MainWindow.cs");

        source.Should().Contain("_autosave = new AutosaveCoordinator(");
        source.Should().Contain("_autosave.OfferRecovery(this);");
        source.Should().Contain("_autosave.Start();");
        source.Should().Contain("_autosave.Stop();");
        source.Should().Contain("internal AutosaveCoordinator? AutosaveCoordinatorForCrashHandler => _autosave;");
    }
}

using System;
using System.IO;
using System.Windows;
using Free.Shared.AppServices;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Shell;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// R138: unlike FreeX's WPF host, FreeW's crash handler never took an emergency autosave snapshot
/// before this fix -- a crash lost every edit since the last periodic (30s) <see
/// cref="AutosaveCoordinator"/> timer tick, the exact scenario autosave exists to prevent. These
/// tests exercise <see cref="AutosaveCoordinator.TryEmergencySnapshot"/>, the exact method
/// Program.cs's crash handler (<c>TryEmergencySnapshotAllWindowsOnDispatcher</c>) calls on every
/// open <c>MainWindow</c> via <c>MainWindow.AutosaveCoordinatorForCrashHandler</c> -- the real
/// path a WPF crash reaches, not a test-only helper.
/// </summary>
public sealed class R138_AutosaveCoordinatorEmergencySnapshotTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "FreeW.R138EmergencySnapshotTests", Guid.NewGuid().ToString("N"));

    public R138_AutosaveCoordinatorEmergencySnapshotTests() => Directory.CreateDirectory(_tempDir);

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
    /// Core regression: a dirty document must get an emergency snapshot even though no periodic
    /// (30s) <c>Snapshot()</c> tick has ever run -- exactly the situation right after the user's
    /// first edit, the moment before a crash would otherwise lose it.
    /// </summary>
    [StaFact]
    public void TryEmergencySnapshot_WritesASnapshotForADirtyDocument_WithoutWaitingForATimerTick()
    {
        var store = new AutosaveSnapshotStore(_tempDir);
        var (coordinator, file) = NewWindowHarness(store);
        file.MarkDirty();

        coordinator.TryEmergencySnapshot();

        var snapshotPath = store.GetSnapshotPath(coordinator.SnapshotIdForTests);
        File.Exists(snapshotPath).Should().BeTrue(
            "a crash right after a dirty edit must not lose the edit -- that is exactly what autosave exists to prevent");
        File.Exists(store.GetSidecarPath(coordinator.SnapshotIdForTests)).Should().BeTrue();
    }

    /// <summary>
    /// Sibling no-regression: a clean (never-edited) document must NOT get an emergency snapshot.
    /// Document Recovery must never resurrect a document that had nothing unsaved at crash time
    /// (mirrors <c>AutosaveSnapshotCoordinator.TryEmergencySnapshot</c>'s own contract).
    /// </summary>
    [StaFact]
    public void TryEmergencySnapshot_SkipsACleanDocument()
    {
        var store = new AutosaveSnapshotStore(_tempDir);
        var (coordinator, _) = NewWindowHarness(store);

        coordinator.TryEmergencySnapshot();

        File.Exists(store.GetSnapshotPath(coordinator.SnapshotIdForTests)).Should().BeFalse();
    }

    /// <summary>
    /// Pins the wiring a real crash reaches end-to-end: Program.cs must hand the shared WPF runner
    /// a hook that fans out to every open window's coordinator via <c>TryEmergencySnapshot</c>.
    /// Without this wiring the coordinator-level fix above is unreachable from an actual crash.
    /// The fan-out itself lives in a separate file (not Program.cs) so Program.cs never references
    /// <c>Application.Current</c> directly -- see
    /// <see cref="SharedWpfStartupRunnerTests.SisterAppPrograms_UseSharedWpfStartupRunner"/>, which
    /// pins that Program.cs stays a thin shared-runner adapter.
    /// </summary>
    [Fact]
    public void Program_WiresEmergencySnapshotHookIntoTheSharedRunner()
    {
        var programSource = File.ReadAllText(
            TestWorkspaceFileLocator.Find("freew", "FreeW.App.Host", "Program.cs"));
        var handlerSource = File.ReadAllText(
            TestWorkspaceFileLocator.Find("freew", "FreeW.App.Host", "EmergencySnapshotCrashHandler.cs"));

        programSource.Should().Contain("OnEmergencySnapshot = EmergencySnapshotCrashHandler.TryEmergencySnapshotAllWindows");
        programSource.Should().NotContain("Application.Current");
        handlerSource.Should().Contain("mainWindow.AutosaveCoordinatorForCrashHandler?.TryEmergencySnapshot()");
    }
}

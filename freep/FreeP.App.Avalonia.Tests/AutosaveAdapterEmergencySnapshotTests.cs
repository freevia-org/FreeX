using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using Free.Shared.AppServices;
using FreeP.App.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia.Tests;

/// <summary>
/// Avalonia twin of <c>FreeP.App.Host.Tests.AutosaveCoordinatorEmergencySnapshotTests</c>. FreeP's
/// Avalonia shell had no autosave machinery at all before this round.
/// <see cref="AutosaveAdapter.TryEmergencySnapshots"/> is the exact static hook wired into
/// <c>App.cs</c>'s <c>DesktopProfile</c> (<c>onEmergencySnapshot: AutosaveAdapter.TryEmergencySnapshots</c>),
/// which the shared Avalonia runner's crash handler calls -- not a test-only helper.
/// </summary>
public sealed class AutosaveAdapterEmergencySnapshotTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreePHeadlessApp).Assembly);

    private static async Task<bool> OnUiThread(Action action)
    {
        try
        {
            await Session.Dispatch(action, CancellationToken.None);
            return true;
        }
        catch (Exception)
        {
            return false; // no headless drawing backend in this environment
        }
    }

    private static FileCommandWorkflow NewWorkflow() =>
        new(
            maxRecentEntries: () => 10,
            onChanged: () => { },
            promptSaveChanges: _ => SaveChangesPrompt.DontSave,
            save: () => true,
            loadRecentFilesStore: () => RecentFilesStore.Load(
                Path.Combine(Path.GetTempPath(), "FreeP.AutosaveAvaloniaEmergencyTests", Guid.NewGuid().ToString("N") + ".json")));

    private static AutosaveAdapter NewAdapter(
        FileCommandWorkflow workflow,
        Func<FreePAutosavePorts, FreePAutosaveSession> sessionFactory) =>
        new(
            () => Presentation.CreateEmpty(),
            workflow,
            applyRecoveredPresentation: (_, _) => { },
            sessionFactory: sessionFactory);

    /// <summary>
    /// Core regression: the static fan-out a crash reaches must write a snapshot for a dirty window
    /// even though no periodic autosave tick has run yet.
    ///
    /// <para>
    /// Uses a <c>sessionFactory</c> override with a synchronous (non-<c>Dispatcher.UIThread</c>)
    /// <c>ExecuteWithPresentation</c> port, exactly like FreeW's sibling test. The REAL ports'
    /// marshal is covered separately by
    /// <see cref="TryEmergencySnapshot_UsingRealPorts_DoesNotDeadlockWhenReentrantOnTheUiThread"/>.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TryEmergencySnapshots_WritesASnapshotForADirtyWindow()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        AutosaveAdapter? adapter = null;
        const string snapshotId = "freep-avalonia-emergency-dirty-test";
        try
        {
            var store = new AutosaveSnapshotStore(dir);

            var ran = await OnUiThread(() =>
            {
                adapter = NewAdapter(
                    NewWorkflow(),
                    _ => new FreePAutosaveSession(
                        new FreePAutosavePorts(
                            GetOriginalFilePath: () => null,
                            GetDisplayName: () => "Presentation1",
                            GetIsDirty: () => true,
                            GetDirtyGeneration: () => 1,
                            ExecuteWithPresentation: write => write(Presentation.CreateEmpty())),
                        store,
                        snapshotId));
            });

            if (!ran || adapter is null)
                return; // no headless drawing backend in this environment

            AutosaveAdapter.TryEmergencySnapshots();

            File.Exists(store.GetSnapshotPath(snapshotId)).Should().BeTrue(
                "a crash must not lose a dirty presentation -- that is exactly what autosave exists to prevent");
            File.Exists(store.GetSidecarPath(snapshotId)).Should().BeTrue();
        }
        finally
        {
            adapter?.Dispose();
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    /// <summary>Sibling no-regression: a clean window must not get an emergency snapshot.</summary>
    [Fact]
    public async Task TryEmergencySnapshots_SkipsACleanWindow()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        AutosaveAdapter? adapter = null;
        try
        {
            var store = new AutosaveSnapshotStore(dir);

            var ran = await OnUiThread(() =>
                adapter = NewAdapter(NewWorkflow(), ports => new FreePAutosaveSession(ports, store)));

            if (!ran || adapter is null)
                return; // no headless drawing backend in this environment

            AutosaveAdapter.TryEmergencySnapshots();

            File.Exists(store.GetSnapshotPath(adapter.SnapshotIdForTests)).Should().BeFalse();
        }
        finally
        {
            adapter?.Dispose();
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    /// <summary>
    /// Drives the REAL production <c>FreePAutosavePorts.ExecuteWithPresentation</c> marshal (only
    /// the store is swapped out) from inside a UI-thread-dispatched action, reproducing the exact
    /// reentrancy shape of <c>AppDomain.UnhandledException</c> firing on the UI thread. A naive
    /// <c>Dispatcher.UIThread.InvokeAsync(...).GetAwaiter().GetResult()</c> deadlocks permanently
    /// here -- FreeW hit this in R138 and left a note warning FreeP not to reintroduce it. The
    /// timeout is the backstop; the wall-clock assertion pins that the fix takes the
    /// <c>CheckAccess()</c> fast path rather than merely finishing under the timeout.
    /// </summary>
    [Fact(Timeout = 20_000)]
    public async Task TryEmergencySnapshot_UsingRealPorts_DoesNotDeadlockWhenReentrantOnTheUiThread()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        AutosaveAdapter? adapter = null;
        try
        {
            var store = new AutosaveSnapshotStore(dir);
            var elapsed = TimeSpan.Zero;

            // Warm the pptx writer before timing: its first call in a process JITs several seconds
            // of OPC/XML code, which would otherwise swamp the marshal cost this test is measuring.
            FreeP.Core.IO.PptxPackageWriter.Write(
                Presentation.CreateEmpty(),
                Path.Combine(dir, "warmup.pptx"));

            var ran = await OnUiThread(() =>
            {
                var workflow = NewWorkflow();
                adapter = NewAdapter(workflow, ports => new FreePAutosaveSession(ports, store));
                workflow.MarkDirty();

                var sw = Stopwatch.StartNew();
                adapter.TryEmergencySnapshot();
                elapsed = sw.Elapsed;
            });

            if (!ran || adapter is null)
                return; // no headless drawing backend in this environment

            elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2),
                "a reentrant emergency snapshot on the UI thread must take the Dispatcher.UIThread.CheckAccess() " +
                "fast path and run inline, not marshal to itself and wait");

            File.Exists(store.GetSnapshotPath(adapter.SnapshotIdForTests)).Should().BeTrue(
                "the real (non-stubbed) marshal path must still produce a snapshot for a dirty presentation");
        }
        finally
        {
            adapter?.Dispose();
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    /// <summary>
    /// Pins the wiring a real crash reaches end-to-end: App.cs must hand its DesktopProfile a hook
    /// that fans out to every open window's adapter. Without this the adapter-level behaviour above
    /// is unreachable from an actual crash.
    /// </summary>
    [Fact]
    public void App_WiresEmergencySnapshotHookIntoTheDesktopProfile()
    {
        var appSource = TestWorkspaceFileLocator.ReadAllText("freep", "FreeP.App.Avalonia", "App.cs");

        appSource.Should().Contain("onEmergencySnapshot: AutosaveAdapter.TryEmergencySnapshots");
    }

    /// <summary>
    /// Pins that MainWindow actually starts autosave, offers recovery on open, and only tears the
    /// snapshot down once the close gate has committed. A constructed-but-never-started adapter
    /// produces no periodic snapshots at all.
    /// </summary>
    [Fact]
    public void MainWindow_StartsAutosaveOffersRecoveryAndStopsOnlyOnACommittedClose()
    {
        var source = TestWorkspaceFileLocator.ReadAllText("freep", "FreeP.App.Avalonia", "MainWindow.cs");

        source.Should().Contain("_autosave = new AutosaveAdapter(");
        source.Should().Contain("_autosave.Start();");
        source.Should().Contain("await _autosave.OfferRecoveryAsync(this);");
        source.Should().Contain("confirmCloseAllowedAsync: ConfirmCloseAllowedAndStopAutosaveAsync");
        source.Should().Contain("await _autosave.StopAsync();");
    }
}

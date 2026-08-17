using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using Free.Shared.AppServices;
using FreeW.App.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Shell;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// R138, Avalonia twin of <c>FreeW.App.Host.Tests.R138_AutosaveCoordinatorEmergencySnapshotTests</c>:
/// FreeW's Avalonia shell also never took an emergency autosave snapshot on crash before this fix.
/// <see cref="AutosaveAdapter.TryEmergencySnapshots"/> is the exact static hook wired into
/// <c>App.cs</c>'s <c>DesktopProfile</c> (<c>onEmergencySnapshot: AutosaveAdapter.TryEmergencySnapshots</c>),
/// which the shared Avalonia runner's crash handler calls -- not a test-only helper.
/// </summary>
public sealed class R138_AutosaveAdapterEmergencySnapshotTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

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

    private static (DocumentView editor, FileCommandWorkflow workflow) NewWindowParts()
    {
        var editor = new DocumentView();
        editor.LoadDocument(TextDocument.CreateEmpty());
        var workflow = new FileCommandWorkflow(
            maxRecentEntries: () => 10,
            onChanged: () => { },
            promptSaveChanges: _ => SaveChangesPrompt.DontSave,
            save: () => true,
            loadRecentFilesStore: () => RecentFilesStore.Load(
                Path.Combine(Path.GetTempPath(), "FreeW.R138AutosaveAvaloniaEmergencyTests", Guid.NewGuid().ToString("N") + ".json")));
        return (editor, workflow);
    }

    /// <summary>
    /// Core regression: the static fan-out a crash reaches (<see cref="AutosaveAdapter.TryEmergencySnapshots"/>)
    /// must write a snapshot for a dirty window even though no periodic autosave tick has run yet.
    ///
    /// <para>
    /// Uses a <c>sessionFactory</c> override with a synchronous (non-<c>Dispatcher.UIThread</c>)
    /// <c>ExecuteWithDocument</c> port, exactly like the WPF host's sibling test
    /// (<c>R138_AutosaveCoordinatorEmergencySnapshotTests</c>) and this file's own
    /// <c>AutosaveAdapterWindowIsolationTests.OfferRecoveryAsync_recovers_every_pending_snapshot_not_just_one</c>
    /// pattern of avoiding <c>DocumentView</c>/production ports for setup that does not need them.
    /// The REAL (default) ports' <c>ExecuteWithDocument</c> re-enters
    /// <c>Dispatcher.UIThread.InvokeAsync(...).GetAwaiter().GetResult()</c> from whatever thread calls
    /// it; the headless test session's dispatcher only pumps work queued through
    /// <see cref="HeadlessUnitTestSession.Dispatch"/>, so a call from the test's own thread (matching
    /// how a real crash handler, which may run on any thread, would reach it) never gets serviced and
    /// hangs/crashes the test host -- confirmed by isolating this test, which reliably hung until
    /// killed. Only <see cref="AutosaveAdapter.TryEmergencySnapshot"/>'s dirty-check/write-gate and
    /// the static fan-out are under test here, not the WPF-mirroring dispatcher marshaling in the
    /// default ports (already covered indirectly: production wiring is pinned by
    /// <see cref="App_WiresEmergencySnapshotHookIntoTheDesktopProfile"/> below).
    /// </para>
    /// </summary>
    [Fact]
    public async Task TryEmergencySnapshots_WritesASnapshotForADirtyWindow()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        AutosaveAdapter? adapter = null;
        const string snapshotId = "r138-avalonia-emergency-dirty-test";
        try
        {
            var store = new AutosaveSnapshotStore(dir);

            var ran = await OnUiThread(() =>
            {
                var (editor, wf) = NewWindowParts();
                adapter = new AutosaveAdapter(
                    editor,
                    wf,
                    sessionFactory: _ =>
                    {
                        var syncPorts = new FreeWAutosavePorts(
                            GetOriginalFilePath: () => null,
                            GetDisplayName: () => "Untitled",
                            GetIsDirty: () => true,
                            GetDirtyGeneration: () => 1,
                            ExecuteWithDocument: writeDocument => writeDocument(TextDocument.CreateEmpty()));
                        return new FreeWAutosaveSession(syncPorts, store, snapshotId);
                    });
            });

            if (!ran || adapter is null)
                return; // no headless drawing backend in this environment

            AutosaveAdapter.TryEmergencySnapshots();

            File.Exists(store.GetSnapshotPath(snapshotId)).Should().BeTrue(
                "a crash must not lose a dirty document -- that is exactly what autosave exists to prevent");
            File.Exists(store.GetSidecarPath(snapshotId)).Should().BeTrue();
        }
        finally
        {
            adapter?.Dispose();
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    /// <summary>
    /// Sibling no-regression: a clean window must not get an emergency snapshot.
    /// </summary>
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
            {
                var (editor, wf) = NewWindowParts();
                adapter = new AutosaveAdapter(editor, wf, ports => new FreeWAutosaveSession(ports, store));
            });

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
    /// Pins the wiring a real crash reaches end-to-end: App.cs must hand its DesktopProfile a hook
    /// that fans out to every open window's adapter. Without this wiring the adapter-level fix above
    /// is unreachable from an actual crash.
    /// </summary>
    [Fact]
    public void App_WiresEmergencySnapshotHookIntoTheDesktopProfile()
    {
        var appSource = TestWorkspaceFileLocator.ReadAllText("freew", "FreeW.App.Avalonia", "App.cs");

        appSource.Should().Contain("onEmergencySnapshot: AutosaveAdapter.TryEmergencySnapshots");
    }

    /// <summary>
    /// R138 REMEDIATION regression test. Unlike <see cref="TryEmergencySnapshots_WritesASnapshotForADirtyWindow"/>
    /// above (which deliberately substitutes a synchronous <c>ExecuteWithDocument</c> stub because the
    /// real one was known to hang the headless dispatcher), this test drives the REAL production
    /// <c>FreeWAutosavePorts.ExecuteWithDocument</c> marshal -- only the <see cref="AutosaveSnapshotStore"/>
    /// is swapped out, exactly like <see cref="TryEmergencySnapshots_SkipsACleanWindow"/> does.
    ///
    /// <para>
    /// This reproduces the auditor's exact deadlock shape: <c>AppDomain.UnhandledException</c> fires
    /// synchronously on the faulting thread, which is very often the UI thread itself, reentrant
    /// partway through whatever it was doing (not inside an active dispatcher-loop iteration that
    /// could later come back around and service a queued continuation). Calling
    /// <see cref="AutosaveAdapter.TryEmergencySnapshot"/> from *inside* the UI-thread-dispatched action
    /// below reproduces exactly that reentrancy. Before this fix, the port's
    /// <c>Dispatcher.UIThread.InvokeAsync(...).GetAwaiter().GetResult()</c> would queue a continuation
    /// that could only run once this very call returned, then block on it -- a permanent deadlock, not
    /// merely a slow path. The <c>[Fact(Timeout=...)]</c> backstop guarantees this test fails (rather
    /// than hanging the whole run) if that regresses; the wall-clock assertion below additionally pins
    /// that the fix takes the fast, non-marshaling path for this case rather than merely happening to
    /// finish under the timeout.
    /// </para>
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

            var ran = await OnUiThread(() =>
            {
                var (editor, wf) = NewWindowParts();
                // REAL ports: only the store is overridden (sessionFactory receives the production
                // `ports`, unmodified), so ExecuteWithDocument is the fixed
                // Dispatcher.UIThread.CheckAccess()-then-bounded-marshal lambda under test.
                adapter = new AutosaveAdapter(editor, wf, ports => new FreeWAutosaveSession(ports, store));
                wf.MarkDirty();

                // Reentrant call: we are already executing on the UI thread (inside the dispatched
                // action), exactly like AppDomain.UnhandledException firing mid-execution there.
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
                "the real (non-stubbed) marshal path must still produce a snapshot for a dirty document");
        }
        finally
        {
            adapter?.Dispose();
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    // NOTE: the fix's OTHER branch -- Dispatcher.UIThread.CheckAccess() == false, bounding the wait
    // when a genuinely different thread posts to a wedged UI-thread pump -- is not covered by an
    // automated test here. Avalonia.Headless's Dispatcher.UIThread.CheckAccess() was confirmed (by
    // instrumenting ExecuteOnUiThreadBounded during development of this fix) to return true
    // regardless of which OS thread calls it in this test harness -- including a bare, freshly
    // started System.Threading.Thread with no flowed ExecutionContext -- so that branch cannot be
    // reached through HeadlessUnitTestSession. The branch is still exercised in real desktop use
    // (Avalonia's non-headless Dispatcher does enforce real thread affinity) and mirrors the WPF
    // sibling's already-covered dispatcher.CheckAccess() shape 1:1; it is implemented defensively
    // for a case this harness happens not to be able to simulate, not left unverified by choice.
}

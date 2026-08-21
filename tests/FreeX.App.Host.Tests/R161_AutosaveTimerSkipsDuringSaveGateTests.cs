using System.IO;
using System.Reflection;
using System.Windows;
using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for the shared-autosave-timing F1 finding: the periodic autosave
/// <see cref="System.Windows.Threading.DispatcherTimer"/> (MainWindow.Autosave.cs) was never
/// gated against an in-progress explicit save. <c>SaveWorkbookToTargetAsync</c>
/// (MainWindow.Backstage.cs) serializes the LIVE <see cref="Workbook"/> on a background thread
/// while holding the save-input gate (<c>AdjustSaveGate</c> / <c>_saveGateHoldCount</c>) both in
/// the saving window and, via <c>WorkbookWindowRegistry.BroadcastSaveInProgress</c>, in every
/// "New Window" sibling over the same document. Before the fix, the autosave timer's Tick handler
/// called <c>AutosaveService.OnTimerTick()</c> unconditionally, which reconciles THIS window's own
/// view-state onto the shared <see cref="Sheet"/> objects
/// (<c>IAutosaveWorkbookSource.ReconcileViewStateForSnapshot</c>) and then serializes the whole
/// live <see cref="Workbook"/> -- both unsynchronized with the concurrent background save thread.
///
/// Exercises the real Tick handler body via the private <c>OnAutosaveTimerTick</c> method (the
/// same reflection seam <see cref="R115_SaveGateSiblingWindowRaceTests"/> uses for the save entry
/// point), so the fix is verified through the exact code path the real <c>DispatcherTimer.Tick</c>
/// invokes, not just through <c>AutosaveService.OnTimerTick()</c> directly (which several other
/// tests call unconditionally and must keep doing so unaffected -- see
/// <see cref="MultiWindowAutosaveOwnershipTests"/> and <c>R16_autosave_Tests</c>).
/// </summary>
public sealed class R161_AutosaveTimerSkipsDuringSaveGateTests
{
    private static MainWindow CreateWindow(
        WorkbookRef workbookRef,
        WorkbookWindowRegistry registry,
        WorkbookDocumentState documentState,
        ICommandBus commandBus,
        RecalcEngine recalcEngine,
        WorkbookSession? workbookSession = null)
    {
        var window = new MainWindow(
            NullLogger<MainWindow>.Instance,
            new ViewportService(),
            commandBus,
            recalcEngine,
            [],
            workbookRef,
            workbookRef.Current,
            NullUserMessageService.Instance,
            documentState,
            windowRegistry: registry,
            workbookSession: workbookSession)
        {
            WindowState = WindowState.Normal,
            Width = 1280,
            Height = 720
        };

        return window;
    }

    private static void PumpDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    private static int SnapshotFileCount(string recoveryDirectory) =>
        Directory.Exists(recoveryDirectory)
            ? Directory.GetFiles(recoveryDirectory, "*.fxl").Length
            : 0;

    /// <summary>
    /// Invokes the private Tick-handler body directly -- the same seam the real
    /// <c>DispatcherTimer.Tick</c> event invokes -- so the test exercises production code, not a
    /// re-implementation of it.
    /// </summary>
    private static void InvokeAutosaveTimerTick(MainWindow window)
    {
        var method = typeof(MainWindow).GetMethod(
            "OnAutosaveTimerTick",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull("OnAutosaveTimerTick is the Tick handler this finding concerns");
        method!.Invoke(window, null);
    }

    /// <summary>
    /// The primary regression scenario. Two windows share one document (Excel "New Window"): the
    /// primary is mid-save (its save-input gate is engaged, exactly as
    /// <c>SaveWorkbookToTargetAsync</c> leaves it for the duration of the background serialize).
    /// While that gate is held, ticking the SIBLING's autosave timer -- the exact race the finding
    /// describes, where the sibling's own independent timer can fire mid-save -- must NOT write a
    /// snapshot. Ticking the SAVING window's own timer must not either.
    /// </summary>
    [Fact]
    public void AutosaveTick_DuringSaveGate_DoesNotWriteASnapshot_ForSavingWindowOrSibling()
    {
        using var temp = new TestTemporaryDirectory("FreeX.R161.Autosave-");
        StaTestRunner.Run(() =>
        {
            var store = new AutosaveSnapshotStore(temp.Path);

            var workbook = new Workbook("Book1");
            workbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = workbook };
            var registry = new WorkbookWindowRegistry();
            var documentState = new WorkbookDocumentState();
            var commandBus = new CommandBus(_ => new TestCommandContext(workbookRef.Current));
            var recalcEngine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());

            var primary = CreateWindow(workbookRef, registry, documentState, commandBus, recalcEngine);
            primary.AttachAutosaveService(new AutosaveService(store), store);
            primary.Show();
            primary.Activate();
            PumpDispatcher();

            var secondary = CreateWindow(
                workbookRef,
                registry,
                documentState,
                commandBus,
                recalcEngine,
                primary.Session.CreateSiblingView(1, 1));
            secondary.AttachAutosaveService(new AutosaveService(store), store);
            secondary.Show();
            secondary.Activate();
            PumpDispatcher();

            try
            {
                registry.Count.Should().Be(2);
                primary.Session.MarkDirtyForRecovery();

                // Simulate the primary window being mid-explicit-save: SaveWorkbookToTargetAsync
                // acquires its own hold via AdjustSaveGate(true) and broadcasts the same into every
                // sibling over the same document via ApplySaveInProgress -- exactly reproduced here
                // without needing a real background Task.Run to observe the race deterministically.
                primary.ApplySaveInProgress(true);
                secondary.ApplySaveInProgress(true);

                primary.ShouldSkipAutosaveTickForSave.Should().BeTrue(
                    "the saving window's own save-input gate is engaged");
                secondary.ShouldSkipAutosaveTickForSave.Should().BeTrue(
                    "the broadcast reaches every sibling over the same document");

                InvokeAutosaveTimerTick(primary);
                InvokeAutosaveTimerTick(secondary);

                SnapshotFileCount(temp.Path).Should().Be(0,
                    "an autosave tick landing while a save is in flight -- in the saving window or a " +
                    "sibling -- must be skipped, not serialize/mutate the same live Workbook the " +
                    "background save thread is concurrently reading");

                // Once the save completes, the gate releases and autosave resumes normally.
                primary.ApplySaveInProgress(false);
                secondary.ApplySaveInProgress(false);

                InvokeAutosaveTimerTick(primary);
                SnapshotFileCount(temp.Path).Should().Be(1,
                    "once the save-input gate releases, the autosave timer must resume writing " +
                    "snapshots as normal -- ticks are only deferred, never permanently dropped");
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(secondary);
                MainWindowTestCleanup.CloseWithoutSavePrompt(primary);
                PumpDispatcher();
            }
        });
    }

    /// <summary>
    /// No-regression sibling: with no save in progress at all (the ordinary case), an autosave
    /// tick must still write a snapshot exactly as before -- the fix must only suppress the tick
    /// while <c>_saveGateHoldCount</c> is actually held, never unconditionally.
    /// </summary>
    [Fact]
    public void AutosaveTick_WithNoSaveInProgress_StillWritesASnapshot()
    {
        using var temp = new TestTemporaryDirectory("FreeX.R161.Autosave-");
        StaTestRunner.Run(() =>
        {
            var store = new AutosaveSnapshotStore(temp.Path);

            var workbook = new Workbook("Book1");
            workbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = workbook };
            var registry = new WorkbookWindowRegistry();
            var documentState = new WorkbookDocumentState();
            var commandBus = new CommandBus(_ => new TestCommandContext(workbookRef.Current));
            var recalcEngine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());

            var primary = CreateWindow(workbookRef, registry, documentState, commandBus, recalcEngine);
            primary.AttachAutosaveService(new AutosaveService(store), store);
            primary.Show();
            primary.Activate();
            PumpDispatcher();

            try
            {
                primary.Session.MarkDirtyForRecovery();
                primary.ShouldSkipAutosaveTickForSave.Should().BeFalse("no save is in progress");

                InvokeAutosaveTimerTick(primary);

                SnapshotFileCount(temp.Path).Should().Be(1,
                    "the ordinary, non-racing autosave path must be unaffected by the fix");
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(primary);
                PumpDispatcher();
            }
        });
    }
}

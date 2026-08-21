using System.Windows;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// freex-workbook-views F1 (R160): Undo/Redo of a grouped view-setting change (Zoom, Gridlines,
/// Headings, Rulers, View Mode, Show Formulas) is pushed onto the shared undo stack as ONE
/// <see cref="CompositeWorkbookCommand"/> spanning every grouped sheet (see
/// <c>MainWindow.CommandExecution.cs</c>'s <c>CurrentGroupedEditSheetIds</c>/
/// <c>TryExecuteGroupedSheetCommand</c> and <c>MainWindow.ViewCommands.cs</c>'s
/// <c>TryExecuteGroupedWorksheetViewState</c>). <c>ApplyWorkbookSessionHistoryResult</c> -- the one
/// completion routine for both <c>ExecuteUndo</c> and <c>ExecuteRedo</c> -- used to unconditionally
/// call <c>SyncWindowViewState([_currentSheetId])</c>, refreshing the WPF host's per-window
/// <see cref="WorksheetViewStateStore"/> cache for only the currently active sheet no matter how
/// many sheets the undone/redone command actually targeted. A grouped sheet that wasn't active when
/// Undo/Redo ran therefore kept returning its stale pre-undo/redo Zoom/Gridlines/etc. snapshot the
/// next time the user switched to its tab, and that stale snapshot was what
/// <c>ReconcileViewStateForSave</c> then wrote into the saved file. The fix mirrors the Avalonia
/// shell's <c>WorkbookSession.ApplySuccessfulHistoryResult</c>, which loops every
/// <c>Workbook.Sheets</c> entry through <c>InvalidateAllPerViewOverridesForSheet</c> whenever the
/// undone/redone command reports no affected cells (exactly the shape of these metadata-only
/// view-setting commands) -- here, by syncing every sheet's window cache instead of only the active
/// one in that same case.
/// </summary>
public sealed class R160_UndoRedoGroupedViewStateWindowCacheTests
{
    [Fact]
    public void UndoOfGroupedZoomChange_RefreshesNonActiveGroupedSheetsWindowCacheToo()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = new MainWindowHarness();
            var workbook = harness.Workbook;
            var sheet1 = workbook.GetSheetAt(0);
            var sheet2 = workbook.AddSheet("Sheet2");
            IReadOnlyList<SheetId> targetSheetIds = [sheet1.Id, sheet2.Id];

            // Both sheets start at the workbook default (Zoom 100%), and this window's cache is
            // seeded to match by reading each sheet at least once.
            harness.GetEffectiveViewState(sheet1).ZoomPercent.Should().Be(100);
            harness.GetEffectiveViewState(sheet2).ZoomPercent.Should().Be(100);

            harness.Window.SetGroupedSheetIdsForTest(targetSheetIds);
            harness.Window.SelectSingleSheetTabForTest(sheet1.Id);
            harness.Window.UpdateViewportForTest();

            // Group Sheets + change Zoom to 200%: the real forward path fans this out as ONE
            // CompositeWorkbookCommand across every grouped sheet (mirrors
            // TryExecuteGroupedWorksheetViewState -> CompleteWorksheetSessionCommand) and syncs
            // this window's cache for every targeted sheet, exactly like R88/R126's harnesses seed
            // their own diverged per-window state via the same real command + SyncWindowViewState
            // pairing.
            harness.ExecuteCommand(new CompositeWorkbookCommand(
                "Zoom",
                [new SetWorksheetZoomCommand(sheet1.Id, 200), new SetWorksheetZoomCommand(sheet2.Id, 200)]));
            harness.Window.SyncWindowViewStateForTest(targetSheetIds);

            harness.Sheet1ZoomPercent.Should().Be(200);
            harness.Sheet2ZoomPercent.Should().Be(200);
            harness.GetEffectiveViewState(sheet1).ZoomPercent.Should().Be(200);
            harness.GetEffectiveViewState(sheet2).ZoomPercent.Should().Be(200, "sanity: both grouped sheets' window cache must reflect the just-applied zoom");

            // Sheet1 (the currently active sheet) is the one Ctrl+Z runs against.
            harness.Window.ExecuteUndoForTest().Should().BeTrue();

            // The shared Sheet fields are correctly reverted for BOTH sheets either way (this part
            // already worked -- Undo replays the composite's Revert for every child command).
            harness.Sheet1ZoomPercent.Should().Be(100);
            harness.Sheet2ZoomPercent.Should().Be(100);

            // The window's own per-window cache must reflect the reverted value for BOTH sheets --
            // including Sheet2, which was not the active sheet when Undo ran. Before the fix,
            // Sheet2's cache stayed at the stale pre-undo 200%.
            harness.GetEffectiveViewState(sheet1).ZoomPercent.Should().Be(100);
            harness.GetEffectiveViewState(sheet2).ZoomPercent.Should().Be(
                100,
                "the non-active grouped sheet's window cache must be refreshed by Undo too, not just the active sheet's");
        });
    }

    /// <summary>
    /// No-regression sibling: Undo of an ordinary (non-grouped, non-view-setting) cell edit --
    /// which DOES report affected cells -- must keep refreshing only the active sheet's window
    /// cache, exactly as before this fix. This proves the fix is scoped to the metadata-only
    /// (zero-affected-cells) branch and does not needlessly touch every sheet's cache on a plain
    /// cell-edit Undo.
    /// </summary>
    [Fact]
    public void UndoOfPlainCellEdit_StillOnlyTouchesActiveSheetsWindowCache()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = new MainWindowHarness();
            var workbook = harness.Workbook;
            var sheet1 = workbook.GetSheetAt(0);
            var sheet2 = workbook.AddSheet("Sheet2");

            // Sheet2's window cache diverges from the shared model's default before Undo runs, via
            // the same real command + SyncWindowViewState pairing R88/R126 use to set up their own
            // "already diverged" cases.
            harness.ExecuteCommand(new SetWorksheetZoomCommand(sheet2.Id, 175));
            harness.Window.SyncWindowViewStateForTest([sheet2.Id]);
            harness.GetEffectiveViewState(sheet2).ZoomPercent.Should().Be(175);

            // Now perform and undo a plain cell edit on Sheet1 (the active sheet) -- this reports a
            // non-empty AffectedCells list, unlike the view-setting commands above.
            harness.Window.SelectSingleSheetTabForTest(sheet1.Id);
            harness.Window.UpdateViewportForTest();
            var address = new CellAddress(sheet1.Id, 1, 1);
            var editResult = harness.ExecuteCommand(EditCellsCommand.ForFormula(sheet1.Id, address, "42"));
            editResult.AffectedCells.Should().NotBeEmpty("a plain cell edit reports the cells it touched");

            harness.Window.ExecuteUndoForTest().Should().BeTrue();

            // Sheet2's window cache -- untouched by this Undo -- must still read its own diverged
            // 175%, not be silently reset to the workbook default by an over-broad fix.
            harness.GetEffectiveViewState(sheet2).ZoomPercent.Should().Be(
                175,
                "a plain cell-edit Undo must not disturb an unrelated sheet's own window view-state cache");
        });
    }

    private sealed class MainWindowHarness : IDisposable
    {
        public MainWindow Window { get; }
        public Workbook Workbook { get; }

        public MainWindowHarness()
        {
            var initialWorkbook = new Workbook("Book1");
            initialWorkbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = initialWorkbook };
            Window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
                [],
                workbookRef,
                initialWorkbook,
                NullUserMessageService.Instance);

            Window.Show();
            PumpDispatcher();

            Workbook = workbookRef.Current;
        }

        public WorkbookCellEditResult ExecuteCommand(IWorkbookCommand command)
        {
            var outcome = Window.Session.ExecuteCommandPreservingSelection(command);
            PumpDispatcher();
            return outcome;
        }

        public WorksheetViewStateSnapshot GetEffectiveViewState(Sheet sheet) =>
            Window.GetEffectiveViewStateForTest(sheet);

        public int Sheet1ZoomPercent => Workbook.GetSheetAt(0).ZoomPercent;
        public int Sheet2ZoomPercent => Workbook.Sheets[1].ZoomPercent;

        public void Dispose()
        {
            foreach (Window ownedWindow in Window.OwnedWindows.Cast<Window>().ToList())
                ownedWindow.Close();
            MainWindowTestCleanup.CloseWithoutSavePrompt(Window);
            PumpDispatcher();
        }
    }

    private static void PumpDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
}

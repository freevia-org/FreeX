using System.Windows;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R88-window-seed-order-guard-sweep-2: every direct View-tab toggle/zoom/view-mode command in the
/// WPF host (MainWindow.ViewCommands.cs) calls <c>SyncWindowViewState(targetSheetIds)</c> right
/// after applying, re-seeding this window's per-window <c>_worksheetViewStates</c> cache
/// (<see cref="WorksheetViewStateStore"/>) from the freshly-written <see cref="Sheet"/> fields.
/// <c>CustomViewsBtn_Click</c> -&gt; <c>ApplyCustomViewWorkbookViewState</c> instead ran the Custom
/// View "Show" command through the command bus and only repositioned the active
/// cell/scrollbars/tabs, never invalidating the per-window view-state cache -- so a window whose
/// own Zoom/Gridlines already diverged from the shared <see cref="Sheet"/> kept showing its stale
/// cached values instead of what the Custom View just restored, until the workbook was closed and
/// reopened. Fixed by having <c>ApplyCustomViewWorkbookViewState</c> clear this window's entire
/// <c>_worksheetViewStates</c> cache before repositioning, so <c>GetEffectiveViewState</c> reseeds
/// fresh from the just-applied <see cref="Sheet"/> fields.
/// </summary>
public sealed class R88_CustomViewWindowStateInvalidationTests
{
    [Fact]
    public void ApplyCustomView_RefreshesWindowViewStateInsteadOfKeepingStaleCachedZoomAndGridlines()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = CustomViewWindowStateHarness.Create();

            // Seed this window's cache at the workbook defaults (Zoom 100%, gridlines on) -- the
            // same "first render" seeding GetOrSeed performs the first time GetEffectiveViewState
            // reads a sheet it hasn't cached yet.
            harness.GetEffectiveViewState().ZoomPercent.Should().Be(100);
            harness.GetEffectiveViewState().ShowGridlines.Should().BeTrue();

            // Save a Custom View at these defaults.
            harness.ExecuteCommand(new SaveCustomViewCommand("A")).Success.Should().BeTrue();

            // This window diverges from the saved view (Zoom 150%, Gridlines off) via the SAME
            // real commands + SyncWindowViewState call every View-tab handler performs, so this
            // window's cache now holds 150/off -- exactly the failure scenario's setup.
            harness.ExecuteCommand(new SetWorksheetZoomCommand(harness.SheetId, 150)).Success.Should().BeTrue();
            harness.ExecuteCommand(new SetWorksheetViewOptionsCommand(harness.SheetId, false, true, true))
                .Success.Should().BeTrue();
            harness.SyncWindowViewState();

            harness.GetEffectiveViewState().ZoomPercent.Should().Be(150);
            harness.GetEffectiveViewState().ShowGridlines.Should().BeFalse();

            // Apply the saved Custom View back through the real command bus, then run the exact
            // private method CustomViewsBtn_Click calls after a successful Show.
            harness.ExecuteCommand(new ApplyCustomViewCommand("A")).Success.Should().BeTrue();
            harness.ApplyCustomViewWorkbookViewState();

            // The shared Sheet fields are correctly restored either way (this part already worked).
            harness.Sheet.ZoomPercent.Should().Be(100);
            harness.Sheet.ShowGridlines.Should().BeTrue();

            // The window's own per-window view state must reflect the just-applied values
            // immediately, not the stale pre-Apply cache.
            harness.GetEffectiveViewState().ZoomPercent.Should().Be(100);
            harness.GetEffectiveViewState().ShowGridlines.Should().BeTrue();
        });
    }

    [Fact]
    public void ApplyCustomView_WindowThatNeverDivergedStillReadsCorrectValuesAfterApply()
    {
        // No-regression sibling: a window whose own cache already agreed with the shared Sheet
        // (never diverged) must still read the correct values after Apply -- the fix (clearing the
        // whole per-window cache) must not corrupt or lose an already-correct entry.
        StaTestRunner.Run(() =>
        {
            using var harness = CustomViewWindowStateHarness.Create();

            harness.GetEffectiveViewState().ZoomPercent.Should().Be(100);
            harness.GetEffectiveViewState().ShowGridlines.Should().BeTrue();

            harness.ExecuteCommand(new SaveCustomViewCommand("A")).Success.Should().BeTrue();

            // No divergence this time -- apply the same view straight back.
            harness.ExecuteCommand(new ApplyCustomViewCommand("A")).Success.Should().BeTrue();
            harness.ApplyCustomViewWorkbookViewState();

            harness.Sheet.ZoomPercent.Should().Be(100);
            harness.Sheet.ShowGridlines.Should().BeTrue();
            harness.GetEffectiveViewState().ZoomPercent.Should().Be(100);
            harness.GetEffectiveViewState().ShowGridlines.Should().BeTrue();
        });
    }

    private sealed class CustomViewWindowStateHarness : IDisposable
    {
        private readonly MainWindow _window;

        private CustomViewWindowStateHarness(MainWindow window)
        {
            _window = window;
        }

        // MainWindow_Loaded unconditionally calls CreateNewWorkbook() (unless adopting a shared
        // document via a WorkbookWindowRegistry, which this harness doesn't provide), replacing
        // whatever workbook was passed into the constructor -- so the live workbook/sheet must be
        // read fresh from the session AFTER Show()/Loaded has run (mirrors
        // R31_ViewportSelectionLogicTests.ViewportSelectionHarness).
        private Workbook LiveWorkbook => _window.Session.Workbook;

        public Sheet Sheet => LiveWorkbook.Sheets[0];
        public SheetId SheetId => Sheet.Id;

        public WorkbookCellEditResult ExecuteCommand(IWorkbookCommand command)
        {
            var outcome = command is SaveCustomViewCommand or ApplyCustomViewCommand or DeleteCustomViewCommand
                ? _window.Session.ExecuteCustomViewCommand(command)
                : _window.Session.ExecuteCommandPreservingSelection(command);
            PumpDispatcher();
            return outcome;
        }

        public void SyncWindowViewState()
        {
            IReadOnlyList<SheetId> targetSheetIds = [SheetId];
            _window.SyncWindowViewStateForTest(targetSheetIds);
        }

        public WorksheetViewStateSnapshot GetEffectiveViewState() =>
            _window.GetEffectiveViewStateForTest(Sheet);

        public void ApplyCustomViewWorkbookViewState()
        {
            _window.ApplyCustomViewWorkbookViewStateForTest();
            PumpDispatcher();
        }

        public static CustomViewWindowStateHarness Create()
        {
            var workbook = new Workbook("Book1");
            workbook.AddSheet("Sheet1");

            var workbookRef = new WorkbookRef { Current = workbook };
            var graph = new DependencyGraph();
            var evaluator = new FormulaEvaluator();
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(graph, evaluator),
                Array.Empty<FreeX.Core.IO.IFileAdapter>(),
                workbookRef,
                workbook,
                NullUserMessageService.Instance)
            {
                WindowState = WindowState.Normal,
                Width = 1280,
                Height = 720
            };

            window.Show();
            window.Activate();
            window.UpdateLayout();
            PumpDispatcher();

            return new CustomViewWindowStateHarness(window);
        }

        public void Dispose()
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(_window);
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

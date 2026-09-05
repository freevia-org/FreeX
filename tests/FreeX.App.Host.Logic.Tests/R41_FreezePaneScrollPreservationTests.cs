using System.Reflection;
using FluentAssertions;
using Free.Shared.AppServices;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

// Regression coverage for R41-render-frozen-pane-scroll-3-1
// (src/FreeX.App.Host/MainWindow.ViewCommands.cs, SetFreezePanes).
//
// Before the fix: freezing or unfreezing panes while scrolled away from row 1/col A
// reinterpreted the (unchanged) scrollbar Value under the NEW FrozenRows/FrozenCols count
// (WorkbookViewportScrollPlanner.ScrollbarValueToWorksheetIndex: origin = frozenCount + scrollValue),
// jumping the viewport to the wrong absolute row instead of preserving what the user was
// looking at (Excel's actual behavior - freezing/unfreezing never relocates the view).
//
// After the fix, SetFreezePanes captures the absolute top-left row/col under the OLD frozen
// counts before executing the command, then re-derives a scrollbar Value for the NEW frozen
// counts that resolves back to that same absolute row/col.
public sealed class R41_FreezePaneScrollPreservationTests
{
    [Fact]
    public void SetFreezePanes_FreezingWhileScrolled_KeepsPreviouslyVisibleTopRowAsFirstScrollableRow()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = FreezePaneHarness.Create(dataRowCount: 200);

            // Scrolled so the first (unfrozen) visible row is row 50 (VerticalScroll.Value ==
            // worksheet row when FrozenRows == 0).
            harness.Window.VerticalScroll.Value = 50;

            // Freeze at B55 -> frozenRows = 54 (row 55 - 1), matching the finding's repro.
            harness.SetFreezePanes(frozenRows: 54, frozenCols: 0);

            harness.Sheet.FrozenRows.Should().Be(54u);

            // The pre-freeze visible top row (50) now falls inside the newly-frozen band
            // (rows 1-54), so the scrollable pane must start right after the frozen band (row
            // 55) - NOT jump further down the sheet to row 104 (54 + the stale scroll value 50),
            // which is what the pre-fix code produced.
            harness.Window.VerticalScroll.Value.Should().Be(1,
                "the stale scrollbar Value must be re-derived for the new frozen-row count, " +
                "not reinterpreted as-is under it");
            harness.Sheet.ViewTopRow.Should().Be(55u,
                "freezing must keep the view where the user was looking (row ~50-55), " +
                "not relocate it to row 104");
        });
    }

    [Fact]
    public void SetFreezePanes_UnfreezingWhileScrolled_PreservesAbsoluteScrollPosition()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = FreezePaneHarness.Create(dataRowCount: 200);

            // Freeze rows 1-54 first, from an unscrolled position.
            harness.SetFreezePanes(frozenRows: 54, frozenCols: 0);
            harness.Sheet.FrozenRows.Should().Be(54u);

            // Scroll the (now-frozen) view so the scrollable pane's first row is absolute row 64
            // (frozenRows(54) + VerticalScroll.Value(10)).
            harness.Window.VerticalScroll.Value = 10;

            // Unfreeze.
            harness.SetFreezePanes(frozenRows: 0, frozenCols: 0);

            harness.Sheet.FrozenRows.Should().Be(0u);

            // Absolute row 64 must still be the first visible row after unfreezing - not
            // reinterpreted as scrollbar value 10 under the new FrozenRows == 0 (which would jump
            // backward up the sheet to row 10, 54 rows earlier than where the user was looking).
            harness.Window.VerticalScroll.Value.Should().Be(64,
                "the stale scrollbar Value must be re-derived for the cleared frozen-row count");
            harness.Sheet.ViewTopRow.Should().Be(64u,
                "unfreezing must keep showing the same rows (~row 64), not jump back to row 10");
        });
    }

    [Fact]
    public void SetFreezePanes_FreezeAtTopRowWithNoScroll_StaysAtRowOne()
    {
        // Sibling no-regression case: freezing/unfreezing while already unscrolled (Value == 1,
        // absolute row == 1) must behave exactly as before the fix - no jump is introduced for
        // the freeze-at-A1 case.
        StaTestRunner.Run(() =>
        {
            using var harness = FreezePaneHarness.Create(dataRowCount: 200);

            harness.Window.VerticalScroll.Value.Should().Be(1);

            harness.SetFreezePanes(frozenRows: 1, frozenCols: 0);

            harness.Sheet.FrozenRows.Should().Be(1u);
            harness.Window.VerticalScroll.Value.Should().Be(1,
                "freezing from an unscrolled view must not introduce any scroll jump");
            harness.Sheet.ViewTopRow.Should().Be(2u,
                "row 1 is now frozen, so the scrollable pane correctly starts at row 2");
        });
    }

    private sealed class FreezePaneHarness : IDisposable
    {

        private FreezePaneHarness(MainWindow window, Workbook workbook)
        {
            Window = window;
            Workbook = workbook;
        }

        public MainWindow Window { get; }

        public Workbook Workbook { get; }

        public Sheet Sheet => Workbook.GetSheetAt(0);

        public void SetFreezePanes(uint frozenRows, uint frozenCols)
        {
            Window.SetFreezePanes(frozenRows, frozenCols);
            PumpDispatcher();
        }

        public static FreezePaneHarness Create(int dataRowCount)
        {
            var initialWorkbook = new Workbook("Book1");
            initialWorkbook.AddSheet("Sheet1");

            var workbookRef = new WorkbookRef { Current = initialWorkbook };
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
                [],
                workbookRef,
                initialWorkbook,
                new RecordingUserMessageService());

            window.Show();
            PumpDispatcher();

            // MainWindow_Loaded (MainWindow.Startup.cs) unconditionally replaces the workbook
            // passed to the constructor with a brand-new default one (CreateNewWorkbook) unless
            // adopting a shared document - so the workbook actually live in the window (and the
            // one SetFreezePanes's SetFreezePanesCommand will mutate) is whatever
            // workbookRef.Current now points to, not `initialWorkbook`.
            var workbook = workbookRef.Current;
            var sheet = workbook.GetSheetAt(0);
            for (var row = 1; row <= dataRowCount; row++)
                sheet.SetCell(new CellAddress(sheet.Id, (uint)row, 1), new NumberValue(row));

            return new FreezePaneHarness(window, workbook);
        }

        public void Dispose()
        {
            Window.SuppressNextClosePrompt();
            Window.Close();
            PumpDispatcher();
        }
    }

    // r446: delegates to the one fixed implementation -- see R49MainWindowTestHarness.
    private static void PumpDispatcher() => R49MainWindowTestHarness.PumpDispatcher();

    /// <summary>
    /// No-op <see cref="IUserMessageService"/> for tests that construct <see cref="MainWindow"/>
    /// directly and don't want real WPF MessageBox windows popping up.
    /// </summary>
    private sealed class RecordingUserMessageService : IUserMessageService
    {
        public void ShowError(string message, string title = "Error") { }
        public void ShowWarning(string message, string title = "Warning") { }
        public void ShowInfo(string message, string title = "Information") { }
        public bool AskYesNo(string message, string title = "Confirm") => false;
        public UserMessageResult ShowMessage(
            string message,
            string title,
            UserMessageButtons buttons,
            UserMessageIcon icon) => UserMessageResult.Ok;
    }
}

using System.Reflection;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

// freex-formula-edit-refs-F1: R160-formula-editing-F2 made TryExecuteWorksheetStructure
// (MainWindow.CommandExecution.cs) call TryCommitPendingSpellCheckEdit() before every
// Insert/Delete Rows/Columns/Cells command, specifically so a still-open Formula Bar edit commits
// to its pre-shift address before the shift runs. But TryCommitPendingSpellCheckEdit()
// deliberately returns false, WITHOUT committing anything, when a formula-reference point-mode
// entry is active (MainWindow.Editing.cs) -- and that return value used to be discarded. The
// structural command ran anyway, its success path resynchronized the Formula Bar from the
// post-shift ActiveCell (clobbering the still-open "=A1" text), while _formulaEditCell kept
// pointing at the stale pre-shift address -- so finishing the edit afterwards committed
// blank/wrong content to a cell that was never the one being edited. The fix makes
// TryExecuteWorksheetStructure bail out entirely (matching the "do nothing" pattern its own
// callers already use for other early exits, e.g. a cancelled CellShiftDialog) when the pending
// edit could not be committed, leaving the point-mode edit completely untouched instead.
public sealed class R164_FormulaEditRefsPointModeStructuralInsertTests
{
    [Fact]
    public void InsertRowAbove_WhilePointModeFormulaEditIsOpen_LeavesTheEditAndGridUntouched()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = FormulaEditPointModeInsertHarness.Create();

            // A1 holds a value the in-progress "=A1" formula will end up referencing.
            harness.SetCellText(1, 1, "original-A1");

            // Start a point-mode formula edit on B1 (row 1, col 2), matching the finding's gesture:
            // click an empty cell, press '=', type 'A1' -- Formula Bar shows "=A1", status bar is in
            // Point mode.
            var editCell = new CellAddress(harness.SheetId, 1, 2);
            harness.BeginPointModeFormulaEdit(editCell, "=A1");
            harness.HasActiveFormulaPointMode.Should().BeTrue(
                "the harness must reproduce the same point-mode state the user gesture leaves behind");

            // Ribbon Home > Cells > Insert > Insert Sheet Rows, with the point-mode edit still open.
            harness.InsertRowBtnClick();

            // Before the fix: the structural command ran anyway, blanked the Formula Bar (or filled
            // it with B1's post-shift content), and shifted "original-A1" down to A2 while
            // _formulaEditCell kept pointing at the now-wrong B1. After the fix: nothing happens --
            // the insert never runs, and the point-mode edit is left exactly as the user left it.
            harness.FormulaBoxText.Should().Be(
                "=A1",
                "a ribbon structural command must not silently discard the user's in-progress point-mode formula");
            harness.HasActiveFormulaPointMode.Should().BeTrue(
                "the point-mode edit must still be open after the blocked insert attempt");
            harness.CellText(1, 1).Should().Be(
                "original-A1",
                "no row shift may happen while the point-mode edit could not be committed first");

            // The user can still finish the edit normally afterwards, and it must land on the SAME
            // cell they started editing (B1), not a stale/shifted address.
            harness.CommitEdit().Should().BeTrue("the untouched point-mode edit must still be committable");
            harness.CellFormula(1, 2).Should().Be(
                "A1",
                "the formula must commit to B1, the cell the user was actually editing, since the " +
                "blocked insert never shifted anything (Cell.FormulaText is stored without the leading '=')");
        });
    }

    [Fact]
    public void InsertRowAbove_WithPlainTextEditOpen_StillCommitsBeforeTheShift()
    {
        // Sibling/no-regression case: a plain (non-formula) pending edit is NOT point-mode, so
        // TryCommitPendingSpellCheckEdit() must still commit it and the structural insert must still
        // proceed -- this is the already-correct R160-formula-editing-F2 behavior and must be
        // unaffected by the new early-bailout path.
        StaTestRunner.Run(() =>
        {
            using var harness = FormulaEditPointModeInsertHarness.Create();

            harness.SetCellText(1, 1, "original");
            harness.SelectActiveCell(new CellAddress(harness.SheetId, 1, 1));

            harness.EditActiveCellInFormulaBar();
            harness.SetFormulaBarText("typed");
            harness.HasActiveFormulaPointMode.Should().BeFalse(
                "plain text entry (no leading '=') never enters formula point mode");

            harness.InsertRowBtnClick();

            harness.CommitEdit().Should().BeTrue("the pending edit must still be committable after the insert");

            harness.CellText(1, 1).Should().BeNull("row 1 is the freshly-inserted blank row");
            harness.CellText(2, 1).Should().Be(
                "typed",
                "the still-open Formula Bar edit must still commit to A1 BEFORE the insert shifts row 1 " +
                "down to row 2, exactly as R160-formula-editing-F2 already fixed");
        });
    }

    private sealed class FormulaEditPointModeInsertHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly Sheet _sheet;
        private readonly Action<object, System.Windows.RoutedEventArgs> _insertRowBtnClick;

        private FormulaEditPointModeInsertHarness(MainWindow window, Sheet sheet)
        {
            _window = window;
            _sheet = sheet;

            var method = typeof(MainWindow).GetMethod("InsertRowBtn_Click", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "InsertRowBtn_Click");
            _insertRowBtnClick = method.CreateDelegate<Action<object, System.Windows.RoutedEventArgs>>(_window);
        }

        public SheetId SheetId => _sheet.Id;

        public bool HasActiveFormulaPointMode => _window.HasActiveFormulaPointMode;

        public string FormulaBoxText => _window.FormulaBoxTextForTest;

        public void SetCellText(uint row, uint col, string text) =>
            _sheet.SetCell(new CellAddress(_sheet.Id, row, col), Cell.FromValue(new TextValue(text)));

        public string? CellText(uint row, uint col) =>
            _sheet.GetCell(new CellAddress(_sheet.Id, row, col))?.Value is TextValue text ? text.Value : null;

        public string? CellFormula(uint row, uint col) =>
            _sheet.GetCell(new CellAddress(_sheet.Id, row, col))?.FormulaText;

        public void SelectActiveCell(CellAddress address)
        {
            _window.SetActiveCellForTest(address);
            DispatcherTestPump.PumpDispatcher();
        }

        public void EditActiveCellInFormulaBar()
        {
            _window.EditActiveCellInFormulaBarForTest();
            DispatcherTestPump.PumpDispatcher();
        }

        public void SetFormulaBarText(string text)
        {
            _window.FormulaBoxTextForTest = text;
            DispatcherTestPump.PumpDispatcher();
        }

        public void BeginPointModeFormulaEdit(CellAddress address, string formulaText)
        {
            _window.BeginFormulaPointModeEditForTest(address, formulaText);
            DispatcherTestPump.PumpDispatcher();
        }

        public void InsertRowBtnClick()
        {
            _insertRowBtnClick(_window, new System.Windows.RoutedEventArgs());
            DispatcherTestPump.PumpDispatcher();
        }

        public bool CommitEdit()
        {
            var committed = _window.CommitEditForTest();
            DispatcherTestPump.PumpDispatcher();
            return committed;
        }

        public static FormulaEditPointModeInsertHarness Create()
        {
            var workbook = new Workbook("Book1");
            workbook.AddSheet("Sheet1");

            var workbookRef = new WorkbookRef { Current = workbook };
            var graph = new DependencyGraph();
            var evaluator = new FormulaEvaluator();
            var commandBus = new CommandBus(_ => new TestCommandContext(workbookRef.Current));
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                commandBus,
                new RecalcEngine(graph, evaluator),
                Array.Empty<IFileAdapter>(),
                workbookRef,
                workbook,
                NullUserMessageService.Instance)
            {
                Width = 1280,
                Height = 720
            };

            window.Show();
            window.Activate();
            var sheet = workbookRef.Current.Sheets[0];

            window.UpdateLayout();
            DispatcherTestPump.PumpDispatcher();
            return new FormulaEditPointModeInsertHarness(window, sheet);
        }

        public void Dispose() => MainWindowTestCleanup.CloseWithoutSavePrompt(_window);
    }
}

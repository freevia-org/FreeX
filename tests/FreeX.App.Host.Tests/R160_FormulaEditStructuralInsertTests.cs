using System.Reflection;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

// R160-formula-editing-F2: Insert/Delete Rows or Columns executed while a Formula Bar edit is open
// used to commit that edit to the STALE pre-shift cell address. The WPF host tracks the cell being
// edited in a private _formulaEditCell field (CaptureFormulaEditCell, MainWindow.FormulaReferenceEditing.cs),
// which is never touched by a structural ribbon command -- TryExecuteWorksheetStructure
// (MainWindow.CommandExecution.cs, the single choke point InsertRows/DeleteRows/InsertColumns/
// DeleteColumns/InsertSelectedCells/DeleteSelectedCells all funnel through) re-synchronizes that same
// stale field into WorkbookSession.FormulaEditAddress right before AND right after the structural
// shift runs, so a still-open edit committed after the shift landed on whatever now occupies the
// pre-shift address instead of following the cell it actually belonged to. The fix makes
// TryExecuteWorksheetStructure commit (or, in formula-reference point mode, leave untouched) any
// pending edit BEFORE the structural command executes -- mirroring Excel, which always finishes an
// in-progress edit before a ribbon structural command can run, so the edit's own value shifts along
// with everything else instead of being silently resurrected onto the wrong cell afterwards.
public sealed class R160_FormulaEditStructuralInsertTests
{
    [Fact]
    public void InsertRowAbove_WhileFormulaBarEditIsOpen_CommitsBeforeTheShiftInsteadOfAfter()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = FormulaEditInsertHarness.Create();

            harness.SetCellText(1, 1, "original");
            harness.SelectActiveCell(new CellAddress(harness.SheetId, 1, 1));

            // "single-click directly into the Formula Bar" (MainWindow.FormulaReferenceEditing.cs:31)
            // -- captures _formulaEditCell = A1 without committing or cancelling anything.
            harness.EditActiveCellInFormulaBar();
            harness.SetFormulaBarText("typed");

            // Ribbon Home > Cells > Insert > Insert Sheet Rows, with the Formula Bar edit still open.
            harness.InsertRowBtnClick();

            // Finish the still-open edit exactly as the user gesture does: click back into the
            // Formula Bar (already focused) and press Enter.
            harness.CommitEdit().Should().BeTrue("the pending edit must still be committable after the insert");

            // Before the fix: "typed" is resurrected onto stale A1 (now the freshly-inserted blank
            // row) when Enter is pressed, and "original" survives untouched at A2 -- the user's edit
            // and the shifted original content end up swapped across the two cells. After the fix,
            // TryExecuteWorksheetStructure commits "typed" to A1 BEFORE the insert runs, so the
            // insert then correctly carries it down to A2 along with the rest of row 1.
            harness.CellText(1, 1).Should().BeNull("row 1 is the freshly-inserted blank row");
            harness.CellText(2, 1).Should().Be(
                "typed",
                "the still-open Formula Bar edit must commit to A1 BEFORE the insert shifts row 1 down to row 2, " +
                "not resurrect the stale pre-shift address afterwards and leave the pre-edit value behind");
        });
    }

    [Fact]
    public void InsertRowAbove_WithNoFormulaEditInProgress_StillShiftsContentNormally()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = FormulaEditInsertHarness.Create();

            harness.SetCellText(1, 1, "original");
            harness.SelectActiveCell(new CellAddress(harness.SheetId, 1, 1));

            // No Formula Bar edit is open here (_formulaEditCell is null) -- the sibling, already-
            // correct case: an ordinary Insert Sheet Rows with nothing pending must keep shifting
            // existing content exactly as it always has, with no spurious commit side effect from
            // the new pre-shift-commit gate.
            harness.InsertRowBtnClick();

            harness.CellText(1, 1).Should().BeNull("row 1 is the freshly-inserted blank row");
            harness.CellText(2, 1).Should().Be(
                "original",
                "with no open edit, the insert must still shift existing content down exactly as before");
        });
    }

    private sealed class FormulaEditInsertHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly Sheet _sheet;
        private readonly Action<object, System.Windows.RoutedEventArgs> _insertRowBtnClick;

        private FormulaEditInsertHarness(MainWindow window, Sheet sheet)
        {
            _window = window;
            _sheet = sheet;

            var method = typeof(MainWindow).GetMethod("InsertRowBtn_Click", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "InsertRowBtn_Click");
            _insertRowBtnClick = method.CreateDelegate<Action<object, System.Windows.RoutedEventArgs>>(_window);
        }

        public SheetId SheetId => _sheet.Id;

        public void SetCellText(uint row, uint col, string text) =>
            _sheet.SetCell(new CellAddress(_sheet.Id, row, col), Cell.FromValue(new TextValue(text)));

        public string? CellText(uint row, uint col) =>
            _sheet.GetCell(new CellAddress(_sheet.Id, row, col))?.Value is TextValue text ? text.Value : null;

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

        public static FormulaEditInsertHarness Create()
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
            return new FormulaEditInsertHarness(window, sheet);
        }

        public void Dispose() => MainWindowTestCleanup.CloseWithoutSavePrompt(_window);
    }
}

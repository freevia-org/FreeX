using System.Reflection;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

// R123-cellscmds-multiarea-insert-1: mirror of R123_MultiAreaHeaderDeleteTests on the Insert side.
// InsertRowBtn_Click/InsertColBtn_Click (reached from the ribbon "Insert Sheet Rows/Columns" buttons
// and the worksheet right-click "Insert Row Above/Left" menu items, all of which funnel through the
// shared InsertRows/InsertColumns helpers) and the keyboard Ctrl+Plus path (ExecuteKeyboardInsert)
// used to read ONLY the active SheetGrid.SelectedRange, so a disjoint multi-area row/column-header
// selection (built via Ctrl+click -- AddAdditionalRowSelection/AddAdditionalColumnSelection) had
// every area but the active one silently dropped from the insert -- unlike real Excel, which inserts
// one new row/column at EACH disjoint area of a multi-area selection in a single operation. The fix
// routes the insert through the same selection-ranges-aware plumbing the Delete side already uses,
// building one Insert*Command per disjoint area, processed in descending row/column order so
// inserting at one area never renumbers the still-pending index of another queued area.
public sealed class R123_MultiAreaHeaderInsertTests
{
    [Fact]
    public void InsertRowBtn_Click_MultiAreaHeaderSelection_InsertsAtEveryDisjointRow()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MultiAreaSelectionHarness.Create();

            // Ctrl+click rows 2 and 5 (disjoint) via SelectRow (plain click, row 2) then
            // AddAdditionalRowSelection (Ctrl+click, row 5) -- exactly the real mouse-handler
            // sequence (SheetGrid_MouseDown's Ctrl+click branch on a row header).
            harness.SelectRow(2);
            harness.AddAdditionalRowSelection(5);

            harness.SelectedRanges.Should().NotBeNull();
            harness.SelectedRanges!.Count.Should().Be(2, "two disjoint row-header areas must be tracked before the insert");

            harness.InsertRowBtnClick();

            // Before the fix, only the active area (row 5) got a blank row inserted above it, and
            // row 2's area was silently skipped entirely.
            harness.MarkerAt(1, 1).Should().Be("R1", "row 1 (above both inserted areas) must be untouched");
            harness.MarkerAt(2, 1).Should().BeNull("a blank row must be inserted above original row 2");
            harness.MarkerAt(3, 1).Should().Be("R2", "original row 2's marker shifts down into row 3");
            harness.MarkerAt(4, 1).Should().Be("R3");
            harness.MarkerAt(5, 1).Should().Be("R4");
            harness.MarkerAt(6, 1).Should().BeNull("a SECOND blank row must be inserted above original row 5 (the second disjoint area)");
            harness.MarkerAt(7, 1).Should().Be("R5", "original row 5's marker shifts down into row 7 once both inserts have run");
            harness.MarkerAt(8, 1).Should().Be("R6");
            harness.LastMarkerRow().Should().Be(12, "10 original rows plus the 2 inserted disjoint blank rows leaves the last marker at row 12");
        });
    }

    [Fact]
    public void InsertColBtn_Click_MultiAreaHeaderSelection_InsertsAtEveryDisjointColumn()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MultiAreaSelectionHarness.Create();

            harness.SelectColumn(2);
            harness.AddAdditionalColumnSelection(5);

            harness.SelectedRanges.Should().NotBeNull();
            harness.SelectedRanges!.Count.Should().Be(2);

            harness.InsertColBtnClick();

            harness.MarkerAt(12, 1).Should().Be("C1", "column 1 (left of both inserted areas) must be untouched");
            harness.MarkerAt(12, 2).Should().BeNull("a blank column must be inserted left of original column 2");
            harness.MarkerAt(12, 3).Should().Be("C2", "original column 2's marker shifts right into column 3");
            harness.MarkerAt(12, 6).Should().BeNull("a SECOND blank column must be inserted left of original column 5 (the second disjoint area)");
            harness.MarkerAt(12, 7).Should().Be("C5", "original column 5's marker shifts right into column 7 once both inserts have run");
            harness.LastMarkerCol().Should().Be(12, "10 original columns plus the 2 inserted disjoint blank columns leaves the last marker at column 12");
        });
    }

    [Fact]
    public void ExecuteKeyboardInsert_MultiAreaRowSelection_InsertsAtEveryDisjointRow()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MultiAreaSelectionHarness.Create();

            // The Ctrl+Plus keyboard path (ExecuteKeyboardInsert) must behave identically to the
            // ribbon/right-click Insert Row path for a multi-area row-header selection.
            harness.SelectRow(2);
            harness.AddAdditionalRowSelection(5);

            harness.ExecuteKeyboardInsert();

            harness.MarkerAt(1, 1).Should().Be("R1");
            harness.MarkerAt(2, 1).Should().BeNull();
            harness.MarkerAt(3, 1).Should().Be("R2");
            harness.MarkerAt(6, 1).Should().BeNull();
            harness.MarkerAt(7, 1).Should().Be("R5");
            harness.LastMarkerRow().Should().Be(12);
        });
    }

    // No-regression sibling: a plain SINGLE active-range row insert (the overwhelmingly common case
    // -- no Ctrl+click involved) must keep inserting exactly one blank row above that band, unaffected
    // by routing the command construction through the ranges-aware plumbing.
    [Fact]
    public void InsertRowBtn_Click_SingleActiveRange_StillInsertsOnlyAtThatRow()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MultiAreaSelectionHarness.Create();

            harness.SelectRow(3);
            harness.SelectedRanges.Should().BeNull("a plain single-row click must not create a multi-area selection");

            harness.InsertRowBtnClick();

            harness.MarkerAt(1, 1).Should().Be("R1");
            harness.MarkerAt(2, 1).Should().Be("R2");
            harness.MarkerAt(3, 1).Should().BeNull("a blank row is inserted above original row 3");
            harness.MarkerAt(4, 1).Should().Be("R3", "row 3's original marker shifts down into row 4");
            harness.LastMarkerRow().Should().Be(11, "10 original rows plus the single inserted row leaves 11");
        });
    }

    private sealed class MultiAreaSelectionHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly Sheet _sheet;
        private readonly SheetId _sheetId;
        private readonly Action<uint> _selectRow;
        private readonly Action<uint> _selectColumn;
        private readonly Action<uint> _addAdditionalRowSelection;
        private readonly Action<uint> _addAdditionalColumnSelection;
        private readonly Action<object, System.Windows.RoutedEventArgs> _insertRowBtnClick;
        private readonly Action<object, System.Windows.RoutedEventArgs> _insertColBtnClick;
        private readonly Action _executeKeyboardInsert;

        private MultiAreaSelectionHarness(MainWindow window, Sheet sheet)
        {
            _window = window;
            _sheet = sheet;
            _sheetId = sheet.Id;

            _selectRow = BindVoidMethod<uint>("SelectRow");
            _selectColumn = BindVoidMethod<uint>("SelectColumn");
            _addAdditionalRowSelection = BindVoidMethod<uint>("AddAdditionalRowSelection");
            _addAdditionalColumnSelection = BindVoidMethod<uint>("AddAdditionalColumnSelection");
            _insertRowBtnClick = BindVoidMethod<object, System.Windows.RoutedEventArgs>("InsertRowBtn_Click");
            _insertColBtnClick = BindVoidMethod<object, System.Windows.RoutedEventArgs>("InsertColBtn_Click");
            _executeKeyboardInsert = BindVoidMethod("ExecuteKeyboardInsert");
        }

        private Action BindVoidMethod(string name)
        {
            var method = typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), name);
            return method.CreateDelegate<Action>(_window);
        }

        private Action<T> BindVoidMethod<T>(string name)
        {
            var method = typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), name);
            return method.CreateDelegate<Action<T>>(_window);
        }

        private Action<T1, T2> BindVoidMethod<T1, T2>(string name)
        {
            var method = typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), name);
            return method.CreateDelegate<Action<T1, T2>>(_window);
        }

        public IReadOnlyList<GridRange>? SelectedRanges => _window.SheetGrid.SelectedRanges;

        public void SelectRow(uint row) => _selectRow(row);
        public void SelectColumn(uint col) => _selectColumn(col);
        public void AddAdditionalRowSelection(uint row) => _addAdditionalRowSelection(row);
        public void AddAdditionalColumnSelection(uint col) => _addAdditionalColumnSelection(col);
        public void InsertRowBtnClick() => _insertRowBtnClick(_window, new System.Windows.RoutedEventArgs());
        public void InsertColBtnClick() => _insertColBtnClick(_window, new System.Windows.RoutedEventArgs());
        public void ExecuteKeyboardInsert() => _executeKeyboardInsert();

        public string? MarkerAt(uint row, uint col)
        {
            var cell = _sheet.GetCell(new CellAddress(_sheetId, row, col));
            return (cell?.Value as TextValue)?.Value;
        }

        public uint LastMarkerRow()
        {
            uint last = 0;
            for (uint row = 1; row <= 20; row++)
            {
                if (_sheet.GetCell(new CellAddress(_sheetId, row, 1)) is { } cell &&
                    cell.Value is TextValue { Value: var text } && text.StartsWith('R'))
                    last = row;
            }
            return last;
        }

        public uint LastMarkerCol()
        {
            uint last = 0;
            for (uint col = 1; col <= 20; col++)
            {
                if (_sheet.GetCell(new CellAddress(_sheetId, 12, col)) is { } cell &&
                    cell.Value is TextValue { Value: var text } && text.StartsWith('C'))
                    last = col;
            }
            return last;
        }

        public static MultiAreaSelectionHarness Create()
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
            var sheet = workbookRef.Current.Sheets[0];

            // Row markers down column A ("R1".."R10", used by the row-insert tests) and column
            // markers across row 12 ("C1".."C10", used by the column-insert tests) -- kept well
            // apart (row 12 is outside the row 1-10 marker band) so the two marker sets never
            // collide in the same cell.
            for (uint row = 1; row <= 10; row++)
                sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"R{row}"));
            for (uint col = 1; col <= 10; col++)
                sheet.SetCell(new CellAddress(sheet.Id, 12, col), new TextValue($"C{col}"));

            window.UpdateLayout();
            DispatcherTestPump.PumpDispatcher();
            return new MultiAreaSelectionHarness(window, sheet);
        }

        public void Dispose()
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(_window);
        }
    }
}

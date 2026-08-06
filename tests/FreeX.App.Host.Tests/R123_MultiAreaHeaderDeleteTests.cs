using System.Reflection;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

// R123: WPF row/column-header Ctrl+click (AddAdditionalRowSelection/AddAdditionalColumnSelection,
// MainWindow.Selection.cs) is a first-class multi-area selection gesture -- SheetGrid.SelectedRanges
// holds every clicked whole-row/column GridRange, while SheetGrid.SelectedRange is only the
// last-clicked (active) one. DeleteSelectedRows/DeleteSelectedColumns (reached from the ribbon
// "Delete Sheet Rows/Columns" buttons and the worksheet right-click "Delete Rows/Columns" menu) and
// the keyboard Ctrl+Minus path (ExecuteKeyboardDelete) used to read ONLY SheetGrid.SelectedRange, so
// every area but the active one was silently dropped from the delete -- unlike real Excel, which
// deletes every selected row/column across a disjoint multi-area selection in one operation. The fix
// routes the delete through the same selection-ranges-aware plumbing Clear Contents/style commands
// already use, building one Delete*Command per disjoint area, processed in descending row/column
// order so deleting one band never renumbers the still-pending index of another queued area.
public sealed class R123_MultiAreaHeaderDeleteTests
{
    [Fact]
    public void DeleteSelectedRows_MultiAreaHeaderSelection_DeletesEveryDisjointRow()
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
            harness.SelectedRanges!.Count.Should().Be(2, "two disjoint row-header areas must be tracked before the delete");

            harness.DeleteSelectedRows();

            // Row 2's marker ("R2") must be gone -- deleted, not merely shifted -- and so must row
            // 5's ORIGINAL marker ("R5"): before the fix, only the active area (row 5) was deleted,
            // leaving R2 (and everything shifted up under it) fully intact.
            harness.MarkerAt(1, 1).Should().Be("R1", "row 1 (above both deleted areas) must be untouched");
            harness.MarkerAt(2, 1).Should().Be("R3", "row 2 was deleted, so row 3's marker shifts up into its place");
            harness.MarkerAt(3, 1).Should().Be("R4", "row 4 shifts up to row 3 once row 2 is gone");
            // Original rows 2 and 5 are both gone: only 8 of the original 10 marker rows remain.
            harness.MarkerAt(4, 1).Should().Be("R6", "row 5 was also deleted (the SECOND disjoint area), so row 6 shifts up next to row 4");
            harness.LastMarkerRow().Should().Be(8, "10 original rows minus the 2 deleted disjoint rows leaves 8");
        });
    }

    [Fact]
    public void DeleteSelectedColumns_MultiAreaHeaderSelection_DeletesEveryDisjointColumn()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MultiAreaSelectionHarness.Create();

            harness.SelectColumn(2);
            harness.AddAdditionalColumnSelection(5);

            harness.SelectedRanges.Should().NotBeNull();
            harness.SelectedRanges!.Count.Should().Be(2);

            harness.DeleteSelectedColumns();

            harness.MarkerAt(12, 1).Should().Be("C1", "column 1 (left of both deleted areas) must be untouched");
            harness.MarkerAt(12, 2).Should().Be("C3", "column 2 was deleted, so column 3's marker shifts left into its place");
            harness.MarkerAt(12, 3).Should().Be("C4");
            harness.MarkerAt(12, 4).Should().Be("C6", "column 5 was also deleted (the SECOND disjoint area), so column 6 shifts left next to column 4");
            harness.LastMarkerCol().Should().Be(8, "10 original columns minus the 2 deleted disjoint columns leaves 8");
        });
    }

    [Fact]
    public void ExecuteKeyboardDelete_MultiAreaRowSelection_DeletesEveryDisjointRow()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MultiAreaSelectionHarness.Create();

            // The Ctrl+Minus keyboard path (ExecuteKeyboardDelete) must behave identically to the
            // ribbon/right-click Delete Rows path for a multi-area row-header selection.
            harness.SelectRow(2);
            harness.AddAdditionalRowSelection(5);

            harness.ExecuteKeyboardDelete();

            harness.MarkerAt(1, 1).Should().Be("R1");
            harness.MarkerAt(2, 1).Should().Be("R3");
            harness.LastMarkerRow().Should().Be(8);
        });
    }

    // No-regression sibling: a plain SINGLE active-range row delete (the overwhelmingly common case
    // -- no Ctrl+click involved) must keep deleting exactly that one contiguous band, unaffected by
    // routing the command construction through the ranges-aware plumbing.
    [Fact]
    public void DeleteSelectedRows_SingleActiveRange_StillDeletesOnlyThatBand()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MultiAreaSelectionHarness.Create();

            harness.SelectRow(3);
            harness.SelectedRanges.Should().BeNull("a plain single-row click must not create a multi-area selection");

            harness.DeleteSelectedRows();

            harness.MarkerAt(1, 1).Should().Be("R1");
            harness.MarkerAt(2, 1).Should().Be("R2");
            harness.MarkerAt(3, 1).Should().Be("R4", "row 3 alone was deleted, so row 4 shifts up into its place");
            harness.LastMarkerRow().Should().Be(9, "10 original rows minus the single deleted row leaves 9");
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
        private readonly Action _deleteSelectedRows;
        private readonly Action _deleteSelectedColumns;
        private readonly Action _executeKeyboardDelete;

        private MultiAreaSelectionHarness(MainWindow window, Sheet sheet)
        {
            _window = window;
            _sheet = sheet;
            _sheetId = sheet.Id;

            _selectRow = BindVoidMethod<uint>("SelectRow");
            _selectColumn = BindVoidMethod<uint>("SelectColumn");
            _addAdditionalRowSelection = BindVoidMethod<uint>("AddAdditionalRowSelection");
            _addAdditionalColumnSelection = BindVoidMethod<uint>("AddAdditionalColumnSelection");
            _deleteSelectedRows = BindVoidMethod("DeleteSelectedRows");
            _deleteSelectedColumns = BindVoidMethod("DeleteSelectedColumns");
            _executeKeyboardDelete = BindVoidMethod("ExecuteKeyboardDelete");
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

        public IReadOnlyList<GridRange>? SelectedRanges => _window.SheetGrid.SelectedRanges;

        public void SelectRow(uint row) => _selectRow(row);
        public void SelectColumn(uint col) => _selectColumn(col);
        public void AddAdditionalRowSelection(uint row) => _addAdditionalRowSelection(row);
        public void AddAdditionalColumnSelection(uint col) => _addAdditionalColumnSelection(col);
        public void DeleteSelectedRows() => _deleteSelectedRows();
        public void DeleteSelectedColumns() => _deleteSelectedColumns();
        public void ExecuteKeyboardDelete() => _executeKeyboardDelete();

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

            // Row markers down column A ("R1".."R10", used by the row-delete tests) and column
            // markers across row 12 ("C1".."C10", used by the column-delete tests) -- kept well
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

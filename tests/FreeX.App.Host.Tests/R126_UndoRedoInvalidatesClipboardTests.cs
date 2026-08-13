using System.Reflection;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;
using FreeX.App.Presentation.Editing;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R126 (round 126 review wave): every cell-content-mutating action the WPF host recognizes (an
/// ordinary committed cell edit via <c>TryExecuteEditCells</c>, Clear Contents, Insert/Delete
/// Rows/Columns/Cells) explicitly invalidates a pending Copy/Cut's internal clipboard snapshot and
/// marching-ants marquee (R54 / <c>ClearClipboardMarqueeAfterStructuralEdit</c>) -- the existing
/// rationale being that a subsequent Paste must not silently reuse a source range the user has since
/// overwritten. <c>ExecuteUndo</c>/<c>ExecuteRedo</c> are exactly this kind of cell-content mutation
/// (they change what a cell actually contains) but never touched the clipboard at all: an Undo that
/// reverted a cell inside an already-copied range left the clipboard's cached
/// <c>WorkbookClipboardSnapshot.Cells</c> snapshot (a detached <c>Cell.Clone()</c> taken at Copy time) holding
/// the pre-undo value, so a later Paste silently resurrected data the user had just explicitly undone.
/// <para>
/// These tests drive the REAL WPF entry points (TryExecuteEditCells, ExecuteCopy, ExecuteUndo,
/// ExecuteRedo) via reflection, exactly as R112_CellAreaCtrlClickMultiSelectionTests and
/// R124_UndoDeleteDrawingObjectSelectionTests already do -- never constructing a workbook clipboard snapshot or a
/// CommandOutcome by hand.
/// </para>
/// </summary>
public sealed class R126_UndoRedoInvalidatesClipboardTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void Undo_AfterCopyOfARangeContainingTheEditedCell_InvalidatesTheClipboardSnapshot()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = ClipboardUndoHarness.Create();
            var sheet = harness.Sheet;
            var c3 = new CellAddress(sheet.Id, 3, 3);

            // (1) C3 initially holds 100.
            sheet.SetCell(c3, new Cell { Value = new NumberValue(100) });

            // (2) The user edits C3 to 200 and commits (a real, undoable EditCellsCommand).
            harness.CommitCellEdit(c3, new NumberValue(200));
            sheet.GetCell(c3)!.Value.Should().Be(new NumberValue(200), "sanity: the edit landed");

            // (3) The user selects C1:C5 and copies -- the internal clipboard snapshot captures C3=200.
            harness.SelectRange(new GridRange(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 5, 3)));
            harness.ExecuteCopy();
            harness.InternalClipboard.Should().NotBeNull("sanity: Copy must have populated the internal clipboard");
            harness.ClipboardRange.Should().NotBeNull("sanity: Copy must show the marching-ants marquee");

            // (4) The user presses Ctrl+Z -- C3 reverts to 100 in the live sheet.
            harness.ExecuteUndo();
            sheet.GetCell(c3)!.Value.Should().Be(new NumberValue(100), "sanity: undo must have reverted C3");

            // The stale clipboard snapshot (and marquee) must be gone -- otherwise a subsequent Paste
            // would silently resurrect the just-undone 200 value.
            harness.InternalClipboard.Should().BeNull(
                "Undo reverted a cell inside the copied range, so the stale Cell.Clone() snapshot must be invalidated");
            harness.ClipboardRange.Should().BeNull(
                "Undo must cancel the marching-ants marquee, matching every other content-mutating edit");
        });
    }

    [Fact]
    public void Redo_ReapplyingAnEditInsideACopiedRange_InvalidatesTheClipboardSnapshot()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = ClipboardUndoHarness.Create();
            var sheet = harness.Sheet;
            var c3 = new CellAddress(sheet.Id, 3, 3);

            sheet.SetCell(c3, new Cell { Value = new NumberValue(100) });
            harness.CommitCellEdit(c3, new NumberValue(200));
            harness.ExecuteUndo();
            sheet.GetCell(c3)!.Value.Should().Be(new NumberValue(100));

            // Copy AFTER the undo, so the snapshot legitimately holds the reverted 100.
            harness.SelectRange(new GridRange(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 5, 3)));
            harness.ExecuteCopy();
            harness.InternalClipboard.Should().NotBeNull();

            // Redo re-applies the 200 edit -- the just-taken Copy snapshot (holding 100) is now stale.
            harness.ExecuteRedo();
            sheet.GetCell(c3)!.Value.Should().Be(new NumberValue(200), "sanity: redo must have re-applied the edit");

            harness.InternalClipboard.Should().BeNull(
                "Redo re-applied a change to a cell inside the copied range, so the stale pre-redo snapshot must be invalidated");
            harness.ClipboardRange.Should().BeNull();
        });
    }

    // No-regression sibling: an Undo/Redo that has NOTHING to undo/redo (the stack is empty, so the
    // command bus outcome is unsuccessful) must leave an unrelated, still-valid pending Copy/Cut
    // completely untouched -- a fix that cleared the clipboard unconditionally (regardless of
    // outcome.Success) would spuriously cancel marching ants on every failed Ctrl+Z/Ctrl+Y, e.g. right
    // after opening a fresh workbook.
    [Fact]
    public void Undo_WithNothingToUndo_LeavesAPendingCopyUntouched()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = ClipboardUndoHarness.Create();
            var sheet = harness.Sheet;
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new Cell { Value = new NumberValue(7) });

            harness.SelectRange(new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)));
            harness.ExecuteCopy();
            harness.InternalClipboard.Should().NotBeNull();

            // The undo stack is empty (no command has ever been executed through the command bus in
            // this test), so this Undo must be a no-op at the CommandBus level.
            harness.ExecuteUndo();

            harness.InternalClipboard.Should().NotBeNull(
                "an Undo that had nothing to undo must not spuriously cancel an unrelated pending Copy");
            harness.ClipboardRange.Should().NotBeNull();
        });
    }

    private sealed class ClipboardUndoHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly Action<GridRange> _selectRange;
        private readonly Action<bool> _executeCopy;
        private readonly Action _executeUndo;
        private readonly Action _executeRedo;
        private readonly MethodInfo _tryExecuteEditCells;
        private readonly FieldInfo _workbookClipboardSessionField;

        public Sheet Sheet { get; }

        private ClipboardUndoHarness(MainWindow window, Sheet sheet)
        {
            _window = window;
            Sheet = sheet;

            // SheetGrid.SelectedRange is the single source of truth for the current selection
            // (mirrors how R112_CellAreaCtrlClickMultiSelectionTests/R124_UndoDeleteDrawingObjectSelectionTests
            // drive selection directly rather than through mouse/keyboard event plumbing).
            _selectRange = range => window.SheetGrid.SelectedRange = range;

            var executeCopy = typeof(MainWindow).GetMethod("ExecuteCopy", PrivateInstance)
                ?? throw new MissingMethodException(nameof(MainWindow), "ExecuteCopy");
            _executeCopy = isCut => executeCopy.Invoke(window, [isCut]);

            var executeUndo = typeof(MainWindow).GetMethod("ExecuteUndo", PrivateInstance)
                ?? throw new MissingMethodException(nameof(MainWindow), "ExecuteUndo");
            _executeUndo = () => executeUndo.Invoke(window, []);

            var executeRedo = typeof(MainWindow).GetMethod("ExecuteRedo", PrivateInstance)
                ?? throw new MissingMethodException(nameof(MainWindow), "ExecuteRedo");
            _executeRedo = () => executeRedo.Invoke(window, []);

            _tryExecuteEditCells = typeof(MainWindow)
                .GetMethods(PrivateInstance)
                .Single(m => m.Name == "TryExecuteEditCells" && m.GetParameters().Length == 2);

            _workbookClipboardSessionField = typeof(MainWindow)
                .GetField("_workbookClipboardSession", PrivateInstance)
                ?? throw new MissingFieldException(nameof(MainWindow), "_workbookClipboardSession");
        }

        public void SelectRange(GridRange range) => _selectRange(range);

        public void ExecuteCopy() => _executeCopy(false);

        public void ExecuteUndo() => _executeUndo();

        public void ExecuteRedo() => _executeRedo();

        public void CommitCellEdit(CellAddress address, ScalarValue value)
        {
            var edits = new List<(CellAddress Address, Cell NewCell)>
            {
                (address, new Cell { Value = value })
            };
            _tryExecuteEditCells.Invoke(_window, [edits, "Edit Cell"]);
        }

        public WorkbookClipboardSnapshot? InternalClipboard =>
            ((WorkbookClipboardSession?)_workbookClipboardSessionField.GetValue(_window))?.Content;

        public GridRange? ClipboardRange => _window.SheetGrid.ClipboardRange;

        public static ClipboardUndoHarness Create()
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
            window.UpdateLayout();
            DispatcherTestPump.PumpDispatcher();
            return new ClipboardUndoHarness(window, sheet);
        }

        public void Dispose() => MainWindowTestCleanup.CloseWithoutSavePrompt(_window);
    }
}

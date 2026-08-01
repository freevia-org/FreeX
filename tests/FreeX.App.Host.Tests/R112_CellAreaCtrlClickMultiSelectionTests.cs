using System.Reflection;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

// R112: cell-area Ctrl+click multi-area selection was broken -- CreateAdditionalSelectionRanges
// (MainWindow.Selection.cs) distinguished "extend the area currently being drawn" from "start a
// genuinely new area" by testing whether SheetGrid.SelectedRange still equalled the accumulated
// list's LAST entry. But every call ends by setting SheetGrid.SelectedRange to exactly that last
// entry, so on the NEXT call they were ALWAYS equal and it ALWAYS took the "extend" branch -- a
// second Ctrl+click on a disjoint cell could never append a second area. The fix threads the
// mouse handlers' own extendSelection flag (mouse-down: false/new area; Ctrl+drag continuation:
// true/extend) straight through instead of re-deriving it from selection state after the fact.
//
// Every test here first establishes a known single-cell baseline via SetActiveCell (mirroring a
// plain click) before Ctrl+clicking, exactly like a real user session: Excel's Ctrl+click always
// ADDS a new area to whatever was already selected, so the baseline cell is expected to remain
// area 0 throughout.
public sealed class R112_CellAreaCtrlClickMultiSelectionTests
{
    [Fact]
    public void CtrlClickAddsSecondDisjointCellArea()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = CellAreaSelectionHarness.Create();
            harness.SetActiveCell(new CellAddress(harness.SheetId, 1, 1));

            // First Ctrl+click (mouse-down: extendSelection=false) starts area 1 at B2, on top of
            // the A1 baseline (area 0).
            harness.AddOrMoveAdditionalSelection(new CellAddress(harness.SheetId, 2, 2), extendSelection: false);
            // Second Ctrl+click on a genuinely disjoint cell (mouse-down: extendSelection=false
            // again, exactly like SheetGrid_MouseDown's Ctrl+click branch) must APPEND D2 as a
            // THIRD area, not fold it into/replace area 1 (the actual bug: this always took the
            // "extend" branch and left only [D2] behind, discarding A1 and B2 both).
            harness.AddOrMoveAdditionalSelection(new CellAddress(harness.SheetId, 2, 4), extendSelection: false);

            var ranges = harness.SelectedRanges;
            ranges.Should().NotBeNull("a second disjoint Ctrl+click must produce a multi-area selection");
            ranges!.Count.Should().Be(3, "the A1 baseline plus both disjoint Ctrl+clicked cells must all be present as separate areas");
            ranges[0].Start.Row.Should().Be(1);
            ranges[0].Start.Col.Should().Be(1);
            ranges[1].Start.Row.Should().Be(2);
            ranges[1].Start.Col.Should().Be(2);
            ranges[2].Start.Row.Should().Be(2);
            ranges[2].Start.Col.Should().Be(4);
        });
    }

    [Fact]
    public void CtrlDragExtendsNewestAreaInsteadOfAppendingPerMouseMove()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = CellAreaSelectionHarness.Create();
            harness.SetActiveCell(new CellAddress(harness.SheetId, 1, 1));

            harness.AddOrMoveAdditionalSelection(new CellAddress(harness.SheetId, 2, 2), extendSelection: false);
            harness.AddOrMoveAdditionalSelection(new CellAddress(harness.SheetId, 2, 4), extendSelection: false);

            // Simulate several Ctrl+drag MouseMove steps continuing to draw the newest (third)
            // area -- each one passes extendSelection=true, exactly like SheetGrid_MouseMove's
            // _dragSelectAddsAdditionalRange branch.
            harness.AddOrMoveAdditionalSelection(new CellAddress(harness.SheetId, 3, 4), extendSelection: true);
            harness.AddOrMoveAdditionalSelection(new CellAddress(harness.SheetId, 4, 5), extendSelection: true);
            harness.AddOrMoveAdditionalSelection(new CellAddress(harness.SheetId, 5, 6), extendSelection: true);

            var ranges = harness.SelectedRanges;
            ranges.Should().NotBeNull();
            ranges!.Count.Should().Be(3, "dragging must keep extending the SAME newest area, never append a new one per mouse-move");
            // Area 0 (A1 baseline) and area 1 (B2) must be untouched by the drag extending area 2.
            ranges[0].Start.Row.Should().Be(1);
            ranges[0].Start.Col.Should().Be(1);
            ranges[1].Start.Row.Should().Be(2);
            ranges[1].Start.Col.Should().Be(2);
            ranges[1].End.Row.Should().Be(2);
            ranges[1].End.Col.Should().Be(2);
            // Area 2 must now be the full dragged rectangle D2:F5.
            ranges[2].Start.Row.Should().Be(2);
            ranges[2].Start.Col.Should().Be(4);
            ranges[2].End.Row.Should().Be(5);
            ranges[2].End.Col.Should().Be(6);
        });
    }

    [Fact]
    public void CtrlClickInsideAlreadySelectedAreaDoesNotCorruptExistingAreas()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = CellAreaSelectionHarness.Create();
            harness.SetActiveCell(new CellAddress(harness.SheetId, 1, 1));

            harness.AddOrMoveAdditionalSelection(new CellAddress(harness.SheetId, 2, 2), extendSelection: false);
            harness.AddOrMoveAdditionalSelection(new CellAddress(harness.SheetId, 10, 10), extendSelection: false);
            // A fresh Ctrl+click landing back inside an EXISTING area (not the newest/active one).
            harness.AddOrMoveAdditionalSelection(new CellAddress(harness.SheetId, 2, 2), extendSelection: false);

            var ranges = harness.SelectedRanges;
            ranges.Should().NotBeNull();
            // The pre-existing areas must still all be present somewhere in the list -- re-clicking
            // inside an existing area must never silently drop or merge other areas.
            ranges!.Should().Contain(r => r.Start.Row == 1 && r.Start.Col == 1 && r.End.Row == 1 && r.End.Col == 1);
            ranges.Should().Contain(r => r.Start.Row == 2 && r.Start.Col == 2 && r.End.Row == 2 && r.End.Col == 2);
            ranges.Should().Contain(r => r.Start.Row == 10 && r.Start.Col == 10 && r.End.Row == 10 && r.End.Col == 10);
        });
    }

    [Fact]
    public void PlainClickStillReplacesWholeSelection()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = CellAreaSelectionHarness.Create();
            harness.SetActiveCell(new CellAddress(harness.SheetId, 1, 1));

            harness.AddOrMoveAdditionalSelection(new CellAddress(harness.SheetId, 2, 2), extendSelection: false);
            harness.AddOrMoveAdditionalSelection(new CellAddress(harness.SheetId, 2, 4), extendSelection: false);
            harness.SelectedRanges.Should().NotBeNull();
            harness.SelectedRanges!.Count.Should().Be(3);

            // A plain (non-Ctrl) click just moves the active cell and collapses back to a single
            // selection, exactly as SheetGrid_MouseDown's non-Ctrl branch does via SetActiveCell.
            harness.SetActiveCell(new CellAddress(harness.SheetId, 8, 8));

            harness.SelectedRanges.Should().BeNull("a plain click must collapse a multi-area selection back down to one area");
        });
    }

    [Fact]
    public void HeaderCtrlClickStillAppendsSecondColumnArea()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = CellAreaSelectionHarness.Create();
            harness.SetActiveCell(new CellAddress(harness.SheetId, 1, 1));

            harness.AddAdditionalColumnSelection(3);
            harness.AddAdditionalColumnSelection(6);

            var ranges = harness.SelectedRanges;
            ranges.Should().NotBeNull();
            ranges!.Count.Should().Be(3, "header Ctrl+click must still always append, unaffected by the cell-area fix");
            ranges[1].Start.Col.Should().Be(3);
            ranges[2].Start.Col.Should().Be(6);
        });
    }

    [Fact]
    public void CopyingGenuineMultiAreaCellSelectionCarriesAllSourceAreas()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = CellAreaSelectionHarness.Create();
            // Baseline and both Ctrl+clicks all on the SAME row so MultiRangeCopyPlanner accepts
            // the resulting three areas as a combinable multi-area copy (Excel's "same rows OR
            // same columns" rule).
            harness.SetActiveCell(new CellAddress(harness.SheetId, 5, 1));

            harness.AddOrMoveAdditionalSelection(new CellAddress(harness.SheetId, 5, 2), extendSelection: false);
            harness.AddOrMoveAdditionalSelection(new CellAddress(harness.SheetId, 5, 4), extendSelection: false);
            harness.SelectedRanges.Should().NotBeNull();
            harness.SelectedRanges!.Count.Should().Be(3);

            harness.ExecuteCopy();

            var sourceAreas = harness.InternalClipboardSourceAreas();
            sourceAreas.Should().NotBeNull("copying a real multi-area cell selection must carry every area into the internal clipboard");
            sourceAreas!.Count.Should().Be(3);
        });
    }

    private sealed class CellAreaSelectionHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly Action<CellAddress, bool> _addOrMoveAdditionalSelection;
        private readonly Action<CellAddress> _setActiveCell;
        private readonly Action<uint> _addAdditionalColumnSelection;
        private readonly Action _executeCopy;
        private readonly FieldInfo _internalClipboardField;

        public SheetId SheetId { get; }

        private CellAreaSelectionHarness(MainWindow window, SheetId sheetId)
        {
            _window = window;
            SheetId = sheetId;

            var addOrMoveAdditionalSelection = typeof(MainWindow)
                .GetMethod("AddOrMoveAdditionalSelection", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "AddOrMoveAdditionalSelection");
            _addOrMoveAdditionalSelection = addOrMoveAdditionalSelection.CreateDelegate<Action<CellAddress, bool>>(window);

            var setActiveCell = typeof(MainWindow)
                .GetMethod("SetActiveCell", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "SetActiveCell");
            _setActiveCell = setActiveCell.CreateDelegate<Action<CellAddress>>(window);

            var addAdditionalColumnSelection = typeof(MainWindow)
                .GetMethod("AddAdditionalColumnSelection", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "AddAdditionalColumnSelection");
            _addAdditionalColumnSelection = addAdditionalColumnSelection.CreateDelegate<Action<uint>>(window);

            var executeCopy = typeof(MainWindow)
                .GetMethod("ExecuteCopy", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "ExecuteCopy");
            _executeCopy = () => executeCopy.Invoke(window, [false]);

            _internalClipboardField = typeof(MainWindow)
                .GetField("_internalClipboard", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_internalClipboard");
        }

        public IReadOnlyList<GridRange>? SelectedRanges => _window.SheetGrid.SelectedRanges;

        public void AddOrMoveAdditionalSelection(CellAddress target, bool extendSelection) =>
            _addOrMoveAdditionalSelection(target, extendSelection);

        public void SetActiveCell(CellAddress target) => _setActiveCell(target);

        public void AddAdditionalColumnSelection(uint col) => _addAdditionalColumnSelection(col);

        public void ExecuteCopy() => _executeCopy();

        public IReadOnlyList<GridRange>? InternalClipboardSourceAreas()
        {
            var clip = _internalClipboardField.GetValue(_window);
            if (clip is null)
                return null;

            var property = clip.GetType().GetProperty("SourceAreas")
                ?? throw new MissingMemberException("InternalClipboard", "SourceAreas");
            return (IReadOnlyList<GridRange>?)property.GetValue(clip);
        }

        public static CellAreaSelectionHarness Create()
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
            for (uint row = 1; row <= 30; row++)
            {
                for (uint col = 1; col <= 30; col++)
                    sheet.SetCell(new CellAddress(sheet.Id, row, col), new NumberValue(row * col));
            }

            window.UpdateLayout();
            DispatcherTestPump.PumpDispatcher();
            return new CellAreaSelectionHarness(window, sheet.Id);
        }

        public void Dispose()
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(_window);
        }
    }
}

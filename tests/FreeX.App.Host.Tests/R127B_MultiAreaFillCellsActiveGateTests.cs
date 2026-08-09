using System.Reflection;
using System.Windows;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

// R127B-fillcmds-multiarea-gate-1 (r127 ScopeAudit follow-up to R127-fillcmds-multiarea-1, see
// R127_MultiAreaFillCellsTests.cs). The r127 fix routed the body of ExecuteFillCells through
// GetCurrentSelectionRanges/SelectionStyleCommandPlanner.CreateRangeCommand so every disjoint area
// of a Ctrl+click multi-area selection fills independently -- but it left the method's *entry gate*
// unchanged:
//
//   if (SheetGrid.SelectedRange is not { } range || !FillSeriesPlanner.CanFill(range, direction))
//       return;
//
// That gate only ever looked at SheetGrid.SelectedRange, the active/last-clicked area. When the
// active area is too small to fill in the requested direction (e.g. a single selected cell)
// but a disjoint sibling area in the very same selection IS large enough, the whole method
// returned right here and did NOTHING -- not even the sibling area got filled, unlike real Excel,
// which fills every qualifying area of a multi-area selection regardless of which one happens to
// be active. This is the mirror image of the case the original r127 fix already covers (there the
// non-active area was too small; here the ACTIVE area is too small).
//
// The fix widens the gate to check whether ANY area returned by GetCurrentSelectionRanges
// qualifies, matching WorkbookSession.FillSelectedRange (WorkbookSession.cs), which has no
// single-active-range gate at all.
public sealed class R127B_MultiAreaFillCellsActiveGateTests
{
    [Fact]
    public void FillDownMenuItem_Click_ActiveAreaTooSmallToFill_StillFillsQualifyingSiblingArea()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MultiAreaFillGateHarness.Create();

            harness.SetNumber(1, 1, 99); // A1 -- the too-small active area's only cell
            harness.SetNumber(1, 3, 20); // C1 -- seed for the qualifying sibling area

            var tooSmallActive = harness.Range(1, 1, 1, 1); // A1:A1 -- one row, cannot Fill Down
            var areaC = harness.Range(1, 3, 3, 3);           // C1:C3 -- qualifies, NOT active

            // Ctrl+click A1 last: it becomes the active/last-clicked SheetGrid.SelectedRange while
            // C1:C3 (selected earlier) remains part of SelectedRanges but is not the active area.
            harness.SetMultiAreaSelection(active: tooSmallActive, all: [tooSmallActive, areaC]);

            harness.FillDownMenuItemClick();

            // Before the fix: ExecuteFillCells returned at the entry gate (SheetGrid.SelectedRange
            // == A1:A1 fails FillSeriesPlanner.CanFill for Down) before the multi-area logic ever
            // ran, so C2:C3 stayed blank despite C1:C3 being a perfectly fillable disjoint area.
            harness.Sheet.GetValue(2, 3).Should().Be(new NumberValue(20), "C2 in the qualifying disjoint area must be filled down even though the active area is too small");
            harness.Sheet.GetValue(3, 3).Should().Be(new NumberValue(20), "C3 in the qualifying disjoint area must be filled down even though the active area is too small");
            // The too-small active area itself has no fill target below it -- untouched, as seeded.
            harness.Sheet.GetValue(1, 1).Should().Be(new NumberValue(99));
        });
    }

    // No-regression sibling: when NOT ONE area in the multi-area selection qualifies for the
    // requested direction, the operation must still cleanly no-op (no exception, no partial edit,
    // nothing marked dirty) -- exactly the prior single-area behavior, just generalized to "no area
    // qualifies" instead of "the one area doesn't qualify".
    [Fact]
    public void FillDownMenuItem_Click_NoAreaQualifies_NoOpsCleanly()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MultiAreaFillGateHarness.Create();

            harness.SetNumber(1, 1, 1); // A1
            harness.SetNumber(1, 3, 2); // C1

            var areaA = harness.Range(1, 1, 1, 1); // A1:A1 -- one row, cannot Fill Down
            var areaC = harness.Range(1, 3, 1, 3); // C1:C1 -- one row, cannot Fill Down

            harness.SetMultiAreaSelection(active: areaC, all: [areaA, areaC]);

            var act = () => harness.FillDownMenuItemClick();
            act.Should().NotThrow();

            harness.Sheet.GetValue(1, 1).Should().Be(new NumberValue(1));
            harness.Sheet.GetValue(1, 3).Should().Be(new NumberValue(2));
        });
    }

    private sealed class MultiAreaFillGateHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly Action<object, RoutedEventArgs> _fillDownMenuItemClick;

        private MultiAreaFillGateHarness(MainWindow window, Workbook workbook, Sheet sheet)
        {
            _window = window;
            Workbook = workbook;
            Sheet = sheet;

            _fillDownMenuItemClick = BindVoidMethod<object, RoutedEventArgs>("FillDownMenuItem_Click");
        }

        private Action<T1, T2> BindVoidMethod<T1, T2>(string name)
        {
            var method = typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), name);
            return method.CreateDelegate<Action<T1, T2>>(_window);
        }

        public Workbook Workbook { get; }
        public Sheet Sheet { get; }

        public GridRange Range(uint startRow, uint startCol, uint endRow, uint endCol) =>
            new(new CellAddress(Sheet.Id, startRow, startCol), new CellAddress(Sheet.Id, endRow, endCol));

        public void SetNumber(uint row, uint col, double value) =>
            Sheet.SetCell(new CellAddress(Sheet.Id, row, col), new NumberValue(value));

        // Mirrors the SheetGrid dependency-property state AddOrMoveAdditionalSelection leaves
        // behind after a real Ctrl+click builds a multi-area cell selection: SelectedRanges holds
        // every disjoint area, SelectedRange is only the last-clicked (active) one.
        public void SetMultiAreaSelection(GridRange active, IReadOnlyList<GridRange> all)
        {
            _window.SheetGrid.SelectedRanges = all;
            _window.SheetGrid.SelectedRange = active;
        }

        public void FillDownMenuItemClick() => _fillDownMenuItemClick(_window, new RoutedEventArgs());

        public static MultiAreaFillGateHarness Create()
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
            return new MultiAreaFillGateHarness(window, workbookRef.Current, sheet);
        }

        public void Dispose()
        {
            foreach (Window ownedWindow in _window.OwnedWindows.Cast<Window>().ToList())
                ownedWindow.Close();
            MainWindowTestCleanup.CloseWithoutSavePrompt(_window);
        }
    }
}

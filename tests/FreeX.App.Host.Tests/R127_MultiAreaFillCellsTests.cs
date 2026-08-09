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

// R127-fillcmds-multiarea-1: Fill Down/Up/Left/Right (ribbon buttons, their menu items, and the
// Ctrl+D/Ctrl+R keyboard shortcuts) all funnel through ExecuteFillCells, which used to read only
// SheetGrid.SelectedRange -- the single "active" area of a Ctrl+click multi-area selection -- and
// pass just that one GridRange into FillCellsCommand. With areas A1:A3 and C1:C3 Ctrl+click
// selected (C1:C3 active/last-clicked), Fill Down used to fill C2:C3 from C1 and silently leave
// A2:A3 untouched, unlike real Excel, which fills every disjoint area of a multi-area selection
// independently from its own edge in one Fill Down action. The fix routes ExecuteFillCells through
// the same GetCurrentSelectionRanges/SelectionStyleCommandPlanner.CreateRangeCommand choke point
// the R124 Group/Ungroup fix and Ctrl+Enter (CommitEditAcrossSelection) already use for this exact
// scenario.
public sealed class R127_MultiAreaFillCellsTests
{
    [Fact]
    public void FillDownMenuItem_Click_MultiAreaSelection_FillsEveryDisjointArea()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MultiAreaFillHarness.Create();

            harness.SetNumber(1, 1, 10); // A1
            harness.SetNumber(1, 3, 20); // C1

            var areaA = harness.Range(1, 1, 3, 1); // A1:A3
            var areaC = harness.Range(1, 3, 3, 3); // C1:C3 -- the last-clicked/active area

            // Mirrors the SheetGrid state a real Ctrl+click on cells in column A then column C
            // leaves behind: SelectedRanges holds both disjoint areas, SelectedRange is only the
            // active (last-clicked) one.
            harness.SetMultiAreaSelection(active: areaC, all: [areaA, areaC]);

            harness.FillDownMenuItemClick();

            // Before the fix, only column C (the active area) was filled down from C1; column A
            // was silently left untouched.
            harness.Sheet.GetValue(2, 1).Should().Be(new NumberValue(10), "A2 in the disjoint area must also be filled down");
            harness.Sheet.GetValue(3, 1).Should().Be(new NumberValue(10), "A3 in the disjoint area must also be filled down");
            harness.Sheet.GetValue(2, 3).Should().Be(new NumberValue(20), "C2 (the active area) must be filled down");
            harness.Sheet.GetValue(3, 3).Should().Be(new NumberValue(20), "C3 (the active area) must be filled down");
        });
    }

    [Fact]
    public void FillRightMenuItem_Click_MultiAreaSelection_FillsEveryDisjointArea()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MultiAreaFillHarness.Create();

            harness.SetNumber(1, 1, 10); // A1
            harness.SetNumber(3, 1, 20); // A3

            var areaTop = harness.Range(1, 1, 1, 3); // A1:C1
            var areaBottom = harness.Range(3, 1, 3, 3); // A3:C3 -- active area

            harness.SetMultiAreaSelection(active: areaBottom, all: [areaTop, areaBottom]);

            harness.FillRightMenuItemClick();

            harness.Sheet.GetValue(1, 2).Should().Be(new NumberValue(10), "B1 in the disjoint area must also be filled right");
            harness.Sheet.GetValue(1, 3).Should().Be(new NumberValue(10), "C1 in the disjoint area must also be filled right");
            harness.Sheet.GetValue(3, 2).Should().Be(new NumberValue(20), "B3 (the active area) must be filled right");
            harness.Sheet.GetValue(3, 3).Should().Be(new NumberValue(20), "C3 (the active area) must be filled right");
        });
    }

    // No-regression sibling: a plain SINGLE active-range Fill Down (the overwhelmingly common
    // case -- no Ctrl+click involved) must keep filling exactly that one range, unaffected by
    // routing the command construction through the ranges-aware plumbing.
    [Fact]
    public void FillDownMenuItem_Click_SingleActiveRange_StillFillsOnlyThatRange()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MultiAreaFillHarness.Create();

            harness.SetNumber(1, 1, 10); // A1
            harness.SetNumber(1, 3, 99); // C1 -- outside the selection, must stay untouched

            var areaA = harness.Range(1, 1, 3, 1); // A1:A3
            harness.SetSingleAreaSelection(areaA);
            harness.SelectedRanges.Should().BeNull("a plain single-range selection must not create a multi-area selection");

            harness.FillDownMenuItemClick();

            harness.Sheet.GetValue(2, 1).Should().Be(new NumberValue(10));
            harness.Sheet.GetValue(3, 1).Should().Be(new NumberValue(10));
            // Column C was never part of the selection: unaffected by Fill Down and unaffected by
            // routing through the multi-area-aware plumbing.
            harness.Sheet.GetValue(2, 3).Should().BeOfType<BlankValue>();
            harness.Sheet.GetValue(3, 3).Should().BeOfType<BlankValue>();
        });
    }

    // Combination: one disjoint area is too small to fill in the requested direction (a single
    // row for Fill Down). Excel just leaves that area alone rather than erroring out the whole
    // multi-area fill -- the qualifying area must still get filled.
    [Fact]
    public void FillDownMenuItem_Click_MultiAreaSelection_SkipsAreaTooSmallToFill()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MultiAreaFillHarness.Create();

            harness.SetNumber(1, 1, 10); // A1 (only row of the too-small area)
            harness.SetNumber(1, 3, 20); // C1

            var tooSmall = harness.Range(1, 1, 1, 1); // A1:A1 -- one row, cannot Fill Down
            var areaC = harness.Range(1, 3, 3, 3); // C1:C3 -- active area, qualifies

            harness.SetMultiAreaSelection(active: areaC, all: [tooSmall, areaC]);

            harness.FillDownMenuItemClick();

            harness.Sheet.GetValue(2, 3).Should().Be(new NumberValue(20));
            harness.Sheet.GetValue(3, 3).Should().Be(new NumberValue(20));
            // The too-small area produced no target cells at all -- nothing below A1 to touch,
            // and A1 itself must be left exactly as seeded.
            harness.Sheet.GetValue(1, 1).Should().Be(new NumberValue(10));
        });
    }

    private sealed class MultiAreaFillHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly Action<object, RoutedEventArgs> _fillDownMenuItemClick;
        private readonly Action<object, RoutedEventArgs> _fillRightMenuItemClick;

        private MultiAreaFillHarness(MainWindow window, Workbook workbook, Sheet sheet)
        {
            _window = window;
            Workbook = workbook;
            Sheet = sheet;

            _fillDownMenuItemClick = BindVoidMethod<object, RoutedEventArgs>("FillDownMenuItem_Click");
            _fillRightMenuItemClick = BindVoidMethod<object, RoutedEventArgs>("FillRightMenuItem_Click");
        }

        private Action<T1, T2> BindVoidMethod<T1, T2>(string name)
        {
            var method = typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), name);
            return method.CreateDelegate<Action<T1, T2>>(_window);
        }

        public Workbook Workbook { get; }
        public Sheet Sheet { get; }

        public IReadOnlyList<GridRange>? SelectedRanges => _window.SheetGrid.SelectedRanges;

        public GridRange Range(uint startRow, uint startCol, uint endRow, uint endCol) =>
            new(new CellAddress(Sheet.Id, startRow, startCol), new CellAddress(Sheet.Id, endRow, endCol));

        public void SetNumber(uint row, uint col, double value) =>
            Sheet.SetCell(new CellAddress(Sheet.Id, row, col), new NumberValue(value));

        public void SetSingleAreaSelection(GridRange range)
        {
            _window.SheetGrid.SelectedRanges = null;
            _window.SheetGrid.SelectedRange = range;
        }

        // Mirrors the SheetGrid dependency-property state AddOrMoveAdditionalSelection leaves
        // behind after a real Ctrl+click builds a multi-area cell selection: SelectedRanges holds
        // every disjoint area, SelectedRange is only the last-clicked (active) one.
        public void SetMultiAreaSelection(GridRange active, IReadOnlyList<GridRange> all)
        {
            _window.SheetGrid.SelectedRanges = all;
            _window.SheetGrid.SelectedRange = active;
        }

        public void FillDownMenuItemClick() => _fillDownMenuItemClick(_window, new RoutedEventArgs());
        public void FillRightMenuItemClick() => _fillRightMenuItemClick(_window, new RoutedEventArgs());

        public static MultiAreaFillHarness Create()
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
            return new MultiAreaFillHarness(window, workbookRef.Current, sheet);
        }

        public void Dispose()
        {
            foreach (Window ownedWindow in _window.OwnedWindows.Cast<Window>().ToList())
                ownedWindow.Close();
            MainWindowTestCleanup.CloseWithoutSavePrompt(_window);
        }
    }
}

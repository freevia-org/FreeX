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

// R127-homeformatting-multiarea-merge-1: Merge & Center / Merge Cells / Merge Across / Unmerge
// Cells (MainWindow.HomeFormatting.cs) all used to key off SheetGrid.SelectedRange alone -- the
// single "active" (last-clicked) area of a Ctrl+click multi-area selection -- and build their
// command from just that one GridRange. With areas B1:C1 and E1:F1 Ctrl+click selected (E1:F1
// active/last-clicked), Merge Cells used to merge only E1:F1 and silently leave B1:C1 untouched,
// unlike real Excel, which merges/unmerges every disjoint area of a multi-area selection
// independently in one action. The fix routes all four handlers through the same
// GetCurrentSelectionRanges/TryExecuteRepeatableCurrentRangesCommand and
// TryExecuteRepeatableCurrentSelectionRangesCommand choke points the R124 Group/Ungroup and Row
// Height/AutoFit multi-area fixes already use (MainWindow.CommandExecution.cs).
public sealed class R127_MultiAreaMergeCellsTests
{
    [Fact]
    public void MergeCellsMenuItem_Click_MultiAreaSelection_MergesEveryDisjointArea()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MultiAreaMergeHarness.Create();

            var areaB = harness.Range(1, 2, 1, 3); // B1:C1
            var areaE = harness.Range(1, 5, 1, 6); // E1:F1 -- the last-clicked/active area
            harness.SetMultiAreaSelection(active: areaE, all: [areaB, areaE]);

            harness.MergeCellsMenuItemClick();

            // Before the fix, only E1:F1 (the active area) was merged; B1:C1 was silently left
            // untouched.
            harness.Sheet.MergedRegions.Should().Contain(areaB, "B1:C1's disjoint area must also be merged");
            harness.Sheet.MergedRegions.Should().Contain(areaE, "E1:F1 (the active area) must be merged");
        });
    }

    [Fact]
    public void MergeCenterBtn_Click_MultiAreaSelection_MergesEveryDisjointArea()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MultiAreaMergeHarness.Create();

            var areaB = harness.Range(1, 2, 1, 3); // B1:C1
            var areaE = harness.Range(1, 5, 1, 6); // E1:F1
            harness.SetMultiAreaSelection(active: areaE, all: [areaB, areaE]);

            harness.MergeCenterBtnClick();

            harness.Sheet.MergedRegions.Should().Contain(areaB, "B1:C1's disjoint area must also be merged by Merge & Center");
            harness.Sheet.MergedRegions.Should().Contain(areaE, "E1:F1 (the active area) must be merged");
        });
    }

    [Fact]
    public void MergeAcrossMenuItem_Click_MultiAreaSelection_MergesEveryDisjointAreaPerRow()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MultiAreaMergeHarness.Create();

            var areaB = harness.Range(1, 2, 2, 3); // B1:C2 (two rows)
            var areaE = harness.Range(1, 5, 2, 6); // E1:F2 (two rows) -- active
            harness.SetMultiAreaSelection(active: areaE, all: [areaB, areaE]);

            harness.MergeAcrossMenuItemClick();

            // Merge Across merges each ROW of each area independently: B1:C1, B2:C2, E1:F1, E2:F2.
            harness.Sheet.MergedRegions.Should().Contain(harness.Range(1, 2, 1, 3), "row 1 of the disjoint B area must be merged");
            harness.Sheet.MergedRegions.Should().Contain(harness.Range(2, 2, 2, 3), "row 2 of the disjoint B area must be merged");
            harness.Sheet.MergedRegions.Should().Contain(harness.Range(1, 5, 1, 6), "row 1 of the active E area must be merged");
            harness.Sheet.MergedRegions.Should().Contain(harness.Range(2, 5, 2, 6), "row 2 of the active E area must be merged");
        });
    }

    // Combination: one disjoint area is single-column (nothing to merge across), the other
    // qualifies. Excel merges the qualifying area and simply leaves the narrow one alone, rather
    // than rejecting the whole multi-area action.
    [Fact]
    public void MergeAcrossMenuItem_Click_MultiAreaSelection_SkipsSingleColumnArea()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MultiAreaMergeHarness.Create();

            var narrow = harness.Range(1, 2, 2, 2); // B1:B2 -- single column, cannot Merge Across
            var areaE = harness.Range(1, 5, 2, 6); // E1:F2 -- active, qualifies
            harness.SetMultiAreaSelection(active: areaE, all: [narrow, areaE]);

            harness.MergeAcrossMenuItemClick();

            harness.Sheet.MergedRegions.Should().Contain(harness.Range(1, 5, 1, 6));
            harness.Sheet.MergedRegions.Should().Contain(harness.Range(2, 5, 2, 6));
            harness.Sheet.MergedRegions.Should().NotContain(region => region.Start.Col == 2, "the single-column area has nothing to merge across");
        });
    }

    [Fact]
    public void UnmergeCellsMenuItem_Click_MultiAreaSelection_UnmergesEveryDisjointArea()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MultiAreaMergeHarness.Create();

            var areaB = harness.Range(1, 2, 1, 3); // B1:C1
            var areaE = harness.Range(1, 5, 1, 6); // E1:F1
            harness.Sheet.AddMergedRegion(areaB);
            harness.Sheet.AddMergedRegion(areaE);

            harness.SetMultiAreaSelection(active: areaE, all: [areaB, areaE]);

            harness.UnmergeCellsMenuItemClick();

            // Before the fix, only E1:F1 (the active area) was unmerged; B1:C1 silently stayed merged.
            harness.Sheet.MergedRegions.Should().NotContain(areaB, "B1:C1's disjoint area must also be unmerged");
            harness.Sheet.MergedRegions.Should().NotContain(areaE, "E1:F1 (the active area) must be unmerged");
        });
    }

    // No-regression sibling: a plain SINGLE active-range Merge Cells (the overwhelmingly common
    // case -- no Ctrl+click involved) must keep merging exactly that one range, unaffected by
    // routing the command construction through the ranges-aware plumbing.
    [Fact]
    public void MergeCellsMenuItem_Click_SingleActiveRange_StillMergesOnlyThatRange()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MultiAreaMergeHarness.Create();

            var range = harness.Range(3, 2, 3, 3); // B3:C3
            harness.SetSingleAreaSelection(range);

            harness.MergeCellsMenuItemClick();

            harness.Sheet.MergedRegions.Should().ContainSingle();
            harness.Sheet.MergedRegions.Should().Contain(range);
        });
    }

    private sealed class MultiAreaMergeHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly Action<object, RoutedEventArgs> _mergeCellsMenuItemClick;
        private readonly Action<object, RoutedEventArgs> _mergeCenterBtnClick;
        private readonly Action<object, RoutedEventArgs> _mergeAcrossMenuItemClick;
        private readonly Action<object, RoutedEventArgs> _unmergeCellsMenuItemClick;

        private MultiAreaMergeHarness(MainWindow window, Workbook workbook, Sheet sheet)
        {
            _window = window;
            Workbook = workbook;
            Sheet = sheet;

            _mergeCellsMenuItemClick = BindVoidMethod<object, RoutedEventArgs>("MergeCellsMenuItem_Click");
            _mergeCenterBtnClick = BindVoidMethod<object, RoutedEventArgs>("MergeCenterBtn_Click");
            _mergeAcrossMenuItemClick = BindVoidMethod<object, RoutedEventArgs>("MergeAcrossMenuItem_Click");
            _unmergeCellsMenuItemClick = BindVoidMethod<object, RoutedEventArgs>("UnmergeCellsMenuItem_Click");
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

        public void SetSingleAreaSelection(GridRange range)
        {
            _window.SheetGrid.SelectedRanges = null;
            _window.SheetGrid.SelectedRange = range;
        }

        // Mirrors the SheetGrid dependency-property state a real Ctrl+click leaves behind when it
        // builds a multi-area cell selection: SelectedRanges holds every disjoint area,
        // SelectedRange is only the last-clicked (active) one. Matches R127_MultiAreaFillCellsTests'
        // own SetMultiAreaSelection helper.
        public void SetMultiAreaSelection(GridRange active, IReadOnlyList<GridRange> all)
        {
            _window.SheetGrid.SelectedRanges = all;
            _window.SheetGrid.SelectedRange = active;
        }

        public void MergeCellsMenuItemClick() => _mergeCellsMenuItemClick(_window, new RoutedEventArgs());
        public void MergeCenterBtnClick() => _mergeCenterBtnClick(_window, new RoutedEventArgs());
        public void MergeAcrossMenuItemClick() => _mergeAcrossMenuItemClick(_window, new RoutedEventArgs());
        public void UnmergeCellsMenuItemClick() => _unmergeCellsMenuItemClick(_window, new RoutedEventArgs());

        public static MultiAreaMergeHarness Create()
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
            return new MultiAreaMergeHarness(window, workbookRef.Current, sheet);
        }

        public void Dispose()
        {
            foreach (Window ownedWindow in _window.OwnedWindows.Cast<Window>().ToList())
                ownedWindow.Close();
            MainWindowTestCleanup.CloseWithoutSavePrompt(_window);
        }
    }
}

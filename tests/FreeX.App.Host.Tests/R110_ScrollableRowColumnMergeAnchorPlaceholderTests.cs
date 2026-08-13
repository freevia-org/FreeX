using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;
using SheetGridView = FreeX.App.UI.GridView;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression tests for round-110 finding (src/FreeX.App.Host/MainWindow.Viewport.cs:974-996,
/// consumed by MainWindow.Selection.cs Page Up/Down/Left/Right and by
/// MainWindow.Viewport.cs's VerticalScroll/HorizontalScroll.ViewportSize/LargeChange):
///
/// When the viewport has scrolled so a merge's anchor row/column sits above/left of the visible
/// window but the merge's remainder is still on-screen, FreeX.Core.Calc's
/// PrependScrolledPastMergeAnchorRows/Cols materializes zero-height/zero-width placeholder
/// RowMetric/ColMetric entries for every row/column between the anchor and the window's first
/// visible row/column, purely so the merge's still-visible remainder keeps drawing. These
/// placeholders are not real on-screen rows/columns. MainWindow's private CountScrollableRows/
/// CountScrollableColumns counted them anyway (any entry with Row/Col past the frozen boundary),
/// inflating both the Page Up/Down/Left/Right jump distance and the scrollbar
/// ViewportSize/LargeChange by one row/column per placeholder -- unlike real Excel, whose Page
/// Down always jumps by exactly one screenful of genuinely on-screen rows and whose scrollbar
/// thumb size always reflects the actual on-screen row/column count.
/// </summary>
public sealed class R110_ScrollableRowColumnMergeAnchorPlaceholderTests
{
    [Fact]
    public void PageDown_ViewportScrolledPastTallMergeAnchor_AdvancesByGenuineOnScreenRowCountOnly()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = ScrollableCountHarness.Create();

            // E31:E60 merge is tall enough that scrolling the window's top row to 40 still leaves
            // the merge's remainder visible (anchor row 31 < window start 40 <= merge end row 60),
            // which is exactly the condition PrependScrolledPastMergeAnchorRows requires to
            // materialize zero-height placeholder rows 31..39 ahead of the real window.
            // PrependScrolledPastMergeAnchorRows scans every merge sheet-wide regardless of its
            // column, so this still produces the placeholder rows in RowMetrics -- but the merge
            // is deliberately in column 5, not column 1 (where the test navigates), so the PageDown
            // landing cell in column 1 can never accidentally snap into this merge's own selection
            // region and confound the row-number assertion below.
            harness.Sheet.AddMergedRegion(new GridRange(
                new CellAddress(harness.SheetId, 31, 5),
                new CellAddress(harness.SheetId, 60, 5)));

            harness.SelectActiveCell(5, 1);
            harness.ScrollVerticalTo(40);
            harness.RefreshViewport();

            var viewport = harness.Viewport;
            var placeholderRows = viewport.RowMetrics.Where(r => r.Height <= 0).ToList();
            placeholderRows.Should().NotBeEmpty(
                "the merge scrolled-past-anchor placeholder rows must actually be present for this test to distinguish the bug from the fix");

            var genuineScrollableRowCount = viewport.RowMetrics.Count(r => r.Row > 0 && r.Height > 0);
            var buggyScrollableRowCount = viewport.RowMetrics.Count(r => r.Row > 0);
            buggyScrollableRowCount.Should().BeGreaterThan(genuineScrollableRowCount,
                "sanity check: the naive (buggy) count must actually be inflated by the placeholder rows");

            harness.SelectActiveCell(5, 1);
            harness.PressKey(Key.PageDown);

            var correctPageSize = Math.Max(1, genuineScrollableRowCount - 1);
            var buggyPageSize = Math.Max(1, buggyScrollableRowCount - 1);
            buggyPageSize.Should().NotBe(correctPageSize);

            var expectedRow = (uint)Math.Min(CellAddress.MaxRow, 5 + correctPageSize);
            harness.ActiveCellAddress.Row.Should().Be(expectedRow);
        });
    }

    // Alt+PageDown/Alt+PageUp (horizontal paging) is driven by MainWindow.Selection.cs reading the
    // REAL physical `Keyboard.Modifiers` (not the simulated KeyEventArgs), which a headless xunit
    // test has no supported way to fake -- so this drops to the nearest seam: it invokes the exact
    // production CountScrollableColumns method (MainWindow.Viewport.cs, the same one
    // MainWindow.Selection.cs's Alt+PageDown/PageUp handler calls to compute colPageSize) via
    // reflection on a REAL post-UpdateViewport ViewportModel, rather than reimplementing the count.
    [Fact]
    public void CountScrollableColumns_ViewportScrolledPastWideMergeAnchor_ExcludesZeroWidthPlaceholders()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = ScrollableCountHarness.Create();

            // Column counterpart: row 1 merge spans columns 3..20, scroll the window's left column
            // to 10 so the anchor (col 3) has scrolled off but the merge's remainder (through
            // col 20) is still visible.
            harness.Sheet.AddMergedRegion(new GridRange(
                new CellAddress(harness.SheetId, 1, 3),
                new CellAddress(harness.SheetId, 1, 20)));

            harness.ScrollHorizontalTo(10);
            harness.RefreshViewport();

            var viewport = harness.Viewport;
            var placeholderCols = viewport.ColMetrics.Where(c => c.Width <= 0).ToList();
            placeholderCols.Should().NotBeEmpty(
                "the merge scrolled-past-anchor placeholder columns must actually be present for this test to distinguish the bug from the fix");

            var genuineScrollableColCount = viewport.ColMetrics.Count(c => c.Col > 0 && c.Width > 0);
            var buggyScrollableColCount = viewport.ColMetrics.Count(c => c.Col > 0);
            buggyScrollableColCount.Should().BeGreaterThan(genuineScrollableColCount,
                "sanity check: the naive (buggy) count must actually be inflated by the placeholder columns");

            var productionResult = harness.InvokeCountScrollableColumns(viewport, frozenCols: 0);

            productionResult.Should().Be(genuineScrollableColCount);
            productionResult.Should().NotBe(buggyScrollableColCount);
        });
    }

    [Fact]
    public void ScrollbarViewportSize_ViewportScrolledPastTallMergeAnchor_ReflectsGenuineOnScreenRowCountOnly()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = ScrollableCountHarness.Create();

            harness.Sheet.AddMergedRegion(new GridRange(
                new CellAddress(harness.SheetId, 31, 5),
                new CellAddress(harness.SheetId, 60, 5)));

            harness.ScrollVerticalTo(40);
            harness.RefreshViewport();

            var viewport = harness.Viewport;
            var genuineScrollableRowCount = viewport.RowMetrics.Count(r => r.Row > 0 && r.Height > 0);
            var buggyScrollableRowCount = viewport.RowMetrics.Count(r => r.Row > 0);
            buggyScrollableRowCount.Should().BeGreaterThan(genuineScrollableRowCount);

            harness.VerticalScrollViewportSize.Should().Be(genuineScrollableRowCount);
            harness.VerticalScrollLargeChange.Should().Be(Math.Max(1, genuineScrollableRowCount));
        });
    }

    [Fact]
    public void PageDown_WithNoMergesInView_StillAdvancesByFullScrollableRowCount()
    {
        // Sibling already-working case: with no merge whose anchor has scrolled off-window there
        // are no placeholder rows at all, so the naive and genuine counts coincide -- the fix must
        // not change this ordinary, already-correct behavior.
        StaTestRunner.Run(() =>
        {
            using var harness = ScrollableCountHarness.Create();
            harness.SelectActiveCell(5, 1);
            harness.RefreshViewport();

            var viewport = harness.Viewport;
            var genuineScrollableRowCount = viewport.RowMetrics.Count(r => r.Row > 0 && r.Height > 0);
            genuineScrollableRowCount.Should().Be(viewport.RowMetrics.Count,
                "with no scrolled-past merge anchor, every RowMetric should already have nonzero height");

            harness.PressKey(Key.PageDown);

            var expectedPageSize = Math.Max(1, genuineScrollableRowCount - 1);
            harness.ActiveCellAddress.Row.Should().Be((uint)Math.Min(CellAddress.MaxRow, 5 + expectedPageSize));
        });
    }

    private sealed class ScrollableCountHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly MethodInfo _setActiveCell;
        private readonly MethodInfo _mainWindowKeyDown;
        private readonly MethodInfo _updateViewport;
        private readonly FieldInfo _selectionAnchorField;
        private readonly MethodInfo _countScrollableColumns;

        private ScrollableCountHarness(MainWindow window)
        {
            _window = window;
            _setActiveCell = typeof(MainWindow)
                .GetMethod("SetActiveCell", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "SetActiveCell");
            _mainWindowKeyDown = typeof(MainWindow)
                .GetMethod("MainWindow_KeyDown", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "MainWindow_KeyDown");
            _updateViewport = typeof(MainWindow)
                .GetMethod("UpdateViewport", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "UpdateViewport");
            _selectionAnchorField = typeof(MainWindow)
                .GetField("_selectionAnchorField", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_selectionAnchorField");
            _countScrollableColumns = typeof(MainWindow)
                .GetMethod("CountScrollableColumns", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new MissingMethodException(nameof(MainWindow), "CountScrollableColumns");
        }

        // MainWindow_Loaded unconditionally calls CreateNewWorkbook() (unless adopting a shared
        // document via a WorkbookWindowRegistry, which this harness doesn't provide), replacing
        // whatever workbook was passed into the constructor. So the live workbook/sheet must be
        // read fresh from the session AFTER Show()/Loaded has run, never captured beforehand.
        private Workbook LiveWorkbook => _window.Session.Workbook;

        public Sheet Sheet => LiveWorkbook.Sheets[0];
        public SheetId SheetId => Sheet.Id;

        private SheetGridView Grid => (SheetGridView)_window.FindName("SheetGrid");

        public CellAddress ActiveCellAddress =>
            (CellAddress)(_selectionAnchorField.GetValue(_window)
                ?? throw new InvalidOperationException("No active cell is set."));

        public ViewportModel Viewport => Grid.Viewport
            ?? throw new InvalidOperationException("Viewport has not been computed yet.");

        public double VerticalScrollViewportSize => _window.VerticalScroll.ViewportSize;
        public double VerticalScrollLargeChange => _window.VerticalScroll.LargeChange;

        public void SelectActiveCell(uint row, uint col)
        {
            _setActiveCell.Invoke(_window, [new CellAddress(SheetId, row, col)]);
            PumpDispatcher();
        }

        public void ScrollVerticalTo(double value)
        {
            _window.VerticalScroll.Value = value;
            PumpDispatcher();
        }

        public void ScrollHorizontalTo(double value)
        {
            _window.HorizontalScroll.Value = value;
            PumpDispatcher();
        }

        public void RefreshViewport()
        {
            _updateViewport.Invoke(_window, []);
            PumpDispatcher();
        }

        public int InvokeCountScrollableColumns(ViewportModel viewport, uint frozenCols) =>
            (int)(_countScrollableColumns.Invoke(null, [viewport, frozenCols])
                ?? throw new InvalidOperationException("CountScrollableColumns returned null."));

        public void PressKey(Key key)
        {
            var source = PresentationSource.FromVisual(_window)
                ?? throw new InvalidOperationException("MainWindow presentation source is not available.");
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
            {
                RoutedEvent = Keyboard.KeyDownEvent
            };
            _mainWindowKeyDown.Invoke(_window, [_window, args]);
            PumpDispatcher();
        }

        public static ScrollableCountHarness Create()
        {
            var workbook = new Workbook("Book1");
            workbook.AddSheet("Sheet1");

            var workbookRef = new WorkbookRef { Current = workbook };
            var graph = new DependencyGraph();
            var evaluator = new FormulaEvaluator();
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(graph, evaluator),
                Array.Empty<FreeX.Core.IO.IFileAdapter>(),
                workbookRef,
                workbook,
                NullUserMessageService.Instance)
            {
                WindowState = WindowState.Normal,
                Width = 1280,
                Height = 720
            };

            window.Show();
            window.Activate();
            window.UpdateLayout();
            PumpDispatcher();

            // MainWindow_Loaded (fired by Show/PumpDispatcher above) replaces the constructor's
            // workbook with a brand new one via CreateNewWorkbook() -- so populate cells on the
            // LIVE post-Loaded sheet, not the one passed into the constructor.
            var harness = new ScrollableCountHarness(window);
            var liveSheet = harness.Sheet;
            for (uint row = 1; row <= 200; row++)
            {
                for (uint col = 1; col <= 40; col++)
                    liveSheet.SetCell(new CellAddress(liveSheet.Id, row, col), new NumberValue(row * 100 + col));
            }

            return harness;
        }

        public void Dispose()
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(_window);
            PumpDispatcher();
        }
    }

    private static void PumpDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
}

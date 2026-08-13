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
/// Regression tests for round-31 findings R31-viewport-selection-logic-deep-1/2
/// (src/FreeX.App.Host/MainWindow.Selection.cs):
///   - deep-1: PageUp/PageDown must derive the page-jump delta from the SCROLLABLE row/column
///     count only (excluding frozen rows/columns), not the combined frozen+body RowMetrics/
///     ColMetrics count -- otherwise the jump overshoots by exactly the frozen-row/column count.
///   - deep-2: Enter/Tab with a multi-cell range already selected must move the active cell
///     WITHIN the range (wrapping at its edges) and keep the whole range highlighted, instead of
///     collapsing the selection down to a single cell.
/// </summary>
public sealed class R31_ViewportSelectionLogicTests
{
    [Fact]
    public void PageDown_WithFrozenRows_AdvancesByScrollableRowCountOnly_NotCombinedRowMetricsCount()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = ViewportSelectionHarness.Create();
            harness.SetFreezePanes(2, 0);
            harness.SelectActiveCell(5, 1);
            harness.RefreshViewport();

            var viewport = harness.Viewport;
            var totalRowMetricsCount = viewport.RowMetrics.Count;
            var scrollableRowCount = viewport.RowMetrics.Count(r => r.Row > harness.FrozenRows);

            // Sanity check: the combined RowMetrics list really does include the frozen rows
            // alongside the scrollable body rows, otherwise this test can't distinguish the bug
            // from the fix.
            scrollableRowCount.Should().BeLessThan(totalRowMetricsCount);

            harness.PressKey(Key.PageDown);

            var correctPageSize = Math.Max(1, scrollableRowCount - 1);
            var buggyPageSize = Math.Max(1, totalRowMetricsCount - 1);
            var expectedRow = (uint)Math.Min(1_048_576, 5 + correctPageSize);
            var buggyRow = (uint)Math.Min(1_048_576, 5 + buggyPageSize);

            harness.ActiveCellAddress.Row.Should().Be(expectedRow);
            buggyPageSize.Should().NotBe(correctPageSize);
            harness.ActiveCellAddress.Row.Should().NotBe(buggyRow);
        });
    }

    [Fact]
    public void PageDown_WithNoFrozenRows_StillAdvancesByFullScrollableRowCount()
    {
        // Sibling already-working case: with no frozen rows the scrollable count equals the
        // combined RowMetrics count, so the fix must not change this ordinary behavior.
        StaTestRunner.Run(() =>
        {
            using var harness = ViewportSelectionHarness.Create();
            harness.SetFreezePanes(0, 0);
            harness.SelectActiveCell(5, 1);
            harness.RefreshViewport();

            var viewport = harness.Viewport;
            var scrollableRowCount = viewport.RowMetrics.Count(r => r.Row > harness.FrozenRows);
            scrollableRowCount.Should().Be(viewport.RowMetrics.Count);

            harness.PressKey(Key.PageDown);

            var expectedPageSize = Math.Max(1, scrollableRowCount - 1);
            harness.ActiveCellAddress.Row.Should().Be((uint)Math.Min(1_048_576, 5 + expectedPageSize));
        });
    }

    [Fact]
    public void Tab_WithMultiCellRangeSelected_MovesActiveCellWithinRangeAndKeepsSelectionHighlighted()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = ViewportSelectionHarness.Create();
            var sheetId = harness.SheetId;
            var range = new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 3, 3));
            harness.SelectRangeWithAnchor(range, new CellAddress(sheetId, 1, 1));

            harness.PressKey(Key.Tab);
            harness.SelectedRange.Should().Be(range);
            harness.ActiveCellAddress.Should().Be(new CellAddress(sheetId, 1, 2));

            harness.PressKey(Key.Tab);
            harness.SelectedRange.Should().Be(range);
            harness.ActiveCellAddress.Should().Be(new CellAddress(sheetId, 1, 3));

            // Wraps from the top-right corner (C1) to the start of the next row (A2), keeping the
            // whole A1:C3 marquee highlighted the entire time -- Excel's documented behavior.
            harness.PressKey(Key.Tab);
            harness.SelectedRange.Should().Be(range);
            harness.ActiveCellAddress.Should().Be(new CellAddress(sheetId, 2, 1));
        });
    }

    [Fact]
    public void Enter_WithMultiCellRangeSelected_MovesActiveCellWithinRangeAndKeepsSelectionHighlighted()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = ViewportSelectionHarness.Create();
            var sheetId = harness.SheetId;
            var range = new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 3, 3));
            harness.SelectRangeWithAnchor(range, new CellAddress(sheetId, 1, 1));

            harness.PressKey(Key.Enter);
            harness.SelectedRange.Should().Be(range);
            harness.ActiveCellAddress.Should().Be(new CellAddress(sheetId, 2, 1));

            harness.PressKey(Key.Enter);
            harness.SelectedRange.Should().Be(range);
            harness.ActiveCellAddress.Should().Be(new CellAddress(sheetId, 3, 1));

            // Wraps from the bottom-left corner (A3) to the top of the next column (B1).
            harness.PressKey(Key.Enter);
            harness.SelectedRange.Should().Be(range);
            harness.ActiveCellAddress.Should().Be(new CellAddress(sheetId, 1, 2));
        });
    }

    [Fact]
    public void Tab_WithSingleCellSelected_StillMovesToNextCellAndCollapsesSelectionAsBefore()
    {
        // Sibling already-working case: single-cell Enter/Tab behavior must be unchanged by the
        // multi-cell-range fix.
        StaTestRunner.Run(() =>
        {
            using var harness = ViewportSelectionHarness.Create();
            var sheetId = harness.SheetId;
            harness.SelectActiveCell(2, 2);

            harness.PressKey(Key.Tab);

            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(sheetId, 2, 3),
                new CellAddress(sheetId, 2, 3)));
            harness.ActiveCellAddress.Should().Be(new CellAddress(sheetId, 2, 3));
        });
    }

    private sealed class ViewportSelectionHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly MethodInfo _setActiveCell;
        private readonly MethodInfo _mainWindowKeyDown;
        private readonly MethodInfo _updateViewport;
        private readonly MethodInfo _setFreezePanes;
        private readonly FieldInfo _selectionAnchorField;

        private ViewportSelectionHarness(MainWindow window)
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
            _setFreezePanes = typeof(MainWindow)
                .GetMethod("SetFreezePanes", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "SetFreezePanes");
            _selectionAnchorField = typeof(MainWindow)
                .GetField("_selectionAnchorField", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_selectionAnchorField");
        }

        // MainWindow_Loaded unconditionally calls CreateNewWorkbook() (unless adopting a shared
        // document via a WorkbookWindowRegistry, which this harness doesn't provide), replacing
        // whatever workbook was passed into the constructor. So the live workbook/sheet must be
        // read fresh from the session AFTER Show()/Loaded has run, never captured beforehand.
        private Workbook LiveWorkbook => _window.Session.Workbook;

        public Sheet Sheet => LiveWorkbook.Sheets[0];
        public SheetId SheetId => Sheet.Id;

        public uint FrozenRows => _window.Session.GetEffectiveFrozenRows();

        private SheetGridView Grid => (SheetGridView)_window.FindName("SheetGrid");

        public GridRange? SelectedRange => Grid.SelectedRange;

        public CellAddress ActiveCellAddress =>
            (CellAddress)(_selectionAnchorField.GetValue(_window)
                ?? throw new InvalidOperationException("No active cell is set."));

        public ViewportModel Viewport => Grid.Viewport
            ?? throw new InvalidOperationException("Viewport has not been computed yet.");

        public void SelectActiveCell(uint row, uint col)
        {
            _setActiveCell.Invoke(_window, [new CellAddress(SheetId, row, col)]);
            PumpDispatcher();
        }

        public void SelectRangeWithAnchor(GridRange range, CellAddress anchor)
        {
            Grid.SelectedRanges = null;
            Grid.SelectedRange = range;
            _selectionAnchorField.SetValue(_window, anchor);
            PumpDispatcher();
        }

        public void RefreshViewport()
        {
            _updateViewport.Invoke(_window, []);
            PumpDispatcher();
        }

        public void SetFreezePanes(uint frozenRows, uint frozenColumns)
        {
            _setFreezePanes.Invoke(_window, [frozenRows, frozenColumns]);
            PumpDispatcher();
        }

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

        public static ViewportSelectionHarness Create()
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
            var harness = new ViewportSelectionHarness(window);
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

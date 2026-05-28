using System.Reflection;
using System.Windows;
using FluentAssertions;
using Freexcel.Core.Calc;
using Freexcel.Core.Commands;
using Freexcel.Core.Formula;
using Freexcel.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace Freexcel.App.Host.Tests;

public sealed class MainWindowMouseResizeTests
{
    [Fact]
    public void DoubleClickColumnResizeBorder_AutoFitsColumn()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.SetCell(2, 3, "a much longer display value for autofit");

            harness.AutoFitColumn(3);

            harness.CurrentSheet.ColumnWidths[3]
                .Should()
                .BeGreaterThan(harness.CurrentSheet.DefaultColumnWidth);
        });
    }

    [Fact]
    public void DoubleClickRowResizeBorder_AutoFitsRow()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.SetCell(4, 2, "first\nsecond\nthird");

            harness.AutoFitRow(4);

            harness.CurrentSheet.RowHeights[4]
                .Should()
                .BeGreaterThan(harness.CurrentSheet.DefaultRowHeight);
        });
    }

    [Fact]
    public void DragColumnResize_PreviewsWithoutRefreshingViewportOrMutatingSheetUntilCommit()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.CurrentSheet.ColumnWidths[3] = 10;
            var initialViewport = harness.SheetGrid.Viewport;

            harness.ResetViewportCallCount();
            harness.PreviewColumnResize(3, 128);
            harness.PreviewColumnResize(3, 144);

            harness.ViewportCallCount.Should().Be(0);
            harness.SheetGrid.Viewport.Should().BeSameAs(initialViewport);
            harness.CurrentSheet.ColumnWidths[3].Should().Be(10);

            harness.CommitColumnResize(3, 144);

            harness.ViewportCallCount.Should().BeGreaterThan(0);
            harness.CurrentSheet.ColumnWidths[3].Should().BeApproximately(18, 0.0001);
        });
    }

    [Fact]
    public void DragRowResize_PreviewsWithoutRefreshingViewportOrMutatingSheetUntilCommit()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.CurrentSheet.RowHeights[4] = 20;
            var initialViewport = harness.SheetGrid.Viewport;

            harness.ResetViewportCallCount();
            harness.PreviewRowResize(4, 34);
            harness.PreviewRowResize(4, 42);

            harness.ViewportCallCount.Should().Be(0);
            harness.SheetGrid.Viewport.Should().BeSameAs(initialViewport);
            harness.CurrentSheet.RowHeights[4].Should().Be(20);

            harness.CommitRowResize(4, 42);

            harness.ViewportCallCount.Should().BeGreaterThan(0);
            harness.CurrentSheet.RowHeights[4].Should().BeApproximately(42, 0.0001);
        });
    }

    [Fact]
    public void DragColumnResize_UsesPreviewSelectionRangeAtCommit()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.SelectRange(1, 2, 1, 4);

            harness.PreviewColumnResize(3, 160);
            harness.SelectRange(1, 6, 1, 6);
            harness.CommitColumnResize(3, 160);

            harness.CurrentSheet.ColumnWidths[2].Should().BeApproximately(20, 0.0001);
            harness.CurrentSheet.ColumnWidths[3].Should().BeApproximately(20, 0.0001);
            harness.CurrentSheet.ColumnWidths[4].Should().BeApproximately(20, 0.0001);
            harness.CurrentSheet.ColumnWidths.ContainsKey(6).Should().BeFalse();
        });
    }

    [Fact]
    public void DragRowResize_UsesPreviewSelectionRangeAtCommit()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.SelectRange(2, 1, 4, 1);

            harness.PreviewRowResize(3, 36);
            harness.SelectRange(6, 1, 6, 1);
            harness.CommitRowResize(3, 36);

            harness.CurrentSheet.RowHeights[2].Should().BeApproximately(36, 0.0001);
            harness.CurrentSheet.RowHeights[3].Should().BeApproximately(36, 0.0001);
            harness.CurrentSheet.RowHeights[4].Should().BeApproximately(36, 0.0001);
            harness.CurrentSheet.RowHeights.ContainsKey(6).Should().BeFalse();
        });
    }

    private sealed class MainWindowHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly CountingViewportService _viewportService;
        private readonly MethodInfo _onColumnResizing;
        private readonly MethodInfo _onColumnResized;
        private readonly MethodInfo _onColumnAutoFitRequested;
        private readonly MethodInfo _onRowResizing;
        private readonly MethodInfo _onRowResized;
        private readonly MethodInfo _onRowAutoFitRequested;
        private readonly FieldInfo _workbookField;
        private readonly FieldInfo _currentSheetIdField;

        private MainWindowHarness(MainWindow window, CountingViewportService viewportService)
        {
            _window = window;
            _viewportService = viewportService;
            _onColumnResizing = typeof(MainWindow)
                .GetMethod("OnColumnResizing", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "OnColumnResizing");
            _onColumnResized = typeof(MainWindow)
                .GetMethod("OnColumnResized", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "OnColumnResized");
            _onColumnAutoFitRequested = typeof(MainWindow)
                .GetMethod("OnColumnAutoFitRequested", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "OnColumnAutoFitRequested");
            _onRowResizing = typeof(MainWindow)
                .GetMethod("OnRowResizing", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "OnRowResizing");
            _onRowResized = typeof(MainWindow)
                .GetMethod("OnRowResized", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "OnRowResized");
            _onRowAutoFitRequested = typeof(MainWindow)
                .GetMethod("OnRowAutoFitRequested", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "OnRowAutoFitRequested");
            _workbookField = typeof(MainWindow)
                .GetField("_workbook", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_workbook");
            _currentSheetIdField = typeof(MainWindow)
                .GetField("_currentSheetId", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_currentSheetId");
        }

        public Sheet CurrentSheet
        {
            get
            {
                var sheetId = (SheetId)_currentSheetIdField.GetValue(_window)!;
                return CurrentWorkbook.GetSheet(sheetId) ?? throw new InvalidOperationException("Current sheet was not found.");
            }
        }

        public Freexcel.App.UI.GridView SheetGrid =>
            (Freexcel.App.UI.GridView)_window.FindName("SheetGrid");

        public int ViewportCallCount => _viewportService.GetViewportCallCount;

        public void ResetViewportCallCount() => _viewportService.Reset();

        public void SetCell(uint row, uint col, string text)
        {
            CurrentSheet.SetCell(new CellAddress(CurrentSheet.Id, row, col), new TextValue(text));
            PumpDispatcher();
        }

        public void SelectRange(uint startRow, uint startCol, uint endRow, uint endCol)
        {
            SheetGrid.SelectedRange = new GridRange(
                new CellAddress(CurrentSheet.Id, startRow, startCol),
                new CellAddress(CurrentSheet.Id, endRow, endCol));
        }

        public void PreviewColumnResize(uint col, double width)
        {
            _onColumnResizing.Invoke(_window, [col, width]);
            PumpDispatcher();
        }

        public void CommitColumnResize(uint col, double width)
        {
            _onColumnResized.Invoke(_window, [col, width]);
            PumpDispatcher();
        }

        public void AutoFitColumn(uint col)
        {
            _onColumnAutoFitRequested.Invoke(_window, [col]);
            PumpDispatcher();
        }

        public void PreviewRowResize(uint row, double height)
        {
            _onRowResizing.Invoke(_window, [row, height]);
            PumpDispatcher();
        }

        public void CommitRowResize(uint row, double height)
        {
            _onRowResized.Invoke(_window, [row, height]);
            PumpDispatcher();
        }

        public void AutoFitRow(uint row)
        {
            _onRowAutoFitRequested.Invoke(_window, [row]);
            PumpDispatcher();
        }

        public static MainWindowHarness Create()
        {
            var workbook = new Workbook("Book1");
            workbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = workbook };
            var graph = new DependencyGraph();
            var evaluator = new FormulaEvaluator();
            var viewportService = new CountingViewportService(new ViewportService());
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                viewportService,
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(graph, evaluator),
                [],
                workbookRef,
                workbook)
            {
                WindowState = WindowState.Normal,
                Width = 1280,
                Height = 720
            };

            window.Show();
            window.UpdateLayout();
            PumpDispatcher();
            viewportService.Reset();
            return new MainWindowHarness(window, viewportService);
        }

        private Workbook CurrentWorkbook =>
            (Workbook)_workbookField.GetValue(_window)!;

        public void Dispose()
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(_window);
            PumpDispatcher();
        }
    }

    private sealed class CountingViewportService(IViewportService inner) : IViewportService
    {
        public int GetViewportCallCount { get; private set; }

        public ViewportModel GetViewport(Workbook workbook, SheetId sheetId, ViewportRequest request)
        {
            GetViewportCallCount++;
            return inner.GetViewport(workbook, sheetId, request);
        }

        public CellAddress? HitTest(Workbook workbook, SheetId sheetId, double x, double y, double zoom) =>
            inner.HitTest(workbook, sheetId, x, y, zoom);

        public void Reset() => GetViewportCallCount = 0;
    }

    private static void PumpDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new InvalidOperationException($"Sheet {sheetId} not found");
    }
}

using System.Windows;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

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
    public void DragColumnResize_PreviewRefreshesViewportAndAppliesLiveSheetWidth()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.CurrentSheet.ColumnWidths[3] = 10;
            var initialViewport = harness.SheetGrid.Viewport;

            harness.ResetViewportCallCount();
            harness.PreviewColumnResize(3, 128);
            harness.PreviewColumnResize(3, 144);

            harness.ViewportCallCount.Should().BeGreaterThan(0);
            harness.SheetGrid.Viewport.Should().NotBeSameAs(initialViewport);
            harness.CurrentSheet.ColumnWidths[3].Should().BeApproximately(ColumnWidthPixelMapper.PixelsToColumnWidth(144), 0.0001);
            harness.GetRenderedColumnWidth(3).Should().Be(144);

            harness.CommitColumnResize(3, 144);

            harness.ViewportCallCount.Should().BeGreaterThan(0);
            harness.CurrentSheet.ColumnWidths[3].Should().BeApproximately(ColumnWidthPixelMapper.PixelsToColumnWidth(144), 0.0001);
            harness.GetRenderedColumnWidth(3).Should().Be(144);
        });
    }

    [Fact]
    public void DragRowResize_PreviewRefreshesViewportAndAppliesLiveSheetHeight()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.CurrentSheet.RowHeights[4] = 20;
            var initialViewport = harness.SheetGrid.Viewport;

            harness.ResetViewportCallCount();
            harness.PreviewRowResize(4, 34);
            harness.PreviewRowResize(4, 42);

            harness.ViewportCallCount.Should().BeGreaterThan(0);
            harness.SheetGrid.Viewport.Should().NotBeSameAs(initialViewport);
            harness.CurrentSheet.RowHeights[4].Should().BeApproximately(42, 0.0001);

            harness.CommitRowResize(4, 42);

            harness.ViewportCallCount.Should().BeGreaterThan(0);
            harness.CurrentSheet.RowHeights[4].Should().BeApproximately(42, 0.0001);
        });
    }

    [Fact]
    public void DragColumnResize_UndoRestoresPrePreviewWidth()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.CurrentSheet.ColumnWidths[3] = 10;

            harness.PreviewColumnResize(3, 160);
            harness.CurrentSheet.ColumnWidths[3].Should().BeApproximately(ColumnWidthPixelMapper.PixelsToColumnWidth(160), 0.0001);

            harness.CommitColumnResize(3, 144);
            harness.CurrentSheet.ColumnWidths[3].Should().BeApproximately(ColumnWidthPixelMapper.PixelsToColumnWidth(144), 0.0001);

            harness.Undo().Should().BeTrue();
            harness.CurrentSheet.ColumnWidths[3].Should().BeApproximately(10, 0.0001);
        });
    }

    [Fact]
    public void DragRowResize_UndoRestoresPrePreviewHeight()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.CurrentSheet.RowHeights[4] = 20;

            harness.PreviewRowResize(4, 34);
            harness.CurrentSheet.RowHeights[4].Should().BeApproximately(34, 0.0001);

            harness.CommitRowResize(4, 42);
            harness.CurrentSheet.RowHeights[4].Should().BeApproximately(42, 0.0001);

            harness.Undo().Should().BeTrue();
            harness.CurrentSheet.RowHeights[4].Should().BeApproximately(20, 0.0001);
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

            var expectedWidth = ColumnWidthPixelMapper.PixelsToColumnWidth(160);
            harness.CurrentSheet.ColumnWidths[2].Should().BeApproximately(expectedWidth, 0.0001);
            harness.CurrentSheet.ColumnWidths[3].Should().BeApproximately(expectedWidth, 0.0001);
            harness.CurrentSheet.ColumnWidths[4].Should().BeApproximately(expectedWidth, 0.0001);
            harness.CurrentSheet.ColumnWidths.ContainsKey(6).Should().BeFalse();
        });
    }

    [Fact]
    public void DragColumnResize_ZeroWidthHidesSelectedColumns()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.SelectRange(1, 2, 1, 4);

            harness.PreviewColumnResize(3, 0);
            harness.CommitColumnResize(3, 0);

            harness.CurrentSheet.ColumnWidths.ContainsKey(2).Should().BeFalse();
            harness.CurrentSheet.ColumnWidths.ContainsKey(3).Should().BeFalse();
            harness.CurrentSheet.ColumnWidths.ContainsKey(4).Should().BeFalse();
            harness.CurrentSheet.HiddenCols.Should().Contain([2u, 3u, 4u]);
        });
    }

    [Fact]
    public void DragColumnCollapsedBoundary_UnhidesOnlyContiguousHiddenColumns()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.SelectRange(1, 1, 1, 5);
            harness.CurrentSheet.HiddenCols.Add(2);
            harness.CurrentSheet.HiddenCols.Add(3);
            harness.CurrentSheet.HiddenCols.Add(4);

            harness.PreviewColumnResize(2, 96);
            harness.CommitColumnResize(2, 96);

            harness.CurrentSheet.HiddenCols.Should().NotContain([2u, 3u, 4u]);
            harness.CurrentSheet.ColumnWidths.Should().ContainKeys(2u, 3u, 4u);
            var expectedWidth = ColumnWidthPixelMapper.PixelsToColumnWidth(96);
            harness.CurrentSheet.ColumnWidths[2].Should().BeApproximately(expectedWidth, 0.0001);
            harness.CurrentSheet.ColumnWidths[3].Should().BeApproximately(expectedWidth, 0.0001);
            harness.CurrentSheet.ColumnWidths[4].Should().BeApproximately(expectedWidth, 0.0001);
            harness.CurrentSheet.ColumnWidths.ContainsKey(1).Should().BeFalse();
            harness.CurrentSheet.ColumnWidths.ContainsKey(5).Should().BeFalse();
        });
    }

    [Fact]
    public void CanceledColumnResizePreview_DoesNotReuseStaleSelectionRange()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.SelectRange(1, 2, 1, 4);
            harness.PreviewColumnResize(3, 160);
            harness.CancelResizePreview();

            harness.SelectRange(1, 6, 1, 6);
            harness.CommitColumnResize(6, 120);

            harness.CurrentSheet.ColumnWidths.ContainsKey(2).Should().BeFalse();
            harness.CurrentSheet.ColumnWidths.ContainsKey(3).Should().BeFalse();
            harness.CurrentSheet.ColumnWidths.ContainsKey(4).Should().BeFalse();
            harness.CurrentSheet.ColumnWidths[6].Should().BeApproximately(ColumnWidthPixelMapper.PixelsToColumnWidth(120), 0.0001);
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

    [Fact]
    public void DragRowResize_ZeroHeightHidesSelectedRows()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.SelectRange(2, 1, 4, 1);

            harness.PreviewRowResize(3, 0);
            harness.CommitRowResize(3, 0);

            harness.CurrentSheet.RowHeights.ContainsKey(2).Should().BeFalse();
            harness.CurrentSheet.RowHeights.ContainsKey(3).Should().BeFalse();
            harness.CurrentSheet.RowHeights.ContainsKey(4).Should().BeFalse();
            harness.CurrentSheet.HiddenRows.Should().Contain([2u, 3u, 4u]);
        });
    }

    [Fact]
    public void DragRowCollapsedBoundary_UnhidesOnlyContiguousHiddenRows()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.SelectRange(1, 1, 5, 1);
            harness.CurrentSheet.HiddenRows.Add(2);
            harness.CurrentSheet.HiddenRows.Add(3);
            harness.CurrentSheet.HiddenRows.Add(4);

            harness.PreviewRowResize(2, 28);
            harness.CommitRowResize(2, 28);

            harness.CurrentSheet.HiddenRows.Should().NotContain([2u, 3u, 4u]);
            harness.CurrentSheet.RowHeights.Should().ContainKeys(2u, 3u, 4u);
            harness.CurrentSheet.RowHeights[2].Should().BeApproximately(28, 0.0001);
            harness.CurrentSheet.RowHeights[3].Should().BeApproximately(28, 0.0001);
            harness.CurrentSheet.RowHeights[4].Should().BeApproximately(28, 0.0001);
            harness.CurrentSheet.RowHeights.ContainsKey(1).Should().BeFalse();
            harness.CurrentSheet.RowHeights.ContainsKey(5).Should().BeFalse();
        });
    }

    [Fact]
    public void CanceledRowResizePreview_DoesNotReuseStaleSelectionRange()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.SelectRange(2, 1, 4, 1);
            harness.PreviewRowResize(3, 36);
            harness.CancelResizePreview();

            harness.SelectRange(6, 1, 6, 1);
            harness.CommitRowResize(6, 28);

            harness.CurrentSheet.RowHeights.ContainsKey(2).Should().BeFalse();
            harness.CurrentSheet.RowHeights.ContainsKey(3).Should().BeFalse();
            harness.CurrentSheet.RowHeights.ContainsKey(4).Should().BeFalse();
            harness.CurrentSheet.RowHeights[6].Should().BeApproximately(28, 0.0001);
        });
    }

    private sealed class MainWindowHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly CountingViewportService _viewportService;

        private MainWindowHarness(MainWindow window, CountingViewportService viewportService)
        {
            _window = window;
            _viewportService = viewportService;
        }

        public Sheet CurrentSheet
        {
            get
            {
                var sheetId = _window.CurrentSheetIdForTest;
                return CurrentWorkbook.GetSheet(sheetId) ?? throw new InvalidOperationException("Current sheet was not found.");
            }
        }

        public FreeX.App.UI.GridView SheetGrid =>
            (FreeX.App.UI.GridView)_window.FindName("SheetGrid");

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
            _window.PreviewColumnResizeForTest(col, width);
            PumpDispatcher();
        }

        public void CommitColumnResize(uint col, double width)
        {
            _window.CommitColumnResizeForTest(col, width);
            PumpDispatcher();
        }

        public double GetRenderedColumnWidth(uint col)
        {
            var metric = SheetGrid.Viewport?.ColMetrics.FirstOrDefault(metric => metric.Col == col);
            return metric?.Width
                   ?? throw new InvalidOperationException($"Column {col} is not in the current viewport.");
        }

        public void CancelResizePreview()
        {
            _window.CancelResizePreviewForTest();
            PumpDispatcher();
        }

        public void AutoFitColumn(uint col)
        {
            _window.AutoFitColumnForTest(col);
            PumpDispatcher();
        }

        public void PreviewRowResize(uint row, double height)
        {
            _window.PreviewRowResizeForTest(row, height);
            PumpDispatcher();
        }

        public void CommitRowResize(uint row, double height)
        {
            _window.CommitRowResizeForTest(row, height);
            PumpDispatcher();
        }

        public void AutoFitRow(uint row)
        {
            _window.AutoFitRowForTest(row);
            PumpDispatcher();
        }

        public bool Undo()
        {
            var result = _window.ExecuteUndoForTest();
            PumpDispatcher();
            return result;
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
                workbook,
                NullUserMessageService.Instance)
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

        private Workbook CurrentWorkbook => _window.Session.Workbook;

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

        public (uint LastVisibleRow, IReadOnlyList<OutlineGroupRange> RowOutlineGroups)
            ComputeRowMetricsSummary(Workbook workbook, SheetId sheetId, ViewportRequest request) =>
            inner.ComputeRowMetricsSummary(workbook, sheetId, request);

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
}

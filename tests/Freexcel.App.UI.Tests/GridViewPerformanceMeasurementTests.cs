using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Freexcel.App.UI;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.App.UI.Tests;

public sealed class GridViewPerformanceMeasurementTests
{
    [Fact]
    public void Benchmark_RenderTextHeavyViewport_ReportsTiming()
    {
        StaTestRunner.Run(() =>
        {
            const int iterations = 12;
            const int width = 1440;
            const int height = 900;
            var grid = CreateTextHeavyGrid(width, height);

            RenderOnce(grid, width, height);
            RenderOnce(grid, width, height);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var timings = new List<double>(iterations);
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var total = Stopwatch.StartNew();
            for (var i = 0; i < iterations; i++)
            {
                var step = Stopwatch.StartNew();
                RenderOnce(grid, width, height);
                step.Stop();
                timings.Add(step.Elapsed.TotalMilliseconds);
            }

            total.Stop();
            var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            var ordered = timings.OrderBy(value => value).ToArray();
            var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

            Console.WriteLine(
                "PERF GRID_RENDER_TEXT_HEAVY " +
                $"steps={iterations} total_ms={total.Elapsed.TotalMilliseconds:F2} " +
                $"mean_ms={timings.Average():F2} p95_ms={p95:F2} max_ms={ordered[^1]:F2} " +
                $"allocated_bytes={allocatedBytes:N0}");

            timings.Average().Should().BeGreaterThan(0);
        });
    }

    private static GridView CreateTextHeavyGrid(double width, double height)
    {
        const int rowCount = 80;
        const int columnCount = 26;
        const double rowHeight = 20;
        const double columnWidth = 64;

        var sheetId = SheetId.New();
        var rows = Enumerable
            .Range(0, rowCount)
            .Select(index => new RowMetric((uint)(index + 1), rowHeight, index * rowHeight))
            .ToArray();
        var columns = Enumerable
            .Range(0, columnCount)
            .Select(index => new ColMetric((uint)(index + 1), columnWidth, index * columnWidth))
            .ToArray();
        var cells = new List<DisplayCell>(rowCount * columnCount);
        foreach (var row in rows)
        {
            foreach (var column in columns)
            {
                var text = $"R{row.Row}C{column.Col}";
                cells.Add(new DisplayCell(
                    row.Row,
                    column.Col,
                    new TextValue(text),
                    text,
                    null,
                    StyleId.Default,
                    null,
                    null));
            }
        }

        var grid = new GridView
        {
            Width = width,
            Height = height,
            Viewport = new ViewportModel(cells, rows, columns),
            SelectedRange = new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 1, 1))
        };
        grid.Measure(new Size(width, height));
        grid.Arrange(new Rect(0, 0, width, height));
        grid.UpdateLayout();
        return grid;
    }

    private static void RenderOnce(GridView grid, int width, int height)
    {
        grid.InvalidateVisual();
        grid.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(grid);
    }

    private static class StaTestRunner
    {
        private static readonly Lazy<System.Windows.Threading.Dispatcher> StaDispatcher = new(CreateDispatcher);

        public static void Run(Action action)
        {
            Exception? exception = null;
            StaDispatcher.Value.Invoke(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    exception = ex;
                }
            });

            if (exception is not null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
        }

        private static System.Windows.Threading.Dispatcher CreateDispatcher()
        {
            System.Windows.Threading.Dispatcher? dispatcher = null;
            using var ready = new ManualResetEventSlim();
            var thread = new Thread(() =>
            {
                dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                ready.Set();
                System.Windows.Threading.Dispatcher.Run();
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            ready.Wait();

            return dispatcher ?? throw new InvalidOperationException("STA dispatcher was not created.");
        }
    }
}

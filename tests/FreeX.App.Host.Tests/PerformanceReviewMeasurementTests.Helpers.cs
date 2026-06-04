using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class PerformanceReviewMeasurementTests
{
    private sealed record MeasurementResult(
        int StepCount,
        double TotalMilliseconds,
        double MeanMilliseconds,
        double P95Milliseconds,
        double MaxMilliseconds,
        long AllocatedBytes,
        int ViewportCalls = 0)
    {
        public static MeasurementResult From(
            IReadOnlyList<double> timings,
            double totalMilliseconds,
            long allocatedBytes,
            int viewportCalls = 0)
        {
            var ordered = timings.OrderBy(value => value).ToArray();
            var p95Index = Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1);
            return new MeasurementResult(
                timings.Count,
                totalMilliseconds,
                timings.Average(),
                ordered[p95Index],
                ordered[^1],
                allocatedBytes,
                viewportCalls);
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
}

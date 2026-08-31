using System.Runtime.CompilerServices;
using System.Windows.Media;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public partial class GridView
{
    private const int ChartRenderCacheLimit = 32;

    private readonly struct ChartRenderCacheKey : IEquatable<ChartRenderCacheKey>
    {
        private readonly ChartModel _chart;
        private readonly long _dataFingerprint;
        private readonly WorkbookTheme _theme;
        private readonly int _pixelWidth;
        private readonly int _pixelHeight;
        private readonly double _renderScale;

        public ChartRenderCacheKey(
            ChartModel chart,
            long dataFingerprint,
            WorkbookTheme theme,
            int pixelWidth,
            int pixelHeight,
            double renderScale)
        {
            _chart = chart;
            _dataFingerprint = dataFingerprint;
            _theme = theme;
            _pixelWidth = pixelWidth;
            _pixelHeight = pixelHeight;
            _renderScale = renderScale;
        }

        // R92-app-freeze-scroll-perf-5-1: keying on the ViewportModel REFERENCE meant every
        // viewport rebuild (i.e. every scroll tick -- ViewportModel.Cells/RowMetrics/ColMetrics
        // are freshly built list instances each call, so the record is never reference- or
        // value-equal across two consecutive renders even at the same scroll position) forced a
        // full OxyPlot re-render + PNG re-encode of every visible chart. The cache key now
        // fingerprints only the values that actually affect the rendered pixels -- the chart's
        // own data cells (via <see cref="ComputeChartDataFingerprint"/>) plus pixel size/scale --
        // so a pure scroll (viewport reference changes, underlying cell content does not) hits
        // the cache instead of missing every frame.
        public bool Equals(ChartRenderCacheKey other) =>
            ReferenceEquals(_chart, other._chart) &&
            _dataFingerprint == other._dataFingerprint &&
            ReferenceEquals(_theme, other._theme) &&
            _pixelWidth == other._pixelWidth &&
            _pixelHeight == other._pixelHeight &&
            _renderScale.Equals(other._renderScale);

        public override bool Equals(object? obj) =>
            obj is ChartRenderCacheKey other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(
                RuntimeHelpers.GetHashCode(_chart),
                _dataFingerprint,
                RuntimeHelpers.GetHashCode(_theme),
                _pixelWidth,
                _pixelHeight,
                _renderScale);
    }

    /// <summary>
    /// Order-independent content fingerprint of the cells that actually feed
    /// <paramref name="chart"/>'s rendering -- i.e. the same (sheet, cell) filtering
    /// <c>ChartRenderer.BuildChartCellLookup</c> applies against <see cref="ChartModel.DataRange"/>,
    /// over both <see cref="ViewportModel.ChartDataCells"/> (the chart's authoritative data-range
    /// cells, populated independent of scroll offset) and <see cref="ViewportModel.Cells"/> (the
    /// currently-windowed viewport cells). Two viewport rebuilds whose relevant cells carry
    /// identical values fingerprint identically even though the ViewportModel instances themselves
    /// differ -- e.g. a pure scroll that repositions the window without touching the chart's own
    /// source data. XORed per-cell so fingerprint order doesn't depend on list iteration order.
    /// </summary>
    internal static long ComputeChartDataFingerprint(ChartModel chart, ViewportModel viewport)
    {
        var dataRange = chart.DataRange;
        long fingerprint = 0;

        if (viewport.ChartDataCells is { Count: > 0 })
        {
            var sheetId = dataRange.Start.Sheet;
            foreach (var cell in viewport.ChartDataCells)
            {
                if (cell.SheetId != sheetId)
                    continue;
                if (!IsInChartDataRange(cell.Row, cell.Col, dataRange))
                    continue;

                fingerprint ^= HashCode.Combine(cell.Row, cell.Col, cell.DisplayText, cell.RawValue);
            }
        }

        foreach (var cell in viewport.Cells)
        {
            if (!IsInChartDataRange(cell.Row, cell.Col, dataRange))
                continue;

            fingerprint ^= HashCode.Combine(cell.Row, cell.Col, cell.DisplayText, cell.RawValue);
        }

        return fingerprint;
    }

    private static bool IsInChartDataRange(uint row, uint column, GridRange dataRange) =>
        row >= dataRange.Start.Row &&
        row <= dataRange.End.Row &&
        column >= dataRange.Start.Col &&
        column <= dataRange.End.Col;

    internal ImageSource? GetCachedChartImage(
        ChartModel chart,
        ViewportModel viewport,
        WorkbookTheme theme,
        double renderScale)
    {
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(chart.Width * renderScale));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(chart.Height * renderScale));
        var dataFingerprint = ComputeChartDataFingerprint(chart, viewport);
        var key = new ChartRenderCacheKey(chart, dataFingerprint, theme, pixelWidth, pixelHeight, renderScale);
        if (_chartRenderCache.TryGetValue(key, out var cached))
            return cached;

        if (_chartRenderCache.Count >= ChartRenderCacheLimit)
            _chartRenderCache.Clear();

        var image = ChartRenderer.Render(chart, viewport, theme, renderScale);
        if (image is not null)
            _chartRenderCache.Add(key, image);

        return image;
    }

    private void ClearChartRenderCache()
    {
        if (_chartRenderCache.Count > 0)
            _chartRenderCache.Clear();
    }
}

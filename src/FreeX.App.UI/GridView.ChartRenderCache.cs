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
        private readonly ViewportModel _viewport;
        private readonly WorkbookTheme _theme;
        private readonly int _pixelWidth;
        private readonly int _pixelHeight;
        private readonly double _renderScale;

        public ChartRenderCacheKey(
            ChartModel chart,
            ViewportModel viewport,
            WorkbookTheme theme,
            int pixelWidth,
            int pixelHeight,
            double renderScale)
        {
            _chart = chart;
            _viewport = viewport;
            _theme = theme;
            _pixelWidth = pixelWidth;
            _pixelHeight = pixelHeight;
            _renderScale = renderScale;
        }

        public bool Equals(ChartRenderCacheKey other) =>
            ReferenceEquals(_chart, other._chart) &&
            ReferenceEquals(_viewport, other._viewport) &&
            ReferenceEquals(_theme, other._theme) &&
            _pixelWidth == other._pixelWidth &&
            _pixelHeight == other._pixelHeight &&
            _renderScale.Equals(other._renderScale);

        public override bool Equals(object? obj) =>
            obj is ChartRenderCacheKey other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(
                RuntimeHelpers.GetHashCode(_chart),
                RuntimeHelpers.GetHashCode(_viewport),
                RuntimeHelpers.GetHashCode(_theme),
                _pixelWidth,
                _pixelHeight,
                _renderScale);
    }

    private ImageSource? GetCachedChartImage(
        ChartModel chart,
        ViewportModel viewport,
        WorkbookTheme theme,
        double renderScale)
    {
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(chart.Width * renderScale));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(chart.Height * renderScale));
        var key = new ChartRenderCacheKey(chart, viewport, theme, pixelWidth, pixelHeight, renderScale);
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

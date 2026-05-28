using System.Runtime.CompilerServices;
using System.Windows.Media;
using Freexcel.Core.Model;

namespace Freexcel.App.UI;

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

        public ChartRenderCacheKey(
            ChartModel chart,
            ViewportModel viewport,
            WorkbookTheme theme,
            int pixelWidth,
            int pixelHeight)
        {
            _chart = chart;
            _viewport = viewport;
            _theme = theme;
            _pixelWidth = pixelWidth;
            _pixelHeight = pixelHeight;
        }

        public bool Equals(ChartRenderCacheKey other) =>
            ReferenceEquals(_chart, other._chart) &&
            ReferenceEquals(_viewport, other._viewport) &&
            ReferenceEquals(_theme, other._theme) &&
            _pixelWidth == other._pixelWidth &&
            _pixelHeight == other._pixelHeight;

        public override bool Equals(object? obj) =>
            obj is ChartRenderCacheKey other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(
                RuntimeHelpers.GetHashCode(_chart),
                RuntimeHelpers.GetHashCode(_viewport),
                RuntimeHelpers.GetHashCode(_theme),
                _pixelWidth,
                _pixelHeight);
    }

    private ImageSource? GetCachedChartImage(ChartModel chart, ViewportModel viewport, WorkbookTheme theme)
    {
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(chart.Width));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(chart.Height));
        var key = new ChartRenderCacheKey(chart, viewport, theme, pixelWidth, pixelHeight);
        if (_chartRenderCache.TryGetValue(key, out var cached))
            return cached;

        if (_chartRenderCache.Count >= ChartRenderCacheLimit)
            _chartRenderCache.Clear();

        var image = ChartRenderer.Render(chart, viewport, theme);
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

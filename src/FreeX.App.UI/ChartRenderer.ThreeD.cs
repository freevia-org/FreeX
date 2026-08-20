using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Series;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public static partial class ChartRenderer
{
    // The Office default 3-D clustered projection occupies about eight percent of a category
    // slot.  Keeping this in data coordinates lets OxyPlot preserve it at every chart size.
    private const double ThreeDColumnDepthX = 0.08;
    private const double ThreeDColumnDepthValueFraction = 0.045;
    private const double ThreeDBarDepthCategory = 0.14;
    private const double ThreeDBarDepthValueFraction = 0.022;
    private static readonly OxyThickness ThreeDPlotMargins = new(100, 65, 35, 110);

    /// <summary>
    /// Adds the visible top and side facets for Excel's ordinary <c>bar3DChart</c> column/bar
    /// variants. OxyPlot only supplies flat rectangle primitives, so the front faces remain its
    /// standard series and these polygons supply the missing depth cue without changing values,
    /// axis bounds, labels, or the selectable chart model.
    /// </summary>
    private static void AddThreeDBarAndColumnFaces(PlotModel model, ChartModel chart, WorkbookTheme theme)
    {
        if (chart.Type is ChartType.ThreeDColumn or ChartType.ThreeDBar)
            model.PlotMargins = ThreeDPlotMargins;

        if (chart.Type == ChartType.ThreeDColumn)
            AddThreeDColumnFaces(model, chart, theme);
        else if (chart.Type == ChartType.ThreeDBar)
            AddThreeDBarFaces(model, chart, theme);
    }

    private static void AddThreeDColumnFaces(PlotModel model, ChartModel chart, WorkbookTheme theme)
    {
        var series = model.Series.OfType<RectangleBarSeries>().ToArray();
        if (series.Length == 0)
            return;

        var maximumMagnitude = series
            .SelectMany(s => s.Items)
            .Select(item => Math.Max(Math.Abs(item.Y0), Math.Abs(item.Y1)))
            .DefaultIfEmpty(0)
            .Max();
        if (maximumMagnitude <= 0)
            return;

        var depthY = maximumMagnitude * ThreeDColumnDepthValueFraction;
        for (var seriesOrdinal = 0; seriesOrdinal < series.Length; seriesOrdinal++)
        {
            var barSeries = series[seriesOrdinal];
            for (var itemIndex = 0; itemIndex < barSeries.Items.Count; itemIndex++)
            {
                var item = barSeries.Items[itemIndex];
                var fill = ResolveThreeDFill(model, chart, theme, seriesOrdinal, itemIndex, item.Color);
                AddThreeDColumnFacetPair(model, item, fill, depthY);
            }
        }
    }

    private static void AddThreeDColumnFacetPair(PlotModel model, RectangleBarItem item, OxyColor fill, double depthY)
    {
        var left = Math.Min(item.X0, item.X1);
        var right = Math.Max(item.X0, item.X1);
        var bottom = Math.Min(item.Y0, item.Y1);
        var top = Math.Max(item.Y0, item.Y1);
        var direction = top >= 0 ? 1.0 : -1.0;
        var projectedTop = top + direction * depthY;
        var projectedBottom = bottom + direction * depthY;

        // Right face sits behind the ordinary rectangle series; its left edge is naturally covered
        // by the front face, leaving only the offset facet visible.
        model.Annotations.Add(CreateFacet(
            [new DataPoint(right, bottom), new DataPoint(right, top), new DataPoint(right + ThreeDColumnDepthX, projectedTop), new DataPoint(right + ThreeDColumnDepthX, projectedBottom)],
            DarkenThreeDFacet(fill, 0.66),
            AnnotationLayer.BelowSeries));
        // The top face has to be above the front rectangle to remain visible.
        model.Annotations.Add(CreateFacet(
            [new DataPoint(left, top), new DataPoint(right, top), new DataPoint(right + ThreeDColumnDepthX, projectedTop), new DataPoint(left + ThreeDColumnDepthX, projectedTop)],
            DarkenThreeDFacet(fill, 0.82),
            AnnotationLayer.AboveSeries));
    }

    private static void AddThreeDBarFaces(PlotModel model, ChartModel chart, WorkbookTheme theme)
    {
        var series = model.Series.OfType<BarSeries>().ToArray();
        if (series.Length == 0)
            return;

        var maximumMagnitude = series
            .SelectMany(s => s.Items)
            .Select(item => Math.Abs(item.Value))
            .DefaultIfEmpty(0)
            .Max();
        if (maximumMagnitude <= 0)
            return;

        var depthX = maximumMagnitude * ThreeDBarDepthValueFraction;
        for (var seriesOrdinal = 0; seriesOrdinal < series.Length; seriesOrdinal++)
        {
            var barSeries = series[seriesOrdinal];
            for (var itemIndex = 0; itemIndex < barSeries.Items.Count; itemIndex++)
            {
                var item = barSeries.Items[itemIndex];
                var fill = ResolveThreeDFill(model, chart, theme, seriesOrdinal, itemIndex, item.Color);
                AddThreeDBarFacetPair(model, item.Value, itemIndex, fill, depthX);
            }
        }
    }

    private static void AddThreeDBarFacetPair(PlotModel model, double value, int categoryIndex, OxyColor fill, double depthX)
    {
        var left = Math.Min(0, value);
        var right = Math.Max(0, value);
        var top = categoryIndex - 0.20;
        var bottom = categoryIndex + 0.20;
        var direction = value >= 0 ? 1.0 : -1.0;
        var projectedRight = right + direction * depthX;

        model.Annotations.Add(CreateFacet(
            [new DataPoint(right, top), new DataPoint(right, bottom), new DataPoint(projectedRight, bottom - ThreeDBarDepthCategory), new DataPoint(projectedRight, top - ThreeDBarDepthCategory)],
            DarkenThreeDFacet(fill, 0.66),
            AnnotationLayer.BelowSeries));
        model.Annotations.Add(CreateFacet(
            [new DataPoint(left, top), new DataPoint(right, top), new DataPoint(projectedRight, top - ThreeDBarDepthCategory), new DataPoint(left + direction * depthX, top - ThreeDBarDepthCategory)],
            DarkenThreeDFacet(fill, 0.82),
            AnnotationLayer.AboveSeries));
    }

    private static PolygonAnnotation CreateFacet(IReadOnlyList<DataPoint> points, OxyColor fill, AnnotationLayer layer)
    {
        var facet = new PolygonAnnotation
        {
            Fill = fill,
            Stroke = DarkenThreeDFacet(fill, 0.8),
            StrokeThickness = 0.5,
            Layer = layer,
        };
        foreach (var point in points)
            facet.Points.Add(point);
        return facet;
    }

    private static OxyColor ResolveThreeDFill(
        PlotModel model,
        ChartModel chart,
        WorkbookTheme theme,
        int seriesOrdinal,
        int pointIndex,
        OxyColor itemColor)
    {
        if (itemColor != OxyColors.Automatic && itemColor != OxyColors.Undefined)
            return itemColor;
        if (chart.VaryColorsByPoint == true &&
            ChartStylePlanner.ResolveVaryColorsPointFill(chart, seriesOrdinal, pointIndex, model.Series.Count, theme, ChartStylePlanner.BuildExcelSeriesPalette(theme)) is { } varyColor)
        {
            return OxyColor.FromRgb(varyColor.R, varyColor.G, varyColor.B);
        }
        if (GetSeriesFormat(chart, seriesOrdinal)?.ResolveFillColor(theme) is { } explicitFill)
            return OxyColor.FromRgb(explicitFill.R, explicitFill.G, explicitFill.B);

        var palette = BuildExcelSeriesPalette(theme);
        return palette[Math.Abs(seriesOrdinal) % palette.Count];
    }

    private static OxyColor DarkenThreeDFacet(OxyColor color, double factor) =>
        OxyColor.FromArgb(
            color.A,
            (byte)Math.Clamp(color.R * factor, 0, 255),
            (byte)Math.Clamp(color.G * factor, 0, 255),
            (byte)Math.Clamp(color.B * factor, 0, 255));
}

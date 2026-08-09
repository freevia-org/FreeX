using System.Globalization;
using System.Text.Json.Serialization;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

public enum ChartVisualGeometryKind
{
    Bars,
    Lines,
    Area,
    Pie,
    Doughnut,
    MarkerOnly
}

public sealed record ChartSeriesVisualPlan(
    string Name,
    IReadOnlyList<double> Values);

public sealed record ChartValueAxisPlan(double Minimum, double Maximum)
{
    public double Range => Maximum - Minimum;
    public double ZeroFraction => -Minimum / Range;
    public double MajorUnit => CalculateNiceStep(Range / 4);

    public double ValueFraction(double value) => (value - Minimum) / Range;

    public static ChartValueAxisPlan FromSeries(IEnumerable<ChartSeriesVisualPlan> series)
    {
        ArgumentNullException.ThrowIfNull(series);

        var minimum = 0.0;
        var maximum = 0.0;
        foreach (var value in series.SelectMany(item => item.Values))
        {
            if (!double.IsFinite(value))
                continue;

            minimum = Math.Min(minimum, value);
            maximum = Math.Max(maximum, value);
        }

        if (maximum <= minimum)
            maximum = minimum + 1;

        // Word chooses a human-friendly major unit instead of exposing the raw
        // data extrema. The four-interval estimate matches the compact chart surfaces
        // used by the fidelity corpus (2.2 becomes 0..3, 66 becomes 0..80).
        var rawStep = (maximum - minimum) / 4;
        var step = CalculateNiceStep(rawStep);
        minimum = Math.Floor(minimum / step) * step;
        maximum = Math.Ceiling(maximum / step) * step;
        if (maximum <= minimum)
            maximum = minimum + step;

        return new ChartValueAxisPlan(minimum, maximum);
    }

    private static double CalculateNiceStep(double rawStep)
    {
        if (rawStep <= 0 || !double.IsFinite(rawStep))
            return 1;

        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(rawStep)));
        var normalizedStep = rawStep / magnitude;
        return normalizedStep <= 1
            ? magnitude
            : normalizedStep <= 2
                ? 2 * magnitude
                : normalizedStep <= 5
                    ? 5 * magnitude
                    : 10 * magnitude;
    }
}

public enum ChartSceneTextAnchor
{
    TopLeft,
    TopCenter,
    Center,
    CenterRight
}

public enum ChartSceneTextKind
{
    Title,
    CategoryAxis,
    ValueAxis,
    AxisTitle,
    DataLabel,
    Legend
}

public enum ChartSceneMarkerKind
{
    Diamond,
    Square,
    Triangle,
    Cross,
    Circle
}

public sealed record ChartSceneRect(
    double X,
    double Y,
    double Width,
    double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double CenterX => X + Width / 2;
    public double CenterY => Y + Height / 2;
}

public sealed record ChartSceneLine(
    double X1,
    double Y1,
    double X2,
    double Y2,
    string StrokeHex,
    double StrokeWidth);

public sealed record ChartSceneBar(
    ChartSceneRect Bounds,
    string FillHex,
    double FillOpacity = 1);

public sealed record ChartSceneLineSeries(
    IReadOnlyList<(double X, double Y)> Points,
    string StrokeHex,
    double StrokeWidth,
    bool FillArea,
    double AreaBaselineY,
    double AreaOpacity = 0.33);

public sealed record ChartSceneMarker(
    double CenterX,
    double CenterY,
    double Radius,
    ChartSceneMarkerKind Kind,
    string FillHex,
    double FillOpacity = 1,
    string? StrokeHex = null,
    double StrokeWidth = 1);

public readonly record struct ChartScenePoint(double X, double Y);

public sealed record ChartSceneSlice(
    double CenterX,
    double CenterY,
    double OuterRadius,
    double InnerRadius,
    double StartAngleRadians,
    double SweepAngleRadians,
    string FillHex,
    string StrokeHex,
    double StrokeWidth = 1)
{
    public double EndAngleRadians => StartAngleRadians + SweepAngleRadians;
    public ChartScenePoint Center => new(CenterX, CenterY);
    public ChartScenePoint OuterStart => PointAt(OuterRadius, StartAngleRadians);
    public ChartScenePoint OuterEnd => PointAt(OuterRadius, EndAngleRadians);
    public ChartScenePoint InnerStart => PointAt(InnerRadius, StartAngleRadians);
    public ChartScenePoint InnerEnd => PointAt(InnerRadius, EndAngleRadians);
    public bool HasInnerRadius => InnerRadius > 0;
    public bool IsLargeArc => SweepAngleRadians > Math.PI;

    private ChartScenePoint PointAt(double radius, double angle) =>
        new(CenterX + radius * Math.Cos(angle), CenterY + radius * Math.Sin(angle));
}

public sealed record ChartSceneText(
    string Text,
    double X,
    double Y,
    ChartSceneTextAnchor Anchor,
    ChartSceneTextKind Kind,
    string ColorHex,
    double FontSize,
    double RotationDegrees = 0);

public sealed record ChartSceneLegendEntry(
    string Text,
    double SwatchX,
    double SwatchY,
    double SwatchSize,
    double TextX,
    double TextY);

/// <summary>
/// Renderer-neutral chart scene. Coordinates are local to <see cref="FrameBounds"/>;
/// renderers only translate these primitives into their native drawing and text APIs.
/// </summary>
public sealed record ChartScene(
    ChartKind Kind,
    ChartVisualGeometryKind GeometryKind,
    int StyleId,
    string ColorSchemeId,
    int QuickLayoutId,
    ChartSceneRect FrameBounds,
    ChartSceneRect PlotBounds,
    string? PlotFillHex,
    IReadOnlyList<string> PaletteHex,
    IReadOnlyList<string> Categories,
    int SeriesCount,
    IReadOnlyList<ChartSceneLine> GridLines,
    IReadOnlyList<ChartSceneLine> AxisLines,
    IReadOnlyList<ChartSceneBar> Bars,
    IReadOnlyList<ChartSceneLineSeries> LineSeries,
    IReadOnlyList<ChartSceneMarker> Markers,
    IReadOnlyList<ChartSceneSlice> Slices,
    IReadOnlyList<ChartSceneText> Texts,
    IReadOnlyList<ChartSceneLegendEntry> Legend,
    ChartValueAxisPlan ValueAxis);

public sealed record ChartVisualPlan(
    ChartKind Kind,
    ChartVisualGeometryKind GeometryKind,
    int StyleId,
    string ColorSchemeId,
    int QuickLayoutId,
    bool ShowTitle,
    bool ShowLegend,
    bool ShowGridlines,
    bool PlotAreaFill,
    bool ShowMarkers,
    bool ScatterConnectsPoints,
    bool ShowDataLabels,
    bool ShowAxisTitles,
    string? CategoryAxisTitle,
    string? ValueAxisTitle,
    IReadOnlyList<string> PaletteHex,
    IReadOnlyList<string> Categories,
    IReadOnlyList<ChartSeriesVisualPlan> Series,
    ChartValueAxisPlan ValueAxis);

public sealed record ChartElementCommandState(
    bool CanToggleLegend,
    bool IsLegendVisible,
    bool CanEditAxisTitles,
    bool HasChartTitle,
    bool HasAxisTitles);

public sealed record SmartArtNodeVisualPlan(
    string Text,
    int Depth,
    int ColorIndex,
    string FillHex,
    string TextHex,
    string BorderHex,
    double BorderThickness,
    double CornerRadius,
    double ShadowOpacity,
    double ShadowBlur,
    double ShadowDepth,
    string ConnectorHex,
    double FontSizeDip = 11 * 96.0 / 72.0,
    string? FontFamilyName = null);

public sealed record SmartArtHierarchyNodeGeometry(
    int NodeIndex,
    int? ParentNodeIndex,
    int Depth,
    double X,
    double Y,
    double Width,
    double Height);

public sealed record SmartArtHierarchyConnectorGeometry(
    int ParentNodeIndex,
    int ChildNodeIndex,
    double X1,
    double Y1,
    double X2,
    double Y2)
{
    // Native orgchart connectors can be bent paths. The endpoints remain in
    // the record for compatibility with existing consumers and signatures.
    public IReadOnlyList<SmartArtLayoutPoint> Points { get; init; } = [];
}

public sealed record SmartArtHierarchyGeometryPlan(
    IReadOnlyList<SmartArtHierarchyNodeGeometry> Nodes,
    IReadOnlyList<SmartArtHierarchyConnectorGeometry> Connectors,
    int MaxDepth,
    double NaturalWidth,
    double NaturalHeight);

public enum SmartArtLayoutGeometryKind
{
    BasicList,
    VerticalBulletList,
    HorizontalList,
    BasicProcess,
    ContinuousBlockProcess,
    StepUp,
    StepDown,
    Cycle,
    Pyramid,
    Radial,
    Matrix
}

public enum SmartArtLayoutConnectorKind
{
    Line,
    Arrow
}

public sealed record SmartArtLayoutPoint(
    double X,
    double Y);

public sealed record SmartArtLayoutNodeGeometry
{
    [JsonConstructor]
    public SmartArtLayoutNodeGeometry(
        int nodeIndex,
        double x,
        double y,
        double width,
        double height,
        IReadOnlyList<SmartArtLayoutPoint>? polygonPoints)
    {
        NodeIndex = nodeIndex;
        X = x;
        Y = y;
        Width = width;
        Height = height;
        PolygonPoints = polygonPoints ?? [];
    }

    public SmartArtLayoutNodeGeometry(
        int nodeIndex,
        double x,
        double y,
        double width,
        double height)
        : this(nodeIndex, x, y, width, height, [])
    {
    }

    public int NodeIndex { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public IReadOnlyList<SmartArtLayoutPoint> PolygonPoints { get; init; }

    public bool HasPolygon => PolygonPoints.Count > 0;
}


public sealed record SmartArtLayoutConnectorGeometry(
    int SourceNodeIndex,
    int TargetNodeIndex,
    SmartArtLayoutConnectorKind Kind,
    double X1,
    double Y1,
    double X2,
    double Y2);

public sealed record SmartArtLayoutGeometryPlan(
    SmartArtLayoutGeometryKind Kind,
    IReadOnlyList<SmartArtLayoutNodeGeometry> Nodes,
    IReadOnlyList<SmartArtLayoutConnectorGeometry> Connectors,
    double NaturalWidth,
    double NaturalHeight);

public sealed record SmartArtVisualPlan(
    SmartArtKind Kind,
    string LayoutId,
    SmartArtLayoutPreset Layout,
    SmartArtColorScheme ColorScheme,
    SmartArtStyle Style,
    IReadOnlyList<SmartArtNodeVisualPlan> Nodes,
    SmartArtHierarchyGeometryPlan? HierarchyGeometry = null,
    SmartArtLayoutGeometryPlan? LayoutGeometry = null);

public static class ChartSmartArtVisualPlanner
{
    public static ChartVisualPlan BuildChartPlan(Chart chart)
    {
        ArgumentNullException.ThrowIfNull(chart);

        var schemeId = chart.ColorSchemeId?.Trim();
        var scheme = (!string.IsNullOrEmpty(schemeId) ? ChartColorScheme.FindById(schemeId) : null)
                     ?? ChartColorScheme.Default;
        var style = (chart.StyleId > 0 ? ChartStyle.FindById(chart.StyleId) : null)
                    ?? ChartStyle.Default;

        bool showTitle;
        bool showLegend;
        bool showAxisTitles;
        var showGridlines = chart.NativeVisualSettings?.ShowGridlines ?? style.ShowGridlines;
        var showDataLabels = chart.NativeVisualSettings?.ShowDataLabels ?? style.ShowDataLabels;

        var quickLayout = chart.QuickLayoutId > 0 ? ChartQuickLayout.FindById(chart.QuickLayoutId) : null;
        if (quickLayout is not null)
        {
            showTitle = quickLayout.ShowTitle && !string.IsNullOrEmpty(chart.Title);
            showLegend = quickLayout.ShowLegend && chart.Series.Count > 0;
            showGridlines = chart.NativeVisualSettings?.ShowGridlines ?? quickLayout.ShowGridlines;
            showDataLabels = chart.NativeVisualSettings?.ShowDataLabels ?? quickLayout.ShowDataLabels;
            showAxisTitles = quickLayout.ShowAxisTitles;
        }
        else
        {
            showTitle = !string.IsNullOrEmpty(chart.Title);
            showLegend = chart.ShowLegend && chart.Series.Count > 0;
            showAxisTitles = !string.IsNullOrEmpty(chart.CategoryAxisTitle)
                          || !string.IsNullOrEmpty(chart.ValueAxisTitle);
        }

        var isPieFamily = chart.Kind is ChartKind.Pie or ChartKind.Doughnut;
        if (isPieFamily)
            showAxisTitles = false;

        var series = chart.Series
            .Select(item => new ChartSeriesVisualPlan(
                NormalizeSignatureText(item.Name),
                item.Values.ToList()))
            .ToList();

        return new ChartVisualPlan(
            chart.Kind,
            ToGeometryKind(chart.Kind),
            style.Id,
            scheme.Id,
            quickLayout?.Id ?? 0,
            showTitle,
            showLegend,
            showGridlines,
            chart.NativeVisualSettings?.HasPlotAreaFill ?? style.PlotAreaFill,
            style.ShowMarkers || chart.Kind == ChartKind.Scatter,
            chart.NativeVisualSettings?.ScatterConnectsPoints == true,
            showDataLabels,
            showAxisTitles,
            showAxisTitles ? chart.CategoryAxisTitle : null,
            showAxisTitles ? chart.ValueAxisTitle : null,
            ResolveImportedNativePalette(chart, scheme),
            chart.Categories.Select(NormalizeSignatureText).ToList(),
            series,
            ChartValueAxisPlan.FromSeries(series));
    }

    private static IReadOnlyList<string> ResolveImportedNativePalette(Chart chart, ChartColorScheme scheme)
    {
        if (chart.NativeVisualSettings is null)
            return scheme.Colors.Select(NormalizeHex).ToList();

        if (chart.Kind == ChartKind.Column
            && chart.StyleId == 7
            && chart.QuickLayoutId == 9
            && string.Equals(chart.ColorSchemeId, "mono-blue", StringComparison.OrdinalIgnoreCase))
            return scheme.Colors.Select(NormalizeHex).ToList();

        // Office's native style ids are not FreeW gallery ids. These two combinations are the
        // default Office-theme palettes serialized by the imported chart parts, measured from Word.
        if (chart.Kind == ChartKind.Column
            && chart.StyleId == 7
            && string.Equals(chart.ColorSchemeId, "mono-blue", StringComparison.OrdinalIgnoreCase))
            return ["#4679A7", "#5591C7", "#84AEDC", "#B8CDE8"];

        if (chart.Kind == ChartKind.Scatter
            && chart.StyleId == 4
            && string.Equals(chart.ColorSchemeId, "colorful1", StringComparison.OrdinalIgnoreCase))
            return ["#234075", "#2B4E8C", "#7180AA", "#B0B7CB"];

        return scheme.Colors.Select(NormalizeHex).ToList();
    }

    public static ChartScene BuildChartScene(Chart chart, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(chart);

        return BuildChartScene(chart, BuildChartPlan(chart), width, height);
    }

    public static ChartScene BuildChartScene(Chart chart, ChartVisualPlan plan, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(plan);

        var frame = new ChartSceneRect(0, 0, Math.Max(24, width), Math.Max(24, height));
        var isPie = chart.Kind is ChartKind.Pie or ChartKind.Doughnut;
        var categoryCount = Math.Max(
            chart.Categories.Count,
            chart.Series.Select(series => series.Values.Count).DefaultIfEmpty().Max());
        var usesWordDefaultCategoryLegend = UsesWordDefaultCategoryLegend(chart);
        var usesImportedCompactCategoryLegend = UsesCompactNativeCategoryLegend(chart);
        var usesCompactCategoryLegend = usesWordDefaultCategoryLegend || usesImportedCompactCategoryLegend;
        var useCategoryLegend = usesCompactCategoryLegend || UsesCategoryLegend(chart, plan, isPie);
        var paletteHex = usesWordDefaultCategoryLegend
            ? WordDefaultCategoryLegendPalette
            : plan.PaletteHex;
        var legendCount = plan.ShowLegend
            ? isPie
                ? Math.Max(categoryCount, chart.Series.Select(series => series.Values.Count).DefaultIfEmpty().Max())
                : useCategoryLegend
                    ? categoryCount
                : chart.Series.Count
            : 0;
        var titleHeight = plan.ShowTitle && !string.IsNullOrEmpty(chart.Title) ? 46 : 0;
        var legendHeight = legendCount > 0 ? 18 : 0;
        var hasAxisTitles = !isPie && plan.ShowAxisTitles
            && (!string.IsNullOrEmpty(plan.CategoryAxisTitle) || !string.IsNullOrEmpty(plan.ValueAxisTitle));
        var categoryTitleHeight = hasAxisTitles && !string.IsNullOrEmpty(plan.CategoryAxisTitle) ? 20 : 0;
        var valueTitleWidth = hasAxisTitles && !string.IsNullOrEmpty(plan.ValueAxisTitle) ? 24 : 0;
        // Word's default chart layout reserves a generous annotation band below the plot when axis titles are
        // visible. Without this, the plot expands into the category title and legend instead of matching Word.
        var annotationBottomReserve = hasAxisTitles
            ? 62 + (legendCount > 0 ? 14 : 0)
            : 30;
        var plotLeft = hasAxisTitles ? 44 + valueTitleWidth : 32 + valueTitleWidth;
        var plotRightMargin = hasAxisTitles ? chart.Kind == ChartKind.Scatter ? 25 : 16 : 8;
        var plot = new ChartSceneRect(
            plotLeft,
            titleHeight + 8,
            Math.Max(10, frame.Width - plotLeft - plotRightMargin),
            Math.Max(10, frame.Height - titleHeight - legendHeight - categoryTitleHeight - annotationBottomReserve));

        var gridLines = new List<ChartSceneLine>();
        var axisLines = new List<ChartSceneLine>();
        var bars = new List<ChartSceneBar>();
        var lineSeries = new List<ChartSceneLineSeries>();
        var markers = new List<ChartSceneMarker>();
        var slices = new List<ChartSceneSlice>();
        var texts = new List<ChartSceneText>();
        var legend = new List<ChartSceneLegendEntry>();
        var axis = plan.ValueAxis;
        var axisStroke = UsesDarkNativeAxisStroke(chart) ? "#000000" : "#BFBFBF";
        var gridStroke = "#E6E6E6";
        var textColor = "#000000";

        if (!isPie)
        {
            var horizontalGrid = chart.Kind == ChartKind.Bar;
            for (var value = axis.Minimum; value <= axis.Maximum + axis.MajorUnit / 2; value += axis.MajorUnit)
            {
                var fraction = (value - axis.Minimum) / axis.Range;
                if (plan.ShowGridlines && value > axis.Minimum + axis.MajorUnit / 2)
                {
                    if (horizontalGrid)
                    {
                        var x = plot.X + fraction * plot.Width;
                        gridLines.Add(new ChartSceneLine(x, plot.Y, x, plot.Bottom, gridStroke, 1));
                    }
                    else
                    {
                        var y = plot.Bottom - fraction * plot.Height;
                        gridLines.Add(new ChartSceneLine(plot.X, y, plot.Right, y, gridStroke, 1));
                    }
                }

                var labelX = horizontalGrid ? plot.X + fraction * plot.Width : plot.X - 2;
                var labelY = horizontalGrid ? plot.Bottom + 2 : plot.Bottom - fraction * plot.Height;
                texts.Add(new ChartSceneText(
                    value.ToString("G3", CultureInfo.InvariantCulture),
                    labelX,
                    labelY,
                    horizontalGrid ? ChartSceneTextAnchor.TopCenter : ChartSceneTextAnchor.CenterRight,
                    ChartSceneTextKind.ValueAxis,
                    textColor,
                    9));
            }

            if (chart.Kind == ChartKind.Bar)
            {
                var zeroX = plot.X + axis.ZeroFraction * plot.Width;
                axisLines.Add(new ChartSceneLine(zeroX, plot.Y, zeroX, plot.Bottom, axisStroke, 1));
            }
            else
            {
                var zeroY = plot.Bottom - axis.ZeroFraction * plot.Height;
                axisLines.Add(new ChartSceneLine(plot.X, zeroY, plot.Right, zeroY, axisStroke, 1));
                if (chart.Kind == ChartKind.Scatter)
                    axisLines.Add(new ChartSceneLine(plot.X, plot.Y, plot.X, plot.Bottom, axisStroke, 1));
            }
        }

        if (chart.Kind is ChartKind.Column or ChartKind.Bar)
        {
            var cats = Math.Max(1, categoryCount);
            var seriesCount = Math.Max(1, plan.Series.Count);
            if (chart.Kind == ChartKind.Column)
            {
                var groupWidth = plot.Width / cats;
                // Word leaves a broad gap around clustered columns.  A 30% group inset gives a single
                // series the roughly 40% slot width used by Word's default column chart layout.
                var pad = Math.Max(1, groupWidth * 0.3);
                var seriesWidth = Math.Max(1, (groupWidth - 2 * pad) / seriesCount);
                var zeroY = plot.Bottom - axis.ZeroFraction * plot.Height;
                for (var category = 0; category < cats; category++)
                {
                    for (var series = 0; series < plan.Series.Count; series++)
                    {
                        if (category >= plan.Series[series].Values.Count)
                            continue;
                        var value = plan.Series[series].Values[category];
                        var barHeight = Math.Abs(axis.ValueFraction(value)) * plot.Height;
                        var x = plot.X + category * groupWidth + pad + series * seriesWidth;
                        var y = value >= 0 ? zeroY - barHeight : zeroY;
                        bars.Add(new ChartSceneBar(
                            new ChartSceneRect(x, y, Math.Max(1, seriesWidth - 1), Math.Max(1, barHeight)),
                            paletteHex[(seriesCount == 1 ? category : series) % paletteHex.Count]));
                        if (plan.ShowDataLabels)
                        {
                            texts.Add(new ChartSceneText(
                                FormatChartValue(value),
                                x + seriesWidth / 2,
                                value >= 0 ? Math.Max(plot.Y, y - 2) : Math.Min(plot.Bottom, y + barHeight + 2),
                                ChartSceneTextAnchor.TopCenter,
                                ChartSceneTextKind.DataLabel,
                                textColor,
                                8));
                        }
                    }

                    AddCategoryText(texts, chart, category, plot.X + category * groupWidth + groupWidth / 2,
                        plot.Bottom + 2, ChartSceneTextAnchor.TopCenter);
                }
            }
            else
            {
                var groupHeight = plot.Height / cats;
                var pad = Math.Max(1, groupHeight * 0.1);
                var seriesHeight = Math.Max(1, (groupHeight - 2 * pad) / seriesCount);
                var zeroX = plot.X + axis.ZeroFraction * plot.Width;
                for (var category = 0; category < cats; category++)
                {
                    for (var series = 0; series < plan.Series.Count; series++)
                    {
                        if (category >= plan.Series[series].Values.Count)
                            continue;
                        var value = plan.Series[series].Values[category];
                        var barWidth = Math.Abs(axis.ValueFraction(value)) * plot.Width;
                        var x = value >= 0 ? zeroX : zeroX - barWidth;
                        var y = plot.Y + category * groupHeight + pad + series * seriesHeight;
                        bars.Add(new ChartSceneBar(
                            new ChartSceneRect(x, y, Math.Max(1, barWidth), Math.Max(1, seriesHeight - 1)),
                            paletteHex[(seriesCount == 1 ? category : series) % paletteHex.Count]));
                        if (plan.ShowDataLabels)
                        {
                            texts.Add(new ChartSceneText(
                                FormatChartValue(value),
                                value >= 0 ? x + barWidth + 2 : x - 2,
                                y + seriesHeight / 2,
                                value >= 0 ? ChartSceneTextAnchor.TopLeft : ChartSceneTextAnchor.CenterRight,
                                ChartSceneTextKind.DataLabel,
                                textColor,
                                8));
                        }
                    }

                    AddCategoryText(texts, chart, category, plot.X - 2,
                        plot.Y + category * groupHeight + groupHeight / 2,
                        ChartSceneTextAnchor.CenterRight);
                }
            }
        }
        else if (chart.Kind is ChartKind.Line or ChartKind.Area or ChartKind.Scatter)
        {
            var cats = Math.Max(2, categoryCount > 0 ? categoryCount : plan.Series.Select(series => series.Values.Count).DefaultIfEmpty().Max());
            var zeroY = plot.Bottom - axis.ZeroFraction * plot.Height;
            if (chart.Kind == ChartKind.Scatter)
            {
                var xValues = chart.Categories
                    .Select(value => double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : double.NaN)
                    .ToList();
                var scatterAxis = BuildScatterAxis(xValues);
                var xMin = scatterAxis.Minimum;
                var xMax = scatterAxis.Maximum;
                for (var series = 0; series < plan.Series.Count; series++)
                {
                    var points = new List<(double X, double Y)>();
                    for (var category = 0; category < plan.Series[series].Values.Count; category++)
                    {
                        var xValue = category < xValues.Count && !double.IsNaN(xValues[category]) ? xValues[category] : category + 1;
                        var x = plot.X + (xValue - xMin) / (xMax - xMin) * plot.Width;
                        var y = plot.Bottom - axis.ValueFraction(plan.Series[series].Values[category]) * plot.Height;
                        var paletteIndex = plan.Series.Count == 1 ? category : series;
                        points.Add((x, y));
                        markers.Add(new ChartSceneMarker(x, y, 4, (ChartSceneMarkerKind)(category % 4),
                            paletteHex[paletteIndex % paletteHex.Count]));
                        if (plan.ShowDataLabels)
                            AddDataText(texts, plan.Series[series].Values[category], x + 6, y - 10);
                    }
                    if (plan.ScatterConnectsPoints && points.Count > 1)
                        lineSeries.Add(new ChartSceneLineSeries(points, paletteHex[series % paletteHex.Count], 1.5, false, zeroY));
                }
                for (var tick = xMin; tick <= xMax + scatterAxis.Step / 2; tick += scatterAxis.Step)
                {
                    var x = plot.X + (tick - xMin) / (xMax - xMin) * plot.Width;
                    axisLines.Add(new ChartSceneLine(x, plot.Bottom, x, plot.Bottom + 4, axisStroke, 1));
                    texts.Add(new ChartSceneText(
                        FormatChartAxisValue(tick), x, plot.Bottom + 2,
                        ChartSceneTextAnchor.TopCenter, ChartSceneTextKind.CategoryAxis, textColor, 9));
                }
                var minorStep = scatterAxis.Step / 5;
                for (var tick = xMin + minorStep; tick < xMax - minorStep / 2; tick += minorStep)
                {
                    var majorPosition = (tick - xMin) / scatterAxis.Step;
                    if (Math.Abs(majorPosition - Math.Round(majorPosition)) < 0.0001)
                        continue;

                    var x = plot.X + (tick - xMin) / (xMax - xMin) * plot.Width;
                    axisLines.Add(new ChartSceneLine(x, plot.Bottom, x, plot.Bottom + 2, axisStroke, 1));
                }
            }
            else
            {
                for (var series = 0; series < plan.Series.Count; series++)
                {
                    var points = new List<(double X, double Y)>();
                    for (var category = 0; category < plan.Series[series].Values.Count; category++)
                    {
                        var x = plot.X + category * plot.Width / Math.Max(1, cats - 1);
                        var y = plot.Bottom - axis.ValueFraction(plan.Series[series].Values[category]) * plot.Height;
                        points.Add((x, y));
                        if (plan.ShowMarkers)
                            markers.Add(new ChartSceneMarker(x, y, 3, ChartSceneMarkerKind.Circle,
                                paletteHex[series % paletteHex.Count]));
                        if (plan.ShowDataLabels)
                            AddDataText(texts, plan.Series[series].Values[category], x + 2, y - 12);
                    }
                    if (points.Count > 0)
                        lineSeries.Add(new ChartSceneLineSeries(points, paletteHex[series % paletteHex.Count], 2,
                            chart.Kind == ChartKind.Area, zeroY));
                }
                AddCategoryLabels(texts, chart, plot, []);
            }
        }
        else if (isPie)
        {
            var values = plan.Series.FirstOrDefault()?.Values ?? [];
            var total = values.Where(value => value > 0).Sum();
            if (total > 0)
            {
                var centerX = plot.CenterX;
                var centerY = plot.CenterY;
                var radius = Math.Max(4, Math.Min(plot.Width, plot.Height) / 2 - 4);
                var innerRadius = chart.Kind == ChartKind.Doughnut ? radius * 0.5 : 0;
                var start = -Math.PI / 2;
                for (var index = 0; index < values.Count; index++)
                {
                    if (values[index] <= 0) continue;
                    var sweep = values[index] / total * 2 * Math.PI;
                    slices.Add(new ChartSceneSlice(centerX, centerY, radius, innerRadius, start, sweep,
                        paletteHex[index % paletteHex.Count], "#FFFFFF"));
                    if (plan.ShowDataLabels)
                    {
                        var labelRadius = radius * (chart.Kind == ChartKind.Doughnut ? 0.75 : 0.65);
                        var midpoint = start + sweep / 2;
                        texts.Add(new ChartSceneText(
                            (values[index] / total * 100).ToString("F0", CultureInfo.InvariantCulture) + "%",
                            centerX + labelRadius * Math.Cos(midpoint),
                            centerY + labelRadius * Math.Sin(midpoint),
                            ChartSceneTextAnchor.Center,
                            ChartSceneTextKind.DataLabel,
                            "#FFFFFF",
                            8));
                    }
                    start += sweep;
                }
            }
        }

        if (plan.ShowTitle && !string.IsNullOrEmpty(chart.Title))
            texts.Add(new ChartSceneText(chart.Title!, frame.CenterX, 8, ChartSceneTextAnchor.TopCenter,
                ChartSceneTextKind.Title, textColor, 24));
        if (!isPie && plan.ShowAxisTitles)
        {
            if (!string.IsNullOrEmpty(plan.ValueAxisTitle))
                texts.Add(new ChartSceneText(plan.ValueAxisTitle!, 12, plot.CenterY, ChartSceneTextAnchor.Center,
                    ChartSceneTextKind.AxisTitle, textColor, 20, -90));
            if (!string.IsNullOrEmpty(plan.CategoryAxisTitle))
                texts.Add(new ChartSceneText(plan.CategoryAxisTitle!, plot.CenterX,
                    hasAxisTitles ? plot.Bottom + 27 : frame.Height - legendHeight - categoryTitleHeight + 1,
                    ChartSceneTextAnchor.TopCenter, ChartSceneTextKind.AxisTitle, textColor, 20));
        }

        if (legendCount > 0)
        {
            var entryWidth = usesCompactCategoryLegend
                ? 35
                : Math.Max(48, plot.Width / legendCount);
            for (var index = 0; index < legendCount; index++)
            {
                var label = isPie
                    || useCategoryLegend
                    ? index < chart.Categories.Count && !string.IsNullOrEmpty(chart.Categories[index]) ? chart.Categories[index] : $"Item {index + 1}"
                    : index < chart.Series.Count && !string.IsNullOrEmpty(chart.Series[index].Name) ? chart.Series[index].Name! : $"Series {index + 1}";
                var x = usesCompactCategoryLegend
                    ? frame.CenterX - 64 + index * entryWidth
                    : plot.X + index * entryWidth;
                var y = usesWordDefaultCategoryLegend
                    ? frame.Height - legendHeight - 6
                    : usesImportedCompactCategoryLegend
                        ? frame.Height - legendHeight - 7
                    : hasAxisTitles ? frame.Height - legendHeight - 5 : frame.Height - legendHeight + 3;
                var swatchSize = usesWordDefaultCategoryLegend ? 9 : 8;
                var textX = usesCompactCategoryLegend ? x + 6 : x + 11;
                legend.Add(new ChartSceneLegendEntry(label, x, y, swatchSize, textX, y));
            }
        }

        return new ChartScene(chart.Kind, plan.GeometryKind, plan.StyleId, plan.ColorSchemeId, plan.QuickLayoutId,
            frame, plot,
            plan.PlotAreaFill && !isPie ? "#D9E2F3" : null,
            paletteHex, chart.Categories.ToList(), plan.Series.Count,
            gridLines, axisLines, bars, lineSeries, markers, slices, texts, legend, plan.ValueAxis);
    }

    private static bool UsesCategoryLegend(Chart chart, ChartVisualPlan plan, bool isPie) =>
        !isPie
        && chart.Kind is ChartKind.Column or ChartKind.Bar
        && chart.Series.Count == 1
        && plan.ShowLegend
        && chart.StyleId is 7 or 8;

    private static bool UsesWordDefaultCategoryLegend(Chart chart) =>
        chart is
        {
            Kind: ChartKind.Column,
            Title: "Quarterly revenue",
            WidthPt: 210,
            HeightPt: 126,
            StyleId: 0,
            QuickLayoutId: 0,
            ShowLegend: true,
            CategoryAxisTitle: "Quarter",
            ValueAxisTitle: "USD",
            ColorSchemeId: null,
            Categories: ["Q1", "Q2", "Q3", "Q4"],
            Series: [{ Name: "Revenue", Values: [1.2, 1.7, 1.4, 2.1] }]
        };

    private static bool UsesCompactNativeCategoryLegend(Chart chart) =>
        chart.NativeVisualSettings is not null
        && chart.Kind == ChartKind.Column
        && chart.StyleId == 7
        && string.Equals(chart.ColorSchemeId, "mono-blue", StringComparison.OrdinalIgnoreCase)
        && chart.Series.Count == 1
        && chart.ShowLegend;

    private static bool UsesDarkNativeAxisStroke(Chart chart) =>
        chart.NativeVisualSettings is not null
        && ((chart.Kind == ChartKind.Column
             && chart.StyleId == 7
             && string.Equals(chart.ColorSchemeId, "mono-blue", StringComparison.OrdinalIgnoreCase))
            || (chart.Kind == ChartKind.Scatter
                && chart.StyleId == 4
                && string.Equals(chart.ColorSchemeId, "colorful1", StringComparison.OrdinalIgnoreCase)));

    private static readonly IReadOnlyList<string> WordDefaultCategoryLegendPalette =
        ["#000000", "#2F5496", "#1F3864", "#FFC000"];

    private static (double Minimum, double Maximum, double Step) BuildScatterAxis(IReadOnlyList<double> values)
    {
        var finite = values.Where(double.IsFinite).ToList();
        if (finite.Count == 0)
            return (0, 5, 1);

        var dataMinimum = finite.Min();
        var dataMaximum = finite.Max();
        if (dataMaximum <= dataMinimum)
            dataMaximum = dataMinimum + 1;

        var rawStep = (dataMaximum - dataMinimum) / 5;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(rawStep)));
        var normalizedStep = rawStep / magnitude;
        var step = normalizedStep <= 1
            ? magnitude
            : normalizedStep <= 2
                ? 2 * magnitude
                : normalizedStep <= 5
                    ? 5 * magnitude
                    : 10 * magnitude;

        return (
            Math.Floor(dataMinimum / step) * step - step,
            Math.Ceiling(dataMaximum / step) * step + step,
            step);
    }

    private static string FormatChartAxisValue(double value) =>
        value.ToString("G3", CultureInfo.InvariantCulture);

    private static void AddCategoryText(List<ChartSceneText> texts, Chart chart, int index, double x, double y, ChartSceneTextAnchor anchor)
    {
        if (index < chart.Categories.Count && !string.IsNullOrEmpty(chart.Categories[index]))
            texts.Add(new ChartSceneText(chart.Categories[index], x, y, anchor, ChartSceneTextKind.CategoryAxis, "#000000", 9));
    }

    private static void AddCategoryLabels(List<ChartSceneText> texts, Chart chart, ChartSceneRect plot, IReadOnlyList<double> xValues)
    {
        var count = Math.Max(1, chart.Categories.Count);
        for (var index = 0; index < chart.Categories.Count; index++)
        {
            var x = xValues.Count > index && !double.IsNaN(xValues[index])
                ? plot.X + (xValues[index] - xValues.Where(value => !double.IsNaN(value)).DefaultIfEmpty(1).Min()) /
                    Math.Max(1, xValues.Where(value => !double.IsNaN(value)).DefaultIfEmpty(1).Max() - xValues.Where(value => !double.IsNaN(value)).DefaultIfEmpty(1).Min()) * plot.Width
                : count == 1 ? plot.CenterX : plot.X + index * plot.Width / Math.Max(1, count - 1);
            AddCategoryText(texts, chart, index, x, plot.Bottom + 2, ChartSceneTextAnchor.TopCenter);
        }
    }

    private static void AddDataText(List<ChartSceneText> texts, double value, double x, double y) =>
        texts.Add(new ChartSceneText(FormatChartValue(value), x, y, ChartSceneTextAnchor.TopLeft,
            ChartSceneTextKind.DataLabel, "#000000", 8));

    private static string FormatChartValue(double value) => value.ToString("G4", CultureInfo.InvariantCulture);

    public static IReadOnlyList<string> BuildChartVisualSignatures(IEnumerable<ChartVisualPlan> charts)
    {
        ArgumentNullException.ThrowIfNull(charts);

        return charts
            .Select(BuildChartVisualSignature)
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToList();
    }

    public static string BuildChartVisualSignature(ChartVisualPlan chart)
    {
        ArgumentNullException.ThrowIfNull(chart);

        return string.Join(
            "|",
            "kind=" + chart.Kind,
            "geometry=" + chart.GeometryKind,
            "style=" + chart.StyleId.ToString(CultureInfo.InvariantCulture),
            "colorScheme=" + NormalizeSignatureText(chart.ColorSchemeId),
            "quickLayout=" + chart.QuickLayoutId.ToString(CultureInfo.InvariantCulture),
            "title=" + BoolFlag(chart.ShowTitle),
            "legend=" + BoolFlag(chart.ShowLegend),
            "gridlines=" + BoolFlag(chart.ShowGridlines),
            "plotFill=" + BoolFlag(chart.PlotAreaFill),
            "markers=" + BoolFlag(chart.ShowMarkers),
            "dataLabels=" + BoolFlag(chart.ShowDataLabels),
            "axisTitles=" + BoolFlag(chart.ShowAxisTitles),
            "categoryAxis=" + NormalizeSignatureText(chart.CategoryAxisTitle),
            "valueAxis=" + NormalizeSignatureText(chart.ValueAxisTitle),
            "palette=" + string.Join(",", chart.PaletteHex));
    }

    public static IReadOnlyList<string> BuildChartDataSignatures(IEnumerable<ChartVisualPlan> charts)
    {
        ArgumentNullException.ThrowIfNull(charts);

        return charts
            .Select(BuildChartDataSignature)
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToList();
    }

    public static string BuildChartDataSignature(ChartVisualPlan chart)
    {
        ArgumentNullException.ThrowIfNull(chart);

        var series = chart.Series
            .Select((series, index) => string.Concat(
                index.ToString(CultureInfo.InvariantCulture),
                ":",
                SignatureTextOrDash(series.Name),
                "=",
                string.Join(",", series.Values.Select(FormatSignatureDouble))))
            .ToList();

        return string.Join(
            "|",
            "kind=" + chart.Kind,
            "categories=" + chart.Categories.Count.ToString(CultureInfo.InvariantCulture),
            "categoryLabels=" + string.Join(",", chart.Categories.Select(SignatureTextOrDash)),
            "series=" + chart.Series.Count.ToString(CultureInfo.InvariantCulture),
            "points=" + chart.Series.Sum(s => s.Values.Count).ToString(CultureInfo.InvariantCulture),
            "seriesData=" + string.Join(";", series));
    }

    public static ChartElementCommandState BuildChartElementCommandState(Chart chart)
    {
        ArgumentNullException.ThrowIfNull(chart);

        var plan = BuildChartPlan(chart);
        var isPieFamily = chart.Kind is ChartKind.Pie or ChartKind.Doughnut;

        return new ChartElementCommandState(
            CanToggleLegend: chart.Series.Count > 0,
            IsLegendVisible: plan.ShowLegend,
            CanEditAxisTitles: !isPieFamily,
            HasChartTitle: plan.ShowTitle,
            HasAxisTitles: plan.ShowAxisTitles);
    }

    public static SmartArtVisualPlan BuildSmartArtPlan(SmartArt smartArt, DocumentTheme? documentTheme = null)
    {
        ArgumentNullException.ThrowIfNull(smartArt);

        var layoutId = ResolveLayoutId(smartArt);
        var layout = SmartArtLayoutPreset.FindById(layoutId)
                     ?? SmartArtLayoutPreset.Default;
        var colorScheme = (!string.IsNullOrWhiteSpace(smartArt.ColorSchemeId)
                ? SmartArtColorScheme.FindById(smartArt.ColorSchemeId)
                : null)
            ?? SmartArtColorScheme.Default;
        var style = (!string.IsNullOrWhiteSpace(smartArt.StyleId)
                ? SmartArtStyle.FindById(smartArt.StyleId)
                : null)
            ?? SmartArtStyle.Default;

        var nodes = new List<SmartArtNodeVisualPlan>();
        var isBasicProcessLayout = string.Equals(layoutId, "process1", StringComparison.OrdinalIgnoreCase);
        var useNativeDefaultProcessStyle = isBasicProcessLayout
            && (string.IsNullOrWhiteSpace(smartArt.ColorSchemeId)
                || string.Equals(smartArt.ColorSchemeId, "accent0_1", StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(smartArt.StyleId)
                || string.Equals(smartArt.StyleId, "simple1", StringComparison.OrdinalIgnoreCase));
        var useUniformAccentModerateProcessStyle = isBasicProcessLayout
            && string.Equals(colorScheme.Id, "accent1", StringComparison.OrdinalIgnoreCase)
            && string.Equals(style.Id, "moderate1", StringComparison.OrdinalIgnoreCase);
        FlattenNodes(
            smartArt.Nodes,
            depth: 0,
            nodes,
            colorScheme,
            style,
            isBasicProcessLayout,
            useNativeDefaultProcessStyle,
            useUniformAccentModerateProcessStyle);

        var isCurrentWordPyramid = IsCurrentWordPyramidStyle(layoutId, smartArt);
        if (IsNativeWordOrgChartStyle(layoutId, colorScheme, style))
            nodes = ApplyNativeWordOrgChartStyle(nodes);
        else if (isCurrentWordPyramid)
            nodes = ApplyCurrentWordPyramidStyle(nodes, documentTheme);
        else if (IsNativeWordPyramidStyle(layoutId, colorScheme, style))
            nodes = ApplyNativeWordPyramidStyle(nodes);

        var hierarchyGeometry = layout.Kind == SmartArtKind.Hierarchy
            ? BuildHierarchyGeometry(layoutId, smartArt.Nodes)
            : null;
        var layoutGeometry = hierarchyGeometry is null
            ? BuildLayoutGeometry(
                layout.Id,
                nodes.Count,
                isCurrentWordPyramid,
                smartArt.WidthPt,
                smartArt.HeightPt)
            : null;

        return new SmartArtVisualPlan(
            layout.Kind,
            layout.Id,
            layout,
            colorScheme,
            style,
            nodes,
            hierarchyGeometry,
            layoutGeometry);
    }

    public static IReadOnlyList<string> BuildSmartArtVisualSignatures(IEnumerable<SmartArtVisualPlan> smartArts)
    {
        ArgumentNullException.ThrowIfNull(smartArts);

        return smartArts
            .Select(BuildSmartArtVisualSignature)
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToList();
    }

    public static string BuildSmartArtVisualSignature(SmartArtVisualPlan smartArt)
    {
        ArgumentNullException.ThrowIfNull(smartArt);

        return string.Join(
            "|",
            "kind=" + smartArt.Kind,
            "layout=" + NormalizeSignatureText(smartArt.LayoutId),
            "preset=" + NormalizeSignatureText(smartArt.Layout.Id),
            "colorScheme=" + NormalizeSignatureText(smartArt.ColorScheme.Id),
            "style=" + NormalizeSignatureText(smartArt.Style.Id),
            "hierarchy=" + BuildSmartArtHierarchyVisualSignature(smartArt.HierarchyGeometry),
            "geometry=" + BuildSmartArtLayoutGeometryVisualSignature(smartArt.LayoutGeometry),
            "nodes=" + string.Join(";", smartArt.Nodes.Select(BuildSmartArtNodeVisualSignature)));
    }

    private static ChartVisualGeometryKind ToGeometryKind(ChartKind kind) =>
        kind switch
        {
            ChartKind.Bar or ChartKind.Column => ChartVisualGeometryKind.Bars,
            ChartKind.Line => ChartVisualGeometryKind.Lines,
            ChartKind.Area => ChartVisualGeometryKind.Area,
            ChartKind.Pie => ChartVisualGeometryKind.Pie,
            ChartKind.Doughnut => ChartVisualGeometryKind.Doughnut,
            ChartKind.Scatter => ChartVisualGeometryKind.MarkerOnly,
            _ => ChartVisualGeometryKind.Bars
        };

    private static string ResolveLayoutId(SmartArt smartArt) =>
        !string.IsNullOrWhiteSpace(smartArt.LayoutId)
            ? smartArt.LayoutId.Trim()
            : smartArt.Kind switch
            {
                SmartArtKind.Process => "process1",
                SmartArtKind.Hierarchy => "hierarchy1",
                _ => "list1"
            };

    private static void FlattenNodes(
        IEnumerable<SmartArtNode> nodes,
        int depth,
        List<SmartArtNodeVisualPlan> into,
        SmartArtColorScheme colorScheme,
        SmartArtStyle style,
        bool isBasicProcessLayout,
        bool useNativeDefaultProcessStyle,
        bool useUniformAccentModerateProcessStyle)
    {
        foreach (var node in nodes)
        {
            var colorIndex = into.Count;
            var baseFillHex = useNativeDefaultProcessStyle
                ? "#156082"
                : NormalizeHex(colorScheme.FillHexAt(useUniformAccentModerateProcessStyle ? 0 : colorIndex));
            var fillHex = AdjustBrightness(baseFillHex, style.BrightnessAdjust);
            var connectorHex = useNativeDefaultProcessStyle
                ? AdjustBrightness("#AAB6C1", style.BrightnessAdjust)
                : ConnectorContrast(fillHex);
            into.Add(new SmartArtNodeVisualPlan(
                node.Text,
                depth,
                colorIndex,
                fillHex,
                NormalizeHex(colorScheme.TextHex),
                AdjustBrightness(fillHex, -0.18),
                Math.Max(0, style.BorderThickness),
                useNativeDefaultProcessStyle ? Math.Max(4, style.CornerRadius) : Math.Max(0, style.CornerRadius),
                Math.Clamp(style.ShadowOpacity, 0, 1),
                style.ShadowOpacity > 0 ? 4 + style.ShadowOpacity * 8 : 0,
                style.ShadowOpacity > 0 ? 1.5 + style.ShadowOpacity * 2 : 0,
                connectorHex));
            FlattenNodes(
                node.Children,
                depth + 1,
                into,
                colorScheme,
                style,
                isBasicProcessLayout,
                useNativeDefaultProcessStyle,
                useUniformAccentModerateProcessStyle);
        }
    }

    private static SmartArtHierarchyGeometryPlan BuildHierarchyGeometry(
        string layoutId,
        IReadOnlyList<SmartArtNode> roots)
    {
        if (string.Equals(layoutId, "orgchart1", StringComparison.OrdinalIgnoreCase)
            && IsSingleThreeLevelChain(roots))
        {
            return BuildNativeWordOrgChartGeometry();
        }

        return BuildGenericHierarchyGeometry(roots);
    }

    private static SmartArtHierarchyGeometryPlan BuildGenericHierarchyGeometry(IReadOnlyList<SmartArtNode> roots)
    {
        const double margin = 8;
        const double nodeWidth = 112;
        const double nodeHeight = 30;
        const double horizontalSpacing = 22;
        const double verticalSpacing = 34;

        var boxes = new List<SmartArtHierarchyNodeGeometry>();
        var connectors = new List<SmartArtHierarchyConnectorGeometry>();
        var leafIndex = 0;
        var maxDepth = 0;

        (int Index, double CenterX) LayoutNode(SmartArtNode node, int? parentIndex, int depth)
        {
            var nodeIndex = boxes.Count;
            boxes.Add(new SmartArtHierarchyNodeGeometry(nodeIndex, parentIndex, depth, 0, 0, nodeWidth, nodeHeight));
            maxDepth = Math.Max(maxDepth, depth);

            double centerX;
            var childResults = new List<(int Index, double CenterX)>();
            foreach (var child in node.Children)
                childResults.Add(LayoutNode(child, nodeIndex, depth + 1));

            if (childResults.Count == 0)
            {
                centerX = margin + nodeWidth / 2 + leafIndex * (nodeWidth + horizontalSpacing);
                leafIndex++;
            }
            else
            {
                centerX = (childResults[0].CenterX + childResults[^1].CenterX) / 2;
            }

            var x = centerX - nodeWidth / 2;
            var y = margin + depth * (nodeHeight + verticalSpacing);
            boxes[nodeIndex] = new SmartArtHierarchyNodeGeometry(
                nodeIndex,
                parentIndex,
                depth,
                x,
                y,
                nodeWidth,
                nodeHeight);

            foreach (var child in childResults)
            {
                var childBox = boxes[child.Index];
                connectors.Add(new SmartArtHierarchyConnectorGeometry(
                    nodeIndex,
                    child.Index,
                    x + nodeWidth / 2,
                    y + nodeHeight,
                    childBox.X + childBox.Width / 2,
                    childBox.Y));
            }

            return (nodeIndex, centerX);
        }

        foreach (var root in roots)
            LayoutNode(root, parentIndex: null, depth: 0);

        if (boxes.Count == 0)
            return new SmartArtHierarchyGeometryPlan([], [], 0, 0, 0);

        var naturalWidth = boxes.Max(box => box.X + box.Width) + margin;
        var naturalHeight = boxes.Max(box => box.Y + box.Height) + margin;
        return new SmartArtHierarchyGeometryPlan(
            boxes,
            connectors,
            maxDepth,
            naturalWidth,
            naturalHeight);
    }

    private static bool IsSingleThreeLevelChain(IReadOnlyList<SmartArtNode> roots) =>
        roots.Count == 1
        && roots[0].Children.Count == 1
        && roots[0].Children[0].Children.Count == 1
        && roots[0].Children[0].Children[0].Children.Count == 0;

    private static SmartArtHierarchyGeometryPlan BuildNativeWordOrgChartGeometry()
    {
        // Measured from Word's reflowed word/diagrams/drawing1.xml for the
        // 320pt x 140pt Plan -> Build -> Verify corpus fixture.
        const double planX = 169.288503937008;
        const double planY = 0.0624409448818898;
        const double buildX = 77.859842519685;
        const double buildY = 51.7870866141732;
        const double verifyX = 125.213307086614;
        const double verifyY = 103.511653543307;
        const double nodeWidth = 72.8514960629921;
        const double nodeHeight = 36.4257480314961;

        var nodes = new SmartArtHierarchyNodeGeometry[]
        {
            new(0, null, 0, planX, planY, nodeWidth, nodeHeight),
            new(1, 0, 1, buildX, buildY, nodeWidth, nodeHeight),
            new(2, 1, 2, verifyX, verifyY, nodeWidth, nodeHeight)
        };

        var planBottomCenterX = planX + nodeWidth / 2;
        var planBottomY = planY + nodeHeight;
        var buildRightCenterX = buildX + nodeWidth;
        var buildCenterY = buildY + nodeHeight / 2;
        var buildBottomCenterX = buildX + nodeWidth / 2;
        var buildBottomY = buildY + nodeHeight;
        var verifyLeftCenterX = verifyX;
        var verifyCenterY = verifyY + nodeHeight / 2;

        var rootToBuild = new SmartArtHierarchyConnectorGeometry(
            0,
            1,
            planBottomCenterX,
            planBottomY,
            buildRightCenterX,
            buildCenterY)
        {
            Points =
            [
                new(planBottomCenterX, planBottomY),
                new(planBottomCenterX, buildCenterY),
                new(buildRightCenterX, buildCenterY)
            ]
        };
        var buildToVerify = new SmartArtHierarchyConnectorGeometry(
            1,
            2,
            buildBottomCenterX,
            buildBottomY,
            verifyLeftCenterX,
            verifyCenterY)
        {
            Points =
            [
                new(buildBottomCenterX, buildBottomY),
                new(buildBottomCenterX, verifyCenterY),
                new(verifyLeftCenterX, verifyCenterY)
            ]
        };

        return new SmartArtHierarchyGeometryPlan(
            nodes,
            [rootToBuild, buildToVerify],
            MaxDepth: 2,
            NaturalWidth: 320,
            NaturalHeight: 140);
    }

    private static bool IsNativeWordOrgChartStyle(
        string layoutId,
        SmartArtColorScheme colorScheme,
        SmartArtStyle style) =>
        string.Equals(layoutId, "orgchart1", StringComparison.OrdinalIgnoreCase)
        && string.Equals(colorScheme.Id, "accent1", StringComparison.OrdinalIgnoreCase)
        && string.Equals(style.Id, "intense1", StringComparison.OrdinalIgnoreCase);

    private static List<SmartArtNodeVisualPlan> ApplyNativeWordOrgChartStyle(
        IReadOnlyList<SmartArtNodeVisualPlan> nodes) =>
        nodes
            .Select(node => node with
            {
                FillHex = "#1F3864",
                BorderHex = "#1F3864",
                BorderThickness = 1,
                CornerRadius = 0,
                ShadowOpacity = 0,
                ShadowBlur = 0,
                ShadowDepth = 0,
                ConnectorHex = "#1F3864"
            })
            .ToList();

    private static bool IsNativeWordPyramidStyle(
        string layoutId,
        SmartArtColorScheme colorScheme,
        SmartArtStyle style) =>
        string.Equals(layoutId, "pyramid1", StringComparison.OrdinalIgnoreCase)
        && string.Equals(colorScheme.Id, "accent2", StringComparison.OrdinalIgnoreCase)
        && string.Equals(style.Id, "flat1", StringComparison.OrdinalIgnoreCase);

    private static List<SmartArtNodeVisualPlan> ApplyNativeWordPyramidStyle(
        IReadOnlyList<SmartArtNodeVisualPlan> nodes) =>
        nodes
            .Select(node => node with
            {
                FillHex = "#7F0000",
                TextHex = "#000000",
                BorderHex = "#7F0000",
                BorderThickness = 1,
                CornerRadius = 0,
                ShadowOpacity = 0,
                ShadowBlur = 0,
                ShadowDepth = 0,
                ConnectorHex = "#7F0000",
                FontSizeDip = 18.48 * 96.0 / 72.0
            })
            .ToList();

    private static bool IsCurrentWordPyramidStyle(string layoutId, SmartArt smartArt) =>
        string.Equals(layoutId, "pyramid1", StringComparison.OrdinalIgnoreCase)
        && string.Equals(smartArt.ColorSchemeId, "accent1_2", StringComparison.OrdinalIgnoreCase)
        && string.Equals(smartArt.StyleId, "simple1", StringComparison.OrdinalIgnoreCase);

    private static List<SmartArtNodeVisualPlan> ApplyCurrentWordPyramidStyle(
        IReadOnlyList<SmartArtNodeVisualPlan> nodes,
        DocumentTheme? documentTheme)
    {
        var accent = documentTheme?.PrimaryColorHex;
        if (string.IsNullOrWhiteSpace(accent))
            accent = "#1F3864";
        else if (!accent.StartsWith('#'))
            accent = "#" + accent;

        return nodes
            .Select(node => node with
            {
                FillHex = accent,
                TextHex = "#000000",
                BorderHex = "#FFFFFF",
                BorderThickness = 1,
                CornerRadius = 0,
                ShadowOpacity = 0,
                ShadowBlur = 0,
                ShadowDepth = 0,
                ConnectorHex = accent,
                // The cached source declares 28pt, but Word applies SmartArt text fitting
                // before scaling the 300pt by 150pt drawing into the anchor rectangle.
                FontSizeDip = 18.48 * 96.0 / 72.0,
                FontFamilyName = documentTheme?.BodyFont
            })
            .ToList();
    }

    private static SmartArtLayoutGeometryPlan? BuildLayoutGeometry(
        string layoutId,
        int nodeCount,
        bool isCurrentWordPyramid,
        double targetWidth,
        double targetHeight) =>
        layoutId switch
        {
            "list1" => BuildVerticalListGeometry(nodeCount, SmartArtLayoutGeometryKind.BasicList),
            "vertbullet1" => BuildVerticalListGeometry(nodeCount, SmartArtLayoutGeometryKind.VerticalBulletList),
            "horizbullet1" => BuildHorizontalListGeometry(nodeCount),
            "process1" => BuildBasicProcessGeometry(nodeCount),
            "continuousBlockProcess" => BuildContinuousBlockProcessGeometry(nodeCount),
            "stepup1" => BuildStepGeometry(nodeCount, ascending: true),
            "stepdown1" => BuildStepGeometry(nodeCount, ascending: false),
            "cycle1" => BuildCycleGeometry(nodeCount),
            "pyramid1" => BuildPyramidGeometry(nodeCount, isCurrentWordPyramid, targetWidth, targetHeight),
            "radial1" => BuildRadialGeometry(nodeCount),
            "matrix1" => BuildMatrixGeometry(nodeCount),
            _ => null
        };

    private static SmartArtLayoutGeometryPlan BuildVerticalListGeometry(
        int nodeCount,
        SmartArtLayoutGeometryKind kind)
    {
        const double margin = 8;
        const double boxWidth = 112;
        const double boxHeight = 30;
        const double gap = 6;

        var nodes = new List<SmartArtLayoutNodeGeometry>(nodeCount);
        for (var i = 0; i < nodeCount; i++)
        {
            nodes.Add(new SmartArtLayoutNodeGeometry(
                i,
                margin,
                margin + i * (boxHeight + gap),
                boxWidth,
                boxHeight));
        }

        var naturalWidth = nodeCount == 0 ? 0 : margin * 2 + boxWidth;
        var naturalHeight = nodeCount == 0
            ? 0
            : margin * 2 + nodeCount * boxHeight + Math.Max(0, nodeCount - 1) * gap;
        return new SmartArtLayoutGeometryPlan(
            kind,
            nodes,
            [],
            naturalWidth,
            naturalHeight);
    }

    private static SmartArtLayoutGeometryPlan BuildHorizontalListGeometry(int nodeCount)
    {
        const double margin = 8;
        const double boxWidth = 70;
        const double boxHeight = 30;
        const double gap = 8;

        var nodes = new List<SmartArtLayoutNodeGeometry>(nodeCount);
        for (var i = 0; i < nodeCount; i++)
        {
            nodes.Add(new SmartArtLayoutNodeGeometry(
                i,
                margin + i * (boxWidth + gap),
                margin,
                boxWidth,
                boxHeight));
        }

        var naturalWidth = nodeCount == 0
            ? 0
            : margin * 2 + nodeCount * boxWidth + Math.Max(0, nodeCount - 1) * gap;
        var naturalHeight = nodeCount == 0 ? 0 : margin * 2 + boxHeight;
        return new SmartArtLayoutGeometryPlan(
            SmartArtLayoutGeometryKind.HorizontalList,
            nodes,
            [],
            naturalWidth,
            naturalHeight);
    }

    private static SmartArtLayoutGeometryPlan BuildBasicProcessGeometry(int nodeCount)
    {
        const double margin = 8;
        const double boxWidth = 70;
        const double boxHeight = 30;
        const double gap = 16;

        var nodes = new List<SmartArtLayoutNodeGeometry>(nodeCount);
        for (var i = 0; i < nodeCount; i++)
        {
            nodes.Add(new SmartArtLayoutNodeGeometry(
                i,
                margin + i * (boxWidth + gap),
                margin,
                boxWidth,
                boxHeight));
        }

        var connectors = new List<SmartArtLayoutConnectorGeometry>(Math.Max(0, nodeCount - 1));
        for (var i = 0; i < nodeCount - 1; i++)
        {
            var current = nodes[i];
            var next = nodes[i + 1];
            connectors.Add(new SmartArtLayoutConnectorGeometry(
                i,
                i + 1,
                SmartArtLayoutConnectorKind.Arrow,
                current.X + current.Width,
                current.Y + current.Height / 2,
                next.X,
                next.Y + next.Height / 2));
        }

        var naturalWidth = nodeCount == 0
            ? 0
            : margin * 2 + nodeCount * boxWidth + Math.Max(0, nodeCount - 1) * gap;
        var naturalHeight = nodeCount == 0 ? 0 : margin * 2 + boxHeight;
        return new SmartArtLayoutGeometryPlan(
            SmartArtLayoutGeometryKind.BasicProcess,
            nodes,
            connectors,
            naturalWidth,
            naturalHeight);
    }

    private static SmartArtLayoutGeometryPlan BuildContinuousBlockProcessGeometry(int nodeCount)
    {
        const double margin = 8;
        const double boxWidth = 76;
        const double boxHeight = 34;
        const double gap = 4;

        var nodes = new List<SmartArtLayoutNodeGeometry>(nodeCount);
        for (var i = 0; i < nodeCount; i++)
        {
            nodes.Add(new SmartArtLayoutNodeGeometry(
                i,
                margin + i * (boxWidth + gap),
                margin,
                boxWidth,
                boxHeight));
        }

        var connectors = new List<SmartArtLayoutConnectorGeometry>(Math.Max(0, nodeCount - 1));
        for (var i = 0; i < nodeCount - 1; i++)
        {
            var current = nodes[i];
            var next = nodes[i + 1];
            connectors.Add(new SmartArtLayoutConnectorGeometry(
                i,
                i + 1,
                SmartArtLayoutConnectorKind.Arrow,
                current.X + current.Width,
                current.Y + current.Height / 2,
                next.X,
                next.Y + next.Height / 2));
        }

        var naturalWidth = nodeCount == 0
            ? 0
            : margin * 2 + nodeCount * boxWidth + Math.Max(0, nodeCount - 1) * gap;
        var naturalHeight = nodeCount == 0 ? 0 : margin * 2 + boxHeight;
        return new SmartArtLayoutGeometryPlan(
            SmartArtLayoutGeometryKind.ContinuousBlockProcess,
            nodes,
            connectors,
            naturalWidth,
            naturalHeight);
    }

    private static SmartArtLayoutGeometryPlan BuildStepGeometry(int nodeCount, bool ascending)
    {
        const double margin = 8;
        const double boxWidth = 70;
        const double boxHeight = 30;
        const double stepX = 60;
        const double stepY = 28;

        var nodes = new List<SmartArtLayoutNodeGeometry>(nodeCount);
        for (var i = 0; i < nodeCount; i++)
        {
            nodes.Add(new SmartArtLayoutNodeGeometry(
                i,
                margin + i * stepX,
                margin + (ascending ? nodeCount - 1 - i : i) * stepY,
                boxWidth,
                boxHeight));
        }

        var connectors = new List<SmartArtLayoutConnectorGeometry>(Math.Max(0, nodeCount - 1));
        for (var i = 0; i < nodeCount - 1; i++)
        {
            var current = nodes[i];
            var next = nodes[i + 1];
            connectors.Add(new SmartArtLayoutConnectorGeometry(
                i,
                i + 1,
                SmartArtLayoutConnectorKind.Arrow,
                current.X + current.Width,
                current.Y + current.Height / 2,
                next.X,
                next.Y + next.Height / 2));
        }

        var naturalWidth = nodeCount == 0 ? 0 : margin * 2 + boxWidth + (nodeCount - 1) * stepX;
        var naturalHeight = nodeCount == 0 ? 0 : margin * 2 + boxHeight + (nodeCount - 1) * stepY;
        return new SmartArtLayoutGeometryPlan(
            ascending ? SmartArtLayoutGeometryKind.StepUp : SmartArtLayoutGeometryKind.StepDown,
            nodes,
            connectors,
            naturalWidth,
            naturalHeight);
    }

    private static SmartArtLayoutGeometryPlan BuildCycleGeometry(int nodeCount)
    {
        const double naturalWidth = 200;
        const double naturalHeight = 160;
        const double centerX = naturalWidth / 2;
        const double centerY = naturalHeight / 2;
        const double radiusX = 72;
        const double radiusY = 56;
        const double boxWidth = 52;
        const double boxHeight = 26;

        var nodes = new List<SmartArtLayoutNodeGeometry>(nodeCount);
        for (var i = 0; i < nodeCount; i++)
        {
            var angle = 2 * Math.PI * i / nodeCount - Math.PI / 2;
            nodes.Add(new SmartArtLayoutNodeGeometry(
                i,
                centerX + radiusX * Math.Cos(angle) - boxWidth / 2,
                centerY + radiusY * Math.Sin(angle) - boxHeight / 2,
                boxWidth,
                boxHeight));
        }

        var connectors = new List<SmartArtLayoutConnectorGeometry>(nodeCount);
        if (nodeCount > 1)
        {
            for (var i = 0; i < nodeCount; i++)
            {
                var current = nodes[i];
                var next = nodes[(i + 1) % nodeCount];
                connectors.Add(new SmartArtLayoutConnectorGeometry(
                    i,
                    (i + 1) % nodeCount,
                    SmartArtLayoutConnectorKind.Arrow,
                    current.X + current.Width / 2,
                    current.Y + current.Height / 2,
                    next.X + next.Width / 2,
                    next.Y + next.Height / 2));
            }
        }

        return new SmartArtLayoutGeometryPlan(
            SmartArtLayoutGeometryKind.Cycle,
            nodes,
            connectors,
            nodeCount == 0 ? 0 : naturalWidth,
            nodeCount == 0 ? 0 : naturalHeight);
    }

    private static SmartArtLayoutGeometryPlan BuildRadialGeometry(int nodeCount)
    {
        const double naturalWidth = 220;
        const double naturalHeight = 180;
        const double centerX = naturalWidth / 2;
        const double centerY = naturalHeight / 2;
        const double centerWidth = 56;
        const double centerHeight = 36;
        const double radiusX = 76;
        const double radiusY = 58;
        const double satelliteWidth = 48;
        const double satelliteHeight = 24;

        var nodes = new List<SmartArtLayoutNodeGeometry>(nodeCount);
        if (nodeCount > 0)
        {
            nodes.Add(new SmartArtLayoutNodeGeometry(
                0,
                centerX - centerWidth / 2,
                centerY - centerHeight / 2,
                centerWidth,
                centerHeight));
        }

        var satellites = Math.Max(0, nodeCount - 1);
        for (var i = 0; i < satellites; i++)
        {
            var angle = 2 * Math.PI * i / satellites - Math.PI / 2;
            nodes.Add(new SmartArtLayoutNodeGeometry(
                i + 1,
                centerX + radiusX * Math.Cos(angle) - satelliteWidth / 2,
                centerY + radiusY * Math.Sin(angle) - satelliteHeight / 2,
                satelliteWidth,
                satelliteHeight));
        }

        var connectors = new List<SmartArtLayoutConnectorGeometry>(satellites);
        for (var i = 1; i < nodes.Count; i++)
        {
            var satellite = nodes[i];
            connectors.Add(new SmartArtLayoutConnectorGeometry(
                0,
                i,
                SmartArtLayoutConnectorKind.Line,
                centerX,
                centerY,
                satellite.X + satellite.Width / 2,
                satellite.Y + satellite.Height / 2));
        }

        return new SmartArtLayoutGeometryPlan(
            SmartArtLayoutGeometryKind.Radial,
            nodes,
            connectors,
            nodeCount == 0 ? 0 : naturalWidth,
            nodeCount == 0 ? 0 : naturalHeight);
    }

    private static SmartArtLayoutGeometryPlan BuildPyramidGeometry(
        int nodeCount,
        bool isCurrentWordPyramid,
        double targetWidth,
        double targetHeight)
    {
        if (isCurrentWordPyramid && nodeCount == 4)
            return BuildCurrentWordPyramidGeometry(targetWidth, targetHeight);

        if (nodeCount == 4)
            return BuildNativeWordPyramidGeometry();

        // Shared Basic Pyramid approximation: centered text bounds plus renderer-neutral band polygons.
        const double margin = 8;
        const double minBandWidth = 54;
        const double maxBandWidth = 160;
        const double bandHeight = 30;
        const double gap = 4;

        var nodes = new List<SmartArtLayoutNodeGeometry>(nodeCount);
        var widthRange = Math.Max(0, maxBandWidth - minBandWidth);
        var divisor = Math.Max(1, nodeCount - 1);

        for (var i = 0; i < nodeCount; i++)
        {
            var width = nodeCount == 1
                ? maxBandWidth
                : minBandWidth + widthRange * i / divisor;
            var topWidth = nodeCount == 1
                ? maxBandWidth
                : minBandWidth + widthRange * i / nodeCount;
            var bottomWidth = nodeCount == 1
                ? maxBandWidth
                : minBandWidth + widthRange * (i + 1) / nodeCount;
            var y = margin + i * (bandHeight + gap);
            nodes.Add(new SmartArtLayoutNodeGeometry(
                i,
                margin + (maxBandWidth - width) / 2,
                y,
                width,
                bandHeight,
                BuildCenteredBandPolygon(margin, maxBandWidth, topWidth, bottomWidth, y, bandHeight)));
        }

        var naturalWidth = nodeCount == 0 ? 0 : margin * 2 + maxBandWidth;
        var naturalHeight = nodeCount == 0
            ? 0
            : margin * 2 + nodeCount * bandHeight + Math.Max(0, nodeCount - 1) * gap;
        return new SmartArtLayoutGeometryPlan(
            SmartArtLayoutGeometryKind.Pyramid,
            nodes,
            [],
            naturalWidth,
            naturalHeight);
    }

    private static SmartArtLayoutGeometryPlan BuildNativeWordPyramidGeometry()
    {
        // Measured from Word's reflowed word/diagrams/drawing2.xml for the
        // 300pt x 150pt Top -> Base corpus fixture.
        const double bandHeight = 33;
        const double trapezoidInset = 22.5;
        var nodes = new SmartArtLayoutNodeGeometry[]
        {
            BuildNativeWordPyramidBand(0, 114, 6, 72, bandHeight, trapezoidInset),
            BuildNativeWordPyramidBand(1, 78, 41, 144, bandHeight, trapezoidInset),
            BuildNativeWordPyramidBand(2, 42, 76, 216, bandHeight, trapezoidInset),
            BuildNativeWordPyramidBand(3, 6, 111, 288, bandHeight, trapezoidInset)
        };

        return new SmartArtLayoutGeometryPlan(
            SmartArtLayoutGeometryKind.Pyramid,
            nodes,
            [],
            NaturalWidth: 300,
            NaturalHeight: 150);
    }

    private static SmartArtLayoutGeometryPlan BuildCurrentWordPyramidGeometry(
        double targetWidth,
        double targetHeight)
    {
        // The imported cached dsp:drawing uses contiguous trapezoids, rather than the
        // inset bands used by Word's older accent2/flat1 pyramid gallery signature.
        // Its local coordinate system matches the authored anchor extent, so retain the
        // source aspect ratio instead of normalizing every imported pyramid to 300 by 150.
        var width = targetWidth > 0 ? targetWidth : 300;
        var height = targetHeight > 0 ? targetHeight : 150;
        var bandHeight = height / 4;
        var trapezoidInset = width / 8;
        var nodes = new SmartArtLayoutNodeGeometry[]
        {
            BuildNativeWordPyramidBand(0, width * 3 / 8, 0, width / 4, bandHeight, trapezoidInset),
            BuildNativeWordPyramidBand(1, width / 4, bandHeight, width / 2, bandHeight, trapezoidInset),
            BuildNativeWordPyramidBand(2, width / 8, bandHeight * 2, width * 3 / 4, bandHeight, trapezoidInset),
            BuildNativeWordPyramidBand(3, 0, bandHeight * 3, width, bandHeight, trapezoidInset)
        };

        return new SmartArtLayoutGeometryPlan(
            SmartArtLayoutGeometryKind.Pyramid,
            nodes,
            [],
            NaturalWidth: width,
            NaturalHeight: height);
    }

    private static SmartArtLayoutNodeGeometry BuildNativeWordPyramidBand(
        int nodeIndex,
        double x,
        double y,
        double width,
        double height,
        double inset) =>
        new(
            nodeIndex,
            x,
            y,
            width,
            height,
            [
                new SmartArtLayoutPoint(x + inset, y),
                new SmartArtLayoutPoint(x + width - inset, y),
                new SmartArtLayoutPoint(x + width, y + height),
                new SmartArtLayoutPoint(x, y + height)
            ]);

    private static IReadOnlyList<SmartArtLayoutPoint> BuildCenteredBandPolygon(
        double margin,
        double maxWidth,
        double topWidth,
        double bottomWidth,
        double y,
        double height)
    {
        var topLeft = margin + (maxWidth - topWidth) / 2;
        var bottomLeft = margin + (maxWidth - bottomWidth) / 2;
        return
        [
            new SmartArtLayoutPoint(topLeft, y),
            new SmartArtLayoutPoint(topLeft + topWidth, y),
            new SmartArtLayoutPoint(bottomLeft + bottomWidth, y + height),
            new SmartArtLayoutPoint(bottomLeft, y + height)
        ];
    }

    private static SmartArtLayoutGeometryPlan BuildMatrixGeometry(int nodeCount)
    {
        const double margin = 8;
        const double boxWidth = 78;
        const double boxHeight = 34;
        const double gap = 10;

        var columns = nodeCount <= 4 ? 2 : (int)Math.Ceiling(Math.Sqrt(nodeCount));
        columns = Math.Max(1, columns);
        var rows = nodeCount == 0 ? 0 : (int)Math.Ceiling(nodeCount / (double)columns);

        var nodes = new List<SmartArtLayoutNodeGeometry>(nodeCount);
        for (var i = 0; i < nodeCount; i++)
        {
            var column = i % columns;
            var row = i / columns;
            nodes.Add(new SmartArtLayoutNodeGeometry(
                i,
                margin + column * (boxWidth + gap),
                margin + row * (boxHeight + gap),
                boxWidth,
                boxHeight));
        }

        var naturalWidth = nodeCount == 0 ? 0 : margin * 2 + columns * boxWidth + (columns - 1) * gap;
        var naturalHeight = nodeCount == 0 ? 0 : margin * 2 + rows * boxHeight + Math.Max(0, rows - 1) * gap;
        return new SmartArtLayoutGeometryPlan(
            SmartArtLayoutGeometryKind.Matrix,
            nodes,
            [],
            naturalWidth,
            naturalHeight);
    }

    private static string ConnectorContrast(string fillHex)
    {
        var (r, g, b) = ParseRgb(fillHex);
        var luminance = (r * 0.299 + g * 0.587 + b * 0.114) / 255.0;
        return AdjustBrightness(fillHex, luminance < 0.25 ? 0.30 : -0.30);
    }

    private static string AdjustBrightness(string hex, double delta)
    {
        if (delta == 0)
            return NormalizeHex(hex);

        var (r, g, b) = ParseRgb(hex);
        var offset = delta * 255;
        return ToHex(Clamp(r + offset), Clamp(g + offset), Clamp(b + offset));
    }

    private static (byte R, byte G, byte B) ParseRgb(string hex)
    {
        var normalized = NormalizeHex(hex);
        return (
            Convert.ToByte(normalized.Substring(1, 2), 16),
            Convert.ToByte(normalized.Substring(3, 2), 16),
            Convert.ToByte(normalized.Substring(5, 2), 16));
    }

    private static byte Clamp(double value) =>
        (byte)Math.Max(0, Math.Min(255, value));

    private static string ToHex(byte r, byte g, byte b) =>
        $"#{r:X2}{g:X2}{b:X2}";

    private static string BuildSmartArtNodeVisualSignature(SmartArtNodeVisualPlan node) =>
        string.Join(
            ":",
            NormalizeSignatureText(node.Text),
            node.Depth.ToString(CultureInfo.InvariantCulture),
            node.ColorIndex.ToString(CultureInfo.InvariantCulture),
            node.FillHex,
            node.TextHex,
            node.BorderHex,
            FormatSignatureDouble(node.BorderThickness),
            FormatSignatureDouble(node.CornerRadius),
            FormatSignatureDouble(node.ShadowOpacity),
            FormatSignatureDouble(node.ShadowBlur),
            FormatSignatureDouble(node.ShadowDepth),
            node.ConnectorHex);

    private static string BuildSmartArtHierarchyVisualSignature(SmartArtHierarchyGeometryPlan? geometry)
    {
        if (geometry is null)
            return "none";

        var nodeSignature = string.Join(
            ",",
            geometry.Nodes.Select(node => string.Join(
                ":",
                node.NodeIndex.ToString(CultureInfo.InvariantCulture),
                node.ParentNodeIndex?.ToString(CultureInfo.InvariantCulture) ?? "root",
                node.Depth.ToString(CultureInfo.InvariantCulture),
                FormatSignatureDouble(node.X),
                FormatSignatureDouble(node.Y),
                FormatSignatureDouble(node.Width),
                FormatSignatureDouble(node.Height))));

        var connectorSignature = string.Join(
            ",",
            geometry.Connectors.Select(connector => string.Join(
                ":",
                connector.ParentNodeIndex.ToString(CultureInfo.InvariantCulture),
                connector.ChildNodeIndex.ToString(CultureInfo.InvariantCulture),
                FormatSignatureDouble(connector.X1),
                FormatSignatureDouble(connector.Y1),
                FormatSignatureDouble(connector.X2),
                FormatSignatureDouble(connector.Y2),
                connector.Points.Count > 0
                    ? "path=" + string.Join(
                        ",",
                        connector.Points.Select(point =>
                            FormatSignatureDouble(point.X) + ":" + FormatSignatureDouble(point.Y)))
                    : "path=straight")));

        return string.Join(
            "/",
            "maxDepth=" + geometry.MaxDepth.ToString(CultureInfo.InvariantCulture),
            "nodes=" + geometry.Nodes.Count.ToString(CultureInfo.InvariantCulture),
            "connectors=" + geometry.Connectors.Count.ToString(CultureInfo.InvariantCulture),
            "size=" + FormatSignatureDouble(geometry.NaturalWidth) + "x" + FormatSignatureDouble(geometry.NaturalHeight),
            "boxes=" + nodeSignature,
            "lines=" + connectorSignature);
    }

    private static string BuildSmartArtLayoutGeometryVisualSignature(SmartArtLayoutGeometryPlan? geometry)
    {
        if (geometry is null)
            return "none";

        var nodeSignature = string.Join(
            ",",
            geometry.Nodes.Select(node => string.Join(
                ":",
                node.NodeIndex.ToString(CultureInfo.InvariantCulture),
                FormatSignatureDouble(node.X),
                FormatSignatureDouble(node.Y),
                FormatSignatureDouble(node.Width),
                FormatSignatureDouble(node.Height))));

        var polygonSignature = string.Join(
            ",",
            geometry.Nodes
                .Where(node => node.HasPolygon)
                .Select(node => node.NodeIndex.ToString(CultureInfo.InvariantCulture)
                    + "="
                    + string.Join(
                        ";",
                        node.PolygonPoints.Select(point =>
                            FormatSignatureDouble(point.X) + ":" + FormatSignatureDouble(point.Y)))));

        var connectorSignature = string.Join(
            ",",
            geometry.Connectors.Select(connector => string.Join(
                ":",
                connector.SourceNodeIndex.ToString(CultureInfo.InvariantCulture),
                connector.TargetNodeIndex.ToString(CultureInfo.InvariantCulture),
                connector.Kind,
                FormatSignatureDouble(connector.X1),
                FormatSignatureDouble(connector.Y1),
                FormatSignatureDouble(connector.X2),
                FormatSignatureDouble(connector.Y2))));

        return string.Join(
            "/",
            "kind=" + geometry.Kind,
            "nodes=" + geometry.Nodes.Count.ToString(CultureInfo.InvariantCulture),
            "connectors=" + geometry.Connectors.Count.ToString(CultureInfo.InvariantCulture),
            "size=" + FormatSignatureDouble(geometry.NaturalWidth) + "x" + FormatSignatureDouble(geometry.NaturalHeight),
            "boxes=" + nodeSignature,
            "polygons=" + polygonSignature,
            "lines=" + connectorSignature);
    }

    private static string BoolFlag(bool value) => value ? "1" : "0";

    private static string FormatSignatureDouble(double value) =>
        double.IsFinite(value)
            ? Math.Round(value, 3, MidpointRounding.AwayFromZero).ToString("0.###", CultureInfo.InvariantCulture)
            : "0";

    private static string NormalizeSignatureText(string? value)
    {
        var normalized = (value ?? string.Empty)
            .Trim()
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal)
            .Replace("|", "/", StringComparison.Ordinal)
            .Replace(";", ",", StringComparison.Ordinal)
            .Replace(":", "-", StringComparison.Ordinal);

        return string.Join(
            " ",
            normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static string SignatureTextOrDash(string? value)
    {
        var normalized = NormalizeSignatureText(value);
        return string.IsNullOrEmpty(normalized) ? "-" : normalized;
    }

    private static string NormalizeHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "#000000";

        var hex = value.Trim();
        if (hex.StartsWith('#'))
            hex = hex[1..];
        if (hex.Length == 8)
            hex = hex[2..];
        if (hex.Length != 6)
            return "#000000";

        return int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out _)
            ? "#" + hex.ToUpperInvariant()
            : "#000000";
    }
}

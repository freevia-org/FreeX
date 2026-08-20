using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;

using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;

using AvaloniaPath = Avalonia.Controls.Shapes.Path;
using AvaloniaPoint = Avalonia.Point;
using AvaloniaPolygon = Avalonia.Controls.Shapes.Polygon;
using AvaloniaPolyline = Avalonia.Controls.Shapes.Polyline;
using AvaloniaRectangle = Avalonia.Controls.Shapes.Rectangle;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;
using ModelChartType = FreeX.Core.Model.ChartType;

namespace FreeX.App.Avalonia.Charts;

/// <summary>
/// Turns a portable <see cref="ChartLayout"/> into Avalonia visuals on a <see cref="Canvas"/>. All
/// positioning math is already done by the layout engine; this class is purely a painter mapping
/// geometry (<see cref="SeriesBar"/>, <see cref="SeriesPoint"/>, <see cref="SeriesSlice"/>, axes,
/// legend, data labels) onto shapes/text. Series colors come from the chart model's
/// <see cref="ChartModel.SeriesFormats"/>, falling back to a theme-derived Excel accent palette that
/// mirrors the WPF renderer's BuildExcelSeriesPalette (Accent1–6 × five tint rounds).
/// </summary>
public sealed class AvaloniaChartRenderer
{
    private const double MarkerRadius = 3.5;
    private const double AxisLabelFontSize = 10;
    private const double DefaultLegendFontSize = 11;
    private const double DefaultDataLabelFontSize = 10;
    private const double TickLength = 4;
    private const double AxisTitleFontSize = 11;

    // F19: fallback stroke thickness for Line/Radar series when the series has no explicit
    // ChartSeriesFormat.StrokeThickness override — matches the renderer's prior hardcoded value.
    private const double DefaultSeriesStrokeThickness = 2;
    private const double TrendlineAnnotationFontSize = 10;

    private static readonly IBrush AxisBrush = SolidBrush(0x59, 0x59, 0x59);
    private static readonly IBrush AxisLabelBrush = SolidBrush(0x40, 0x40, 0x40);
    private static readonly IBrush DefaultPlotBackground = SolidBrush(0xFF, 0xFF, 0xFF);
    private static readonly IBrush DefaultPlotBorderBrush = SolidBrush(0xD9, 0xD9, 0xD9);
    private static readonly IBrush DefaultDataLabelBrush = SolidBrush(0x40, 0x40, 0x40);
    private static readonly IBrush GridlineBrush = SolidBrush(0xDC, 0xDC, 0xDC);
    // R87-render-chart-plot-5-4: matches ChartRenderer.Axes.cs ApplyGridlineStyle's WPF default minor
    // gridline color (235, 235, 235) used when the chart model doesn't override it.
    private static readonly IBrush MinorGridlineBrush = SolidBrush(0xEB, 0xEB, 0xEB);
    private static readonly IBrush TrendlineBrush = SolidBrush(0x80, 0x80, 0x80);

    // Dash arrays for StrokeDashArray — values are in stroke-thickness units.
    // Dash: 4 on, 3 off. Dot: 1.5 on, 1.5 off.
    private static readonly AvaloniaList<double> DashArray = [4, 3];
    private static readonly AvaloniaList<double> DotArray = [1.5, 1.5];

    private readonly ChartModel _chart;
    private readonly WorkbookTheme _theme;

    // Lazily-built theme-derived palette (same algorithm as WPF BuildExcelSeriesPalette).
    private CellColor[]? _themePalette;

    public AvaloniaChartRenderer(ChartModel chart, WorkbookTheme theme)
    {
        _chart = chart ?? throw new ArgumentNullException(nameof(chart));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
    }

    // ── Public entry point ───────────────────────────────────────────────────

    /// <summary>
    /// Renders <paramref name="layout"/> into a fresh <see cref="Canvas"/> sized to
    /// <paramref name="width"/> x <paramref name="height"/> (the chart object's on-sheet pixel box).
    /// </summary>
    public Canvas Render(ChartLayout layout, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(layout);

        // Resolve chart-area fill (canvas background).
        // R44-meta-1: "No Fill" is an explicit user choice distinct from "nothing set" -- paint
        // nothing (transparent) instead of falling back to the opaque default white background.
        IBrush canvasBackground = _chart.IsChartAreaFillSuppressed
            ? Brushes.Transparent
            : _chart.ResolveChartAreaFillColor(_theme) is { } chartFill
                ? SolidBrush(chartFill)
                : SolidBrush(0xFF, 0xFF, 0xFF);

        var canvas = new Canvas
        {
            Width = Math.Max(1, width),
            Height = Math.Max(1, height),
            Background = canvasBackground,
            ClipToBounds = true,
            IsHitTestVisible = false,
        };

        // Chart-area border (on top of background, under everything else).
        AddChartAreaBorder(canvas, width, height);

        // Chart title — rendered above the plot, centered horizontally. Constrained to the space
        // already reserved above layout.PlotArea so a large title font (or a small chart box) never
        // overlaps the plot/top axis (see AddChartTitle).
        if (!string.IsNullOrWhiteSpace(_chart.Title))
            AddChartTitle(canvas, width, layout.PlotArea.Top);

        var isPie = layout.Series.Any(s => s.Kind == SeriesGeometryKind.PieSlices);
        if (!isPie)
            AddPlotBackground(canvas, layout.PlotArea);

        // Fix 1: Gridlines — draw before axes so the axis line draws on top.
        RenderGridlines(canvas, layout.ValueAxis, layout.PlotArea, isValueAxis: true);
        RenderGridlines(canvas, layout.CategoryAxis, layout.PlotArea, isValueAxis: false);

        RenderAxis(canvas, layout.ValueAxis, isValueAxis: true);
        RenderAxis(canvas, layout.CategoryAxis, isValueAxis: false);

        // Fix 3: Secondary axis — render right-side axis when present.
        // CE1: Do NOT draw gridlines for the secondary value axis — only the primary value axis
        // paints major gridlines, matching WPF (AddSecondaryAxisIfRequested sets MajorGridlineStyle=None)
        // and Excel default. The secondary axis still draws its ticks, labels, and axis line.
        if (layout.SecondaryValueAxis is not null)
        {
            RenderAxis(canvas, layout.SecondaryValueAxis, isValueAxis: true);
        }

        // Fix 2: Axis titles — render after axes so they don't overlap tick labels awkwardly.
        RenderAxisTitle(canvas, layout.ValueAxis, layout.PlotArea);
        RenderAxisTitle(canvas, layout.CategoryAxis, layout.PlotArea);
        if (layout.SecondaryValueAxis is not null)
            RenderAxisTitle(canvas, layout.SecondaryValueAxis, layout.PlotArea);

        var isTreemap = layout.Type == ModelChartType.Treemap;
        // Excel only applies "Vary colors by point" (c:varyColors) to bar/column charts when there
        // is exactly one plotted series (see ChartStylePlanner.ResolveVaryColorsPointFill) — count
        // the actual bar/column series in this layout (combo line/scatter overlays are laid out
        // with a different Kind and so are correctly excluded).
        var barSeriesCount = layout.Series.Count(s => s.Kind is SeriesGeometryKind.Columns or SeriesGeometryKind.Bars);
        foreach (var series in layout.Series)
            RenderSeries(canvas, series, isTreemap ? layout.DataLabels : [], barSeriesCount);

        RenderLegend(canvas, layout.Legend);
        // Treemap labels are rendered inline with white text inside RenderTreemapTiles.
        if (!isTreemap)
            RenderDataLabels(canvas, layout.DataLabels);

        return canvas;
    }

    // ── Chart-area border ────────────────────────────────────────────────────

    private void AddChartAreaBorder(Canvas canvas, double width, double height)
    {
        // R44-meta-1: "No Line" is an explicit user choice -- draw no border at all.
        if (_chart.IsChartAreaLineSuppressed)
            return;

        var borderColor = _chart.ResolveChartAreaBorderColor(_theme);
        var thickness = _chart.ChartAreaBorderThickness ?? 0;
        if (borderColor is null || thickness <= 0)
            return;

        var rect = new AvaloniaRectangle
        {
            Width = Math.Max(1, width),
            Height = Math.Max(1, height),
            Fill = Brushes.Transparent,
            Stroke = SolidBrush(borderColor.Value),
            StrokeThickness = thickness,
        };
        Canvas.SetLeft(rect, 0);
        Canvas.SetTop(rect, 0);
        canvas.Children.Add(rect);
    }

    // ── Chart title ──────────────────────────────────────────────────────────

    /// <summary>
    /// Draws the chart title above the plot, shrinking its font size (never growing it) so the
    /// title's rendered box fits within <paramref name="availableHeight"/> — the vertical space
    /// already reserved above <c>layout.PlotArea</c> by the caller. Without this clamp a large
    /// title font (e.g. 20-24pt "Format Chart Title") or a small chart box renders past the fixed
    /// 4px top margin and overlaps the plot area's top border/gridline and axis, unlike Excel (which
    /// always reserves title space) and the WPF host (OxyPlot auto-manages title spacing).
    /// </summary>
    private void AddChartTitle(Canvas canvas, double canvasWidth, double availableHeight)
    {
        var title = _chart.Title;
        if (string.IsNullOrWhiteSpace(title))
            return;

        var fontSize = _chart.ChartTitleFontSize > 0 ? _chart.ChartTitleFontSize : 16;
        IBrush foreground = _chart.ResolveChartTitleTextColor(_theme) is { } titleColor
            ? SolidBrush(titleColor)
            : SolidBrush(0x26, 0x26, 0x26);

        const double topMargin = 4;

        var tb = new TextBlock
        {
            Text = title,
            FontSize = fontSize,
            FontWeight = FontWeight.Bold,
            Foreground = foreground,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
        };

        // Measure so we can center it properly even without layout pass.
        tb.Measure(new Size(canvasWidth, double.PositiveInfinity));
        var measuredHeight = tb.DesiredSize.Height > 0 ? tb.DesiredSize.Height : fontSize + 4;

        var maxTitleHeight = Math.Max(0, availableHeight - topMargin);
        var fittedFontSize = ChartTitleFit.ResolveFittingFontSize(fontSize, measuredHeight, maxTitleHeight);
        if (fittedFontSize < fontSize)
        {
            tb.FontSize = fittedFontSize;
            tb.Measure(new Size(canvasWidth, double.PositiveInfinity));
        }

        var labelWidth = tb.DesiredSize.Width > 0 ? tb.DesiredSize.Width : canvasWidth * 0.8;

        Canvas.SetLeft(tb, (canvasWidth - labelWidth) / 2);
        Canvas.SetTop(tb, topMargin);
        canvas.Children.Add(tb);
    }

    // ── Plot-area background + border ────────────────────────────────────────

    private void AddPlotBackground(Canvas canvas, LayoutRect plot)
    {
        if (plot.Width <= 0 || plot.Height <= 0)
            return;

        // R44-meta-1: "No Fill"/"No Line" are explicit user choices distinct from "nothing set" --
        // paint nothing (transparent) instead of falling back to the default plot-area brushes.
        IBrush fill = _chart.IsPlotAreaFillSuppressed
            ? Brushes.Transparent
            : _chart.ResolvePlotAreaFillColor(_theme) is { } plotFill
                ? SolidBrush(plotFill)
                : DefaultPlotBackground;

        IBrush borderBrush = _chart.IsPlotAreaLineSuppressed
            ? Brushes.Transparent
            : _chart.ResolvePlotAreaBorderColor(_theme) is { } plotBorder
                ? SolidBrush(plotBorder)
                : DefaultPlotBorderBrush;

        double borderThickness = _chart.IsPlotAreaLineSuppressed ? 0 : _chart.PlotAreaBorderThickness;

        var rect = new AvaloniaRectangle
        {
            Width = plot.Width,
            Height = plot.Height,
            Fill = fill,
            Stroke = borderBrush,
            StrokeThickness = borderThickness,
        };
        Canvas.SetLeft(rect, plot.Left);
        Canvas.SetTop(rect, plot.Top);
        canvas.Children.Add(rect);
    }

    // ── Series ──────────────────────────────────────────────────────────────

    private void RenderSeries(Canvas canvas, SeriesLayout series, IReadOnlyList<DataLabelBox> extraLabels, int barSeriesCount = 0)
    {
        switch (series.Kind)
        {
            case SeriesGeometryKind.Columns:
            case SeriesGeometryKind.Bars:
                RenderBars(canvas, series, barSeriesCount);
                break;
            case SeriesGeometryKind.Line:
                RenderLine(canvas, series);
                break;
            case SeriesGeometryKind.Area:
                RenderArea(canvas, series);
                break;
            case SeriesGeometryKind.ScatterPoints:
                RenderScatter(canvas, series);
                break;
            case SeriesGeometryKind.PieSlices:
                RenderPie(canvas, series);
                break;
            case SeriesGeometryKind.Bubbles:
                RenderBubbles(canvas, series);
                break;
            case SeriesGeometryKind.RadarPolyline:
                RenderRadar(canvas, series);
                break;
            case SeriesGeometryKind.StockBars:
                RenderStock(canvas, series);
                break;
            case SeriesGeometryKind.BoxWhiskers:
                RenderBoxWhiskers(canvas, series);
                break;
            case SeriesGeometryKind.TreemapTiles:
                RenderTreemapTiles(canvas, series, extraLabels);
                break;
            case SeriesGeometryKind.SurfaceCells:
                RenderSurfaceCells(canvas, series);
                break;
        }

        // Fix 4: Trendlines — draw trendline overlay after the series geometry.
        if (series.Trendline is { Points.Count: >= 2 } tl)
            RenderTrendline(canvas, tl, series.SeriesIndex);

        // M40: Error bars — draw the whisker overlay after the series geometry (and any trendline),
        // matching the source renderer's AddErrorBarsIfRequested (added in its own pass, after all
        // series are plotted).
        if (series.ErrorBars is { Whiskers.Count: > 0 } errorBars)
            RenderErrorBars(canvas, errorBars);
    }

    private void RenderBars(Canvas canvas, SeriesLayout series, int barSeriesCount = 0)
    {
        // Fix 7: NoFill / NoLine — transparent helper/invisible bars.
        var paint = ChartStylePlanner.ResolveBarPaint(_chart, series.SeriesIndex, _theme, ThemePalette);
        var stroke = paint.StrokeColor is { } strokeColor ? SolidBrush(strokeColor) : null;
        var isThreeD = _chart.Type is ModelChartType.ThreeDColumn or ModelChartType.ThreeDBar;

        foreach (var bar in series.Bars)
        {
            if (bar.Rect.Width <= 0 && bar.Rect.Height <= 0)
                continue;

            // Per-bar fill override: an explicit per-point <c:dPt> fill or (for a single-series
            // chart) "Vary colors by point" takes priority over the series-level fill; waterfall's
            // increase/decrease/total coloring also arrives via FillColorOverride.
            var varyColorsFill = ChartStylePlanner.ResolveVaryColorsPointFill(
                _chart, series.SeriesIndex, bar.PointIndex, barSeriesCount, _theme, ThemePalette);
            CellColor? barColor = bar.FillColorOverride
                ?? varyColorsFill
                ?? paint.FillColor;
            IBrush barFill = barColor is { } resolvedColor
                ? SolidBrush(resolvedColor)
                : Brushes.Transparent;

            // The shared layout deliberately preserves the ordinary front-face rectangle geometry.
            // Paint only the Office-style depth facets here, in the host that owns pixel geometry,
            // so WPF/Avalonia retain identical values, axes, and hit targets.
            if (isThreeD && barColor is { } threeDFill)
                AddThreeDSideFacet(canvas, bar.Rect, threeDFill);

            var rect = new AvaloniaRectangle
            {
                Width = Math.Max(1, bar.Rect.Width),
                Height = Math.Max(1, bar.Rect.Height),
                Fill = barFill,
                Stroke = stroke,
                StrokeThickness = paint.StrokeThickness,
            };
            Canvas.SetLeft(rect, bar.Rect.Left);
            Canvas.SetTop(rect, bar.Rect.Top);
            canvas.Children.Add(rect);

            if (isThreeD && barColor is { } topFill)
                AddThreeDTopFacet(canvas, bar.Rect, topFill);
        }

        // Waterfall connector lines between bars.
        if (series.WaterfallConnectors.Count > 0)
            RenderWaterfallConnectors(canvas, series.WaterfallConnectors);
    }

    private void AddThreeDSideFacet(Canvas canvas, LayoutRect rect, CellColor fill)
    {
        var (depthX, depthY) = ResolveThreeDDepth(rect);
        var points = _chart.Type == ModelChartType.ThreeDColumn
            ? new[]
            {
                new AvaloniaPoint(rect.Right, rect.Bottom),
                new AvaloniaPoint(rect.Right, rect.Top),
                new AvaloniaPoint(rect.Right + depthX, rect.Top - depthY),
                new AvaloniaPoint(rect.Right + depthX, rect.Bottom - depthY),
            }
            : new[]
            {
                new AvaloniaPoint(rect.Right, rect.Top),
                new AvaloniaPoint(rect.Right, rect.Bottom),
                new AvaloniaPoint(rect.Right + depthX, rect.Bottom - depthY),
                new AvaloniaPoint(rect.Right + depthX, rect.Top - depthY),
            };
        canvas.Children.Add(CreateThreeDFacet(points, DarkenThreeDFacet(fill, 0.66)));
    }

    private void AddThreeDTopFacet(Canvas canvas, LayoutRect rect, CellColor fill)
    {
        var (depthX, depthY) = ResolveThreeDDepth(rect);
        var points = new[]
        {
            new AvaloniaPoint(rect.Left, rect.Top),
            new AvaloniaPoint(rect.Right, rect.Top),
            new AvaloniaPoint(rect.Right + depthX, rect.Top - depthY),
            new AvaloniaPoint(rect.Left + depthX, rect.Top - depthY),
        };
        canvas.Children.Add(CreateThreeDFacet(points, DarkenThreeDFacet(fill, 0.82)));
    }

    private static (double X, double Y) ResolveThreeDDepth(LayoutRect rect)
    {
        var x = Math.Clamp(rect.Width * 0.25, 4, 12);
        var y = Math.Clamp(x * 0.8, 3, 10);
        return (x, y);
    }

    private static AvaloniaPolygon CreateThreeDFacet(IEnumerable<AvaloniaPoint> points, CellColor fill) =>
        new()
        {
            Points = points.ToList(),
            Fill = SolidBrush(fill),
            Stroke = SolidBrush(DarkenThreeDFacet(fill, 0.8)),
            StrokeThickness = 0.5,
        };

    private static CellColor DarkenThreeDFacet(CellColor color, double factor) =>
        new(
            (byte)Math.Clamp(color.R * factor, 0, 255),
            (byte)Math.Clamp(color.G * factor, 0, 255),
            (byte)Math.Clamp(color.B * factor, 0, 255));

    private static void RenderWaterfallConnectors(
        Canvas canvas,
        IReadOnlyList<(LayoutPoint Left, LayoutPoint Right)> connectors)
    {
        var stroke = SolidBrush(0x59, 0x59, 0x59);
        foreach (var (left, right) in connectors)
        {
            var line = new global::Avalonia.Controls.Shapes.Line
            {
                StartPoint = new AvaloniaPoint(left.X, left.Y),
                EndPoint   = new AvaloniaPoint(right.X, right.Y),
                Stroke = stroke,
                StrokeThickness = 1,
            };
            canvas.Children.Add(line);
        }
    }

    private void RenderLine(Canvas canvas, SeriesLayout series)
    {
        var format = FindSeriesFormat(series.SeriesIndex);
        var stroke = SeriesStroke(series.SeriesIndex);
        // Fix 6: Line dash style.
        var dashStyle = format?.DashStyle;
        // Fix 5: Marker shapes. Line series show no markers by default (matches WPF/OxyPlot's
        // MarkerType.None default and Excel's plain Line chart), only when explicitly requested.
        var markerStyle = format?.MarkerStyle ?? ChartMarkerStyle.None;
        // F19: honor the series' persisted StrokeThickness, falling back to 2 only when unset.
        var strokeThickness = format?.StrokeThickness ?? DefaultSeriesStrokeThickness;
        AddPolyline(canvas, series.Points, stroke, dashStyle, strokeThickness);
        AddMarkers(canvas, series.Points, series.SeriesIndex, SeriesFill(series.SeriesIndex), stroke, markerStyle);
    }

    private void RenderArea(Canvas canvas, SeriesLayout series)
    {
        if (series.Points.Count == 0)
            return;

        var format = FindSeriesFormat(series.SeriesIndex);
        var fill = SeriesFill(series.SeriesIndex, alpha: 0xA0);
        var stroke = SeriesStroke(series.SeriesIndex);
        var dashStyle = format?.DashStyle;
        // F19: honor the series' persisted StrokeThickness, falling back to DefaultSeriesStrokeThickness
        // (2) to match WPF/OxyPlot's AreaSeries default when unset.
        var strokeThickness = format?.StrokeThickness ?? DefaultSeriesStrokeThickness;

        var polygon = new AvaloniaPolygon
        {
            Fill = fill,
            Stroke = stroke,
            StrokeThickness = strokeThickness,
            Points = BuildAreaPoints(series),
        };
        canvas.Children.Add(polygon);
        // Area series show no markers by default (matches WPF/OxyPlot's AreaSeries default and Excel).
        AddMarkers(canvas, series.Points, series.SeriesIndex, fill, stroke, format?.MarkerStyle ?? ChartMarkerStyle.None);
    }

    private void RenderScatter(Canvas canvas, SeriesLayout series)
    {
        var format = FindSeriesFormat(series.SeriesIndex);
        AddMarkers(
            canvas,
            series.Points,
            series.SeriesIndex,
            SeriesFill(series.SeriesIndex),
            SeriesStroke(series.SeriesIndex),
            format?.MarkerStyle ?? ChartMarkerStyle.Circle);
    }

    private void RenderPie(Canvas canvas, SeriesLayout series)
    {
        var stroke = SolidBrush(0xFF, 0xFF, 0xFF);
        foreach (var slice in series.Slices)
        {
            if (slice.Arc.SweepAngleDegrees <= 0 || slice.Arc.OuterRadius <= 0)
                continue;

            // Per-slice fill override: an explicit per-point <c:dPt> fill (Format Data Point in
            // Excel) takes priority over the theme-palette-by-index color, matching WPF's
            // ChartRenderer which resolves GetPointFillColor(chart, seriesIndex, pointIndex, theme)
            // before falling back to the palette.
            IBrush sliceFill = ChartStylePlanner.ResolvePointFillColor(_chart, series.SeriesIndex, slice.PointIndex, _theme) is { } overrideColor
                ? SolidBrush(overrideColor)
                : PaletteFill(slice.PointIndex);

            var path = new AvaloniaPath
            {
                Fill = sliceFill,
                Stroke = stroke,
                StrokeThickness = 1,
                Data = BuildSliceGeometry(slice.Arc),
            };
            canvas.Children.Add(path);
        }
    }

    private void RenderBubbles(Canvas canvas, SeriesLayout series)
    {
        // Excel draws bubbles as semi-transparent filled circles sized by the size dimension.
        var fill = SeriesFill(series.SeriesIndex, alpha: 0x99);
        var stroke = SeriesStroke(series.SeriesIndex);
        foreach (var bubble in series.Bubbles)
        {
            var radius = Math.Max(1, bubble.Radius);
            var marker = new Ellipse
            {
                Width = radius * 2,
                Height = radius * 2,
                Fill = fill,
                Stroke = stroke,
                StrokeThickness = 1,
            };
            Canvas.SetLeft(marker, bubble.Center.X - radius);
            Canvas.SetTop(marker, bubble.Center.Y - radius);
            canvas.Children.Add(marker);
        }
    }

    private void RenderRadar(Canvas canvas, SeriesLayout series)
    {
        if (series.Points.Count == 0)
            return;

        var format = FindSeriesFormat(series.SeriesIndex);
        var fill = SeriesFill(series.SeriesIndex, alpha: 0x40);
        var stroke = SeriesStroke(series.SeriesIndex);
        // F19: honor the series' persisted StrokeThickness, falling back to 2 only when unset.
        var strokeThickness = format?.StrokeThickness ?? DefaultSeriesStrokeThickness;

        // Closed polygon connecting the category points back to the first point, with a light fill.
        var points = new Points();
        foreach (var p in series.Points)
            points.Add(new AvaloniaPoint(p.Position.X, p.Position.Y));

        canvas.Children.Add(new AvaloniaPolygon
        {
            Fill = fill,
            Stroke = stroke,
            StrokeThickness = strokeThickness,
            StrokeJoin = PenLineJoin.Round,
            Points = points,
        });

        AddMarkers(canvas, series.Points, series.SeriesIndex, SeriesFill(series.SeriesIndex), stroke, format?.MarkerStyle ?? ChartMarkerStyle.Circle);
    }

    private void RenderStock(Canvas canvas, SeriesLayout series)
    {
        var stroke = SeriesStroke(series.SeriesIndex);
        var upFill = SolidBrush(0xFF, 0xFF, 0xFF);
        var downFill = stroke;
        const double tickLength = 4;

        foreach (var element in series.StockElements)
        {
            // High-low vertical line for every category.
            canvas.Children.Add(new Line
            {
                StartPoint = new AvaloniaPoint(element.X, element.HighY),
                EndPoint = new AvaloniaPoint(element.X, element.LowY),
                Stroke = stroke,
                StrokeThickness = 1,
            });

            if (element.HasOpen)
            {
                // Candlestick: a box spanning open..close, white when up and filled when down.
                var top = Math.Min(element.OpenY, element.CloseY);
                var bottom = Math.Max(element.OpenY, element.CloseY);
                var box = new AvaloniaRectangle
                {
                    Width = tickLength * 2,
                    Height = Math.Max(1, bottom - top),
                    Fill = element.IsUp ? upFill : downFill,
                    Stroke = stroke,
                    StrokeThickness = 1,
                };
                Canvas.SetLeft(box, element.X - tickLength);
                Canvas.SetTop(box, top);
                canvas.Children.Add(box);
            }
            else
            {
                // High-low-close: a left open tick and a right close tick on the vertical line.
                canvas.Children.Add(new Line
                {
                    StartPoint = new AvaloniaPoint(element.X - tickLength, element.OpenY),
                    EndPoint = new AvaloniaPoint(element.X, element.OpenY),
                    Stroke = stroke,
                    StrokeThickness = 1,
                });
                canvas.Children.Add(new Line
                {
                    StartPoint = new AvaloniaPoint(element.X, element.CloseY),
                    EndPoint = new AvaloniaPoint(element.X + tickLength, element.CloseY),
                    Stroke = stroke,
                    StrokeThickness = 1,
                });
            }
        }
    }

    // Box-and-whisker overlay — paired SeriesPoints encode whisker/median segments.
    // Layout: groups of 6 points per box: [medianL, medianR, lowerW, Q1, Q3, upperW].
    // medianL/medianR → horizontal median line; lowerW/Q1 → lower whisker; Q3/upperW → upper whisker.
    private void RenderBoxWhiskers(Canvas canvas, SeriesLayout series)
    {
        var pts = series.Points;
        if (pts.Count == 0)
            return;

        IBrush stroke = SolidBrush(0x1F, 0x49, 0x7D); // dark blue, matches WPF Stroke
        const double thickness = 1.5;

        // Points arrive in groups of 6 per box: [0]=medL, [1]=medR, [2]=lowW, [3]=Q1, [4]=Q3, [5]=upW
        var i = 0;
        while (i + 5 < pts.Count)
        {
            var medL  = pts[i + 0].Position;
            var medR  = pts[i + 1].Position;
            var lowW  = pts[i + 2].Position;
            var q1Pt  = pts[i + 3].Position;
            var q3Pt  = pts[i + 4].Position;
            var upW   = pts[i + 5].Position;

            // Median line (horizontal).
            canvas.Children.Add(new Line
            {
                StartPoint = new AvaloniaPoint(medL.X, medL.Y),
                EndPoint   = new AvaloniaPoint(medR.X, medR.Y),
                Stroke = stroke,
                StrokeThickness = thickness + 0.5,
            });

            // Lower whisker: vertical center line down from Q1 to lowerWhisker.
            canvas.Children.Add(new Line
            {
                StartPoint = new AvaloniaPoint(lowW.X, lowW.Y),
                EndPoint   = new AvaloniaPoint(q1Pt.X, q1Pt.Y),
                Stroke = stroke,
                StrokeThickness = thickness,
            });

            // Upper whisker: vertical center line up from Q3 to upperWhisker.
            canvas.Children.Add(new Line
            {
                StartPoint = new AvaloniaPoint(q3Pt.X, q3Pt.Y),
                EndPoint   = new AvaloniaPoint(upW.X,  upW.Y),
                Stroke = stroke,
                StrokeThickness = thickness,
            });

            // Whisker caps (short horizontal ticks at the ends).
            var cx      = (medL.X + medR.X) / 2.0;
            var capHalf = (medR.X - medL.X) * 0.25;
            canvas.Children.Add(new Line
            {
                StartPoint = new AvaloniaPoint(cx - capHalf, lowW.Y),
                EndPoint   = new AvaloniaPoint(cx + capHalf, lowW.Y),
                Stroke = stroke,
                StrokeThickness = thickness,
            });
            canvas.Children.Add(new Line
            {
                StartPoint = new AvaloniaPoint(cx - capHalf, upW.Y),
                EndPoint   = new AvaloniaPoint(cx + capHalf, upW.Y),
                Stroke = stroke,
                StrokeThickness = thickness,
            });

            i += 6;
        }
    }

    // M40: Error-bar whiskers (Std Error / Percentage / Fixed Value / Custom) — draws one disjoint
    // line segment per plotted point (mirroring the source (WPF) renderer's AddWhisker), plus
    // optional perpendicular end-cap ticks, in the chart's configured error-bar color/thickness/dash
    // style. The layout engine has already resolved which side(s) (plus/minus) are drawn and where
    // every endpoint sits in pixel space; this method is purely a painter, matching the pattern of
    // every other Render* method in this class.
    private void RenderErrorBars(Canvas canvas, ErrorBarLayout errorBars)
    {
        var barColor = _chart.ErrorBarThemeColor?.Resolve(_theme) ?? _chart.ErrorBarColor;
        IBrush stroke = barColor is { } color ? SolidBrush(color) : Brushes.Black;
        var strokeThickness = _chart.ErrorBarThickness > 0 ? _chart.ErrorBarThickness : 1;
        var dashArray = ToAvaloniaStrokeDashArray(_chart.ErrorBarDashStyle);

        Line NewLine(LayoutPoint start, LayoutPoint end)
        {
            var line = new Line
            {
                StartPoint = new AvaloniaPoint(start.X, start.Y),
                EndPoint = new AvaloniaPoint(end.X, end.Y),
                Stroke = stroke,
                StrokeThickness = strokeThickness,
            };
            if (dashArray is not null)
                line.StrokeDashArray = dashArray;
            return line;
        }

        foreach (var whisker in errorBars.Whiskers)
        {
            if (whisker.HasPlus)
            {
                canvas.Children.Add(NewLine(whisker.Center, whisker.PlusEnd));
                if (errorBars.EndCaps)
                    canvas.Children.Add(NewLine(whisker.PlusCapStart, whisker.PlusCapEnd));
            }

            if (whisker.HasMinus)
            {
                canvas.Children.Add(NewLine(whisker.Center, whisker.MinusEnd));
                if (errorBars.EndCaps)
                    canvas.Children.Add(NewLine(whisker.MinusCapStart, whisker.MinusCapEnd));
            }
        }
    }

    // Treemap tiles — SeriesBars carry per-bar FillColorOverride (palette color). White stroke between tiles.
    // Labels are drawn inline with white text centered in each tile (WPF uses white TextAnnotations).
    private void RenderTreemapTiles(Canvas canvas, SeriesLayout series, IReadOnlyList<DataLabelBox> dataLabels)
    {
        foreach (var bar in series.Bars)
        {
            if (bar.Rect.Width <= 0 || bar.Rect.Height <= 0)
                continue;

            var fillColor = bar.FillColorOverride ?? ThemePaletteColor(bar.PointIndex);
            var fill      = SolidBrush(fillColor.R, fillColor.G, fillColor.B, 0xDC);

            var rect = new AvaloniaRectangle
            {
                Width  = bar.Rect.Width,
                Height = bar.Rect.Height,
                Fill   = fill,
                Stroke = Brushes.White,
                StrokeThickness = 2,
            };
            Canvas.SetLeft(rect, bar.Rect.Left);
            Canvas.SetTop(rect,  bar.Rect.Top);
            canvas.Children.Add(rect);
        }

        // Draw tile labels in white, centered in each tile (always shown, regardless of ShowDataLabels).
        foreach (var box in dataLabels)
        {
            if (string.IsNullOrEmpty(box.Text))
                continue;

            var label = new TextBlock
            {
                Text      = box.Text,
                FontSize  = 10,
                Foreground = Brushes.White,
                TextAlignment = TextAlignment.Center,
            };
            // Center the label in the tile's bounds.
            Canvas.SetLeft(label, box.Bounds.Left);
            Canvas.SetTop(label,  box.Bounds.Top);
            canvas.Children.Add(label);
        }
    }

    // Fix 4: Trendline rendering — dashed polyline in a muted gray (or chart trendline color).
    private void RenderTrendline(Canvas canvas, TrendlineLayout tl, int seriesIndex)
    {
        if (tl.Points.Count < 2)
            return;

        IBrush brush = _chart.ResolveTrendlineColor(_theme) is { } tlColor
            ? SolidBrush(tlColor)
            : TrendlineBrush;

        var dashArray = ToAvaloniaStrokeDashArray(_chart.TrendlineDashStyle);

        var points = new Points();
        foreach (var pt in tl.Points)
            points.Add(new AvaloniaPoint(pt.X, pt.Y));

        var polyline = new AvaloniaPolyline
        {
            Stroke = brush,
            StrokeThickness = _chart.TrendlineThickness > 0 ? _chart.TrendlineThickness : 1.5,
            StrokeJoin = PenLineJoin.Round,
            Points = points,
        };

        if (dashArray is not null)
            polyline.StrokeDashArray = dashArray;

        canvas.Children.Add(polyline);

        // F18: equation / R-squared annotation text, mirroring the WPF TextAnnotation placed at the
        // trendline's data anchor (top-left = source data's min X, max Y) with a light background box.
        if (tl.AnnotationLines.Count > 0)
            RenderTrendlineAnnotation(canvas, tl);
    }

    // F18: draws the trendline equation / R² text lines as a small background box with border,
    // mirroring the WPF renderer's TextAnnotation (light background, gray border, dark text,
    // left/top-anchored at the annotation's data-space anchor point).
    private void RenderTrendlineAnnotation(Canvas canvas, TrendlineLayout tl)
    {
        var text = string.Join(Environment.NewLine, tl.AnnotationLines);
        if (string.IsNullOrEmpty(text))
            return;

        var textBlock = new TextBlock
        {
            Text = text,
            FontSize = TrendlineAnnotationFontSize,
            Foreground = AxisLabelBrush,
        };

        const double padding = 4;
        textBlock.Measure(Size.Infinity);
        var width = (textBlock.DesiredSize.Width > 0 ? textBlock.DesiredSize.Width : 40) + (padding * 2);
        var height = (textBlock.DesiredSize.Height > 0 ? textBlock.DesiredSize.Height : TrendlineAnnotationFontSize + 4) + (padding * 2);

        var background = new AvaloniaRectangle
        {
            Width = width,
            Height = height,
            Fill = SolidBrush(0xFF, 0xFF, 0xFF, 0xDC),
            Stroke = GridlineBrush,
            StrokeThickness = 1,
        };
        Canvas.SetLeft(background, tl.AnnotationAnchor.X);
        Canvas.SetTop(background, tl.AnnotationAnchor.Y);
        canvas.Children.Add(background);

        Canvas.SetLeft(textBlock, tl.AnnotationAnchor.X + padding);
        Canvas.SetTop(textBlock, tl.AnnotationAnchor.Y + padding);
        canvas.Children.Add(textBlock);
    }

    private static Points BuildAreaPoints(SeriesLayout series)
    {
        var points = new Points();
        foreach (var p in series.Points)
            points.Add(new AvaloniaPoint(p.Position.X, p.Position.Y));

        if (series.BaselinePoints.Count > 0)
        {
            // Stacked-area band: close the ring back along the per-category bottom baseline (the
            // cumulative top of the bands below), walked in reverse so the polygon fills exactly the
            // ribbon between this band's top and its variable baseline — the shell analogue of
            // WPF/OxyPlot's AreaSeries.Points/Points2. Matches the WPF stacked-area render.
            for (var i = series.BaselinePoints.Count - 1; i >= 0; i--)
            {
                var b = series.BaselinePoints[i].Position;
                points.Add(new AvaloniaPoint(b.X, b.Y));
            }
        }
        else
        {
            // Plain (non-stacked) area: close the polygon down to the flat scalar baseline (zero line).
            var last = series.Points[^1].Position;
            var first = series.Points[0].Position;
            points.Add(new AvaloniaPoint(last.X, series.AreaBaseline));
            points.Add(new AvaloniaPoint(first.X, series.AreaBaseline));
        }

        return points;
    }

    // Fix 6: dash style parameter for polyline stroke.
    // F19: strokeThickness parameter — callers pass the series' persisted StrokeThickness, falling
    // back to DefaultSeriesStrokeThickness (2) only when the series has no explicit override.
    private static void AddPolyline(
        Canvas canvas,
        IReadOnlyList<SeriesPoint> seriesPoints,
        IBrush stroke,
        ChartLineDashStyle? dashStyle = null,
        double strokeThickness = DefaultSeriesStrokeThickness)
    {
        if (seriesPoints.Count < 2)
            return;

        var points = new Points();
        foreach (var p in seriesPoints)
            points.Add(new AvaloniaPoint(p.Position.X, p.Position.Y));

        var polyline = new AvaloniaPolyline
        {
            Stroke = stroke,
            StrokeThickness = strokeThickness,
            StrokeJoin = PenLineJoin.Round,
            Points = points,
        };

        var dashArray = ToAvaloniaStrokeDashArray(dashStyle);
        if (dashArray is not null)
            polyline.StrokeDashArray = dashArray;

        canvas.Children.Add(polyline);
    }

    // Fix 5: marker shapes — honor ChartMarkerStyle to produce non-circle geometries.
    // R91-render-chart-series-format-5-3: per-point overrides (symbol/size/fill/border) from a
    // <c:dPt>'s <c:marker> — read into ChartModel.PointMarkerFormats by
    // XlsxChartSeriesFormatReader.ApplyPointMarkerOverride — take priority over the series-level
    // style/fill/stroke for that one point, matching Excel's Format Data Point > Marker Options. A
    // per-point style override can turn markers on for a single point even when the series itself
    // shows none; a point with no override simply falls back to the series-level style/fill/stroke.
    private void AddMarkers(
        Canvas canvas,
        IReadOnlyList<SeriesPoint> seriesPoints,
        int seriesIndex,
        IBrush fill,
        IBrush stroke,
        ChartMarkerStyle markerStyle = ChartMarkerStyle.Circle)
    {
        foreach (var p in seriesPoints)
        {
            var pointFormat = FindPointMarkerFormat(seriesIndex, p.PointIndex);
            var style = pointFormat?.MarkerStyle ?? markerStyle;
            if (style == ChartMarkerStyle.None)
                continue;

            var pointFill = pointFormat?.ResolveFillColor(_theme) is { } fillOverride
                ? SolidBrush(fillOverride)
                : fill;
            var pointStroke = pointFormat?.ResolveBorderColor(_theme) is { } borderOverride
                ? SolidBrush(borderOverride)
                : stroke;
            var radius = pointFormat?.MarkerSize ?? MarkerRadius;

            var cx = p.Position.X;
            var cy = p.Position.Y;
            var control = BuildMarker(style, cx, cy, pointFill, pointStroke, radius);
            if (control is not null)
                canvas.Children.Add(control);
        }
    }

    /// <summary>
    /// Finds the per-point marker override (<see cref="ChartModel.PointMarkerFormats"/>) for a series
    /// index/point index pair, last-match-wins like <see cref="ChartStylePlanner.FindSeriesFormat"/>.
    /// </summary>
    private ChartPointMarkerFormat? FindPointMarkerFormat(int seriesIndex, int pointIndex)
    {
        var formats = _chart.PointMarkerFormats;
        for (var i = formats.Count - 1; i >= 0; i--)
        {
            var format = formats[i];
            if (format.SeriesIndex == seriesIndex && format.PointIndex == pointIndex)
                return format;
        }

        return null;
    }

    /// <summary>
    /// Builds a single marker control at (cx, cy). Returns null for <see cref="ChartMarkerStyle.None"/>.
    /// Circle is an Ellipse; Square is a Rectangle; Diamond/Triangle/X/Plus/Star use a Path.
    /// </summary>
    internal static Control? BuildMarker(ChartMarkerStyle style, double cx, double cy, IBrush fill, IBrush stroke, double radius = MarkerRadius)
    {
        var r = radius;
        switch (style)
        {
            case ChartMarkerStyle.None:
                return null;

            case ChartMarkerStyle.X:
            {
                // "X": two crossing diagonal lines, open (unfilled) path.
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    ctx.BeginFigure(new AvaloniaPoint(cx - r, cy - r), isFilled: false);
                    ctx.LineTo(new AvaloniaPoint(cx + r, cy + r));
                    ctx.EndFigure(isClosed: false);
                    ctx.BeginFigure(new AvaloniaPoint(cx - r, cy + r), isFilled: false);
                    ctx.LineTo(new AvaloniaPoint(cx + r, cy - r));
                    ctx.EndFigure(isClosed: false);
                }
                return new AvaloniaPath { Data = geo, Stroke = stroke, StrokeThickness = 1.5 };
            }

            case ChartMarkerStyle.Plus:
            {
                // "+": horizontal and vertical lines, open (unfilled) path.
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    ctx.BeginFigure(new AvaloniaPoint(cx - r, cy), isFilled: false);
                    ctx.LineTo(new AvaloniaPoint(cx + r, cy));
                    ctx.EndFigure(isClosed: false);
                    ctx.BeginFigure(new AvaloniaPoint(cx, cy - r), isFilled: false);
                    ctx.LineTo(new AvaloniaPoint(cx, cy + r));
                    ctx.EndFigure(isClosed: false);
                }
                return new AvaloniaPath { Data = geo, Stroke = stroke, StrokeThickness = 1.5 };
            }

            case ChartMarkerStyle.Star:
            {
                // Asterisk: horizontal + vertical + both diagonals (8-point star), open path.
                var d = r * 0.7071067811865476; // r * cos(45deg): keeps diagonal arm length == r.
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    ctx.BeginFigure(new AvaloniaPoint(cx - r, cy), isFilled: false);
                    ctx.LineTo(new AvaloniaPoint(cx + r, cy));
                    ctx.EndFigure(isClosed: false);
                    ctx.BeginFigure(new AvaloniaPoint(cx, cy - r), isFilled: false);
                    ctx.LineTo(new AvaloniaPoint(cx, cy + r));
                    ctx.EndFigure(isClosed: false);
                    ctx.BeginFigure(new AvaloniaPoint(cx - d, cy - d), isFilled: false);
                    ctx.LineTo(new AvaloniaPoint(cx + d, cy + d));
                    ctx.EndFigure(isClosed: false);
                    ctx.BeginFigure(new AvaloniaPoint(cx - d, cy + d), isFilled: false);
                    ctx.LineTo(new AvaloniaPoint(cx + d, cy - d));
                    ctx.EndFigure(isClosed: false);
                }
                return new AvaloniaPath { Data = geo, Stroke = stroke, StrokeThickness = 1.5 };
            }

            case ChartMarkerStyle.Dot:
            {
                // Dot: a smaller filled circle than the default Circle marker.
                var dotR = r * 0.45;
                var ellipse = new Ellipse
                {
                    Width = dotR * 2,
                    Height = dotR * 2,
                    Fill = fill,
                    Stroke = stroke,
                    StrokeThickness = 1,
                };
                Canvas.SetLeft(ellipse, cx - dotR);
                Canvas.SetTop(ellipse, cy - dotR);
                return ellipse;
            }

            case ChartMarkerStyle.Dash:
            {
                // Dash: a single horizontal line segment, open (unfilled) path.
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    ctx.BeginFigure(new AvaloniaPoint(cx - r, cy), isFilled: false);
                    ctx.LineTo(new AvaloniaPoint(cx + r, cy));
                    ctx.EndFigure(isClosed: false);
                }
                return new AvaloniaPath { Data = geo, Stroke = stroke, StrokeThickness = 1.5 };
            }

            case ChartMarkerStyle.Square:
            {
                var rect = new AvaloniaRectangle
                {
                    Width = r * 2,
                    Height = r * 2,
                    Fill = fill,
                    Stroke = stroke,
                    StrokeThickness = 1,
                };
                Canvas.SetLeft(rect, cx - r);
                Canvas.SetTop(rect, cy - r);
                return rect;
            }

            case ChartMarkerStyle.Diamond:
            {
                // Diamond: rotated square — four points at top/right/bottom/left.
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    ctx.BeginFigure(new AvaloniaPoint(cx, cy - r), isFilled: true);
                    ctx.LineTo(new AvaloniaPoint(cx + r, cy));
                    ctx.LineTo(new AvaloniaPoint(cx, cy + r));
                    ctx.LineTo(new AvaloniaPoint(cx - r, cy));
                    ctx.EndFigure(isClosed: true);
                }
                return new AvaloniaPath { Data = geo, Fill = fill, Stroke = stroke, StrokeThickness = 1 };
            }

            case ChartMarkerStyle.Triangle:
            {
                // Upward-pointing equilateral triangle.
                var h = r * 1.732; // r * sqrt(3) ≈ height of equilateral triangle with half-base r.
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    ctx.BeginFigure(new AvaloniaPoint(cx, cy - r), isFilled: true);
                    ctx.LineTo(new AvaloniaPoint(cx + r, cy + h / 2));
                    ctx.LineTo(new AvaloniaPoint(cx - r, cy + h / 2));
                    ctx.EndFigure(isClosed: true);
                }
                return new AvaloniaPath { Data = geo, Fill = fill, Stroke = stroke, StrokeThickness = 1 };
            }

            case ChartMarkerStyle.Auto: // Auto uses the automatic/default marker (Circle).
            default: // Circle fallback for Circle and any unrecognized value.
            {
                var ellipse = new Ellipse
                {
                    Width = r * 2,
                    Height = r * 2,
                    Fill = fill,
                    Stroke = stroke,
                    StrokeThickness = 1,
                };
                Canvas.SetLeft(ellipse, cx - r);
                Canvas.SetTop(ellipse, cy - r);
                return ellipse;
            }
        }
    }

    private static Geometry BuildSliceGeometry(LayoutArc arc)
    {
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();

        var outerStart = Polar(arc.Center, arc.StartAngleDegrees, arc.OuterRadius);
        var outerEnd = Polar(arc.Center, arc.EndAngleDegrees, arc.OuterRadius);
        var isLargeArc = arc.SweepAngleDegrees > 180;
        var outerSize = new Size(arc.OuterRadius, arc.OuterRadius);

        if (arc.InnerRadius > 0)
        {
            // Doughnut: outer arc clockwise, then inner arc counter-clockwise back to the start.
            var innerStart = Polar(arc.Center, arc.StartAngleDegrees, arc.InnerRadius);
            var innerEnd = Polar(arc.Center, arc.EndAngleDegrees, arc.InnerRadius);
            var innerSize = new Size(arc.InnerRadius, arc.InnerRadius);

            ctx.BeginFigure(outerStart, isFilled: true);
            ctx.ArcTo(outerEnd, outerSize, 0, isLargeArc, SweepDirection.Clockwise);
            ctx.LineTo(innerEnd);
            ctx.ArcTo(innerStart, innerSize, 0, isLargeArc, SweepDirection.CounterClockwise);
            ctx.EndFigure(isClosed: true);
        }
        else
        {
            // Pie wedge: center -> outer start -> arc -> back to center.
            ctx.BeginFigure(new AvaloniaPoint(arc.Center.X, arc.Center.Y), isFilled: true);
            ctx.LineTo(outerStart);
            ctx.ArcTo(outerEnd, outerSize, 0, isLargeArc, SweepDirection.Clockwise);
            ctx.EndFigure(isClosed: true);
        }

        return geometry;
    }

    /// <summary>
    /// Maps a polar angle (degrees, clockwise from 12 o'clock — the engine's pie convention) plus a
    /// radius to a pixel point. Mirrors <c>ChartLayoutEngine.PolarToPixel</c>.
    /// </summary>
    private static AvaloniaPoint Polar(LayoutPoint center, double angleDegrees, double radius)
    {
        var radians = Math.PI / 180.0 * angleDegrees;
        return new AvaloniaPoint(
            center.X + (radius * Math.Sin(radians)),
            center.Y - (radius * Math.Cos(radians)));
    }

    // ── Gridlines (Fix 1) ─────────────────────────────────────────────────────

    /// <summary>
    /// Draws major gridlines across the plot area at the axis tick positions.
    /// For a value axis (vertical) the gridlines run horizontally across the plot.
    /// For a category axis (horizontal) they run vertically.
    /// </summary>
    private void RenderGridlines(Canvas canvas, AxisLayout? axis, LayoutRect plot, bool isValueAxis)
    {
        if (axis is null || plot.Width <= 0 || plot.Height <= 0)
            return;

        var horizontal = axis.Side is AxisSide.Bottom or AxisSide.Top;

        // Determine which model gridline settings apply.
        bool showMajor;
        CellColor? majorColor;
        double thickness;
        CellColor? minorColor;

        if (horizontal)
        {
            showMajor = _chart.ShowXAxisMajorGridlines;
            majorColor = _chart.XAxisMajorGridlineColor;
            thickness = Math.Max(0.5, _chart.XAxisGridlineThickness);
            minorColor = _chart.XAxisMinorGridlineColor;
        }
        else
        {
            showMajor = _chart.ShowYAxisMajorGridlines;
            majorColor = _chart.YAxisMajorGridlineColor;
            thickness = Math.Max(0.5, _chart.YAxisGridlineThickness);
            minorColor = _chart.YAxisMinorGridlineColor;
        }

        // R87-render-chart-plot-5-4: minor gridlines are an independent setting from major ones (the
        // WPF renderer draws them via axis.MinorGridlineStyle regardless of the major setting), so
        // draw them here from AxisLayout.MinorTicks before the early-return that gates only the
        // major gridlines below.
        if (axis.MinorTicks is { Count: > 0 } minorTicks)
        {
            IBrush minorBrush = minorColor is { } mnc
                ? SolidBrush(mnc)
                : MinorGridlineBrush;
            var minorThickness = Math.Max(0.25, thickness * 0.75);
            foreach (var tick in minorTicks)
            {
                if (horizontal)
                {
                    canvas.Children.Add(new Line
                    {
                        StartPoint = new AvaloniaPoint(tick.Position, plot.Top),
                        EndPoint = new AvaloniaPoint(tick.Position, plot.Bottom),
                        Stroke = minorBrush,
                        StrokeThickness = minorThickness,
                        StrokeDashArray = DotArray,
                    });
                }
                else
                {
                    canvas.Children.Add(new Line
                    {
                        StartPoint = new AvaloniaPoint(plot.Left, tick.Position),
                        EndPoint = new AvaloniaPoint(plot.Right, tick.Position),
                        Stroke = minorBrush,
                        StrokeThickness = minorThickness,
                        StrokeDashArray = DotArray,
                    });
                }
            }
        }

        if (!showMajor)
            return;

        IBrush gridBrush = majorColor is { } mc
            ? SolidBrush(mc)
            : GridlineBrush;

        foreach (var tick in axis.Ticks)
        {
            if (horizontal)
            {
                // Vertical gridline at this category tick position.
                canvas.Children.Add(new Line
                {
                    StartPoint = new AvaloniaPoint(tick.Position, plot.Top),
                    EndPoint = new AvaloniaPoint(tick.Position, plot.Bottom),
                    Stroke = gridBrush,
                    StrokeThickness = thickness,
                });
            }
            else
            {
                // Horizontal gridline at this value tick position.
                canvas.Children.Add(new Line
                {
                    StartPoint = new AvaloniaPoint(plot.Left, tick.Position),
                    EndPoint = new AvaloniaPoint(plot.Right, tick.Position),
                    Stroke = gridBrush,
                    StrokeThickness = thickness,
                });
            }
        }
    }

    // ── Axes ────────────────────────────────────────────────────────────────

    /// <summary>Length (px) used for minor tick marks -- shorter than <see cref="TickLength"/>, matching the
    /// visual convention that minor ticks are subordinate to major ones (mirrors OxyPlot's smaller
    /// MinorTickSize relative to MajorTickSize on the WPF renderer).</summary>
    private const double MinorTickLength = TickLength * 0.6;

    private void RenderAxis(Canvas canvas, AxisLayout? axis, bool isValueAxis)
    {
        if (axis is null)
            return;

        var horizontal = axis.Side is AxisSide.Bottom or AxisSide.Top;
        var axisLineBrush = horizontal
            ? _chart.XAxisLineColor is { } xLineColor ? SolidBrush(xLineColor) : AxisBrush
            : _chart.YAxisLineColor is { } yLineColor ? SolidBrush(yLineColor) : AxisBrush;
        var axisLineThickness = horizontal
            ? PositiveOrDefault(_chart.XAxisLineThickness, 1)
            : PositiveOrDefault(_chart.YAxisLineThickness, 1);
        var labelBrush = horizontal
            ? _chart.ResolveXAxisLabelTextColor(_theme) is { } xLabelColor ? SolidBrush(xLabelColor) : AxisLabelBrush
            : _chart.ResolveYAxisLabelTextColor(_theme) is { } yLabelColor ? SolidBrush(yLabelColor) : AxisLabelBrush;
        var labelFontSize = horizontal
            ? PositiveOrDefault(_chart.XAxisLabelFontSize, AxisLabelFontSize)
            : PositiveOrDefault(_chart.YAxisLabelFontSize, AxisLabelFontSize);
        var showLabels = horizontal ? _chart.ShowXAxisLabels : _chart.ShowYAxisLabels;

        if (horizontal)
            AddLine(canvas, FirstTickPos(axis), axis.LinePosition, LastTickPos(axis), axis.LinePosition,
                axisLineBrush, axisLineThickness);
        else
            AddLine(canvas, axis.LinePosition, FirstTickPos(axis), axis.LinePosition, LastTickPos(axis),
                axisLineBrush, axisLineThickness);

        var labelAngle = axis.LabelAngle;

        // R90-render-chart-axis-titles-5-1: honor the chart's configured major tick-mark type
        // (None/Inside/Outside/Cross) instead of always drawing an outside tick at every position.
        // Keys the X/Y model properties off axis position exactly the way RenderGridlines already
        // does (Bottom/Top -> XAxis*, Left/Right -> YAxis*), and matches the WPF/OxyPlot renderer's
        // ApplyTickAndLabelStyle (ChartRenderer.Axes.cs), which keys off axis.Position the same way.
        var majorTickStyle = horizontal ? _chart.XAxisMajorTickStyle : _chart.YAxisMajorTickStyle;
        var minorTickStyle = horizontal ? _chart.XAxisMinorTickStyle : _chart.YAxisMinorTickStyle;

        foreach (var tick in axis.Ticks)
        {
            // R90-render-chart-axis-titles-5-2: AxisTick.DrawTickMark is false for the category ticks
            // Excel's "Interval between tick marks" (<c:tickMarkSkip>) thins out. Label thinning
            // ("Interval between labels") arrives as an empty tick.Label, which needs no gate here.
            if (tick.DrawTickMark)
                RenderTickMark(canvas, axis, horizontal, tick.Position, majorTickStyle, TickLength, axisLineBrush);

            if (showLabels && horizontal)
            {
                AddTickLabel(canvas, tick.Label, tick.Position, axis.LinePosition + TickLength + 1,
                    centerHorizontally: true, angle: labelAngle, fontSize: labelFontSize, foreground: labelBrush);
            }
            else if (showLabels)
            {
                if (axis.Side == AxisSide.Right)
                    AddTickLabel(canvas, tick.Label, axis.LinePosition + TickLength + 1, tick.Position,
                        centerHorizontally: false, rightAligned: false, angle: labelAngle,
                        fontSize: labelFontSize, foreground: labelBrush);
                else
                    AddTickLabel(canvas, tick.Label, axis.LinePosition - TickLength - 1, tick.Position,
                        centerHorizontally: false, angle: labelAngle,
                        fontSize: labelFontSize, foreground: labelBrush);
            }
        }

        // R90-render-chart-axis-titles-5-1: minor tick marks. AxisLayout.MinorTicks is only populated
        // when the chart requests minor gridlines for this axis (ChartLayoutEngine.BuildValueAxisLayout
        // gates it on ShowXAxisMinorGridlines/ShowYAxisMinorGridlines) -- a genuine portable-layout gap
        // for the case of minor tick marks requested WITHOUT minor gridlines, which this renderer alone
        // cannot close. When minor-tick positions ARE available, honor the configured minor style here.
        if (axis.MinorTicks is { Count: > 0 } minorTicks)
        {
            foreach (var tick in minorTicks)
                RenderTickMark(canvas, axis, horizontal, tick.Position, minorTickStyle, MinorTickLength, axisLineBrush);
        }
    }

    /// <summary>
    /// Draws a single tick mark at <paramref name="tickPos"/> along <paramref name="axis"/>, oriented
    /// per <paramref name="style"/>: None draws nothing, Outside extends away from the plot area (the
    /// prior unconditional behavior), Inside extends toward the plot area, and Cross extends both ways.
    /// </summary>
    private static void RenderTickMark(
        Canvas canvas,
        AxisLayout axis,
        bool horizontal,
        double tickPos,
        ChartAxisTickStyle style,
        double length,
        IBrush stroke)
    {
        var (outerLen, innerLen) = style switch
        {
            ChartAxisTickStyle.None => (0.0, 0.0),
            ChartAxisTickStyle.Inside => (0.0, length),
            ChartAxisTickStyle.Cross => (length, length),
            _ => (length, 0.0), // Outside (default)
        };

        if (outerLen <= 0 && innerLen <= 0)
            return;

        if (horizontal)
        {
            // Bottom axis: outward (away from the plot area above it) is +Y. Top axis: outward is -Y.
            var sign = axis.Side == AxisSide.Top ? -1 : 1;
            AddLine(canvas, tickPos, axis.LinePosition - sign * innerLen, tickPos,
                axis.LinePosition + sign * outerLen, stroke);
        }
        else
        {
            // Right axis: outward (away from the plot area to its left) is +X. Left axis: outward is -X.
            var sign = axis.Side == AxisSide.Right ? 1 : -1;
            AddLine(canvas, axis.LinePosition - sign * innerLen, tickPos,
                axis.LinePosition + sign * outerLen, tickPos, stroke);
        }
    }

    // Fix 2: Axis title rendering.
    private void RenderAxisTitle(Canvas canvas, AxisLayout? axis, LayoutRect plot)
    {
        if (axis is null || string.IsNullOrWhiteSpace(axis.Title))
            return;

        var fontSize = _chart.AxisTitleFontSize > 0 ? _chart.AxisTitleFontSize : AxisTitleFontSize;
        IBrush foreground = _chart.ResolveAxisTitleTextColor(_theme) is { } titleColor
            ? SolidBrush(titleColor)
            : AxisLabelBrush;

        var horizontal = axis.Side is AxisSide.Bottom or AxisSide.Top;

        if (horizontal)
        {
            // X axis title: centered below the tick labels (below the axis line + ticks + labels).
            var tb = new TextBlock
            {
                Text = axis.Title,
                FontSize = fontSize,
                FontWeight = FontWeight.SemiBold,
                Foreground = foreground,
                TextAlignment = TextAlignment.Center,
            };
            tb.Measure(Size.Infinity);
            var labelW = tb.DesiredSize.Width > 0 ? tb.DesiredSize.Width : 60;
            var centerX = (plot.Left + plot.Right) / 2;
            // Place below the axis line + tick (TickLength) + label height (~14) + 2px gap.
            var top = axis.LinePosition + TickLength + 14 + 2;
            Canvas.SetLeft(tb, centerX - labelW / 2);
            Canvas.SetTop(tb, top);
            canvas.Children.Add(tb);
        }
        else
        {
            // Y axis title: rotated 90° counter-clockwise, centered along the plot height.
            var tb = new TextBlock
            {
                Text = axis.Title,
                FontSize = fontSize,
                FontWeight = FontWeight.SemiBold,
                Foreground = foreground,
                TextAlignment = TextAlignment.Center,
                RenderTransformOrigin = RelativePoint.TopLeft,
            };
            tb.Measure(Size.Infinity);
            var textW = tb.DesiredSize.Width > 0 ? tb.DesiredSize.Width : 60;
            var textH = tb.DesiredSize.Height > 0 ? tb.DesiredSize.Height : fontSize + 4;
            var centerY = (plot.Top + plot.Bottom) / 2;

            if (axis.Side == AxisSide.Right)
            {
                // Right axis title: rotated 90° clockwise, to the right of the right axis.
                tb.RenderTransform = new RotateTransform(90);
                Canvas.SetLeft(tb, axis.LinePosition + TickLength + 18);
                Canvas.SetTop(tb, centerY - textW / 2);
            }
            else
            {
                // Left axis title: rotated 90° counter-clockwise (upward reading).
                tb.RenderTransform = new RotateTransform(-90);
                // After -90° rotation the text's left edge becomes its top, so offset accordingly.
                Canvas.SetLeft(tb, axis.LinePosition - TickLength - 18 - textH);
                Canvas.SetTop(tb, centerY + textW / 2);
            }
            canvas.Children.Add(tb);
        }
    }

    private static double FirstTickPos(AxisLayout axis)
    {
        var min = double.PositiveInfinity;
        foreach (var tick in axis.Ticks)
            min = Math.Min(min, tick.Position);
        return double.IsInfinity(min) ? axis.LinePosition : min;
    }

    private static double LastTickPos(AxisLayout axis)
    {
        var max = double.NegativeInfinity;
        foreach (var tick in axis.Ticks)
            max = Math.Max(max, tick.Position);
        return double.IsInfinity(max) ? axis.LinePosition : max;
    }

    private static void AddTickLabel(
        Canvas canvas,
        string text,
        double x,
        double y,
        bool centerHorizontally,
        bool rightAligned = true,
        double angle = 0,
        double fontSize = AxisLabelFontSize,
        IBrush? foreground = null)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var label = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            Foreground = foreground ?? AxisLabelBrush,
        };

        label.Measure(Size.Infinity);
        var w = label.DesiredSize.Width > 0 ? label.DesiredSize.Width : 40;
        var h = label.DesiredSize.Height > 0 ? label.DesiredSize.Height : fontSize + 4;

        if (Math.Abs(angle) < 0.5)
        {
            // No rotation — fast path (matches existing behaviour exactly).
            if (centerHorizontally)
            {
                label.TextAlignment = TextAlignment.Center;
                Canvas.SetLeft(label, x - w / 2);
                Canvas.SetTop(label, y);
            }
            else
            {
                Canvas.SetLeft(label, rightAligned ? x - w : x);
                Canvas.SetTop(label, y - h / 2);
            }
        }
        else
        {
            // Rotated label: anchor the rotation at the label's top-center (horizontal axis) or
            // right-center (vertical axis), matching the source renderer's pivot convention.
            // The label's Canvas position is its unrotated top-left; the transform rotates in place
            // around the chosen anchor offset within the element.
            label.RenderTransformOrigin = RelativePoint.TopLeft;
            label.RenderTransform = new RotateTransform(angle);

            if (centerHorizontally)
            {
                // Horizontal axis (bottom/top): pivot at the label's top-center so it fans out
                // below the axis tick mark regardless of the rotation direction.
                double pivotX = w / 2;   // offset from top-left to top-center
                double pivotY = 0;
                label.RenderTransformOrigin = new RelativePoint(pivotX / w, pivotY / h, RelativeUnit.Absolute);
                Canvas.SetLeft(label, x - pivotX);
                Canvas.SetTop(label, y);
            }
            else
            {
                // Vertical axis: pivot at the right-center of the label (the point closest to the axis).
                double pivotX = rightAligned ? w : 0;
                double pivotY = h / 2;
                label.RenderTransformOrigin = new RelativePoint(pivotX / w, pivotY / h, RelativeUnit.Absolute);
                Canvas.SetLeft(label, rightAligned ? x - w : x);
                Canvas.SetTop(label, y - h / 2);
            }
        }

        canvas.Children.Add(label);
    }

    private static void AddLine(
        Canvas canvas,
        double x1,
        double y1,
        double x2,
        double y2,
        IBrush? stroke = null,
        double thickness = 1)
    {
        canvas.Children.Add(new Line
        {
            StartPoint = new AvaloniaPoint(x1, y1),
            EndPoint = new AvaloniaPoint(x2, y2),
            Stroke = stroke ?? AxisBrush,
            StrokeThickness = PositiveOrDefault(thickness, 1),
        });
    }

    private static double PositiveOrDefault(double value, double fallback) =>
        double.IsFinite(value) && value > 0 ? value : fallback;

    // ── Legend ──────────────────────────────────────────────────────────────

    private void RenderLegend(Canvas canvas, LegendLayout legend)
    {
        if (legend.Position == ChartLegendPosition.None || legend.Entries.Count == 0)
            return;

        // Legend background + border (when set).
        var legendBounds = legend.Bounds;
        if (legendBounds.Width > 0 && legendBounds.Height > 0)
        {
            IBrush? legendFill = _chart.ResolveLegendFillColor(_theme) is { } lFill
                ? SolidBrush(lFill)
                : null;
            IBrush? legendBorder = _chart.ResolveLegendBorderColor(_theme) is { } lBorder
                ? SolidBrush(lBorder)
                : null;
            double legendBorderThickness = _chart.LegendBorderThickness;

            if (legendFill is not null || (legendBorder is not null && legendBorderThickness > 0))
            {
                var bg = new AvaloniaRectangle
                {
                    Width = legendBounds.Width,
                    Height = legendBounds.Height,
                    Fill = legendFill ?? Brushes.Transparent,
                    Stroke = legendBorder,
                    StrokeThickness = legendBorder is not null ? legendBorderThickness : 0,
                };
                Canvas.SetLeft(bg, legendBounds.Left);
                Canvas.SetTop(bg, legendBounds.Top);
                canvas.Children.Add(bg);
            }
        }

        var fontSize = _chart.LegendFontSize > 0 ? _chart.LegendFontSize : DefaultLegendFontSize;
        IBrush legendTextBrush = _chart.ResolveLegendTextColor(_theme) is { } lTextColor
            ? SolidBrush(lTextColor)
            : AxisLabelBrush;

        var isPie = _chart.Type is ModelChartType.Pie or ModelChartType.ThreeDPie or ModelChartType.Doughnut;
        foreach (var entry in legend.Entries)
        {
            // CE2: When the series has NoFill, its legend swatch must also be transparent so the
            // legend doesn't show a solid colored rectangle for an invisible helper/spacer series.
            // Mirroring RenderBars which honors NoFill via Brushes.Transparent.
            var entryFormat = isPie ? null : FindSeriesFormat(entry.SeriesIndex);
            IBrush swatchFill = entryFormat?.NoFill == true
                ? Brushes.Transparent
                : isPie ? PaletteFill(entry.SeriesIndex) : SeriesFill(entry.SeriesIndex);

            var swatch = new AvaloniaRectangle
            {
                Width = Math.Max(1, entry.SwatchRect.Width),
                Height = Math.Max(1, entry.SwatchRect.Height),
                Fill = swatchFill,
            };
            Canvas.SetLeft(swatch, entry.SwatchRect.Left);
            Canvas.SetTop(swatch, entry.SwatchRect.Top);
            canvas.Children.Add(swatch);

            if (string.IsNullOrEmpty(entry.Label))
                continue;

            var label = new TextBlock
            {
                Text = entry.Label,
                FontSize = fontSize,
                Foreground = legendTextBrush,
                VerticalAlignment = AvaloniaVerticalAlignment.Center,
            };
            Canvas.SetLeft(label, entry.LabelRect.Left);
            Canvas.SetTop(label, entry.LabelRect.Top);
            canvas.Children.Add(label);
        }
    }

    // ── Surface / heatmap cells ──────────────────────────────────────────────

    private static void RenderSurfaceCells(Canvas canvas, SeriesLayout series)
    {
        foreach (var cell in series.SurfaceCells)
        {
            if (cell.Rect.Width <= 0 || cell.Rect.Height <= 0)
                continue;

            var rect = new AvaloniaRectangle
            {
                Width  = cell.Rect.Width,
                Height = cell.Rect.Height,
                Fill   = SolidBrush(cell.FillColor),
                Stroke = null,
            };
            Canvas.SetLeft(rect, cell.Rect.Left);
            Canvas.SetTop(rect, cell.Rect.Top);
            canvas.Children.Add(rect);
        }
    }

    // ── Data labels ───────────────────────────────────────────────────────────

    private void RenderDataLabels(Canvas canvas, IReadOnlyList<DataLabelBox> labels)
    {
        if (labels.Count == 0)
            return;

        var fontSize = _chart.DataLabelFontSize > 0 ? _chart.DataLabelFontSize : DefaultDataLabelFontSize;
        IBrush textBrush = _chart.ResolveDataLabelTextColor(_theme) is { } dlTextColor
            ? SolidBrush(dlTextColor)
            : DefaultDataLabelBrush;

        // Resolve optional fill/border for label boxes.
        IBrush? dlFill = _chart.ResolveDataLabelFillColor(_theme) is { } dlFillColor
            ? SolidBrush(dlFillColor)
            : null;
        IBrush? dlBorder = _chart.ResolveDataLabelBorderColor(_theme) is { } dlBorderColor
            ? SolidBrush(dlBorderColor)
            : null;
        double dlBorderThickness = _chart.DataLabelBorderThickness;

        foreach (var box in labels)
        {
            if (string.IsNullOrEmpty(box.Text))
                continue;

            // Draw label background box when fill or border is set.
            if (dlFill is not null || (dlBorder is not null && dlBorderThickness > 0))
            {
                var bg = new AvaloniaRectangle
                {
                    Width = Math.Max(1, box.Bounds.Width),
                    Height = Math.Max(1, box.Bounds.Height),
                    Fill = dlFill ?? Brushes.Transparent,
                    Stroke = dlBorder,
                    StrokeThickness = dlBorder is not null ? dlBorderThickness : 0,
                };
                Canvas.SetLeft(bg, box.Bounds.Left);
                Canvas.SetTop(bg, box.Bounds.Top);
                canvas.Children.Add(bg);
            }

            var label = new TextBlock
            {
                Text = box.Text,
                FontSize = fontSize,
                Foreground = textBrush,
                TextAlignment = TextAlignment.Center,
            };
            Canvas.SetLeft(label, box.Bounds.Left);
            Canvas.SetTop(label, box.Bounds.Top);
            canvas.Children.Add(label);
        }
    }

    // ── Colors ──────────────────────────────────────────────────────────────

    private IBrush SeriesFill(int seriesIndex, byte alpha = 0xFF)
    {
        var color = ChartStylePlanner.ResolveSeriesPaint(_chart, seriesIndex, _theme, ThemePalette).FillColor;
        return SolidBrush(color.R, color.G, color.B, alpha);
    }

    private IBrush SeriesStroke(int seriesIndex)
    {
        var color = ChartStylePlanner.ResolveSeriesPaint(_chart, seriesIndex, _theme, ThemePalette).StrokeColor;
        return SolidBrush(color.R, color.G, color.B);
    }

    private IBrush PaletteFill(int index) => SolidBrush(ThemePaletteColor(index));

    private ChartSeriesFormat? FindSeriesFormat(int seriesIndex) =>
        ChartStylePlanner.FindSeriesFormat(_chart, seriesIndex);

    /// <summary>
    /// Returns a color from the theme-derived Excel series palette. The palette is built once per
    /// renderer instance from Accent1–6 × five tint rounds (30 entries), mirroring the WPF
    /// ChartRenderer.BuildExcelSeriesPalette algorithm. Index wraps within the palette.
    /// </summary>
    private CellColor ThemePaletteColor(int index)
    {
        return ChartStylePlanner.GetPaletteColor(ThemePalette, index);
    }

    /// <summary>
    /// Builds a 30-entry palette: Accent1–6 base colors (tint 0), then four more tint rounds
    /// (+0.4, -0.25, +0.6, -0.5), exactly as WPF's BuildExcelSeriesPalette.
    /// </summary>
    internal static CellColor[] BuildThemePalette(WorkbookTheme theme) =>
        ChartStylePlanner.BuildExcelSeriesPalette(theme);

    private IReadOnlyList<CellColor> ThemePalette => _themePalette ??= BuildThemePalette(_theme);

    // ── Dash style helpers (Fix 6) ───────────────────────────────────────────

    /// <summary>
    /// Returns an Avalonia StrokeDashArray for the given dash style, or null for solid (no dash array needed).
    /// Values are in stroke-thickness units (Avalonia convention).
    /// </summary>
    internal static AvaloniaList<double>? ToAvaloniaStrokeDashArray(ChartLineDashStyle? dashStyle) =>
        dashStyle switch
        {
            ChartLineDashStyle.Dash => DashArray,
            ChartLineDashStyle.Dot => DotArray,
            _ => null, // Solid or null → no dash array
        };

    private static IBrush SolidBrush(CellColor color) => SolidBrush(color.R, color.G, color.B);

    private static IBrush SolidBrush(byte r, byte g, byte b, byte a = 0xFF) =>
        new ImmutableSolidColorBrush(Color.FromArgb(a, r, g, b));
}

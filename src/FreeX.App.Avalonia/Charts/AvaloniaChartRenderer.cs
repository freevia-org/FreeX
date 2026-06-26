using Avalonia;
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

    // Title reserve: when a chart title is present we shift the plot area down by this many points.
    private const double TitleAreaHeight = 28;

    private static readonly IBrush AxisBrush = SolidBrush(0x59, 0x59, 0x59);
    private static readonly IBrush AxisLabelBrush = SolidBrush(0x40, 0x40, 0x40);
    private static readonly IBrush DefaultPlotBackground = SolidBrush(0xFF, 0xFF, 0xFF);
    private static readonly IBrush DefaultPlotBorderBrush = SolidBrush(0xD9, 0xD9, 0xD9);
    private static readonly IBrush DefaultDataLabelBrush = SolidBrush(0x40, 0x40, 0x40);

    // Accent tint schedule mirrors WPF ChartRenderer.AccentTintSchedule.
    private static readonly double[] AccentTintSchedule = [0.0, 0.4, -0.25, 0.6, -0.5];

    private static readonly WorkbookThemeColorSlot[] AccentSlots =
    [
        WorkbookThemeColorSlot.Accent1,
        WorkbookThemeColorSlot.Accent2,
        WorkbookThemeColorSlot.Accent3,
        WorkbookThemeColorSlot.Accent4,
        WorkbookThemeColorSlot.Accent5,
        WorkbookThemeColorSlot.Accent6,
    ];

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
        IBrush canvasBackground = _chart.ResolveChartAreaFillColor(_theme) is { } chartFill
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

        // Chart title — rendered above the plot, centered horizontally.
        if (!string.IsNullOrWhiteSpace(_chart.Title))
            AddChartTitle(canvas, width);

        var isPie = layout.Series.Any(s => s.Kind == SeriesGeometryKind.PieSlices);
        if (!isPie)
            AddPlotBackground(canvas, layout.PlotArea);

        RenderAxis(canvas, layout.ValueAxis, isValueAxis: true);
        RenderAxis(canvas, layout.CategoryAxis, isValueAxis: false);

        foreach (var series in layout.Series)
            RenderSeries(canvas, series);

        RenderLegend(canvas, layout.Legend);
        RenderDataLabels(canvas, layout.DataLabels);

        return canvas;
    }

    // ── Chart-area border ────────────────────────────────────────────────────

    private void AddChartAreaBorder(Canvas canvas, double width, double height)
    {
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

    private void AddChartTitle(Canvas canvas, double canvasWidth)
    {
        var title = _chart.Title;
        if (string.IsNullOrWhiteSpace(title))
            return;

        var fontSize = _chart.ChartTitleFontSize > 0 ? _chart.ChartTitleFontSize : 16;
        IBrush foreground = _chart.ResolveChartTitleTextColor(_theme) is { } titleColor
            ? SolidBrush(titleColor)
            : SolidBrush(0x26, 0x26, 0x26);

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
        var labelWidth = tb.DesiredSize.Width > 0 ? tb.DesiredSize.Width : canvasWidth * 0.8;

        Canvas.SetLeft(tb, (canvasWidth - labelWidth) / 2);
        Canvas.SetTop(tb, 4);
        canvas.Children.Add(tb);
    }

    // ── Plot-area background + border ────────────────────────────────────────

    private void AddPlotBackground(Canvas canvas, LayoutRect plot)
    {
        if (plot.Width <= 0 || plot.Height <= 0)
            return;

        IBrush fill = _chart.ResolvePlotAreaFillColor(_theme) is { } plotFill
            ? SolidBrush(plotFill)
            : DefaultPlotBackground;

        IBrush borderBrush = _chart.ResolvePlotAreaBorderColor(_theme) is { } plotBorder
            ? SolidBrush(plotBorder)
            : DefaultPlotBorderBrush;

        double borderThickness = _chart.PlotAreaBorderThickness;

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

    private void RenderSeries(Canvas canvas, SeriesLayout series)
    {
        switch (series.Kind)
        {
            case SeriesGeometryKind.Columns:
            case SeriesGeometryKind.Bars:
                RenderBars(canvas, series);
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
        }
    }

    private void RenderBars(Canvas canvas, SeriesLayout series)
    {
        var fill = SeriesFill(series.SeriesIndex);
        var stroke = SeriesStroke(series.SeriesIndex);
        foreach (var bar in series.Bars)
        {
            if (bar.Rect.Width <= 0 && bar.Rect.Height <= 0)
                continue;

            var rect = new AvaloniaRectangle
            {
                Width = Math.Max(1, bar.Rect.Width),
                Height = Math.Max(1, bar.Rect.Height),
                Fill = fill,
                Stroke = stroke,
                StrokeThickness = 0.75,
            };
            Canvas.SetLeft(rect, bar.Rect.Left);
            Canvas.SetTop(rect, bar.Rect.Top);
            canvas.Children.Add(rect);
        }
    }

    private void RenderLine(Canvas canvas, SeriesLayout series)
    {
        var stroke = SeriesStroke(series.SeriesIndex);
        AddPolyline(canvas, series.Points, stroke);
        AddMarkers(canvas, series.Points, SeriesFill(series.SeriesIndex), stroke);
    }

    private void RenderArea(Canvas canvas, SeriesLayout series)
    {
        if (series.Points.Count == 0)
            return;

        var fill = SeriesFill(series.SeriesIndex, alpha: 0xA0);
        var stroke = SeriesStroke(series.SeriesIndex);

        var polygon = new AvaloniaPolygon
        {
            Fill = fill,
            Stroke = stroke,
            StrokeThickness = 1,
            Points = BuildAreaPoints(series),
        };
        canvas.Children.Add(polygon);
        AddMarkers(canvas, series.Points, fill, stroke);
    }

    private void RenderScatter(Canvas canvas, SeriesLayout series) =>
        AddMarkers(canvas, series.Points, SeriesFill(series.SeriesIndex), SeriesStroke(series.SeriesIndex));

    private void RenderPie(Canvas canvas, SeriesLayout series)
    {
        var stroke = SolidBrush(0xFF, 0xFF, 0xFF);
        foreach (var slice in series.Slices)
        {
            if (slice.Arc.SweepAngleDegrees <= 0 || slice.Arc.OuterRadius <= 0)
                continue;

            var path = new AvaloniaPath
            {
                Fill = PaletteFill(slice.PointIndex),
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

        var fill = SeriesFill(series.SeriesIndex, alpha: 0x40);
        var stroke = SeriesStroke(series.SeriesIndex);

        // Closed polygon connecting the category points back to the first point, with a light fill.
        var points = new Points();
        foreach (var p in series.Points)
            points.Add(new AvaloniaPoint(p.Position.X, p.Position.Y));

        canvas.Children.Add(new AvaloniaPolygon
        {
            Fill = fill,
            Stroke = stroke,
            StrokeThickness = 2,
            StrokeJoin = PenLineJoin.Round,
            Points = points,
        });

        AddMarkers(canvas, series.Points, SeriesFill(series.SeriesIndex), stroke);
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

    private static Points BuildAreaPoints(SeriesLayout series)
    {
        var points = new Points();
        foreach (var p in series.Points)
            points.Add(new AvaloniaPoint(p.Position.X, p.Position.Y));

        // Close the polygon down to the baseline so the fill drops to the zero line.
        var last = series.Points[^1].Position;
        var first = series.Points[0].Position;
        points.Add(new AvaloniaPoint(last.X, series.AreaBaseline));
        points.Add(new AvaloniaPoint(first.X, series.AreaBaseline));
        return points;
    }

    private static void AddPolyline(Canvas canvas, IReadOnlyList<SeriesPoint> seriesPoints, IBrush stroke)
    {
        if (seriesPoints.Count < 2)
            return;

        var points = new Points();
        foreach (var p in seriesPoints)
            points.Add(new AvaloniaPoint(p.Position.X, p.Position.Y));

        canvas.Children.Add(new AvaloniaPolyline
        {
            Stroke = stroke,
            StrokeThickness = 2,
            StrokeJoin = PenLineJoin.Round,
            Points = points,
        });
    }

    private static void AddMarkers(
        Canvas canvas,
        IReadOnlyList<SeriesPoint> seriesPoints,
        IBrush fill,
        IBrush stroke)
    {
        foreach (var p in seriesPoints)
        {
            var marker = new Ellipse
            {
                Width = MarkerRadius * 2,
                Height = MarkerRadius * 2,
                Fill = fill,
                Stroke = stroke,
                StrokeThickness = 1,
            };
            Canvas.SetLeft(marker, p.Position.X - MarkerRadius);
            Canvas.SetTop(marker, p.Position.Y - MarkerRadius);
            canvas.Children.Add(marker);
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

    // ── Axes ────────────────────────────────────────────────────────────────

    private static void RenderAxis(Canvas canvas, AxisLayout? axis, bool isValueAxis)
    {
        if (axis is null)
            return;

        var horizontal = axis.Side is AxisSide.Bottom or AxisSide.Top;
        if (horizontal)
            AddLine(canvas, FirstTickPos(axis), axis.LinePosition, LastTickPos(axis), axis.LinePosition);
        else
            AddLine(canvas, axis.LinePosition, FirstTickPos(axis), axis.LinePosition, LastTickPos(axis));

        foreach (var tick in axis.Ticks)
        {
            if (horizontal)
            {
                AddLine(canvas, tick.Position, axis.LinePosition, tick.Position, axis.LinePosition + TickLength);
                AddTickLabel(canvas, tick.Label, tick.Position, axis.LinePosition + TickLength + 1, centerHorizontally: true);
            }
            else
            {
                AddLine(canvas, axis.LinePosition - TickLength, tick.Position, axis.LinePosition, tick.Position);
                AddTickLabel(canvas, tick.Label, axis.LinePosition - TickLength - 1, tick.Position, centerHorizontally: false);
            }
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

    private static void AddTickLabel(Canvas canvas, string text, double x, double y, bool centerHorizontally)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var label = new TextBlock
        {
            Text = text,
            FontSize = AxisLabelFontSize,
            Foreground = AxisLabelBrush,
        };

        if (centerHorizontally)
        {
            label.TextAlignment = TextAlignment.Center;
            label.Measure(Size.Infinity);
            Canvas.SetLeft(label, x - (label.DesiredSize.Width / 2));
            Canvas.SetTop(label, y);
        }
        else
        {
            label.Measure(Size.Infinity);
            Canvas.SetLeft(label, x - label.DesiredSize.Width);
            Canvas.SetTop(label, y - (label.DesiredSize.Height / 2));
        }

        canvas.Children.Add(label);
    }

    private static void AddLine(Canvas canvas, double x1, double y1, double x2, double y2)
    {
        canvas.Children.Add(new Line
        {
            StartPoint = new AvaloniaPoint(x1, y1),
            EndPoint = new AvaloniaPoint(x2, y2),
            Stroke = AxisBrush,
            StrokeThickness = 1,
        });
    }

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
            var swatch = new AvaloniaRectangle
            {
                Width = Math.Max(1, entry.SwatchRect.Width),
                Height = Math.Max(1, entry.SwatchRect.Height),
                // Pie legends key off the per-slice palette; cartesian legends key off the series.
                Fill = isPie ? PaletteFill(entry.SeriesIndex) : SeriesFill(entry.SeriesIndex),
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
        var format = FindSeriesFormat(seriesIndex);
        var color = format?.ResolveFillColor(_theme)
            ?? format?.ResolveStrokeColor(_theme)
            ?? ThemePaletteColor(seriesIndex);
        return SolidBrush(color.R, color.G, color.B, alpha);
    }

    private IBrush SeriesStroke(int seriesIndex)
    {
        var format = FindSeriesFormat(seriesIndex);
        var color = format?.ResolveStrokeColor(_theme)
            ?? format?.ResolveFillColor(_theme)
            ?? ThemePaletteColor(seriesIndex);
        return SolidBrush(color.R, color.G, color.B);
    }

    private IBrush PaletteFill(int index) => SolidBrush(ThemePaletteColor(index));

    private ChartSeriesFormat? FindSeriesFormat(int seriesIndex)
    {
        foreach (var format in _chart.SeriesFormats)
        {
            if (format.SeriesIndex == seriesIndex)
                return format;
        }

        return null;
    }

    /// <summary>
    /// Returns a color from the theme-derived Excel series palette. The palette is built once per
    /// renderer instance from Accent1–6 × five tint rounds (30 entries), mirroring the WPF
    /// ChartRenderer.BuildExcelSeriesPalette algorithm. Index wraps within the palette.
    /// </summary>
    private CellColor ThemePaletteColor(int index)
    {
        _themePalette ??= BuildThemePalette(_theme);
        var i = index < 0 ? 0 : index;
        return _themePalette[i % _themePalette.Length];
    }

    /// <summary>
    /// Builds a 30-entry palette: Accent1–6 base colors (tint 0), then four more tint rounds
    /// (+0.4, -0.25, +0.6, -0.5), exactly as WPF's BuildExcelSeriesPalette.
    /// </summary>
    internal static CellColor[] BuildThemePalette(WorkbookTheme theme)
    {
        var palette = new CellColor[AccentSlots.Length * AccentTintSchedule.Length];
        var idx = 0;
        foreach (var tint in AccentTintSchedule)
        {
            foreach (var slot in AccentSlots)
            {
                palette[idx++] = theme.ResolveColor(slot, tint);
            }
        }
        return palette;
    }

    private static IBrush SolidBrush(CellColor color) => SolidBrush(color.R, color.G, color.B);

    private static IBrush SolidBrush(byte r, byte g, byte b, byte a = 0xFF) =>
        new ImmutableSolidColorBrush(Color.FromArgb(a, r, g, b));
}

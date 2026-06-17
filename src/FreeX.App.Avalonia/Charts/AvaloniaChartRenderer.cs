using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

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
/// <see cref="ChartModel.SeriesFormats"/>, falling back to a default Excel-like accent palette.
/// </summary>
public sealed class AvaloniaChartRenderer
{
    private const double MarkerRadius = 3.5;
    private const double AxisLabelFontSize = 10;
    private const double LegendFontSize = 11;
    private const double TickLength = 4;

    private static readonly IBrush AxisBrush = SolidBrush(0x59, 0x59, 0x59);
    private static readonly IBrush AxisLabelBrush = SolidBrush(0x40, 0x40, 0x40);
    private static readonly IBrush PlotBackground = SolidBrush(0xFF, 0xFF, 0xFF);
    private static readonly IBrush PlotBorderBrush = SolidBrush(0xD9, 0xD9, 0xD9);
    private static readonly IBrush DataLabelBrush = SolidBrush(0x40, 0x40, 0x40);

    // Excel default accent palette (Office theme Accent1..Accent6) used when a series has no explicit
    // format color.
    private static readonly CellColor[] DefaultPalette =
    [
        new(0x15, 0x60, 0x82),
        new(0xC0, 0x50, 0x4D),
        new(0x9B, 0xBB, 0x59),
        new(0x80, 0x64, 0xA2),
        new(0x4B, 0xAC, 0xC6),
        new(0xF7, 0x96, 0x46),
    ];

    private readonly ChartModel _chart;
    private readonly WorkbookTheme _theme;

    public AvaloniaChartRenderer(ChartModel chart, WorkbookTheme theme)
    {
        _chart = chart ?? throw new ArgumentNullException(nameof(chart));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
    }

    /// <summary>
    /// Renders <paramref name="layout"/> into a fresh <see cref="Canvas"/> sized to
    /// <paramref name="width"/> x <paramref name="height"/> (the chart object's on-sheet pixel box).
    /// </summary>
    public Canvas Render(ChartLayout layout, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var canvas = new Canvas
        {
            Width = Math.Max(1, width),
            Height = Math.Max(1, height),
            Background = SolidBrush(0xFF, 0xFF, 0xFF),
            ClipToBounds = true,
            IsHitTestVisible = false,
        };

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

    private static void AddPlotBackground(Canvas canvas, LayoutRect plot)
    {
        if (plot.Width <= 0 || plot.Height <= 0)
            return;

        var rect = new AvaloniaRectangle
        {
            Width = plot.Width,
            Height = plot.Height,
            Fill = PlotBackground,
            Stroke = PlotBorderBrush,
            StrokeThickness = 1,
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
                FontSize = LegendFontSize,
                Foreground = AxisLabelBrush,
                VerticalAlignment = AvaloniaVerticalAlignment.Center,
            };
            Canvas.SetLeft(label, entry.LabelRect.Left);
            Canvas.SetTop(label, entry.LabelRect.Top);
            canvas.Children.Add(label);
        }
    }

    // ── Data labels ───────────────────────────────────────────────────────────

    private static void RenderDataLabels(Canvas canvas, IReadOnlyList<DataLabelBox> labels)
    {
        foreach (var box in labels)
        {
            if (string.IsNullOrEmpty(box.Text))
                continue;

            var label = new TextBlock
            {
                Text = box.Text,
                FontSize = AxisLabelFontSize,
                Foreground = DataLabelBrush,
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
            ?? PaletteColor(seriesIndex);
        return SolidBrush(color.R, color.G, color.B, alpha);
    }

    private IBrush SeriesStroke(int seriesIndex)
    {
        var format = FindSeriesFormat(seriesIndex);
        var color = format?.ResolveStrokeColor(_theme)
            ?? format?.ResolveFillColor(_theme)
            ?? PaletteColor(seriesIndex);
        return SolidBrush(color.R, color.G, color.B);
    }

    private IBrush PaletteFill(int index) => SolidBrush(PaletteColor(index));

    private ChartSeriesFormat? FindSeriesFormat(int seriesIndex)
    {
        foreach (var format in _chart.SeriesFormats)
        {
            if (format.SeriesIndex == seriesIndex)
                return format;
        }

        return null;
    }

    private static CellColor PaletteColor(int index)
    {
        var i = index < 0 ? 0 : index;
        return DefaultPalette[i % DefaultPalette.Length];
    }

    private static IBrush SolidBrush(CellColor color) => SolidBrush(color.R, color.G, color.B);

    private static IBrush SolidBrush(byte r, byte g, byte b, byte a = 0xFF) =>
        new SolidColorBrush(Color.FromArgb(a, r, g, b));
}

using FreeP.App.Compositor;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Rendering.Wpf;

/// <summary>
/// A WPF panel that renders a single <see cref="Slide"/> using the framework-free
/// <see cref="SlideCompositor"/> to produce draw operations and converts them to WPF primitives.
///
/// Usage: set <see cref="Presentation"/> and <see cref="Slide"/>, call <see cref="Refresh"/>
/// or set the properties — the canvas redraws automatically.
///
/// The control uses OnRender (DrawingContext) to paint all operations directly, which avoids
/// creating a large visual tree of WPF elements for each shape.
/// </summary>
public sealed class SlideCanvas : FrameworkElement
{
    // ── Dependency properties ──────────────────────────────────────────────────

    public static readonly DependencyProperty PresentationProperty =
        DependencyProperty.Register(
            nameof(Presentation),
            typeof(Presentation),
            typeof(SlideCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender,
                OnModelChanged));

    public static readonly DependencyProperty SlideProperty =
        DependencyProperty.Register(
            nameof(Slide),
            typeof(Slide),
            typeof(SlideCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender,
                OnModelChanged));

    public Presentation? Presentation
    {
        get => (Presentation?)GetValue(PresentationProperty);
        set => SetValue(PresentationProperty, value);
    }

    public Slide? Slide
    {
        get => (Slide?)GetValue(SlideProperty);
        set => SetValue(SlideProperty, value);
    }

    private static void OnModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((SlideCanvas)d).Refresh();

    // ── Cached draw ops (invalidated on model change) ─────────────────────────

    private IReadOnlyList<DrawOp>? _cachedOps;
    private double _slideWidthDip;
    private double _slideHeightDip;

    /// <summary>Forces a recomposition and repaint.</summary>
    public void Refresh()
    {
        _cachedOps = null;
        InvalidateVisual();
    }

    // ── Layout: maintain slide aspect ratio ────────────────────────────────────

    protected override Size MeasureOverride(Size availableSize)
    {
        EnsureOps();
        if (_slideWidthDip <= 0 || _slideHeightDip <= 0)
            return base.MeasureOverride(availableSize);

        double ratio = _slideWidthDip / _slideHeightDip;

        double w = double.IsInfinity(availableSize.Width) ? _slideWidthDip : availableSize.Width;
        double h = double.IsInfinity(availableSize.Height) ? _slideHeightDip : availableSize.Height;

        // Fit inside available area preserving aspect ratio
        if (w / h > ratio)
            w = h * ratio;
        else
            h = w / ratio;

        return new Size(Math.Max(1, w), Math.Max(1, h));
    }

    // ── Rendering ──────────────────────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        EnsureOps();

        if (_cachedOps is null || _cachedOps.Count == 0 || _slideWidthDip <= 0)
            return;

        double renderW = ActualWidth;
        double renderH = ActualHeight;
        if (renderW <= 0 || renderH <= 0) return;

        // Scale slide DIP coordinates → actual render pixels (uniform fit).
        double scale = Math.Min(renderW / _slideWidthDip, renderH / _slideHeightDip);
        double offsetX = (renderW - _slideWidthDip * scale) / 2;
        double offsetY = (renderH - _slideHeightDip * scale) / 2;

        var transform = new TransformGroup();
        transform.Children.Add(new ScaleTransform(scale, scale));
        transform.Children.Add(new TranslateTransform(offsetX, offsetY));

        dc.PushTransform(transform);

        foreach (var op in _cachedOps)
            RenderOp(dc, op);

        dc.Pop();
    }

    private void RenderOp(DrawingContext dc, DrawOp op)
    {
        switch (op)
        {
            case DrawOp.Background bg:
                RenderBackground(dc, bg);
                break;
            case DrawOp.Shape shape:
                RenderShape(dc, shape);
                break;
            case DrawOp.Picture pic:
                RenderPicture(dc, pic);
                break;
            case DrawOp.Table table:
                RenderTable(dc, table);
                break;
            case DrawOp.Chart chartOp:
                RenderChart(dc, chartOp);
                break;
        }
    }

    // ── Background ─────────────────────────────────────────────────────────────

    private static void RenderBackground(DrawingContext dc, DrawOp.Background bg)
    {
        var brush = MakeBrush(bg.Fill, bg.BoundsDip);
        if (brush is null) return;

        dc.DrawRectangle(brush, null,
            new Rect(bg.BoundsDip.X, bg.BoundsDip.Y, bg.BoundsDip.Width, bg.BoundsDip.Height));
    }

    // ── AutoShape ──────────────────────────────────────────────────────────────

    private static void RenderShape(DrawingContext dc, DrawOp.Shape shape)
    {
        if (shape.Geometry.Contours.Count == 0 && shape.Text is null) return;

        var bounds = shape.BoundsDip;
        bool hasTransform = shape.RotationDeg != 0 || shape.FlipH || shape.FlipV;

        if (hasTransform)
        {
            dc.PushTransform(BuildShapeTransform(shape));
        }

        // Draw geometry
        if (shape.Geometry.Contours.Count > 0)
        {
            var geometry = ContourListToGeometry(shape.Geometry);
            var fillBrush = MakeBrush(shape.Fill, bounds);
            var pen = MakePen(shape.Outline);
            dc.DrawGeometry(fillBrush, pen, geometry);
        }

        // Draw text overlay
        if (shape.Text is not null)
            RenderText(dc, shape.Text, bounds);

        if (hasTransform)
            dc.Pop();
    }

    private static Transform BuildShapeTransform(DrawOp.Shape shape)
    {
        var bounds = shape.BoundsDip;
        double cx = bounds.X + bounds.Width / 2;
        double cy = bounds.Y + bounds.Height / 2;

        var group = new TransformGroup();

        if (shape.FlipH)
            group.Children.Add(new ScaleTransform(-1, 1, cx, cy));

        if (shape.FlipV)
            group.Children.Add(new ScaleTransform(1, -1, cx, cy));

        if (shape.RotationDeg != 0)
            group.Children.Add(new RotateTransform(shape.RotationDeg, cx, cy));

        return group;
    }

    // ── Picture ────────────────────────────────────────────────────────────────

    private static void RenderPicture(DrawingContext dc, DrawOp.Picture pic)
    {
        if (pic.Bytes.Length == 0) return;

        BitmapSource? bitmap = null;
        try
        {
            using var ms = new System.IO.MemoryStream(pic.Bytes);
            var img = new BitmapImage();
            img.BeginInit();
            img.StreamSource = ms;
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.EndInit();
            if (img.CanFreeze) img.Freeze();
            bitmap = img;
        }
        catch
        {
            // Skip undecodable images rather than crashing the renderer.
            return;
        }

        var dest = new Rect(pic.DestDip.X, pic.DestDip.Y, pic.DestDip.Width, pic.DestDip.Height);

        bool hasRotation = pic.RotationDeg != 0;
        if (hasRotation)
        {
            double cx = dest.Left + dest.Width / 2;
            double cy = dest.Top + dest.Height / 2;
            dc.PushTransform(new RotateTransform(pic.RotationDeg, cx, cy));
        }

        dc.DrawImage(bitmap, dest);

        // P3: draw the picture frame outline if present.
        if (pic.Outline is ResolvedOutline.Visible visOutline)
        {
            var pen = MakePen(visOutline);
            if (pen is not null)
                dc.DrawRectangle(null, pen, dest);
        }

        if (hasRotation) dc.Pop();
    }

    // ── Table ──────────────────────────────────────────────────────────────────

    private static void RenderTable(DrawingContext dc, DrawOp.Table tableOp)
    {
        foreach (var cell in tableOp.Cells)
            RenderTableCell(dc, cell);
    }

    private static void RenderTableCell(DrawingContext dc, TableCellOp cell)
    {
        var rect = new Rect(cell.BoundsDip.X, cell.BoundsDip.Y, cell.BoundsDip.Width, cell.BoundsDip.Height);

        // Fill
        var fillBrush = MakeBrush(cell.Fill, cell.BoundsDip);
        if (fillBrush is not null)
            dc.DrawRectangle(fillBrush, null, rect);

        // Per-side borders (draw as single-pixel lines along each edge to avoid overlap issues).
        DrawCellBorder(dc, cell.BorderTop,
            new Point(rect.Left,  rect.Top), new Point(rect.Right, rect.Top));
        DrawCellBorder(dc, cell.BorderBottom,
            new Point(rect.Left,  rect.Bottom), new Point(rect.Right, rect.Bottom));
        DrawCellBorder(dc, cell.BorderLeft,
            new Point(rect.Left,  rect.Top), new Point(rect.Left,  rect.Bottom));
        DrawCellBorder(dc, cell.BorderRight,
            new Point(rect.Right, rect.Top), new Point(rect.Right, rect.Bottom));

        // Text (respecting cell vertical anchor + insets).
        if (cell.Text is not null)
            RenderTableCellText(dc, cell.Text, cell.BoundsDip, cell.Anchor);
    }

    private static void DrawCellBorder(DrawingContext dc, ResolvedOutline outline, Point p1, Point p2)
    {
        var pen = MakePen(outline);
        if (pen is null) return;
        dc.DrawLine(pen, p1, p2);
    }

    private static void RenderTableCellText(
        DrawingContext dc, ResolvedTextLayout text, LayoutRect bounds,
        FreeP.Core.Model.TableCellAnchor anchor)
    {
        double insetLeft   = text.InsetLeftDip;
        double insetTop    = text.InsetTopDip;
        double insetRight  = text.InsetRightDip;
        double insetBottom = text.InsetBottomDip;

        double textAreaW = Math.Max(0, bounds.Width  - insetLeft - insetRight);
        double textAreaH = Math.Max(0, bounds.Height - insetTop  - insetBottom);

        var formatted = new List<(FormattedText ft, double spaceAfter)>();
        double totalH = 0;

        foreach (var para in text.Paragraphs)
        {
            if (para.Runs.Count == 0) continue;
            var ft = BuildFormattedText(para, textAreaW, text.Wrap);
            formatted.Add((ft, para.SpaceAfterPt * (96.0 / 72.0)));
            totalH += ft.Height + para.SpaceBeforePt * (96.0 / 72.0) + para.SpaceAfterPt * (96.0 / 72.0);
        }

        double startY = anchor switch
        {
            FreeP.Core.Model.TableCellAnchor.Middle => bounds.Y + insetTop + Math.Max(0, (textAreaH - totalH) / 2),
            FreeP.Core.Model.TableCellAnchor.Bottom => bounds.Y + insetTop + Math.Max(0, textAreaH - totalH),
            _ => bounds.Y + insetTop
        };

        double curY = startY;
        int paraIdx = 0;
        foreach (var para in text.Paragraphs)
        {
            if (para.Runs.Count == 0) { paraIdx++; continue; }
            var (ft, spaceAfterDip) = formatted[paraIdx];
            double spaceBefore = para.SpaceBeforePt * (96.0 / 72.0);
            curY += spaceBefore;
            dc.DrawText(ft, new Point(bounds.X + insetLeft, curY));
            curY += ft.Height + spaceAfterDip;
            paraIdx++;
        }
    }

    // ── Chart ──────────────────────────────────────────────────────────────────

    private static void RenderChart(DrawingContext dc, DrawOp.Chart chartOp)
    {
        var bounds = chartOp.BoundsDip;
        var chart  = chartOp.ChartShape;

        // ── Frame background (white) + border ──────────────────────────────────
        var frameBrush = FreezeBrush(new SolidColorBrush(Colors.White));
        var framePen   = new Pen(FreezeBrush(new SolidColorBrush(Color.FromRgb(0xBF, 0xBF, 0xBF))), 0.5);
        if (framePen.CanFreeze) framePen.Freeze();
        var frameRect = new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        dc.DrawRectangle(frameBrush, framePen, frameRect);

        // ── Layout areas ────────────────────────────────────────────────────────
        // Margins inside the frame (DIP)
        const double margin       = 8.0;
        const double titleH       = 18.0;
        const double legendH      = 14.0;
        const double axisLabelW   = 36.0;  // value axis label area (left)
        const double catLabelH    = 16.0;  // category label area (bottom)
        const double gridlinePad  = 2.0;

        double titleAreaH  = chart.Title is not null ? titleH + margin : 0;
        bool   hasLegend   = chart.Legend.HasValue;
        bool   isPie       = chart.ChartType == FreeP.Core.Model.ChartType.Pie;

        // Title
        if (chart.Title is not null)
        {
            var titleRect = new Rect(bounds.X + margin, bounds.Y + margin,
                bounds.Width - 2 * margin, titleH);
            DrawChartLabel(dc, chart.Title, titleRect, isBold: true, fontSize: 9.0,
                align: TextAlignment.Center);
        }

        // Legend area (bottom when position is Bottom or unspecified, right otherwise)
        double legendAreaW = 0, legendAreaH = 0;
        bool   legendRight = chart.Legend is FreeP.Core.Model.LegendPosition.Right or
                                             FreeP.Core.Model.LegendPosition.Left;
        if (hasLegend)
        {
            if (legendRight)
                legendAreaW = Math.Min(80, bounds.Width * 0.22);
            else
                legendAreaH = legendH + margin;
        }

        // Plot area
        double plotX = bounds.X + margin + (isPie ? 0 : axisLabelW);
        double plotY = bounds.Y + margin + titleAreaH;
        double plotW = bounds.Width  - 2 * margin - (isPie ? 0 : axisLabelW) - legendAreaW;
        double plotH = bounds.Height - 2 * margin - titleAreaH - legendAreaH
                                     - (isPie ? 0 : catLabelH);

        if (plotW <= 0 || plotH <= 0) return;

        // ── Gridlines ──────────────────────────────────────────────────────────
        if (!isPie && chart.ValueAxis.HasMajorGridlines)
        {
            var gridPen = new Pen(
                FreezeBrush(new SolidColorBrush(Color.FromRgb(0xD9, 0xD9, 0xD9))), 0.5);
            if (gridPen.CanFreeze) gridPen.Freeze();

            const int gridLines = 5;
            for (int gi = 0; gi <= gridLines; gi++)
            {
                double gy = plotY + plotH - (plotH / gridLines) * gi;
                dc.DrawLine(gridPen,
                    new Point(plotX, gy),
                    new Point(plotX + plotW, gy));
            }
        }

        // ── Dispatch to chart type ─────────────────────────────────────────────
        switch (chart.ChartType)
        {
            case FreeP.Core.Model.ChartType.ColumnClustered:
            case FreeP.Core.Model.ChartType.ColumnStacked:
            case FreeP.Core.Model.ChartType.ColumnStacked100:
                RenderColumnChart(dc, chart, chartOp.SeriesColors, plotX, plotY, plotW, plotH);
                break;

            case FreeP.Core.Model.ChartType.BarClustered:
            case FreeP.Core.Model.ChartType.BarStacked:
            case FreeP.Core.Model.ChartType.BarStacked100:
                RenderBarChart(dc, chart, chartOp.SeriesColors, plotX, plotY, plotW, plotH);
                break;

            case FreeP.Core.Model.ChartType.Line:
            case FreeP.Core.Model.ChartType.LineMarkers:
                RenderLineChart(dc, chart, chartOp.SeriesColors, plotX, plotY, plotW, plotH,
                    withMarkers: chart.ChartType == FreeP.Core.Model.ChartType.LineMarkers);
                break;

            case FreeP.Core.Model.ChartType.Pie:
                RenderPieChart(dc, chart, chartOp.SeriesColors, plotX, plotY, plotW, plotH);
                break;

            case FreeP.Core.Model.ChartType.Area:
            case FreeP.Core.Model.ChartType.AreaStacked:
                RenderAreaChart(dc, chart, chartOp.SeriesColors, plotX, plotY, plotW, plotH);
                break;

            default:
                // Unknown — render a placeholder rectangle
                dc.DrawRectangle(
                    FreezeBrush(new SolidColorBrush(Color.FromArgb(30, 0, 0, 0))),
                    null,
                    new Rect(plotX, plotY, plotW, plotH));
                break;
        }

        // ── Axis labels ────────────────────────────────────────────────────────
        if (!isPie && chart.Categories.Count > 0)
        {
            double catStep = plotW / Math.Max(1, chart.Categories.Count);
            for (int ci = 0; ci < chart.Categories.Count; ci++)
            {
                double lx = plotX + ci * catStep;
                var labelRect = new Rect(lx, plotY + plotH + 2, catStep, catLabelH);
                DrawChartLabel(dc, chart.Categories[ci], labelRect, isBold: false, fontSize: 7.0,
                    align: TextAlignment.Center);
            }
        }

        // Value axis labels (5 tick marks)
        if (!isPie)
        {
            var (minVal, maxVal) = ComputeAxisRange(chart);
            const int ticks = 5;
            for (int ti = 0; ti <= ticks; ti++)
            {
                double val = minVal + (maxVal - minVal) * ti / ticks;
                double vy  = plotY + plotH - plotH * ti / ticks;
                var labelRect = new Rect(bounds.X + margin, vy - 6, axisLabelW - gridlinePad, 12);
                DrawChartLabel(dc, FormatAxisValue(val), labelRect,
                    isBold: false, fontSize: 6.5, align: TextAlignment.Right);
            }
        }

        // ── Legend ─────────────────────────────────────────────────────────────
        if (hasLegend && chart.Series.Count > 0)
        {
            double lx, ly, lw;
            if (legendRight)
            {
                lx = bounds.X + bounds.Width - legendAreaW - margin / 2;
                ly = plotY;
                lw = legendAreaW - margin / 2;
            }
            else
            {
                lx = plotX;
                ly = bounds.Y + bounds.Height - legendAreaH - margin / 2;
                lw = plotW;
            }

            double itemH = legendH;
            int maxItems = (int)Math.Max(1, legendRight ? plotH / itemH : lw / 80);
            int itemsToShow = Math.Min(chart.Series.Count, maxItems);

            for (int si = 0; si < itemsToShow; si++)
            {
                var sc = si < chartOp.SeriesColors.Count
                    ? chartOp.SeriesColors[si]
                    : new SrgbColor(0x4F, 0x81, 0xBD);

                if (legendRight)
                {
                    double iy = ly + si * itemH;
                    dc.DrawRectangle(
                        FreezeBrush(new SolidColorBrush(Color.FromRgb(sc.R, sc.G, sc.B))),
                        null,
                        new Rect(lx, iy + 3, 8, 8));
                    DrawChartLabel(dc, chart.Series[si].Name,
                        new Rect(lx + 10, iy, lw - 10, itemH),
                        isBold: false, fontSize: 7.0, align: TextAlignment.Left);
                }
                else
                {
                    double ix = lx + si * 80.0;
                    dc.DrawRectangle(
                        FreezeBrush(new SolidColorBrush(Color.FromRgb(sc.R, sc.G, sc.B))),
                        null,
                        new Rect(ix, ly + 3, 8, 8));
                    DrawChartLabel(dc, chart.Series[si].Name,
                        new Rect(ix + 10, ly, 70, itemH),
                        isBold: false, fontSize: 7.0, align: TextAlignment.Left);
                }
            }
        }
    }

    // ── Column chart ─────────────────────────────────────────────────────────

    private static void RenderColumnChart(
        DrawingContext dc, FreeP.Core.Model.ChartShape chart,
        IReadOnlyList<SrgbColor> seriesColors,
        double plotX, double plotY, double plotW, double plotH)
    {
        int catCount = Math.Max(1, chart.Categories.Count);
        if (chart.Series.Count == 0) return;

        var (minVal, maxVal) = ComputeAxisRange(chart);
        double range = maxVal - minVal;
        if (range <= 0) return;

        bool stacked = chart.ChartType is FreeP.Core.Model.ChartType.ColumnStacked
                                       or FreeP.Core.Model.ChartType.ColumnStacked100;

        double catW    = plotW / catCount;
        double padding = catW * 0.15;
        double serW    = stacked ? (catW - 2 * padding) : (catW - 2 * padding) / Math.Max(1, chart.Series.Count);

        for (int ci = 0; ci < catCount; ci++)
        {
            double catX = plotX + ci * catW + padding;
            double stackedY = plotY + plotH; // top of next stacked segment

            for (int si = 0; si < chart.Series.Count; si++)
            {
                var series = chart.Series[si];
                double? rawVal = ci < series.Values.Count ? series.Values[ci] : null;
                if (rawVal is null) continue;

                double val = rawVal.Value;
                double barH = Math.Abs(val / range) * plotH;
                if (barH < 0.5) barH = 0.5;

                double barX = stacked ? catX : catX + si * serW;
                double barY = stacked
                    ? (stackedY - barH)
                    : (plotY + plotH - barH);

                var color = GetSeriesColor(chart, si, ci, seriesColors);
                var brush = FreezeBrush(new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B)));

                dc.DrawRectangle(brush, null, new Rect(barX, barY, serW - 1, barH));

                if (stacked) stackedY -= barH;
            }
        }
    }

    // ── Bar (horizontal) chart ────────────────────────────────────────────────

    private static void RenderBarChart(
        DrawingContext dc, FreeP.Core.Model.ChartShape chart,
        IReadOnlyList<SrgbColor> seriesColors,
        double plotX, double plotY, double plotW, double plotH)
    {
        int catCount = Math.Max(1, chart.Categories.Count);
        if (chart.Series.Count == 0) return;

        var (minVal, maxVal) = ComputeAxisRange(chart);
        double range = maxVal - minVal;
        if (range <= 0) return;

        bool stacked = chart.ChartType is FreeP.Core.Model.ChartType.BarStacked
                                       or FreeP.Core.Model.ChartType.BarStacked100;

        double catH    = plotH / catCount;
        double padding = catH * 0.15;
        double serH    = stacked ? (catH - 2 * padding) : (catH - 2 * padding) / Math.Max(1, chart.Series.Count);

        for (int ci = 0; ci < catCount; ci++)
        {
            double catY = plotY + ci * catH + padding;
            double stackedX = plotX; // right edge of last stacked segment

            for (int si = 0; si < chart.Series.Count; si++)
            {
                var series = chart.Series[si];
                double? rawVal = ci < series.Values.Count ? series.Values[ci] : null;
                if (rawVal is null) continue;

                double val   = rawVal.Value;
                double barW  = Math.Abs(val / range) * plotW;
                if (barW < 0.5) barW = 0.5;

                double barY = stacked ? catY : catY + si * serH;
                double barX = stacked ? stackedX : plotX;

                var color = GetSeriesColor(chart, si, ci, seriesColors);
                var brush = FreezeBrush(new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B)));

                dc.DrawRectangle(brush, null, new Rect(barX, barY, barW, serH - 1));

                if (stacked) stackedX += barW;
            }
        }
    }

    // ── Line chart ────────────────────────────────────────────────────────────

    private static void RenderLineChart(
        DrawingContext dc, FreeP.Core.Model.ChartShape chart,
        IReadOnlyList<SrgbColor> seriesColors,
        double plotX, double plotY, double plotW, double plotH,
        bool withMarkers)
    {
        int catCount = Math.Max(1, chart.Categories.Count);
        if (chart.Series.Count == 0) return;

        var (minVal, maxVal) = ComputeAxisRange(chart);
        double range = maxVal - minVal;
        if (range <= 0) return;

        double stepX = plotW / Math.Max(1, catCount - 1);

        for (int si = 0; si < chart.Series.Count; si++)
        {
            var series = chart.Series[si];
            var color  = GetSeriesColor(chart, si, 0, seriesColors);
            var pen    = new Pen(FreezeBrush(new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B))), 1.5);
            if (pen.CanFreeze) pen.Freeze();

            Point? prev = null;
            for (int ci = 0; ci < catCount; ci++)
            {
                double? rawVal = ci < series.Values.Count ? series.Values[ci] : null;
                if (rawVal is null) { prev = null; continue; }

                double px = plotX + ci * stepX;
                double py = plotY + plotH - (rawVal.Value - minVal) / range * plotH;

                var pt = new Point(px, py);

                if (prev.HasValue)
                    dc.DrawLine(pen, prev.Value, pt);

                if (withMarkers)
                {
                    var markerBrush = FreezeBrush(
                        new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B)));
                    dc.DrawEllipse(markerBrush, null, pt, 3, 3);
                }

                prev = pt;
            }
        }
    }

    // ── Pie chart ─────────────────────────────────────────────────────────────

    private static void RenderPieChart(
        DrawingContext dc, FreeP.Core.Model.ChartShape chart,
        IReadOnlyList<SrgbColor> seriesColors,
        double plotX, double plotY, double plotW, double plotH)
    {
        if (chart.Series.Count == 0) return;

        var firstSeries = chart.Series[0];
        var values = firstSeries.Values.Where(v => v.HasValue && v.Value > 0).Select(v => v!.Value).ToList();
        if (values.Count == 0) return;

        double total = values.Sum();
        if (total <= 0) return;

        double cx = plotX + plotW / 2;
        double cy = plotY + plotH / 2;
        double r  = Math.Min(plotW, plotH) / 2 * 0.85;

        double startAngle = -Math.PI / 2; // start at top

        // Accent colors for pie slices (cycle if more slices than theme colors)
        var accentPalette = new[]
        {
            Color.FromRgb(0x4F, 0x81, 0xBD),
            Color.FromRgb(0xC0, 0x50, 0x4D),
            Color.FromRgb(0x9B, 0xBB, 0x59),
            Color.FromRgb(0x80, 0x64, 0xA2),
            Color.FromRgb(0x4B, 0xAC, 0xC6),
            Color.FromRgb(0xF7, 0x96, 0x46)
        };

        for (int i = 0; i < values.Count; i++)
        {
            double sweepAngle = values[i] / total * 2 * Math.PI;
            double endAngle   = startAngle + sweepAngle;

            // Resolve slice color: per-point override → series color → accent palette
            SrgbColor sc;
            if (firstSeries.PointColors.TryGetValue(i, out var pointColor))
                sc = new SrgbColor(pointColor.Resolved.R, pointColor.Resolved.G, pointColor.Resolved.B);
            else if (seriesColors.Count > 0)
                sc = new SrgbColor(
                    accentPalette[i % accentPalette.Length].R,
                    accentPalette[i % accentPalette.Length].G,
                    accentPalette[i % accentPalette.Length].B);
            else
                sc = new SrgbColor(
                    accentPalette[i % accentPalette.Length].R,
                    accentPalette[i % accentPalette.Length].G,
                    accentPalette[i % accentPalette.Length].B);

            var brush = FreezeBrush(new SolidColorBrush(Color.FromRgb(sc.R, sc.G, sc.B)));

            // Build wedge geometry via StreamGeometry
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                bool largeArc = sweepAngle > Math.PI;
                var start = new Point(cx + r * Math.Cos(startAngle), cy + r * Math.Sin(startAngle));
                var end   = new Point(cx + r * Math.Cos(endAngle),   cy + r * Math.Sin(endAngle));

                ctx.BeginFigure(new Point(cx, cy), isFilled: true, isClosed: true);
                ctx.LineTo(start, isStroked: false, isSmoothJoin: false);
                ctx.ArcTo(end, new Size(r, r), 0, largeArc,
                    SweepDirection.Clockwise, isStroked: false, isSmoothJoin: false);
            }
            if (geo.CanFreeze) geo.Freeze();

            var borderPen = new Pen(FreezeBrush(new SolidColorBrush(Colors.White)), 0.8);
            if (borderPen.CanFreeze) borderPen.Freeze();

            dc.DrawGeometry(brush, borderPen, geo);

            startAngle = endAngle;
        }
    }

    // ── Area chart ────────────────────────────────────────────────────────────

    private static void RenderAreaChart(
        DrawingContext dc, FreeP.Core.Model.ChartShape chart,
        IReadOnlyList<SrgbColor> seriesColors,
        double plotX, double plotY, double plotW, double plotH)
    {
        int catCount = Math.Max(1, chart.Categories.Count);
        if (chart.Series.Count == 0) return;

        var (minVal, maxVal) = ComputeAxisRange(chart);
        double range = maxVal - minVal;
        if (range <= 0) return;

        double stepX = plotW / Math.Max(1, catCount - 1);
        double baseY = plotY + plotH;

        // Draw series back to front (later series on top)
        for (int si = chart.Series.Count - 1; si >= 0; si--)
        {
            var series = chart.Series[si];
            var color  = GetSeriesColor(chart, si, 0, seriesColors);
            var brush  = FreezeBrush(new SolidColorBrush(
                Color.FromArgb(200, color.R, color.G, color.B)));

            if (series.Values.Count == 0) continue;

            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(plotX, baseY), isFilled: true, isClosed: true);

                for (int ci = 0; ci < catCount; ci++)
                {
                    double? rawVal = ci < series.Values.Count ? series.Values[ci] : null;
                    double  val    = rawVal ?? 0;
                    double  px     = plotX + ci * stepX;
                    double  py     = plotY + plotH - (val - minVal) / range * plotH;
                    ctx.LineTo(new Point(px, py), isStroked: true, isSmoothJoin: false);
                }

                // Close to bottom-right then bottom-left
                ctx.LineTo(new Point(plotX + plotW, baseY), isStroked: false, isSmoothJoin: false);
            }
            if (geo.CanFreeze) geo.Freeze();

            dc.DrawGeometry(brush, null, geo);
        }
    }

    // ── Chart helpers ─────────────────────────────────────────────────────────

    private static SrgbColor GetSeriesColor(
        FreeP.Core.Model.ChartShape chart, int seriesIndex, int pointIndex,
        IReadOnlyList<SrgbColor> seriesColors)
    {
        if (seriesIndex < seriesColors.Count) return seriesColors[seriesIndex];
        // Fallback cycle
        var fallbacks = new SrgbColor[]
        {
            new(0x4F, 0x81, 0xBD), new(0xC0, 0x50, 0x4D),
            new(0x9B, 0xBB, 0x59), new(0x80, 0x64, 0xA2),
            new(0x4B, 0xAC, 0xC6), new(0xF7, 0x96, 0x46)
        };
        return fallbacks[seriesIndex % fallbacks.Length];
    }

    private static (double min, double max) ComputeAxisRange(FreeP.Core.Model.ChartShape chart)
    {
        double min = 0, max = 0;
        foreach (var series in chart.Series)
        {
            foreach (var v in series.Values)
            {
                if (v.HasValue)
                {
                    min = Math.Min(min, v.Value);
                    max = Math.Max(max, v.Value);
                }
            }
        }

        // Apply explicit axis overrides
        if (chart.ValueAxis.Min.HasValue) min = chart.ValueAxis.Min.Value;
        if (chart.ValueAxis.Max.HasValue) max = chart.ValueAxis.Max.Value;

        if (max <= min) max = min + 1; // avoid zero range

        // Nice round up
        double range   = max - min;
        double niceMax = Math.Ceiling(max / (range / 5)) * (range / 5);
        return (min, niceMax > max ? niceMax : max);
    }

    private static string FormatAxisValue(double v) =>
        Math.Abs(v) >= 1000
            ? $"{v / 1000:G4}K"
            : v == Math.Floor(v)
                ? ((long)v).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : v.ToString("G3", System.Globalization.CultureInfo.InvariantCulture);

    private static void DrawChartLabel(
        DrawingContext dc, string text, Rect rect,
        bool isBold, double fontSize, TextAlignment align)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (rect.Width <= 0 || rect.Height <= 0) return;

        var typeface = new Typeface(
            new FontFamily("Calibri"),
            FontStyles.Normal,
            isBold ? FontWeights.Bold : FontWeights.Normal,
            FontStretches.Normal);

        var ft = new FormattedText(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize * (96.0 / 72.0),
            FreezeBrush(new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40))),
            numberSubstitution: null,
            textFormattingMode: TextFormattingMode.Display,
            pixelsPerDip: 1.0)
        {
            MaxTextWidth   = rect.Width,
            MaxLineCount   = 1,
            TextAlignment  = align,
            Trimming       = TextTrimming.CharacterEllipsis
        };

        dc.DrawText(ft, new Point(rect.X, rect.Y));
    }

    // ── Text ────────────────────────────────────────────────────────────────────

    private static void RenderText(DrawingContext dc, ResolvedTextLayout text, LayoutRect bounds)
    {
        double insetLeft = text.InsetLeftDip;
        double insetTop = text.InsetTopDip;
        double insetRight = text.InsetRightDip;
        double insetBottom = text.InsetBottomDip;

        double textAreaW = Math.Max(0, bounds.Width - insetLeft - insetRight);
        double textAreaH = Math.Max(0, bounds.Height - insetTop - insetBottom);

        // Measure all paragraphs to determine total height for vertical anchoring.
        var formatted = new List<(FormattedText ft, double spaceAfter)>();
        double totalH = 0;

        foreach (var para in text.Paragraphs)
        {
            if (para.Runs.Count == 0) continue;

            var ft = BuildFormattedText(para, textAreaW, text.Wrap);
            formatted.Add((ft, para.SpaceAfterPt * (96.0 / 72.0)));
            totalH += ft.Height + para.SpaceBeforePt * (96.0 / 72.0) + para.SpaceAfterPt * (96.0 / 72.0);
        }

        // Determine starting Y based on vertical anchor.
        double startY = text.Anchor switch
        {
            VerticalAnchor.Middle => bounds.Y + insetTop + Math.Max(0, (textAreaH - totalH) / 2),
            VerticalAnchor.Bottom => bounds.Y + insetTop + Math.Max(0, textAreaH - totalH),
            _ => bounds.Y + insetTop  // Top (default)
        };

        double curY = startY;

        int paraIdx = 0;
        foreach (var para in text.Paragraphs)
        {
            if (para.Runs.Count == 0) { paraIdx++; continue; }

            var (ft, spaceAfterDip) = formatted[paraIdx];
            double spaceBefore = para.SpaceBeforePt * (96.0 / 72.0);
            curY += spaceBefore;

            // Horizontal alignment (FormattedText handles Left/Center/Right/Justify).
            double textX = bounds.X + insetLeft;

            dc.DrawText(ft, new Point(textX, curY));

            curY += ft.Height + spaceAfterDip;
            paraIdx++;
        }
    }

    private static FormattedText BuildFormattedText(ResolvedParagraph para, double maxWidth, bool wrap)
    {
        // Combine all runs into a single string (FormattedText supports range formatting).
        var sb = new System.Text.StringBuilder();
        foreach (var run in para.Runs)
            sb.Append(run.Text);

        string text = sb.ToString();
        if (text.Length == 0) text = " "; // keep non-empty for height measurement

        // Use the first run's properties as the base typeface.
        var firstRun = para.Runs[0];
        var typeface = new Typeface(new FontFamily(firstRun.FontFamily),
            firstRun.Italic ? FontStyles.Italic : FontStyles.Normal,
            firstRun.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStretches.Normal);

        double emSizePx = firstRun.FontSizePt * (96.0 / 72.0);

        var brush = new SolidColorBrush(
            Color.FromRgb(firstRun.Color.R, firstRun.Color.G, firstRun.Color.B));
        if (brush.CanFreeze) brush.Freeze();

        // P1: Use Display formatting mode for GDI-compatible metrics (matches PowerPoint's
        // pixel-grid-snapped text rendering at 96 DPI). pixelsPerDip = 1.0 is correct for
        // RenderTargetBitmap at 96 DPI.
        var ft = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            emSizePx,
            brush,
            numberSubstitution: null,
            textFormattingMode: TextFormattingMode.Display,
            pixelsPerDip: 1.0);

        if (wrap && maxWidth > 0)
            ft.MaxTextWidth = maxWidth;

        ft.TextAlignment = para.Align switch
        {
            TextAlign.Center => TextAlignment.Center,
            TextAlign.Right => TextAlignment.Right,
            TextAlign.Justify or TextAlign.Distributed => TextAlignment.Justify,
            _ => TextAlignment.Left
        };

        // Apply per-run formatting as ranges.
        int pos = 0;
        foreach (var run in para.Runs)
        {
            int len = run.Text.Length;
            if (len == 0) continue;

            if (run.Bold)
                ft.SetFontWeight(FontWeights.Bold, pos, len);
            if (run.Italic)
                ft.SetFontStyle(FontStyles.Italic, pos, len);
            if (run.Underline)
                ft.SetTextDecorations(TextDecorations.Underline, pos, len);
            if (run.Strikethrough)
                ft.SetTextDecorations(TextDecorations.Strikethrough, pos, len);

            ft.SetFontFamily(new FontFamily(run.FontFamily), pos, len);
            ft.SetFontSize(run.FontSizePt * (96.0 / 72.0), pos, len);
            var runBrush = new SolidColorBrush(
                Color.FromRgb(run.Color.R, run.Color.G, run.Color.B));
            if (runBrush.CanFreeze) runBrush.Freeze();
            ft.SetForegroundBrush(runBrush, pos, len);

            pos += len;
        }

        return ft;
    }

    // ── WPF primitive helpers ──────────────────────────────────────────────────

    private static Brush? MakeBrush(ResolvedFill fill, LayoutRect bounds) => fill switch
    {
        ResolvedFill.None => null,
        ResolvedFill.Solid s => FreezeBrush(
            new SolidColorBrush(Color.FromRgb(s.Color.R, s.Color.G, s.Color.B))),
        ResolvedFill.Gradient g => MakeGradientBrush(g, bounds),
        _ => null
    };

    private static Brush MakeGradientBrush(ResolvedFill.Gradient g, LayoutRect bounds)
    {
        // Angle: 0 = left→right, 90 = top→bottom.
        double angleRad = g.AngleDegrees * Math.PI / 180.0;
        double cos = Math.Cos(angleRad);
        double sin = Math.Sin(angleRad);

        // Map angle to WPF start/end points (GradientBrush uses 0,0..1,1 relative space).
        // 0° → left to right; 90° → top to bottom
        var startPoint = new Point(
            cos >= 0 ? 0 : 1,
            sin >= 0 ? 0 : 1);
        var endPoint = new Point(
            cos >= 0 ? 1 : 0,
            sin >= 0 ? 1 : 0);

        var brush = new LinearGradientBrush(
            Color.FromRgb(g.StartColor.R, g.StartColor.G, g.StartColor.B),
            Color.FromRgb(g.EndColor.R, g.EndColor.G, g.EndColor.B),
            startPoint,
            endPoint);

        if (brush.CanFreeze) brush.Freeze();
        return brush;
    }

    private static Pen? MakePen(ResolvedOutline outline)
    {
        if (outline is not ResolvedOutline.Visible vis) return null;

        var brush = new SolidColorBrush(Color.FromRgb(vis.Color.R, vis.Color.G, vis.Color.B));
        if (brush.CanFreeze) brush.Freeze();

        var pen = new Pen(brush, vis.WidthDip);

        var dashStyle = vis.Dash switch
        {
            OutlineDash.Dash => DashStyles.Dash,
            OutlineDash.Dot => DashStyles.Dot,
            OutlineDash.DashDot => DashStyles.DashDot,
            OutlineDash.LongDash => new DashStyle(new[] { 8.0, 3.0 }, 0),
            OutlineDash.LongDashDot => new DashStyle(new[] { 8.0, 3.0, 1.0, 3.0 }, 0),
            OutlineDash.LongDashDotDot => new DashStyle(new[] { 8.0, 3.0, 1.0, 3.0, 1.0, 3.0 }, 0),
            OutlineDash.SystemDash => DashStyles.Dash,
            OutlineDash.SystemDot => DashStyles.Dot,
            OutlineDash.SystemDashDot => DashStyles.DashDot,
            _ => DashStyles.Solid
        };

        pen.DashStyle = dashStyle;
        if (pen.CanFreeze) pen.Freeze();
        return pen;
    }

    private static T FreezeBrush<T>(T brush) where T : Brush
    {
        if (brush.CanFreeze) brush.Freeze();
        return brush;
    }

    // ── ShapeGeometry → WPF StreamGeometry ─────────────────────────────────────

    private static System.Windows.Media.Geometry ContourListToGeometry(ShapeGeometry shape)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            foreach (var contour in shape.Contours)
                AppendContour(ctx, contour);
        }

        if (geometry.CanFreeze) geometry.Freeze();
        return geometry;
    }

    private static void AppendContour(StreamGeometryContext ctx, ShapeContour contour)
    {
        ctx.BeginFigure(ToPoint(contour.Start), contour.Filled, contour.Closed);
        foreach (var seg in contour.Segments)
        {
            switch (seg.Kind)
            {
                case ShapeSegmentKind.Line:
                    ctx.LineTo(ToPoint(seg.End), isStroked: true, isSmoothJoin: false);
                    break;
                case ShapeSegmentKind.CubicBezier:
                    ctx.BezierTo(ToPoint(seg.Control1), ToPoint(seg.Control2), ToPoint(seg.End),
                                 isStroked: true, isSmoothJoin: false);
                    break;
                case ShapeSegmentKind.Arc:
                    ctx.ArcTo(
                        ToPoint(seg.End),
                        new Size(seg.RadiusX, seg.RadiusY),
                        rotationAngle: 0,
                        seg.LargeArc,
                        seg.SweepClockwise ? SweepDirection.Clockwise : SweepDirection.Counterclockwise,
                        isStroked: true,
                        isSmoothJoin: false);
                    break;
            }
        }
    }

    private static Point ToPoint(LayoutPoint p) => new(p.X, p.Y);

    // ── Composition helper ──────────────────────────────────────────────────────

    private void EnsureOps()
    {
        if (_cachedOps is not null) return;

        var presentation = Presentation;
        var slide = Slide;

        if (presentation is null || slide is null)
        {
            _slideWidthDip = 0;
            _slideHeightDip = 0;
            _cachedOps = Array.Empty<DrawOp>();
            return;
        }

        _slideWidthDip = presentation.SlideSizeCxEmu / 9525.0;
        _slideHeightDip = presentation.SlideSizeCyEmu / 9525.0;
        _cachedOps = SlideCompositor.Compose(presentation, slide);
    }
}

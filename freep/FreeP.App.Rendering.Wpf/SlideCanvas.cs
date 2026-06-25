using FreeP.App.Compositor;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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
///
/// Wave 3C: call <see cref="AttachEditing"/> once (and on every file new/open) to enable
/// selection, move/resize/rotate, and in-canvas text editing.
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

    // ── Editing (Wave 3C) ─────────────────────────────────────────────────────

    private CanvasGestureHandler?  _gestureHandler;
    private InCanvasTextEditor?    _textEditor;
    private Canvas?                _textOverlay;   // WPF Canvas layered above SlideCanvas for text-edit overlay

    /// <summary>
    /// The current slide→screen transform (updated on every render pass).
    /// Used by the gesture handler and adorner layer.
    /// </summary>
    public SlideTransform CurrentTransform { get; private set; } = SlideTransform.Identity;

    /// <summary>
    /// Attaches (or re-attaches) editing interaction to an <see cref="EditingSession"/>.
    /// Call once after constructing the canvas and once on every file new/open to wire up
    /// the new Editor instance.  The overlay canvas must already be in the visual tree.
    /// </summary>
    public void AttachEditing(EditingSession editor, Canvas textOverlay)
    {
        // Detach previous handler if any (don't re-add adorner on every call)
        _textEditor    = null;
        _gestureHandler = new CanvasGestureHandler(this, editor);
        _textOverlay   = textOverlay;
        _textEditor    = new InCanvasTextEditor(this, editor, textOverlay);
    }

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

        // Expose the slide→screen transform so the editing layer can use it.
        CurrentTransform = new SlideTransform(scale, offsetX, offsetY, _slideWidthDip, _slideHeightDip);

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
        const double axisLabelW   = 40.0;  // value axis label area (left for column; bottom for bar)
        const double catLabelH    = 16.0;  // category label area height (bottom for column, bottom for bar value axis)
        const double barCatLabelW = 44.0;  // category label width for horizontal bar (left side)
        const double gridlinePad  = 2.0;

        double titleAreaH  = chart.Title is not null ? titleH + margin : 0;
        bool   hasLegend   = chart.Legend.HasValue;
        bool   isPie       = chart.ChartType == FreeP.Core.Model.ChartType.Pie;
        bool   isBar       = chart.ChartType is FreeP.Core.Model.ChartType.BarClustered
                                             or FreeP.Core.Model.ChartType.BarStacked
                                             or FreeP.Core.Model.ChartType.BarStacked100;

        // Title
        if (chart.Title is not null)
        {
            var titleRect = new Rect(bounds.X + margin, bounds.Y + margin,
                bounds.Width - 2 * margin, titleH);
            DrawChartLabel(dc, chart.Title, titleRect, isBold: true, fontSize: 9.0,
                align: TextAlignment.Center);
        }

        // Legend area: PowerPoint default for column/bar/line = bottom; pie = right.
        // Respect the explicit legendPos from XML; when null (no legend element) skip.
        double legendAreaW = 0, legendAreaH = 0;
        bool   legendRight = chart.Legend is FreeP.Core.Model.LegendPosition.Right or
                                             FreeP.Core.Model.LegendPosition.Left;
        if (hasLegend)
        {
            if (legendRight)
                legendAreaW = Math.Min(90, bounds.Width * 0.20);
            else
                legendAreaH = legendH + margin;
        }

        // For horizontal bar charts: category labels are on the left (Y axis),
        // value axis labels are on the bottom (X axis).
        // For column/line/area: category labels on bottom, value labels on left.
        double plotLeft   = bounds.X + margin + (isPie ? 0 : (isBar ? barCatLabelW : axisLabelW));
        double plotTop    = bounds.Y + margin + titleAreaH;
        double plotRight  = bounds.X + bounds.Width  - margin - legendAreaW;
        double plotBottom = bounds.Y + bounds.Height - margin - legendAreaH
                                     - (isPie ? 0 : (isBar ? axisLabelW : catLabelH));
        double plotW = plotRight  - plotLeft;
        double plotH = plotBottom - plotTop;

        if (plotW <= 0 || plotH <= 0) return;

        double plotX = plotLeft;
        double plotY = plotTop;

        // ── Gridlines (drawn before bars so they appear behind) ─────────────────
        if (!isPie && chart.ValueAxis.HasMajorGridlines)
        {
            var gridPen = new Pen(
                FreezeBrush(new SolidColorBrush(Color.FromRgb(0xD9, 0xD9, 0xD9))), 0.5);
            if (gridPen.CanFreeze) gridPen.Freeze();

            var (minVal, maxVal, majorUnit) = ComputeNiceAxisRange(chart);
            double range = maxVal - minVal;

            if (isBar)
            {
                // Vertical gridlines for horizontal bar charts
                double steps = range / majorUnit;
                for (int gi = 0; gi <= (int)Math.Round(steps); gi++)
                {
                    double gx = plotX + plotW * gi / steps;
                    dc.DrawLine(gridPen, new Point(gx, plotY), new Point(gx, plotY + plotH));
                }
            }
            else
            {
                // Horizontal gridlines for column/line/area
                double steps = range / majorUnit;
                for (int gi = 0; gi <= (int)Math.Round(steps); gi++)
                {
                    double gy = plotY + plotH - plotH * gi / steps;
                    dc.DrawLine(gridPen, new Point(plotX, gy), new Point(plotX + plotW, gy));
                }
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
            if (isBar)
            {
                // For bar charts: category labels on left (Y axis), one per category row.
                // PowerPoint reverses category order: index 0 at bottom, last at top.
                int catN = chart.Categories.Count;
                double catStep = plotH / Math.Max(1, catN);
                for (int ci = 0; ci < catN; ci++)
                {
                    int renderRow = catN - 1 - ci;
                    double ly = plotY + renderRow * catStep;
                    var labelRect = new Rect(bounds.X + margin, ly, barCatLabelW - 4, catStep);
                    DrawChartLabel(dc, chart.Categories[ci], labelRect, isBold: false, fontSize: 6.5,
                        align: TextAlignment.Right);
                }
            }
            else
            {
                // For column/line/area: category labels on bottom (X axis)
                double catStep = plotW / Math.Max(1, chart.Categories.Count);
                for (int ci = 0; ci < chart.Categories.Count; ci++)
                {
                    double lx = plotX + ci * catStep;
                    var labelRect = new Rect(lx, plotY + plotH + 2, catStep, catLabelH);
                    DrawChartLabel(dc, chart.Categories[ci], labelRect, isBold: false, fontSize: 7.0,
                        align: TextAlignment.Center);
                }
            }
        }

        // Value axis labels using nice tick values
        if (!isPie)
        {
            var (minVal, maxVal, majorUnit) = ComputeNiceAxisRange(chart);
            double range = maxVal - minVal;
            double steps = range / majorUnit;

            if (isBar)
            {
                // For bar charts: value axis labels on bottom (X axis)
                for (int ti = 0; ti <= (int)Math.Round(steps); ti++)
                {
                    double val = minVal + majorUnit * ti;
                    double vx  = plotX + plotW * ti / steps;
                    var labelRect = new Rect(vx - axisLabelW / 2, plotY + plotH + 2, axisLabelW, catLabelH);
                    DrawChartLabel(dc, FormatAxisValue(val), labelRect,
                        isBold: false, fontSize: 6.5, align: TextAlignment.Center);
                }
            }
            else
            {
                // For column/line/area: value axis labels on left (Y axis)
                for (int ti = 0; ti <= (int)Math.Round(steps); ti++)
                {
                    double val = minVal + majorUnit * ti;
                    double vy  = plotY + plotH - plotH * ti / steps;
                    var labelRect = new Rect(bounds.X + margin, vy - 6, axisLabelW - gridlinePad, 12);
                    DrawChartLabel(dc, FormatAxisValue(val), labelRect,
                        isBold: false, fontSize: 6.5, align: TextAlignment.Right);
                }
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

            if (isPie)
            {
                // Pie chart legend: one entry per category (slice), not per series
                int catItems = chart.Categories.Count > 0 ? chart.Categories.Count
                    : (chart.Series[0].Values.Count > 0 ? chart.Series[0].Values.Count : 0);
                int maxItems = (int)Math.Max(1, legendRight ? plotH / itemH : lw / 80);
                int itemsToShow = Math.Min(catItems, maxItems);

                for (int ci = 0; ci < itemsToShow; ci++)
                {
                    var sc = ci < chartOp.SeriesColors.Count
                        ? chartOp.SeriesColors[ci]
                        : new SrgbColor(0x4F, 0x81, 0xBD);
                    string label = ci < chart.Categories.Count ? chart.Categories[ci] : $"Point {ci + 1}";

                    if (legendRight)
                    {
                        double iy = ly + ci * itemH;
                        dc.DrawRectangle(
                            FreezeBrush(new SolidColorBrush(Color.FromRgb(sc.R, sc.G, sc.B))),
                            null,
                            new Rect(lx, iy + 3, 8, 8));
                        DrawChartLabel(dc, label,
                            new Rect(lx + 10, iy, lw - 10, itemH),
                            isBold: false, fontSize: 7.0, align: TextAlignment.Left);
                    }
                    else
                    {
                        double ix = lx + ci * 80.0;
                        dc.DrawRectangle(
                            FreezeBrush(new SolidColorBrush(Color.FromRgb(sc.R, sc.G, sc.B))),
                            null,
                            new Rect(ix, ly + 3, 8, 8));
                        DrawChartLabel(dc, label,
                            new Rect(ix + 10, ly, 70, itemH),
                            isBold: false, fontSize: 7.0, align: TextAlignment.Left);
                    }
                }
            }
            else
            {
                // Column/bar/line/area: one entry per series
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
    }

    // ── Column chart ─────────────────────────────────────────────────────────

    private static void RenderColumnChart(
        DrawingContext dc, FreeP.Core.Model.ChartShape chart,
        IReadOnlyList<SrgbColor> seriesColors,
        double plotX, double plotY, double plotW, double plotH)
    {
        int catCount = Math.Max(1, chart.Categories.Count);
        if (chart.Series.Count == 0) return;

        var (minVal, maxVal, _) = ComputeNiceAxisRange(chart);
        double range = maxVal - minVal;
        if (range <= 0) return;

        bool stacked = chart.ChartType is FreeP.Core.Model.ChartType.ColumnStacked
                                       or FreeP.Core.Model.ChartType.ColumnStacked100;

        // PowerPoint default: gap width = 150% of bar cluster width.
        // barCluster / (barCluster + gap) = 1 / (1 + 1.5) = 0.4
        // So bars take up 40% of the category slot.
        const double gapRatio   = 1.5;  // gap = 150% of bar cluster
        double catW             = plotW / catCount;
        double clusterW         = catW / (1.0 + gapRatio);  // 40% of catW
        double gapW             = catW - clusterW;           // 60% of catW
        double halfGap          = gapW / 2.0;
        int    serCount         = Math.Max(1, chart.Series.Count);
        double serW             = stacked ? clusterW : clusterW / serCount;

        for (int ci = 0; ci < catCount; ci++)
        {
            double catLeft  = plotX + ci * catW + halfGap;
            double stackedY = plotY + plotH;

            for (int si = 0; si < chart.Series.Count; si++)
            {
                var series = chart.Series[si];
                double? rawVal = ci < series.Values.Count ? series.Values[ci] : null;
                if (rawVal is null) continue;

                double val  = rawVal.Value;
                double barH = Math.Abs((val - minVal) / range * plotH);
                if (barH < 0.5) barH = 0.5;

                double barX = stacked ? catLeft : catLeft + si * serW;
                double barY = stacked
                    ? (stackedY - Math.Abs(val / range) * plotH)
                    : (plotY + plotH - (val - minVal) / range * plotH);

                var color = GetSeriesColor(chart, si, ci, seriesColors);
                var brush = FreezeBrush(new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B)));

                // Leave 1px gap between adjacent series bars
                double drawW = Math.Max(1, stacked ? serW : serW - 1);
                if (stacked)
                {
                    double h = Math.Abs(val / range) * plotH;
                    if (h < 0.5) h = 0.5;
                    dc.DrawRectangle(brush, null, new Rect(barX, stackedY - h, drawW, h));
                    stackedY -= h;
                }
                else
                {
                    dc.DrawRectangle(brush, null, new Rect(barX, barY, drawW, barH));
                }
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

        var (minVal, maxVal, _) = ComputeNiceAxisRange(chart);
        double range = maxVal - minVal;
        if (range <= 0) return;

        bool stacked = chart.ChartType is FreeP.Core.Model.ChartType.BarStacked
                                       or FreeP.Core.Model.ChartType.BarStacked100;

        // PowerPoint default: gap width = 150% of bar cluster height.
        const double gapRatio  = 1.5;
        double catH            = plotH / catCount;
        double clusterH        = catH / (1.0 + gapRatio);
        double gapH            = catH - clusterH;
        double halfGap         = gapH / 2.0;
        int    serCount        = Math.Max(1, chart.Series.Count);
        double serH            = stacked ? clusterH : clusterH / serCount;

        // PowerPoint renders bar chart categories in REVERSE order:
        // category index 0 is at the BOTTOM, last category at the TOP.
        for (int ci = 0; ci < catCount; ci++)
        {
            int    renderRow = catCount - 1 - ci;        // reversed
            double catTop    = plotY + renderRow * catH + halfGap;
            double stackedX  = plotX;

            for (int si = 0; si < chart.Series.Count; si++)
            {
                var series = chart.Series[si];
                double? rawVal = ci < series.Values.Count ? series.Values[ci] : null;
                if (rawVal is null) continue;

                double val  = rawVal.Value;
                double barW = Math.Abs((val - minVal) / range * plotW);
                if (barW < 0.5) barW = 0.5;

                // PowerPoint also reverses series order within each cluster:
                // series index 0 is at the BOTTOM of the cluster.
                int    renderSer = stacked ? si : (serCount - 1 - si);
                double barY      = stacked ? catTop : catTop + renderSer * serH;
                double barX      = stacked ? stackedX : plotX;

                var color = GetSeriesColor(chart, si, ci, seriesColors);
                var brush = FreezeBrush(new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B)));

                double drawH = Math.Max(1, stacked ? serH : serH - 1);
                dc.DrawRectangle(brush, null, new Rect(barX, barY, barW, drawH));

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

        var (minVal, maxVal, _) = ComputeNiceAxisRange(chart);
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

        double startAngle = -Math.PI / 2; // start at top (12 o'clock, clockwise)

        for (int i = 0; i < values.Count; i++)
        {
            double sweepAngle = values[i] / total * 2 * Math.PI;
            double endAngle   = startAngle + sweepAngle;

            // Resolve slice color: seriesColors is pre-expanded per-point by the compositor
            // (cycling accent1-6 from the theme) so index i gives the correct slice fill.
            SrgbColor sc = i < seriesColors.Count
                ? seriesColors[i]
                : new SrgbColor(0x4F, 0x81, 0xBD);

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

        var (minVal, maxVal, _) = ComputeNiceAxisRange(chart);
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

    /// <summary>
    /// Computes nice axis min/max/majorUnit matching PowerPoint's auto-scale algorithm:
    /// major unit is chosen from {1, 2, 2.5, 5} × 10^n so that there are ~4-6 intervals.
    /// Returns (min, max, majorUnit).
    /// </summary>
    internal static (double min, double max, double majorUnit) ComputeNiceAxisRange(
        FreeP.Core.Model.ChartShape chart)
    {
        double dataMin = 0, dataMax = 0;
        foreach (var series in chart.Series)
        {
            foreach (var v in series.Values)
            {
                if (v.HasValue)
                {
                    dataMin = Math.Min(dataMin, v.Value);
                    dataMax = Math.Max(dataMax, v.Value);
                }
            }
        }

        // Apply explicit axis overrides
        double min = chart.ValueAxis.Min ?? (dataMin >= 0 ? 0 : dataMin);
        double max = chart.ValueAxis.Max ?? dataMax;

        if (max <= min) max = min + 1;

        // Pick a nice major unit so we get approximately 4-5 gridlines.
        double range = max - min;
        double rawUnit = range / 4.0;
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(rawUnit)));
        double norm = rawUnit / magnitude;

        double niceMult = norm switch
        {
            < 1.5  => 1.0,
            < 2.25 => 2.0,
            < 3.75 => 2.5,
            < 7.5  => 5.0,
            _      => 10.0
        };

        double majorUnit = niceMult * magnitude;

        // Round max up to next multiple of majorUnit
        double niceMax = Math.Ceiling(max / majorUnit) * majorUnit;
        double niceMin = min >= 0 ? 0 : Math.Floor(min / majorUnit) * majorUnit;

        // PowerPoint adds one more tick of headroom when the data max exactly equals the
        // computed niceMax (it never draws data touching the top gridline).
        if (Math.Abs(niceMax - max) < majorUnit * 1e-9)
            niceMax += majorUnit;

        return (niceMin, niceMax, majorUnit);
    }

    // Keep old signature for compatibility with existing callers that only need min/max
    private static (double min, double max) ComputeAxisRange(FreeP.Core.Model.ChartShape chart)
    {
        var (min, max, _) = ComputeNiceAxisRange(chart);
        return (min, max);
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
        //
        // Wave 6A investigation: GlyphRun-based text rendering was evaluated as an alternative
        // to FormattedText. Baseline parity measurements on text-heavy decks showed:
        //   01-title-slide   : 1.3158% mean channel diff
        //   03-mixed-text    : 1.0469% mean channel diff
        //   04-picture       : 0.1498% mean channel diff
        // Visual inspection and heatmap analysis confirmed the residual diff is pure ClearType
        // sub-pixel antialiasing fringing (red/green halos on glyph stroke edges), not position
        // or metrics errors. PowerPoint's PNG export uses DirectWrite directly; WPF's rendering
        // pipeline (both FormattedText and GlyphRun) applies a different ClearType configuration.
        // GlyphRun uses the same WPF rasterizer so it cannot reduce this AA-floor residual.
        // Decision: keep FormattedText. The ~1% residual is an antialiasing floor, not fixable
        // via metrics changes within the WPF rendering pipeline.
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

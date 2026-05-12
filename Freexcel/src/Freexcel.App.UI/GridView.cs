using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Globalization;
using Freexcel.Core.Model;
using CellHAlign = Freexcel.Core.Model.HorizontalAlignment;

namespace Freexcel.App.UI;

/// <summary>
/// A high-performance, virtualized spreadsheet grid control.
/// Renders only the visible portion of the workbook using low-level DrawingContext.
/// </summary>
public class GridView : FrameworkElement
{
    private const double HeaderSize = 30;
    private const double ResizeHitZone = 4;
    private const double MinCellSize = 5;

    private static readonly Typeface DefaultTypeface = new("Segoe UI");
    private static readonly Brush GridLineBrush = new SolidColorBrush(Color.FromRgb(220, 220, 220));
    private static readonly Brush TextBrush = Brushes.Black;
    private static readonly Brush HeaderBackgroundBrush = new SolidColorBrush(Color.FromRgb(242, 242, 242));
    private static readonly Brush SelectionBrush = new SolidColorBrush(Color.FromArgb(32, 33, 115, 70));
    private static readonly Pen SelectionPen = new(new SolidColorBrush(Color.FromRgb(33, 115, 70)), 2);
    private static readonly Pen GridPen = new(GridLineBrush, 1);
    private static readonly Pen ResizeLinePen = CreateResizeLinePen();
    private static readonly Pen FreezePen = CreateFreezePen();

    private static Pen CreateResizeLinePen()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(100, 100, 100)), 1);
        pen.Freeze();
        return pen;
    }

    private static Pen CreateFreezePen()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(100, 100, 200)), 2);
        pen.Freeze();
        return pen;
    }

    // Dependency Properties
    public static readonly DependencyProperty ViewportProperty =
        DependencyProperty.Register(nameof(Viewport), typeof(ViewportModel), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public ViewportModel? Viewport
    {
        get => (ViewportModel?)GetValue(ViewportProperty);
        set => SetValue(ViewportProperty, value);
    }

    public static readonly DependencyProperty SelectedRangeProperty =
        DependencyProperty.Register(nameof(SelectedRange), typeof(GridRange?), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public GridRange? SelectedRange
    {
        get => (GridRange?)GetValue(SelectedRangeProperty);
        set => SetValue(SelectedRangeProperty, value);
    }

    public static readonly DependencyProperty ChartsProperty =
        DependencyProperty.Register(nameof(Charts), typeof(IReadOnlyList<ChartModel>), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<ChartModel>? Charts
    {
        get => (IReadOnlyList<ChartModel>?)GetValue(ChartsProperty);
        set => SetValue(ChartsProperty, value);
    }

    // Resize drag state
    private enum ResizeTarget { None, Row, Column }
    private ResizeTarget _resizeTarget = ResizeTarget.None;
    private uint _resizeIndex;
    private double _resizeDragStart;
    private double _resizeSizeStart;
    private double _resizeLinePos;

    // Fired when user completes a resize drag
    public event Action<uint, double>? RowResized;
    public event Action<uint, double>? ColumnResized;

    protected override void OnRender(DrawingContext dc)
    {
        if (Viewport == null) return;

        dc.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)));

        RenderHeaders(dc);
        RenderGridLines(dc);
        RenderCells(dc);
        RenderSelection(dc);
        RenderFreezeDivider(dc);
        RenderResizeLine(dc);
        RenderCharts(dc);

        dc.Pop();
    }

    // ── Mouse: resize hit-testing ─────────────────────────────────────────────

    private (ResizeTarget Target, uint Index, double CurrentSize) HitTestResize(Point pos)
    {
        if (Viewport == null) return (ResizeTarget.None, 0, 0);

        // Column: hover over right edge of a column header (y within header strip)
        if (pos.Y < HeaderSize)
        {
            foreach (var col in Viewport.ColMetrics)
            {
                double rightEdge = col.LeftOffset + col.Width + HeaderSize;
                if (Math.Abs(pos.X - rightEdge) <= ResizeHitZone)
                    return (ResizeTarget.Column, col.Col, col.Width);
            }
        }

        // Row: hover over bottom edge of a row header (x within header strip)
        if (pos.X < HeaderSize)
        {
            foreach (var row in Viewport.RowMetrics)
            {
                double bottomEdge = row.TopOffset + row.Height + HeaderSize;
                if (Math.Abs(pos.Y - bottomEdge) <= ResizeHitZone)
                    return (ResizeTarget.Row, row.Row, row.Height);
            }
        }

        return (ResizeTarget.None, 0, 0);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var pos = e.GetPosition(this);

        if (_resizeTarget == ResizeTarget.Column)
        {
            var col = Viewport!.ColMetrics.FirstOrDefault(c => c.Col == _resizeIndex);
            if (col is null) return;
            double newWidth = Math.Max(MinCellSize, _resizeSizeStart + (pos.X - _resizeDragStart));
            _resizeLinePos = col.LeftOffset + newWidth + HeaderSize;
            InvalidateVisual();
        }
        else if (_resizeTarget == ResizeTarget.Row)
        {
            var row = Viewport!.RowMetrics.FirstOrDefault(r => r.Row == _resizeIndex);
            if (row is null) return;
            double newHeight = Math.Max(MinCellSize, _resizeSizeStart + (pos.Y - _resizeDragStart));
            _resizeLinePos = row.TopOffset + newHeight + HeaderSize;
            InvalidateVisual();
        }
        else
        {
            var (target, _, _) = HitTestResize(pos);
            Cursor = target == ResizeTarget.Column ? Cursors.SizeWE
                   : target == ResizeTarget.Row    ? Cursors.SizeNS
                   : null;
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(this);
        var (target, index, size) = HitTestResize(pos);

        if (target != ResizeTarget.None)
        {
            _resizeTarget = target;
            _resizeIndex  = index;
            _resizeSizeStart = size;
            _resizeDragStart = target == ResizeTarget.Column ? pos.X : pos.Y;
            Cursor = target == ResizeTarget.Column ? Cursors.SizeWE : Cursors.SizeNS;

            // Initialise the overlay line at the current border position
            if (target == ResizeTarget.Column)
            {
                var col = Viewport!.ColMetrics.First(c => c.Col == index);
                _resizeLinePos = col.LeftOffset + col.Width + HeaderSize;
            }
            else
            {
                var row = Viewport!.RowMetrics.First(r => r.Row == index);
                _resizeLinePos = row.TopOffset + row.Height + HeaderSize;
            }

            CaptureMouse();
            e.Handled = true;   // prevent cell-selection from also firing
        }
        else
        {
            base.OnMouseLeftButtonDown(e);
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_resizeTarget != ResizeTarget.None)
        {
            var pos = e.GetPosition(this);
            double delta = _resizeTarget == ResizeTarget.Column
                ? pos.X - _resizeDragStart
                : pos.Y - _resizeDragStart;
            double newSize = Math.Max(MinCellSize, _resizeSizeStart + delta);

            if (_resizeTarget == ResizeTarget.Column)
                ColumnResized?.Invoke(_resizeIndex, newSize);
            else
                RowResized?.Invoke(_resizeIndex, newSize);

            _resizeTarget = ResizeTarget.None;
            Cursor = null;
            ReleaseMouseCapture();
            InvalidateVisual();
            e.Handled = true;
        }
        else
        {
            base.OnMouseLeftButtonUp(e);
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (_resizeTarget == ResizeTarget.None)
            Cursor = null;
        base.OnMouseLeave(e);
    }

    // ── Rendering ─────────────────────────────────────────────────────────────

    private void RenderFreezeDivider(DrawingContext dc)
    {
        if (Viewport?.FrozenPanes == null) return;
        var fp = Viewport.FrozenPanes;

        if (fp.Rows > 0)
        {
            var lastFrozenRow = Viewport.RowMetrics.FirstOrDefault(r => r.Row == fp.Rows);
            if (lastFrozenRow != null)
            {
                double y = lastFrozenRow.TopOffset + lastFrozenRow.Height + HeaderSize;
                dc.DrawLine(FreezePen, new Point(0, y), new Point(ActualWidth, y));
            }
        }

        if (fp.Cols > 0)
        {
            var lastFrozenCol = Viewport.ColMetrics.FirstOrDefault(c => c.Col == fp.Cols);
            if (lastFrozenCol != null)
            {
                double x = lastFrozenCol.LeftOffset + lastFrozenCol.Width + HeaderSize;
                dc.DrawLine(FreezePen, new Point(x, 0), new Point(x, ActualHeight));
            }
        }
    }

    private void RenderCharts(DrawingContext dc)
    {
        if (Charts == null || Viewport == null) return;
        foreach (var chart in Charts)
        {
            var img = ChartRenderer.Render(chart, Viewport);
            if (img == null) continue;
            var rect = new Rect(
                chart.Left + HeaderSize, chart.Top + HeaderSize,
                chart.Width, chart.Height);
            dc.DrawImage(img, rect);
        }
    }

    private void RenderResizeLine(DrawingContext dc)
    {
        if (_resizeTarget == ResizeTarget.Column)
            dc.DrawLine(ResizeLinePen,
                new Point(_resizeLinePos, 0),
                new Point(_resizeLinePos, ActualHeight));
        else if (_resizeTarget == ResizeTarget.Row)
            dc.DrawLine(ResizeLinePen,
                new Point(0, _resizeLinePos),
                new Point(ActualWidth, _resizeLinePos));
    }

    private void RenderSelection(DrawingContext dc)
    {
        if (Viewport == null || SelectedRange == null) return;

        var range = SelectedRange.Value;
        double? top = null, left = null, bottom = null, right = null;

        foreach (var row in Viewport.RowMetrics)
        {
            if (row.Row == range.Start.Row) top    = row.TopOffset + HeaderSize;
            if (row.Row == range.End.Row)   bottom = row.TopOffset + row.Height + HeaderSize;
        }

        foreach (var col in Viewport.ColMetrics)
        {
            if (col.Col == range.Start.Col) left  = col.LeftOffset + HeaderSize;
            if (col.Col == range.End.Col)   right = col.LeftOffset + col.Width + HeaderSize;
        }

        if (top.HasValue || bottom.HasValue || left.HasValue || right.HasValue)
        {
            double drawTop    = top    ?? HeaderSize;
            double drawBottom = bottom ?? ActualHeight;
            double drawLeft   = left   ?? HeaderSize;
            double drawRight  = right  ?? ActualWidth;

            var rect = new Rect(new Point(drawLeft, drawTop), new Point(drawRight, drawBottom));
            dc.DrawRectangle(SelectionBrush, null, rect);

            if (top.HasValue)    dc.DrawLine(SelectionPen, new Point(drawLeft,  drawTop),    new Point(drawRight, drawTop));
            if (bottom.HasValue) dc.DrawLine(SelectionPen, new Point(drawLeft,  drawBottom), new Point(drawRight, drawBottom));
            if (left.HasValue)   dc.DrawLine(SelectionPen, new Point(drawLeft,  drawTop),    new Point(drawLeft,  drawBottom));
            if (right.HasValue)  dc.DrawLine(SelectionPen, new Point(drawRight, drawTop),    new Point(drawRight, drawBottom));
        }
    }

    private void RenderHeaders(DrawingContext dc)
    {
        // Column Headers (A, B, C…)
        foreach (var col in Viewport!.ColMetrics)
        {
            var rect = new Rect(col.LeftOffset + HeaderSize, 0, col.Width, HeaderSize);
            dc.DrawRectangle(HeaderBackgroundBrush, GridPen, rect);

            var text = new FormattedText(
                CellAddress.NumberToColumnName(col.Col),
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                DefaultTypeface, 12, TextBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            dc.DrawText(text, new Point(
                rect.Left + (rect.Width  - text.Width)  / 2,
                rect.Top  + (rect.Height - text.Height) / 2));
        }

        // Row Headers (1, 2, 3…)
        foreach (var row in Viewport!.RowMetrics)
        {
            var rect = new Rect(0, row.TopOffset + HeaderSize, HeaderSize, row.Height);
            dc.DrawRectangle(HeaderBackgroundBrush, GridPen, rect);

            var text = new FormattedText(
                row.Row.ToString(),
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                DefaultTypeface, 11, TextBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            dc.DrawText(text, new Point(
                rect.Left + (rect.Width  - text.Width)  / 2,
                rect.Top  + (rect.Height - text.Height) / 2));
        }

        // Top-left corner
        dc.DrawRectangle(HeaderBackgroundBrush, GridPen, new Rect(0, 0, HeaderSize, HeaderSize));
    }

    private void RenderGridLines(DrawingContext dc)
    {
        // Grid lines are drawn as cell/header rectangle borders.
    }

    private void RenderCells(DrawingContext dc)
    {
        var styleLookup = Viewport!.Cells
            .Where(c => c.Style != null)
            .ToDictionary(c => (c.Row, c.Col), c => c.Style!);

        // Pass 1: backgrounds (fill color or white)
        foreach (var rowMetric in Viewport.RowMetrics)
        {
            foreach (var colMetric in Viewport.ColMetrics)
            {
                var rect = new Rect(
                    colMetric.LeftOffset + HeaderSize, rowMetric.TopOffset + HeaderSize,
                    colMetric.Width, rowMetric.Height);

                Brush fill = Brushes.White;
                if (styleLookup.TryGetValue((rowMetric.Row, colMetric.Col), out var bg)
                    && bg.FillColor.HasValue)
                {
                    fill = new SolidColorBrush(Color.FromRgb(
                        bg.FillColor.Value.R, bg.FillColor.Value.G, bg.FillColor.Value.B));
                }

                dc.DrawRectangle(fill, GridPen, rect);
            }
        }

        // Pass 2: explicit cell borders
        foreach (var cell in Viewport.Cells)
        {
            if (cell.Style == null) continue;
            var rowMetric = Viewport.RowMetrics.FirstOrDefault(r => r.Row == cell.Row);
            var colMetric = Viewport.ColMetrics.FirstOrDefault(c => c.Col == cell.Col);
            if (rowMetric is null || colMetric is null) continue;

            double x = colMetric.LeftOffset + HeaderSize;
            double y = rowMetric.TopOffset  + HeaderSize;
            double w = colMetric.Width;
            double h = rowMetric.Height;

            DrawBorderEdge(dc, cell.Style.BorderTop,    new Point(x,     y),     new Point(x + w, y));
            DrawBorderEdge(dc, cell.Style.BorderBottom, new Point(x,     y + h), new Point(x + w, y + h));
            DrawBorderEdge(dc, cell.Style.BorderLeft,   new Point(x,     y),     new Point(x,     y + h));
            DrawBorderEdge(dc, cell.Style.BorderRight,  new Point(x + w, y),     new Point(x + w, y + h));
        }

        // Pass 3: text
        var rowLookup = Viewport.RowMetrics.ToDictionary(r => r.Row);
        var colLookup = Viewport.ColMetrics.ToDictionary(c => c.Col);

        foreach (var cell in Viewport.Cells)
        {
            if (!rowLookup.TryGetValue(cell.Row, out var rowMetric)) continue;
            if (!colLookup.TryGetValue(cell.Col, out var colMetric)) continue;
            if (string.IsNullOrEmpty(cell.DisplayText)) continue;

            var style = cell.Style;
            var rect = new Rect(
                colMetric.LeftOffset + HeaderSize, rowMetric.TopOffset + HeaderSize,
                colMetric.Width, rowMetric.Height);

            var typeface = (style?.Bold == true, style?.Italic == true) switch
            {
                (true,  true)  => new Typeface(new FontFamily("Segoe UI"), FontStyles.Italic,  FontWeights.Bold,   FontStretches.Normal),
                (true,  false) => new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal,  FontWeights.Bold,   FontStretches.Normal),
                (false, true)  => new Typeface(new FontFamily("Segoe UI"), FontStyles.Italic,  FontWeights.Normal, FontStretches.Normal),
                _              => DefaultTypeface
            };

            double fontSize = (style?.FontSize > 0) ? style!.FontSize : 12.0;

            Brush textBrush = TextBrush;
            if (style?.FontColor is { } fc && !fc.IsBlack)
                textBrush = new SolidColorBrush(Color.FromRgb(fc.R, fc.G, fc.B));

            var text = new FormattedText(
                cell.DisplayText,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface, fontSize, textBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            text.MaxTextWidth = Math.Max(0, colMetric.Width - 4);

            var hAlign = style?.HorizontalAlignment ?? CellHAlign.General;
            bool isNumeric = cell.RawValue is NumberValue or DateTimeValue;

            double textX = hAlign switch
            {
                CellHAlign.Right  => rect.Right - Math.Min(text.Width, colMetric.Width - 2) - 2,
                CellHAlign.Center => rect.Left + (rect.Width - text.Width) / 2,
                CellHAlign.General when isNumeric
                                  => rect.Right - Math.Min(text.Width, colMetric.Width - 2) - 2,
                _                 => rect.Left + 2
            };

            dc.DrawText(text, new Point(textX, rect.Top + (rect.Height - text.Height) / 2));
        }
    }

    private static void DrawBorderEdge(DrawingContext dc, CellBorder border, Point p1, Point p2)
    {
        if (border.Style == BorderStyle.None) return;

        double thickness = border.Style switch
        {
            BorderStyle.Thin   => 0.5,
            BorderStyle.Medium => 1.5,
            BorderStyle.Thick  => 2.5,
            _                  => 0.5
        };

        DashStyle dash = border.Style switch
        {
            BorderStyle.Dashed => DashStyles.Dash,
            BorderStyle.Dotted => DashStyles.Dot,
            _                  => DashStyles.Solid
        };

        var pen = new Pen(
            new SolidColorBrush(Color.FromRgb(border.Color.R, border.Color.G, border.Color.B)),
            thickness) { DashStyle = dash };

        dc.DrawLine(pen, p1, p2);
    }
}

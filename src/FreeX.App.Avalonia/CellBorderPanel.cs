using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// A binding-free panel that overlays cell border lines (four edges + optional diagonals) on top
/// of cell content.  It replaces the old solid-<c>Border</c>-strip approach and adds:
/// <list type="bullet">
///   <item>Correct thickness for every <see cref="BorderStyle"/> (Hair, Thin, Medium-variants, Thick).</item>
///   <item>Dash/dot patterns via <c>StrokeDashArray</c> for dashed, dotted, dash-dot, dash-dot-dot,
///         slant-dash-dot, and their medium-weight variants.</item>
///   <item>Diagonal borders (BorderDiagonalDown TL→BR, BorderDiagonalUp BL→TR), drawn as
///         <see cref="Line"/> children whose endpoints are resolved at arrange time.</item>
/// </list>
///
/// <para>
/// Coordinates follow the same arrange-time pattern as <see cref="ConditionalDataBarPanel"/> and
/// <see cref="SparklineCellPanel"/>: children are rebuilt whenever the panel's final size changes,
/// so they always match the real cell dimensions without data binding.
/// </para>
///
/// <para>
/// Edge lines (Top/Right/Bottom/Left) are placed half-a-stroke-width inset from the panel edge so
/// that the visual line centre sits exactly on the boundary — matching the WPF renderer behaviour
/// where <c>DrawBorderEdge</c> draws along the outer pixel of the cell rect.
/// </para>
/// </summary>
internal sealed class CellBorderPanel : Panel
{
    private readonly CellStyle _style;
    private Size _lastArrange = new(-1, -1);

    public CellBorderPanel(CellStyle style)
    {
        _style = style;
        IsHitTestVisible = false;
        ClipToBounds = false;
    }

    // ── Measure / Arrange ────────────────────────────────────────────────────────────────────────

    protected override Size MeasureOverride(Size availableSize) => new(0, 0);

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (finalSize != _lastArrange)
        {
            _lastArrange = finalSize;
            Children.Clear();
            Build(finalSize);
        }

        foreach (var child in Children)
            child.Arrange(new Rect(finalSize));

        return finalSize;
    }

    // ── Line construction ────────────────────────────────────────────────────────────────────────

    private void Build(Size size)
    {
        double w = size.Width;
        double h = size.Height;

        AddEdge(_style.BorderTop,    new Point(0, 0),    new Point(w, 0),    isHorizontal: true);
        AddEdge(_style.BorderBottom, new Point(0, h),    new Point(w, h),    isHorizontal: true);
        AddEdge(_style.BorderLeft,   new Point(0, 0),    new Point(0, h),    isHorizontal: false);
        AddEdge(_style.BorderRight,  new Point(w, 0),    new Point(w, h),    isHorizontal: false);

        // Diagonal borders — drawn across the cell interior (not edge-aligned).
        // Down = Top-Left → Bottom-Right  (Excel diagonalDown="1")
        // Up   = Bottom-Left → Top-Right  (Excel diagonalUp="1")
        if (_style.BorderDiagonalDown.Style != BorderStyle.None)
            AddDiagonal(_style.BorderDiagonalDown, new Point(0, 0), new Point(w, h));
        if (_style.BorderDiagonalUp.Style != BorderStyle.None)
            AddDiagonal(_style.BorderDiagonalUp,   new Point(0, h), new Point(w, 0));
    }

    private void AddEdge(CellBorder border, Point p1, Point p2, bool isHorizontal)
    {
        if (border.Style == BorderStyle.None)
            return;

        var thickness  = CellBorderGeometry.GetThickness(border.Style);
        var dashArray  = CellBorderGeometry.GetDashArray(border.Style);
        var stroke     = ColorToBrush(border.Color);
        var halfThick  = thickness / 2.0;

        // Inset the line centre by half-thickness so the visible stroke sits exactly on the panel
        // edge (matching WPF, where DrawBorderEdge draws along the outermost pixel of the rect).
        Point start, end;
        if (isHorizontal)
        {
            // Move the Y coordinate inward by half a stroke width so the line is fully within the cell.
            var y = p1.Y == 0 ? halfThick : p1.Y - halfThick;
            start = new Point(p1.X, y);
            end   = new Point(p2.X, y);
        }
        else
        {
            var x = p1.X == 0 ? halfThick : p1.X - halfThick;
            start = new Point(x, p1.Y);
            end   = new Point(x, p2.Y);
        }

        AddLineOrDouble(border.Style, start, end, stroke, thickness, dashArray);
    }

    private void AddDiagonal(CellBorder border, Point p1, Point p2)
    {
        var thickness = CellBorderGeometry.GetThickness(border.Style);
        var dashArray = CellBorderGeometry.GetDashArray(border.Style);
        var stroke    = ColorToBrush(border.Color);
        AddLineOrDouble(border.Style, p1, p2, stroke, thickness, dashArray);
    }

    /// <summary>
    /// Adds a single stroked <see cref="Line"/> for the edge, or — for
    /// <see cref="BorderStyle.Double"/> — two thin parallel lines straddling it, matching Excel's
    /// double-border rendering. Mirrors the WPF <c>DrawBorderEdge</c>/<c>DrawDoubleBorderLines</c>
    /// pair (the WPF twin special-cases <c>BorderStyle.Double</c> the same way instead of drawing
    /// one solid line).
    /// </summary>
    private void AddLineOrDouble(BorderStyle style, Point p1, Point p2, IBrush stroke, double thickness, double[]? dashArray)
    {
        if (style == BorderStyle.Double)
        {
            var (x1, y1, x2, y2, x3, y3, x4, y4) =
                CellBorderGeometry.GetDoubleBorderLineOffsets(p1.X, p1.Y, p2.X, p2.Y);
            Children.Add(MakeLine(new Point(x1, y1), new Point(x2, y2), stroke, thickness, null));
            Children.Add(MakeLine(new Point(x3, y3), new Point(x4, y4), stroke, thickness, null));
            return;
        }

        Children.Add(MakeLine(p1, p2, stroke, thickness, dashArray));
    }

    private static Line MakeLine(Point start, Point end, IBrush stroke, double thickness, double[]? dashArray)
    {
        var line = new Line
        {
            StartPoint      = start,
            EndPoint        = end,
            Stroke          = stroke,
            StrokeThickness = thickness,
            IsHitTestVisible = false,
            StrokeLineCap   = PenLineCap.Flat,
        };

        if (dashArray is not null)
        {
            var avDash = new AvaloniaList<double>(dashArray);
            line.StrokeDashArray = avDash;
        }

        return line;
    }

    // ── Brush helper ─────────────────────────────────────────────────────────────────────────────

    private static IBrush ColorToBrush(CellColor color)
        => new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
}

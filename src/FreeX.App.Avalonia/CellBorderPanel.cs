using System;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using FreeX.App.Presentation.Rendering;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// The four neighboring cells' border edges that touch this cell's own four edges (the neighbor
/// above's <c>BorderBottom</c> touches this cell's <c>BorderTop</c>, and so on). Passed into
/// <see cref="CellBorderPanel"/> so it can resolve which of the two conflicting styles describing
/// a shared grid edge actually gets painted via <see cref="CellBorderVisualPlanner.ResolveEdgeWinner"/>,
/// instead of drawing its own edge unconditionally and leaving a paint-order-dependent double-draw
/// wherever both neighboring cells declare a border on the same physical edge.
/// </summary>
internal readonly record struct CellBorderNeighborEdges(
    CellBorder Above = default,
    CellBorder Below = default,
    CellBorder Left = default,
    CellBorder Right = default);

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
    private readonly CellBorderNeighborEdges _neighbors;
    private readonly double _zoomFactor;
    private Size _lastArrange = new(-1, -1);
    private TopLevel? _subscribedTopLevel;

    public CellBorderPanel(CellStyle style, CellBorderNeighborEdges neighbors = default, double zoomFactor = 1.0)
    {
        _style = style;
        _neighbors = neighbors;
        _zoomFactor = double.IsFinite(zoomFactor) && zoomFactor > 0 ? zoomFactor : 1.0;
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

    // ── DPI-change invalidation ──────────────────────────────────────────────────────────────────
    //
    // Build()/AddEdge() bake the *current* RenderScaling into pixel-snapped stroke thickness and
    // position (see GetRenderScaling()/GetDisplayThickness() below), but ArrangeOverride only
    // rebuilds when the panel's own finalSize changes. Dragging the host window to a differently-
    // scaled monitor changes RenderScaling without necessarily changing this panel's arranged size,
    // so without this subscription the stale, previously-snapped geometry would be left in place
    // (unlike WPF, which re-invokes OnRender — and therefore re-queries the DPI — automatically on
    // a per-monitor DPI change). Subscribing to the hosting TopLevel's ScalingChanged and forcing a
    // fresh arrange pass restores that parity.

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _subscribedTopLevel = TopLevel.GetTopLevel(this);
        if (_subscribedTopLevel is not null)
            _subscribedTopLevel.ScalingChanged += OnHostScalingChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_subscribedTopLevel is not null)
        {
            _subscribedTopLevel.ScalingChanged -= OnHostScalingChanged;
            _subscribedTopLevel = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void OnHostScalingChanged(object? sender, EventArgs e)
    {
        // Reset the arrange-gate sentinel so the next arrange pass rebuilds the border lines even
        // though finalSize itself hasn't changed, then force that pass to happen.
        _lastArrange = new Size(-1, -1);
        InvalidateArrange();
    }

    // ── Line construction ────────────────────────────────────────────────────────────────────────

    private void Build(Size size)
    {
        double w = size.Width;
        double h = size.Height;

        // Resolve each edge against the touching neighbor's border (if any) before drawing, so a
        // shared grid edge described by both adjoining cells always renders with the heavier/more-
        // prominent style regardless of which cell's panel happens to be built first — mirrors the
        // WPF GridView.Rendering.cs BorderEdgePrecedence pass. A cell with no neighbor border (the
        // default, all-None CellBorderNeighborEdges) always yields its own style unchanged.
        var top    = CellBorderVisualPlanner.ResolveEdgeWinner(_style.BorderTop,    _neighbors.Above);
        var bottom = CellBorderVisualPlanner.ResolveEdgeWinner(_style.BorderBottom, _neighbors.Below);
        var left   = CellBorderVisualPlanner.ResolveEdgeWinner(_style.BorderLeft,   _neighbors.Left);
        var right  = CellBorderVisualPlanner.ResolveEdgeWinner(_style.BorderRight,  _neighbors.Right);

        AddEdge(top,    new Point(0, 0),    new Point(w, 0),    isHorizontal: true);
        AddEdge(bottom, new Point(0, h),    new Point(w, h),    isHorizontal: true);
        AddEdge(left,   new Point(0, 0),    new Point(0, h),    isHorizontal: false);
        AddEdge(right,  new Point(w, 0),    new Point(w, h),    isHorizontal: false);

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

        var strokePlan = CellBorderVisualPlanner.Plan(border.Style);
        var renderScaling = GetRenderScaling();
        var thickness  = GetDisplayThickness(strokePlan, renderScaling);
        var stroke     = ColorToBrush(border.Color);
        var halfThick  = thickness / 2.0;

        // Inset the line centre by half-thickness so the visible stroke sits exactly on the panel
        // edge (matching WPF, where DrawBorderEdge draws along the outermost pixel of the rect).
        Point start, end;
        if (isHorizontal)
        {
            // Move the Y coordinate inward by half a stroke width so the line is fully within the cell.
            var y = p1.Y == 0 ? halfThick : p1.Y - halfThick;
            y = BorderStrokePixelSnapper.SnapCenter(y, thickness, renderScaling);
            start = new Point(p1.X, y);
            end   = new Point(p2.X, y);
        }
        else
        {
            var x = p1.X == 0 ? halfThick : p1.X - halfThick;
            x = BorderStrokePixelSnapper.SnapCenter(x, thickness, renderScaling);
            start = new Point(x, p1.Y);
            end   = new Point(x, p2.Y);
        }

        AddLineOrDouble(strokePlan, start, end, stroke, thickness, renderScaling);
    }

    private void AddDiagonal(CellBorder border, Point p1, Point p2)
    {
        var strokePlan = CellBorderVisualPlanner.Plan(border.Style);
        var renderScaling = GetRenderScaling();
        var thickness = GetDisplayThickness(strokePlan, renderScaling);
        var stroke    = ColorToBrush(border.Color);
        AddLineOrDouble(strokePlan, p1, p2, stroke, thickness, renderScaling);
    }

    /// <summary>
    /// Adds a single stroked <see cref="Line"/> for the edge, or — for
    /// <see cref="BorderStyle.Double"/> — two thin parallel lines straddling it, matching Excel's
    /// double-border rendering. Mirrors the WPF <c>DrawBorderEdge</c>/<c>DrawDoubleBorderLines</c>
    /// pair (the WPF twin special-cases <c>BorderStyle.Double</c> the same way instead of drawing
    /// one solid line).
    /// </summary>
    private void AddLineOrDouble(
        CellBorderStrokePlan strokePlan,
        Point p1,
        Point p2,
        IBrush stroke,
        double thickness,
        double renderScaling)
    {
        if (strokePlan.IsDouble)
        {
            var doubleEdge = CellBorderVisualPlanner.PlanDoubleEdge(
                p1.X,
                p1.Y,
                p2.X,
                p2.Y,
                thickness,
                renderScaling);
            Children.Add(MakeLine(doubleEdge.First, stroke, thickness, null));
            if (doubleEdge.HasSecond)
                Children.Add(MakeLine(doubleEdge.Second, stroke, thickness, null));
            return;
        }

        Children.Add(MakeLine(p1, p2, stroke, thickness, strokePlan.DashArray));
    }

    private double GetDisplayThickness(CellBorderStrokePlan strokePlan, double renderScaling)
    {
        var displayThickness = strokePlan.Thickness * _zoomFactor;
        return BorderStrokePixelSnapper.SnapThickness(displayThickness, renderScaling);
    }

    private double GetRenderScaling() =>
        BorderStrokePixelSnapper.NormalizePixelsPerDip(TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0);

    private static Line MakeLine(
        CellBorderLinePrimitive line,
        IBrush stroke,
        double thickness,
        IReadOnlyList<double>? dashArray) =>
        MakeLine(new Point(line.X1, line.Y1), new Point(line.X2, line.Y2), stroke, thickness, dashArray);

    private static Line MakeLine(
        Point start,
        Point end,
        IBrush stroke,
        double thickness,
        IReadOnlyList<double>? dashArray)
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

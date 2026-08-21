using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.SparklineUI;
using FreeX.App.Presentation.Sparklines;
using FreeX.Core.Model;

using AvaloniaEllipse = Avalonia.Controls.Shapes.Ellipse;
using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaLine = Avalonia.Controls.Shapes.Line;
using AvaloniaPoint = Avalonia.Point;
using AvaloniaRectangle = Avalonia.Controls.Shapes.Rectangle;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// A binding-free in-cell layer that paints a sparkline (line / column / win-loss) inside the cell,
/// behind the cell text. The geometry comes from the portable <see cref="SparklineLayoutEngine"/>,
/// resolved against the panel's measured size at arrange time so the sparkline tracks the cell's
/// actual rendered box without data binding — the same arrange-time pattern as
/// <see cref="ConditionalDataBarPanel"/>.
///
/// Full WPF-parity rendering:
/// <list type="bullet">
///   <item>Line weight from <see cref="SparklineModel.LineWeight"/> (pt → DIP conversion).</item>
///   <item>Series, negative, axis, markers, high/low/first/last colors from the model.</item>
///   <item>Markers (all-point, high, low, first, last, negative) as filled ellipses.</item>
///   <item>Horizontal axis line when <see cref="SparklineModel.ShowAxis"/> is set.</item>
///   <item>Group and custom axis-bound scaling (Individual / Group / Custom).</item>
/// </list>
/// </summary>
internal sealed class SparklineCellPanel : Panel
{
    // ── Default colors (match Excel's built-in sparkline defaults) ─────────────
    private static readonly CellColor DefaultPositiveColor  = new(33,  115, 70);
    private static readonly CellColor DefaultNegativeColor  = new(192,   0,  0);
    private static readonly CellColor DefaultAxisColor      = new(0,     0,  0);
    private static readonly CellColor DefaultMarkersColor   = new(33,  115, 70);
    private static readonly CellColor DefaultHighColor      = new(216,   0,  0);
    private static readonly CellColor DefaultLowColor       = new(216,   0,  0);
    private static readonly CellColor DefaultFirstColor     = new(33,  115, 70);
    private static readonly CellColor DefaultLastColor      = new(33,  115, 70);

    // Excel default line weight: 0.75 pt at 96 dpi → 1.0 DIP
    private const double DefaultLineWeightPt = 0.75;

    // Marker radius in DIPs — 2.0 px matches Excel's dot size (WPF uses r=2.0 for DrawEllipse).
    private const double MarkerRadius = 2.0;

    // Axis line thickness (WPF uses 0.75 DIP)
    private const double AxisLineThickness = 0.75;

    private const double Inset = SparklineRenderPlanner.CellInset;

    private readonly IReadOnlyList<double> _values;
    private readonly SparklineModel _sparkline;
    private readonly double? _overrideMin;
    private readonly double? _overrideMax;
    private readonly double? _overrideMaxAbs;

    private Size _lastArrange = new(-1, -1);

    /// <param name="values">The numeric series for this sparkline cell.</param>
    /// <param name="sparkline">The full model (colors, markers, axis, scaling flags).</param>
    /// <param name="overrideMin">Group/custom min override for line sparklines; null = individual.</param>
    /// <param name="overrideMax">Group/custom max override for line sparklines; null = individual.</param>
    /// <param name="overrideMaxAbs">Group/custom maxAbs override for column sparklines; null = individual.</param>
    public SparklineCellPanel(
        IReadOnlyList<double> values,
        SparklineModel sparkline,
        double? overrideMin = null,
        double? overrideMax = null,
        double? overrideMaxAbs = null)
    {
        _values = values;
        _sparkline = sparkline;
        _overrideMin = overrideMin;
        _overrideMax = overrideMax;
        _overrideMaxAbs = overrideMaxAbs;
        IsHitTestVisible = false;
        ClipToBounds = true;
    }

    // ── Measure / Arrange ──────────────────────────────────────────────────────

    // Returns (0,0) so the panel does not influence the parent grid's layout
    // (cell size is set by the grid's column/row definitions, not by the sparkline panel).
    // Avalonia will still call ArrangeOverride with the parent's final size.
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

    // ── Build ──────────────────────────────────────────────────────────────────

    private void Build(Size size)
    {
        // Guard: skip degenerate sizes (can happen in headless if arrange is not propagated).
        if (size.Width <= 0 || size.Height <= 0)
            return;

        var rect = new LayoutRect(
            Inset,
            Inset,
            Math.Max(1, size.Width - (Inset * 2)),
            Math.Max(1, size.Height - (Inset * 2)));
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        // Resolve colors with Excel defaults.
        var seriesColor  = _sparkline.SeriesColor  ?? DefaultPositiveColor;
        // Column/win-loss negative bars only use the negative color when "Negative Points" is
        // enabled; otherwise Excel paints them in the series color like any other bar.
        var negativeColor = _sparkline.ShowNegativePoints
            ? _sparkline.NegativeColor ?? DefaultNegativeColor
            : seriesColor;
        var axisColor    = _sparkline.AxisColor     ?? DefaultAxisColor;

        // Draw axis line first (behind the sparkline).
        if (_sparkline.ShowAxis)
            BuildAxisLine(rect, axisColor);

        if (_sparkline.Kind == SparklineKind.Line)
            BuildLine(rect, seriesColor);
        else
            BuildColumns(rect, seriesColor, negativeColor);
    }

    // ── Axis line ──────────────────────────────────────────────────────────────

    private void BuildAxisLine(LayoutRect rect, CellColor axisColor)
    {
        var y = SparklineAxisLinePlanner.ResolveY(
            _sparkline.Kind,
            _values,
            rect,
            _overrideMin,
            _overrideMax);
        if (y is not { } axisY)
            return;

        Children.Add(new AvaloniaLine
        {
            StartPoint = new AvaloniaPoint(rect.Left, axisY),
            EndPoint   = new AvaloniaPoint(rect.Right, axisY),
            Stroke = BrushForColor(axisColor),
            StrokeThickness = AxisLineThickness,
            IsHitTestVisible = false,
        });
    }

    // ── Line sparkline ─────────────────────────────────────────────────────────

    private void BuildLine(LayoutRect rect, CellColor seriesColor)
    {
        var lineWeightDip = PointsToDip(_sparkline.LineWeight ?? DefaultLineWeightPt);
        var stroke = BrushForColor(seriesColor);

        // R91-meta-2: use the SparklineModel-deriving overload so the group's "Plot Data
        // Right-to-Left" option is always honored — the RTL-less overload is easy to silently
        // omit (see round 90's WPF-only fix that missed this shell's separate renderer).
        var layout = SparklineLayoutEngine.CalculateLineLayout(_sparkline, _values, rect, _overrideMin, _overrideMax);

        if (layout.SinglePoint is { } single)
        {
            // Single-point: draw a small ellipse (matches WPF AcceptSinglePoint: r=1.5 ellipse)
            Children.Add(new AvaloniaEllipse
            {
                Width  = 3,
                Height = 3,
                Fill   = stroke,
                Margin = new Thickness(single.X - 1.5, single.Y - 1.5, 0, 0),
                HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
                VerticalAlignment   = AvaloniaVerticalAlignment.Top,
                IsHitTestVisible    = false,
            });
            return;
        }

        foreach (var segment in layout.Segments)
        {
            Children.Add(new AvaloniaLine
            {
                StartPoint      = new AvaloniaPoint(segment.Start.X, segment.Start.Y),
                EndPoint        = new AvaloniaPoint(segment.End.X, segment.End.Y),
                Stroke          = stroke,
                StrokeThickness = lineWeightDip,
                IsHitTestVisible = false,
            });
        }

        // Draw markers over the line (line sparklines only).
        if (_sparkline.ShowMarkers    || _sparkline.ShowHighPoint   || _sparkline.ShowLowPoint  ||
            _sparkline.ShowFirstPoint || _sparkline.ShowLastPoint   || _sparkline.ShowNegativePoints)
        {
            BuildLineMarkers(rect);
        }
    }

    private void BuildLineMarkers(LayoutRect rect)
    {
        if (_values.Count == 0)
            return;

        // R91-meta-2: model-deriving overload — see BuildLine's CalculateLineLayout call above.
        var points = SparklineLayoutEngine.GetLinePoints(_sparkline, _values, rect, _overrideMin, _overrideMax);
        if (points.Count == 0)
            return;

        // Identify special roles across the finite values.
        var minVal = double.MaxValue;
        var maxVal = double.MinValue;
        var firstFiniteIndex = -1;
        var lastFiniteIndex  = -1;

        for (var i = 0; i < _values.Count; i++)
        {
            if (!double.IsFinite(_values[i])) continue;
            if (firstFiniteIndex < 0) firstFiniteIndex = i;
            lastFiniteIndex = i;
            if (_values[i] < minVal) minVal = _values[i];
            if (_values[i] > maxVal) maxVal = _values[i];
        }

        var markersColor = _sparkline.MarkersColor   ?? DefaultMarkersColor;
        var highColor    = _sparkline.HighPointColor  ?? DefaultHighColor;
        var lowColor     = _sparkline.LowPointColor   ?? DefaultLowColor;
        var firstColor   = _sparkline.FirstPointColor ?? DefaultFirstColor;
        var lastColor    = _sparkline.LastPointColor  ?? DefaultLastColor;
        var negColor     = _sparkline.NegativeColor   ?? DefaultNegativeColor;

        foreach (var (index, pt) in points)
        {
            // Determine the highest-priority role for this point.
            // Priority (later assignment wins, drawn last = on top):
            //   base markers → negative → first/last → low/high
            CellColor? markerColor = null;

            if (_sparkline.ShowMarkers)
                markerColor = markersColor;

            if (_sparkline.ShowNegativePoints && double.IsFinite(_values[index]) && _values[index] < 0)
                markerColor = negColor;

            if (_sparkline.ShowFirstPoint && index == firstFiniteIndex)
                markerColor = firstColor;

            if (_sparkline.ShowLastPoint && index == lastFiniteIndex)
                markerColor = lastColor;

            if (_sparkline.ShowLowPoint && double.IsFinite(_values[index]) &&
                Math.Abs(_values[index] - minVal) < 1e-10)
                markerColor = lowColor;

            if (_sparkline.ShowHighPoint && double.IsFinite(_values[index]) &&
                Math.Abs(_values[index] - maxVal) < 1e-10)
                markerColor = highColor;

            if (markerColor.HasValue)
            {
                // Draw as a filled ellipse centered on pt.
                Children.Add(new AvaloniaEllipse
                {
                    Width  = MarkerRadius * 2,
                    Height = MarkerRadius * 2,
                    Fill   = BrushForColor(markerColor.Value),
                    Margin = new Thickness(pt.X - MarkerRadius, pt.Y - MarkerRadius, 0, 0),
                    HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
                    VerticalAlignment   = AvaloniaVerticalAlignment.Top,
                    IsHitTestVisible    = false,
                });
            }
        }
    }

    // ── Column / win-loss sparkline ────────────────────────────────────────────

    private void BuildColumns(LayoutRect rect, CellColor seriesColor, CellColor negativeColor)
    {
        var colors = SparklineColumnColorPlanner.ResolveBarColors(
            _sparkline,
            _values,
            seriesColor,
            negativeColor,
            _sparkline.HighPointColor ?? DefaultHighColor,
            _sparkline.LowPointColor ?? DefaultLowColor,
            _sparkline.FirstPointColor ?? DefaultFirstColor,
            _sparkline.LastPointColor ?? DefaultLastColor);

        // R91-meta-2: model-deriving overload — derives both Kind (winLoss) and RightToLeft from
        // the sparkline itself; see BuildLine's CalculateLineLayout call above for the rationale.
        var layout = SparklineLayoutEngine.CalculateColumnLayout(_sparkline, _values, rect, _overrideMaxAbs);
        for (var index = 0; index < layout.Bars.Count; index++)
        {
            var bar = layout.Bars[index];
            Children.Add(new AvaloniaRectangle
            {
                Width  = Math.Max(1, bar.Rect.Width),
                Height = Math.Max(1, bar.Rect.Height),
                Fill   = BrushForColor(colors[index]),
                Margin = new Thickness(bar.Rect.X, bar.Rect.Y, 0, 0),
                HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
                VerticalAlignment   = AvaloniaVerticalAlignment.Top,
                IsHitTestVisible    = false,
            });
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static IBrush BrushForColor(CellColor c) =>
        new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));

    /// <summary>Converts a point size to Avalonia DIPs (96 dpi / 72 pt per inch).</summary>
    private static double PointsToDip(double pts) => pts * 96.0 / 72.0;
}

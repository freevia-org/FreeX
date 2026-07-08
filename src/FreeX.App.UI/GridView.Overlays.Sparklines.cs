using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

using FreeX.Core.Model;

namespace FreeX.App.UI;

public partial class GridView
{
    // ── Default colors (match Excel's built-in sparkline defaults) ────────────
    private static readonly CellColor DefaultPositiveCellColor  = new(33,  115, 70);
    private static readonly CellColor DefaultNegativeCellColor  = new(192,   0,  0);
    private static readonly CellColor DefaultAxisCellColor      = new(0,     0,  0);
    private static readonly CellColor DefaultMarkersCellColor   = new(33,  115, 70);
    private static readonly CellColor DefaultHighCellColor      = new(216,   0,  0);
    private static readonly CellColor DefaultLowCellColor       = new(216,   0,  0);
    private static readonly CellColor DefaultFirstCellColor     = new(33,  115, 70);
    private static readonly CellColor DefaultLastCellColor      = new(33,  115, 70);

    // Fallback line weight when SparklineModel.LineWeight is null.
    // Excel default is 0.75 pt at 96 dpi: 0.75 * 96 / 72 = 1.0 px (DIPs).
    private const double DefaultLineWeightPt = 0.75;

    // Pen cache keyed by (CellColor, thickness) for sparkline drawing.
    // The existing _brushCache (keyed by CellColor) is reused for fill brushes.
    private readonly Dictionary<(CellColor Color, double Thickness), Pen> _sparklinePenCache = new();

    private Pen GetSparklinePen(CellColor color, double thicknessDip)
    {
        var key = (color, thicknessDip);
        if (!_sparklinePenCache.TryGetValue(key, out var pen))
        {
            pen = new Pen(BrushForCellColor(color, _brushCache), thicknessDip);
            pen.Freeze();
            _sparklinePenCache[key] = pen;
        }

        return pen;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Converts a point size to WPF DIPs (96 dpi / 72 pt per inch).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double PointsToDip(double pts) => pts * 96.0 / 72.0;

    private static SolidColorBrush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }

    // ── Axis-bound resolution ─────────────────────────────────────────────────

    private static double? ResolveAxisMin(SparklineModel sp, Dictionary<int, double> groupMin)
    {
        return sp.MinAxisType switch
        {
            SparklineAxisScaling.Custom => sp.ManualMin,
            SparklineAxisScaling.Group  =>
                groupMin.TryGetValue(sp.GroupId, out var v) && v != double.MaxValue ? v : null,
            _ => null, // Individual: no override
        };
    }

    private static double? ResolveAxisMax(SparklineModel sp, Dictionary<int, double> groupMax)
    {
        return sp.MaxAxisType switch
        {
            SparklineAxisScaling.Custom => sp.ManualMax,
            SparklineAxisScaling.Group  =>
                groupMax.TryGetValue(sp.GroupId, out var v) && v != double.MinValue ? v : null,
            _ => null,
        };
    }

    private static double? ResolveAxisMaxAbs(SparklineModel sp, Dictionary<int, double> groupMaxAbs)
    {
        // For column/win-loss sparklines the bars are scaled symmetrically around zero using a
        // single maxAbs bound.  Resolution priority (per axis):
        //   Custom ManualMax / ManualMin  →  use the explicit bound (abs value for min)
        //   Group                         →  fall back to the shared group maxAbs
        //   Individual                    →  no override (return null)
        //
        // When one axis is Custom and the other is Group, the Custom value wins for its axis and
        // the group value for the other; we take the larger abs so neither axis is clipped.
        double? customAbs = null;

        if (sp.MaxAxisType == SparklineAxisScaling.Custom && sp.ManualMax.HasValue)
            customAbs = Math.Abs(sp.ManualMax.Value);

        if (sp.MinAxisType == SparklineAxisScaling.Custom && sp.ManualMin.HasValue)
        {
            var absMin = Math.Abs(sp.ManualMin.Value);
            customAbs = customAbs.HasValue ? Math.Max(customAbs.Value, absMin) : absMin;
        }

        // If either axis defers to the group, also fetch the group maxAbs.
        double? groupAbs = null;
        if (sp.MaxAxisType == SparklineAxisScaling.Group || sp.MinAxisType == SparklineAxisScaling.Group)
            groupAbs = groupMaxAbs.TryGetValue(sp.GroupId, out var v) ? v : null;

        // Return the larger of any custom and group contributions; null if neither applies.
        if (customAbs.HasValue && groupAbs.HasValue)
            return Math.Max(customAbs.Value, groupAbs.Value);
        return customAbs ?? groupAbs;
    }

    // ── Axis line ─────────────────────────────────────────────────────────────

    private void DrawSparklineAxisLine(DrawingContext dc, Rect rect, CellColor axisColor)
    {
        var y = rect.Top + (rect.Height / 2);
        var pen = GetSparklinePen(axisColor, 0.75);
        dc.DrawLine(pen, new Point(rect.Left, y), new Point(rect.Right, y));
    }

    // ── Line sparkline ────────────────────────────────────────────────────────

    private void DrawLineSparkline(
        DrawingContext dc,
        SparklineModel sparkline,
        IReadOnlyList<double> values,
        Rect rect,
        Pen linePen,
        double? overrideMin,
        double? overrideMax)
    {
        var consumer = new LineSparklineDrawingConsumer(dc, linePen);
        SparklineLayoutPlanner.VisitLineLayout(values, rect, ref consumer, overrideMin, overrideMax);

        // Draw markers (line sparklines only).
        if (sparkline.ShowMarkers    || sparkline.ShowHighPoint   || sparkline.ShowLowPoint  ||
            sparkline.ShowFirstPoint || sparkline.ShowLastPoint   || sparkline.ShowNegativePoints)
        {
            DrawLineMarkers(dc, sparkline, values, rect, overrideMin, overrideMax);
        }
    }

    private void DrawLineMarkers(
        DrawingContext dc,
        SparklineModel sparkline,
        IReadOnlyList<double> values,
        Rect rect,
        double? overrideMin,
        double? overrideMax)
    {
        if (values.Count == 0)
            return;

        var points = SparklineLayoutPlanner.GetLinePoints(values, rect, overrideMin, overrideMax);
        if (points.Count == 0)
            return;

        // Identify special roles.
        var minVal = double.MaxValue;
        var maxVal = double.MinValue;
        var firstFiniteIndex = -1;
        var lastFiniteIndex  = -1;

        for (var i = 0; i < values.Count; i++)
        {
            if (!double.IsFinite(values[i])) continue;
            if (firstFiniteIndex < 0) firstFiniteIndex = i;
            lastFiniteIndex = i;
            if (values[i] < minVal) minVal = values[i];
            if (values[i] > maxVal) maxVal = values[i];
        }

        var markersColor  = sparkline.MarkersColor   ?? DefaultMarkersCellColor;
        var highColor     = sparkline.HighPointColor  ?? DefaultHighCellColor;
        var lowColor      = sparkline.LowPointColor   ?? DefaultLowCellColor;
        var firstColor    = sparkline.FirstPointColor ?? DefaultFirstCellColor;
        var lastColor     = sparkline.LastPointColor  ?? DefaultLastCellColor;
        var negColor      = sparkline.NegativeColor   ?? DefaultNegativeCellColor;

        // Marker radius in DIPs — 2.0 px matches Excel's dot size.
        const double r = 2.0;

        foreach (var (index, pt) in points)
        {
            // Determine the highest-priority role for this point.
            // Priority (later assignment wins, drawn last = on top):
            //   base markers → negative → first/last → low/high
            CellColor? markerColor = null;

            if (sparkline.ShowMarkers)
                markerColor = markersColor;

            if (sparkline.ShowNegativePoints && double.IsFinite(values[index]) && values[index] < 0)
                markerColor = negColor;

            if (sparkline.ShowFirstPoint && index == firstFiniteIndex)
                markerColor = firstColor;

            if (sparkline.ShowLastPoint && index == lastFiniteIndex)
                markerColor = lastColor;

            if (sparkline.ShowLowPoint && double.IsFinite(values[index]) &&
                Math.Abs(values[index] - minVal) < 1e-10)
                markerColor = lowColor;

            if (sparkline.ShowHighPoint && double.IsFinite(values[index]) &&
                Math.Abs(values[index] - maxVal) < 1e-10)
                markerColor = highColor;

            if (markerColor.HasValue)
                dc.DrawEllipse(BrushForCellColor(markerColor.Value, _brushCache), null, pt, r, r);
        }
    }

    // ── Column sparkline ──────────────────────────────────────────────────────

    private static void DrawColumnSparkline(
        DrawingContext dc,
        IReadOnlyList<double> values,
        Rect rect,
        bool winLoss,
        Brush positiveFill,
        Brush negativeFill,
        double? overrideMaxAbs)
    {
        var consumer = new ColumnSparklineDrawingConsumer(dc, positiveFill, negativeFill);
        SparklineLayoutPlanner.VisitColumnLayout(values, rect, winLoss, ref consumer, overrideMaxAbs);
    }

    // ── Consumers (zero-allocation streaming) ─────────────────────────────────

    private readonly struct LineSparklineDrawingConsumer(DrawingContext dc, Pen pen) : ISparklineLineLayoutConsumer
    {
        public void AcceptSinglePoint(Point point) =>
            dc.DrawEllipse(pen.Brush, null, point, 1.5, 1.5);

        public void AcceptSegment(Point start, Point end) =>
            dc.DrawLine(pen, start, end);
    }

    private readonly struct ColumnSparklineDrawingConsumer(
        DrawingContext dc,
        Brush positiveFill,
        Brush negativeFill) : ISparklineColumnLayoutConsumer
    {
        public void AcceptBar(Rect rect, bool isNegative) =>
            dc.DrawRectangle(isNegative ? negativeFill : positiveFill, null, rect);
    }

    // ── Entry point ───────────────────────────────────────────────────────────
    private void RenderSparklines(DrawingContext dc)
    {
        if (Sparklines is not { Count: > 0 } ||
            SparklineValues is not { Count: > 0 } ||
            Viewport == null)
        {
            return;
        }

        var lookups = GetRenderCellLookups(Viewport);
        var rowLookup = lookups.Rows;
        var colLookup = lookups.Columns;
        var visibleLeft = ActualRowHeaderWidth;
        var visibleTop = EffectiveColHeaderHeight;
        var visibleRight = ActualWidth;
        var visibleBottom = ActualHeight;

        // ── Pre-compute group scaling bounds ──────────────────────────────────
        // Group scaling: when MinAxisType or MaxAxisType == Group, find the shared
        // min/max across all sparklines that share the same GroupId.
        var groupMinValues = new Dictionary<int, double>();    // groupId → shared min
        var groupMaxValues = new Dictionary<int, double>();    // groupId → shared max
        var groupMaxAbsValues = new Dictionary<int, double>(); // groupId → shared maxAbs (column)

        foreach (var sp in Sparklines)
        {
            if ((sp.MinAxisType == SparklineAxisScaling.Group ||
                 sp.MaxAxisType == SparklineAxisScaling.Group) &&
                SparklineValues.TryGetValue(sp.Id, out var vals) && vals.Count > 0)
            {
                if (!groupMinValues.ContainsKey(sp.GroupId))
                {
                    groupMinValues[sp.GroupId] = double.MaxValue;
                    groupMaxValues[sp.GroupId] = double.MinValue;
                    groupMaxAbsValues[sp.GroupId] = 0;
                }

                foreach (var v in vals)
                {
                    if (!double.IsFinite(v)) continue;
                    if (v < groupMinValues[sp.GroupId]) groupMinValues[sp.GroupId] = v;
                    if (v > groupMaxValues[sp.GroupId]) groupMaxValues[sp.GroupId] = v;
                    var abs = Math.Abs(v);
                    if (abs > groupMaxAbsValues[sp.GroupId]) groupMaxAbsValues[sp.GroupId] = abs;
                }
            }
        }

        // ── Per-sparkline rendering ───────────────────────────────────────────
        foreach (var sparkline in Sparklines)
        {
            if (!rowLookup.TryGetValue(sparkline.Location.Row, out var row) ||
                !colLookup.TryGetValue(sparkline.Location.Col, out var col) ||
                !SparklineValues.TryGetValue(sparkline.Id, out var values) ||
                values.Count == 0)
            {
                continue;
            }

            var rect = new Rect(
                col.LeftOffset + ActualRowHeaderWidth + 3,
                row.TopOffset + EffectiveColHeaderHeight + 3,
                Math.Max(1, col.Width - 6),
                Math.Max(1, row.Height - 6));

            if (!IntersectsVisibleGrid(rect, visibleLeft, visibleTop, visibleRight, visibleBottom))
                continue;

            // Resolve axis-bound overrides for this sparkline.
            double? overrideMin = ResolveAxisMin(sparkline, groupMinValues);
            double? overrideMax = ResolveAxisMax(sparkline, groupMaxValues);
            double? overrideMaxAbs = ResolveAxisMaxAbs(sparkline, groupMaxAbsValues);

            // Resolve colors.
            var seriesColor = sparkline.SeriesColor ?? DefaultPositiveCellColor;
            // Column/win-loss negative bars only use the negative color when "Negative Points" is
            // enabled; otherwise Excel paints them in the series color like any other bar.
            var negativeColor = sparkline.ShowNegativePoints
                ? sparkline.NegativeColor ?? DefaultNegativeCellColor
                : seriesColor;
            var axisColor = sparkline.AxisColor ?? DefaultAxisCellColor;

            dc.PushClip(GetCellClipGeometry(rect));

            // Draw axis line first (behind the sparkline).
            if (sparkline.ShowAxis)
                DrawSparklineAxisLine(dc, rect, axisColor);

            if (sparkline.Kind == SparklineKind.Line)
            {
                // Line weight: model LineWeight is in points; convert to DIPs (96 dpi / 72 pt).
                var lineWeightDip = PointsToDip(sparkline.LineWeight ?? DefaultLineWeightPt);
                var linePen = GetSparklinePen(seriesColor, lineWeightDip);
                DrawLineSparkline(dc, sparkline, values, rect, linePen, overrideMin, overrideMax);
            }
            else
            {
                DrawColumnSparkline(
                    dc, values, rect,
                    sparkline.Kind == SparklineKind.WinLoss,
                    BrushForCellColor(seriesColor,   _brushCache),
                    BrushForCellColor(negativeColor, _brushCache),
                    overrideMaxAbs);
            }

            dc.Pop();
        }
    }
}

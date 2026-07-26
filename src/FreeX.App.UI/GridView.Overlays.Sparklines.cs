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

    // Static (rather than instance) and cache-parameterized so the print/PDF path
    // (DrawSparklineIntoCell below, called cross-assembly by PrintRenderer.GridCells.cs) can share
    // this exact pen-building logic without an instance -- pass null for a one-shot, uncached pen
    // (R88-render-sparkline-5-1). The interactive grid's own RenderSparklines below still passes its
    // instance caches (_sparklinePenCache/_brushCache), so its behavior is unchanged.
    private static Pen GetSparklinePen(
        CellColor color,
        double thicknessDip,
        Dictionary<(CellColor Color, double Thickness), Pen>? penCache,
        Dictionary<CellColor, SolidColorBrush>? brushCache)
    {
        if (penCache is not null)
        {
            var key = (color, thicknessDip);
            if (!penCache.TryGetValue(key, out var cachedPen))
            {
                cachedPen = new Pen(BrushForCellColor(color, brushCache), thicknessDip);
                cachedPen.Freeze();
                penCache[key] = cachedPen;
            }

            return cachedPen;
        }

        var pen = new Pen(BrushForCellColor(color, brushCache), thicknessDip);
        pen.Freeze();
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

    /// <summary>
    /// Draws the sparkline "Show Axis" horizontal line at the actual zero-value position implied by
    /// the data/scale, matching Excel: for column/win-loss sparklines this is the same zero baseline
    /// <see cref="SparklineLayoutPlanner.CalculateColumnLayout(IReadOnlyList{double}, Rect, bool)"/>'s
    /// underlying engine bars from (rect.Bottom for all-positive data, rect.Top for all-negative, the
    /// midline for mixed-sign or win/loss); for line sparklines it is the pixel position of value 0
    /// within the plotted min/max range, and is only drawn when that range actually spans (or
    /// touches) zero -- otherwise real zero sits outside the visible plot and no line is drawn.
    /// </summary>
    private static void DrawSparklineAxisLine(
        DrawingContext dc,
        IReadOnlyList<double> values,
        Rect rect,
        SparklineKind kind,
        CellColor axisColor,
        double? overrideMin,
        double? overrideMax,
        Dictionary<(CellColor Color, double Thickness), Pen>? penCache,
        Dictionary<CellColor, SolidColorBrush>? brushCache)
    {
        var y = kind == SparklineKind.Line
            ? ResolveLineAxisY(values, rect, overrideMin, overrideMax)
            : ResolveColumnAxisY(values, rect, kind == SparklineKind.WinLoss);

        if (y is not { } axisY)
            return;

        var pen = GetSparklinePen(axisColor, 0.75, penCache, brushCache);
        dc.DrawLine(pen, new Point(rect.Left, axisY), new Point(rect.Right, axisY));
    }

    /// <summary>
    /// Zero-crossing Y for a line sparkline, or null when the plotted min/max range does not include
    /// zero (real zero would sit outside the visible plot, so Excel does not draw the axis line).
    /// </summary>
    private static double? ResolveLineAxisY(IReadOnlyList<double> values, Rect rect, double? overrideMin, double? overrideMax)
    {
        double? min = null;
        double? max = null;
        foreach (var value in values)
        {
            if (!double.IsFinite(value))
                continue;
            if (min is null || value < min)
                min = value;
            if (max is null || value > max)
                max = value;
        }

        if (min is null || max is null)
            return null;

        var lo = overrideMin ?? min.Value;
        var hi = overrideMax ?? max.Value;
        if (lo > hi)
            (lo, hi) = (hi, lo);

        // Real zero falls outside the plotted range -- no axis line to show.
        if (0 < lo || 0 > hi)
            return null;

        var span = Math.Abs(hi - lo) < 0.0000001 ? 1 : hi - lo;
        return rect.Bottom - (Math.Clamp((0 - lo) / span, 0, 1) * rect.Height);
    }

    /// <summary>
    /// Zero-baseline Y for a column/win-loss sparkline: the cell bottom when the data is entirely
    /// positive, the cell top when entirely negative, and the vertical midline for mixed-sign data or
    /// win/loss (which is always keyed on sign alone) -- mirroring
    /// <see cref="FreeX.App.Presentation.Sparklines.SparklineLayoutEngine.VisitColumnLayout{TConsumer}(IReadOnlyList{double}, FreeX.App.Presentation.Sparklines.LayoutRect, bool, ref TConsumer, double?)"/>'s
    /// own baseline computation for the bars themselves.
    /// </summary>
    private static double ResolveColumnAxisY(IReadOnlyList<double> values, Rect rect, bool winLoss)
    {
        var hasPositive = false;
        var hasNegative = false;
        if (!winLoss)
        {
            foreach (var value in values)
            {
                if (!double.IsFinite(value))
                    continue;
                if (value > 0)
                    hasPositive = true;
                else if (value < 0)
                    hasNegative = true;
            }
        }

        if (hasPositive && !hasNegative)
            return rect.Bottom;
        if (hasNegative && !hasPositive)
            return rect.Top;
        return rect.Top + (rect.Height / 2);
    }

    // ── Line sparkline ────────────────────────────────────────────────────────

    private static void DrawLineSparkline(
        DrawingContext dc,
        SparklineModel sparkline,
        IReadOnlyList<double> values,
        Rect rect,
        Pen linePen,
        double? overrideMin,
        double? overrideMax,
        Dictionary<CellColor, SolidColorBrush>? brushCache)
    {
        var consumer = new LineSparklineDrawingConsumer(dc, linePen);
        SparklineLayoutPlanner.VisitLineLayout(values, rect, ref consumer, overrideMin, overrideMax, sparkline.RightToLeft);

        // Draw markers (line sparklines only).
        if (sparkline.ShowMarkers    || sparkline.ShowHighPoint   || sparkline.ShowLowPoint  ||
            sparkline.ShowFirstPoint || sparkline.ShowLastPoint   || sparkline.ShowNegativePoints)
        {
            DrawLineMarkers(dc, sparkline, values, rect, overrideMin, overrideMax, brushCache);
        }
    }

    private static void DrawLineMarkers(
        DrawingContext dc,
        SparklineModel sparkline,
        IReadOnlyList<double> values,
        Rect rect,
        double? overrideMin,
        double? overrideMax,
        Dictionary<CellColor, SolidColorBrush>? brushCache)
    {
        if (values.Count == 0)
            return;

        var points = SparklineLayoutPlanner.GetLinePoints(values, rect, overrideMin, overrideMax, sparkline.RightToLeft);
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
                dc.DrawEllipse(BrushForCellColor(markerColor.Value, brushCache), null, pt, r, r);
        }
    }

    // ── Column sparkline ──────────────────────────────────────────────────────

    private static void DrawColumnSparkline(
        DrawingContext dc,
        SparklineModel sparkline,
        IReadOnlyList<double> values,
        Rect rect,
        bool winLoss,
        Brush positiveFill,
        Brush negativeFill,
        double? overrideMaxAbs)
    {
        var consumer = new ColumnSparklineDrawingConsumer(dc, positiveFill, negativeFill);
        SparklineLayoutPlanner.VisitColumnLayout(values, rect, winLoss, ref consumer, overrideMaxAbs, sparkline.RightToLeft);
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
                DrawSparklineAxisLine(dc, values, rect, sparkline.Kind, axisColor, overrideMin, overrideMax, _sparklinePenCache, _brushCache);

            if (sparkline.Kind == SparklineKind.Line)
            {
                // Line weight: model LineWeight is in points; convert to DIPs (96 dpi / 72 pt).
                var lineWeightDip = PointsToDip(sparkline.LineWeight ?? DefaultLineWeightPt);
                var linePen = GetSparklinePen(seriesColor, lineWeightDip, _sparklinePenCache, _brushCache);
                DrawLineSparkline(dc, sparkline, values, rect, linePen, overrideMin, overrideMax, _brushCache);
            }
            else
            {
                DrawColumnSparkline(
                    dc, sparkline, values, rect,
                    sparkline.Kind == SparklineKind.WinLoss,
                    BrushForCellColor(seriesColor,   _brushCache),
                    BrushForCellColor(negativeColor, _brushCache),
                    overrideMaxAbs);
            }

            dc.Pop();
        }
    }

    // ── Cross-assembly print/PDF reuse (R88-render-sparkline-5-1) ─────────────
    // Sparklines are drawn as a screen-only overlay above (RenderSparklines): the WPF print/PDF
    // path (PrintRenderer.GridCells.cs, a different assembly) never called into it, so Print/
    // Print-Preview/PDF/XPS silently omitted every sparkline while still printing the cell's
    // value/fill/borders/gridlines. These two methods are public (rather than private) so that
    // path can draw the exact same sparkline ink instead of reimplementing the axis/scaling/layout
    // logic a second time -- mirroring how DrawConditionalDataBar/DrawConditionalIcon were already
    // made public for exactly this cross-assembly reuse. Deliberately independent of
    // RenderSparklines above (which stays on the interactive grid's own render caches) so adding
    // these carries no behavior change to the interactive redraw path.

    /// <summary>
    /// Computes the shared min/max/maxAbs bounds for every "Group" axis-scaling sparkline group
    /// among <paramref name="sparklines"/>, exactly as <see cref="RenderSparklines"/>'s own
    /// pre-compute step does.
    /// </summary>
    public static void BuildSparklineGroupScalingBounds(
        IEnumerable<SparklineModel> sparklines,
        IReadOnlyDictionary<Guid, IReadOnlyList<double>> sparklineValues,
        out Dictionary<int, double> groupMinValues,
        out Dictionary<int, double> groupMaxValues,
        out Dictionary<int, double> groupMaxAbsValues)
    {
        groupMinValues = new Dictionary<int, double>();
        groupMaxValues = new Dictionary<int, double>();
        groupMaxAbsValues = new Dictionary<int, double>();

        foreach (var sp in sparklines)
        {
            if ((sp.MinAxisType == SparklineAxisScaling.Group ||
                 sp.MaxAxisType == SparklineAxisScaling.Group) &&
                sparklineValues.TryGetValue(sp.Id, out var vals) && vals.Count > 0)
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
    }

    /// <summary>
    /// Draws one sparkline's axis line (if enabled) and its line/column/win-loss body (+ line
    /// markers) into <paramref name="rect"/>, exactly as the interactive grid's per-sparkline body
    /// in <see cref="RenderSparklines"/> does. <paramref name="brushCache"/>/<paramref name="penCache"/>
    /// are optional -- pass null (the print path's one-shot use) for uncached brushes/pens.
    /// </summary>
    public static void DrawSparklineIntoCell(
        DrawingContext dc,
        SparklineModel sparkline,
        IReadOnlyList<double> values,
        Rect rect,
        Dictionary<int, double> groupMinValues,
        Dictionary<int, double> groupMaxValues,
        Dictionary<int, double> groupMaxAbsValues,
        Dictionary<CellColor, SolidColorBrush>? brushCache = null,
        Dictionary<(CellColor Color, double Thickness), Pen>? penCache = null)
    {
        if (values.Count == 0)
            return;

        var overrideMin = ResolveAxisMin(sparkline, groupMinValues);
        var overrideMax = ResolveAxisMax(sparkline, groupMaxValues);
        var overrideMaxAbs = ResolveAxisMaxAbs(sparkline, groupMaxAbsValues);

        var seriesColor = sparkline.SeriesColor ?? DefaultPositiveCellColor;
        var negativeColor = sparkline.ShowNegativePoints
            ? sparkline.NegativeColor ?? DefaultNegativeCellColor
            : seriesColor;
        var axisColor = sparkline.AxisColor ?? DefaultAxisCellColor;

        var clipGeometry = new RectangleGeometry(rect);
        dc.PushClip(clipGeometry);

        if (sparkline.ShowAxis)
            DrawSparklineAxisLine(dc, values, rect, sparkline.Kind, axisColor, overrideMin, overrideMax, penCache, brushCache);

        if (sparkline.Kind == SparklineKind.Line)
        {
            var lineWeightDip = PointsToDip(sparkline.LineWeight ?? DefaultLineWeightPt);
            var linePen = GetSparklinePen(seriesColor, lineWeightDip, penCache, brushCache);
            DrawLineSparkline(dc, sparkline, values, rect, linePen, overrideMin, overrideMax, brushCache);
        }
        else
        {
            DrawColumnSparkline(
                dc, sparkline, values, rect,
                sparkline.Kind == SparklineKind.WinLoss,
                BrushForCellColor(seriesColor,   brushCache),
                BrushForCellColor(negativeColor, brushCache),
                overrideMaxAbs);
        }

        dc.Pop();
    }
}

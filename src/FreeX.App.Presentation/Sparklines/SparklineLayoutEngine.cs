using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Sparklines;

/// <summary>
/// A single connected segment of a line sparkline, from <see cref="Start"/> to <see cref="End"/>
/// in pixel space. Breaks in the data (non-finite values) split the line into multiple segments.
/// </summary>
public readonly record struct SparklineSegment(LayoutPoint Start, LayoutPoint End);

/// <summary>
/// Geometry for a line sparkline laid out inside a cell-sized rectangle. When the data reduces to a
/// single drawable point (one value, or a single finite value among gaps) it is reported as
/// <see cref="SinglePoint"/> and <see cref="Segments"/> is empty; otherwise the polyline is reported
/// as a list of connected segments.
/// </summary>
public readonly record struct SparklineLineLayout(
    LayoutPoint? SinglePoint,
    IReadOnlyList<SparklineSegment> Segments);

/// <summary>
/// A single bar of a column or win/loss sparkline. <see cref="Rect"/> is the bar rectangle in pixel
/// space; <see cref="IsNegative"/> marks bars whose value is below the axis (drawn below the
/// horizontal midline), which the desktop hosts typically render in a distinct color.
/// </summary>
public readonly record struct SparklineColumnBar(LayoutRect Rect, bool IsNegative);

/// <summary>
/// Geometry for a column or win/loss sparkline: a list of <see cref="SparklineColumnBar"/> laid out
/// inside the cell rectangle.
/// </summary>
public readonly record struct SparklineColumnLayout(IReadOnlyList<SparklineColumnBar> Bars);

/// <summary>
/// Receives line-sparkline geometry as it is computed, without materializing a list. Lets a renderer
/// stream points/segments straight into its drawing surface; the engine's list-producing
/// <see cref="SparklineLayoutEngine.CalculateLineLayout"/> is itself a thin collector over this path.
/// </summary>
public interface ISparklineLineLayoutConsumer
{
    void AcceptSinglePoint(LayoutPoint point);

    void AcceptSegment(LayoutPoint start, LayoutPoint end);
}

/// <summary>
/// Receives column / win-loss bar geometry as it is computed, without materializing a list. See
/// <see cref="ISparklineLineLayoutConsumer"/> for the rationale.
/// </summary>
public interface ISparklineColumnLayoutConsumer
{
    void AcceptBar(LayoutRect rect, bool isNegative);
}

/// <summary>
/// Portable, framework-free sparkline layout math shared by the desktop hosts. Maps a sequence of
/// data values into point/bar geometry within a cell-sized rectangle, faithful to the source
/// (line: min/max normalization with even horizontal spacing; column: per-value bars scaled by the
/// maximum magnitude; win/loss: fixed half-height bars keyed only on the sign of each value). The
/// renderers convert the returned geometry into their own drawing primitives.
/// </summary>
public static class SparklineLayoutEngine
{
    private const double Epsilon = 0.0000001;

    /// <summary>
    /// Lays out a line sparkline. Values are normalized between the minimum and maximum finite
    /// values and spread evenly across the rectangle width; non-finite values break the line.
    /// </summary>
    public static SparklineLineLayout CalculateLineLayout(IReadOnlyList<double> values, LayoutRect rect)
    {
        var consumer = new LineLayoutCollector(values.Count);
        VisitLineLayout(values, rect, ref consumer);
        return consumer.ToLayout();
    }

    /// <summary>
    /// Lays out a line sparkline with optional axis-bound overrides for group or custom scaling.
    /// When <paramref name="overrideMin"/> or <paramref name="overrideMax"/> is non-null the supplied
    /// value replaces the per-sparkline min/max so that all sparklines in a group share the same scale.
    /// </summary>
    public static SparklineLineLayout CalculateLineLayout(
        IReadOnlyList<double> values,
        LayoutRect rect,
        double? overrideMin,
        double? overrideMax)
    {
        var consumer = new LineLayoutCollector(values.Count);
        VisitLineLayout(values, rect, ref consumer, overrideMin, overrideMax);
        return consumer.ToLayout();
    }

    /// <summary>
    /// Lays out a line sparkline whose group has a Date Axis Type configured, spacing points
    /// proportionally to <paramref name="datePositions"/> (e.g. each point's date serial number)
    /// instead of evenly by array index -- matching Excel, which spaces a date-axis sparkline's
    /// points by elapsed time so unevenly-spaced dates bunch together or spread apart on screen.
    /// <paramref name="datePositions"/> must be the same length as <paramref name="values"/>; when it
    /// is null, the wrong length, or every finite entry shares the same position, this falls back to
    /// the even by-index spacing of <see cref="CalculateLineLayout(IReadOnlyList{double}, LayoutRect, double?, double?)"/>.
    /// </summary>
    public static SparklineLineLayout CalculateLineLayout(
        IReadOnlyList<double> values,
        LayoutRect rect,
        double? overrideMin,
        double? overrideMax,
        IReadOnlyList<double>? datePositions)
    {
        var consumer = new LineLayoutCollector(values.Count);
        VisitLineLayout(values, rect, ref consumer, overrideMin, overrideMax, datePositions);
        return consumer.ToLayout();
    }

    /// <summary>
    /// Lays out a line sparkline with optional axis-bound overrides, optional date-axis spacing, and
    /// the sparkline group's "Plot Data Right-to-Left" option. When <paramref name="rightToLeft"/> is
    /// true, every computed X position is mirrored within <paramref name="rect"/> so the first data
    /// point lands at the right edge and the last at the left, matching Excel; the mirroring is
    /// applied to the horizontal fraction before scaling, so it composes correctly with date-axis
    /// spacing (the date-proportional gaps are mirrored, not recomputed evenly).
    /// </summary>
    public static SparklineLineLayout CalculateLineLayout(
        IReadOnlyList<double> values,
        LayoutRect rect,
        double? overrideMin,
        double? overrideMax,
        IReadOnlyList<double>? datePositions,
        bool rightToLeft)
    {
        var consumer = new LineLayoutCollector(values.Count);
        VisitLineLayout(values, rect, ref consumer, overrideMin, overrideMax, datePositions, rightToLeft);
        return consumer.ToLayout();
    }

    /// <summary>
    /// Lays out a line sparkline, deriving the "Plot Data Right-to-Left" option directly from
    /// <paramref name="sparkline"/> instead of requiring the caller to thread a separate
    /// <c>rightToLeft</c> argument. This is the recommended entry point for any caller that already
    /// has the <see cref="SparklineModel"/> in hand: unlike the <c>rightToLeft</c>-parameter overloads
    /// (which a caller can silently omit and so miss the option), this overload makes it impossible to
    /// plot a sparkline without honoring its own group setting.
    /// </summary>
    public static SparklineLineLayout CalculateLineLayout(
        SparklineModel sparkline,
        IReadOnlyList<double> values,
        LayoutRect rect,
        double? overrideMin = null,
        double? overrideMax = null,
        IReadOnlyList<double>? datePositions = null)
    {
        ArgumentNullException.ThrowIfNull(sparkline);
        return CalculateLineLayout(values, rect, overrideMin, overrideMax, datePositions, sparkline.RightToLeft);
    }

    /// <summary>
    /// Streams a line sparkline's geometry into <paramref name="consumer"/>, deriving the "Plot Data
    /// Right-to-Left" option directly from <paramref name="sparkline"/>. See
    /// <see cref="CalculateLineLayout(SparklineModel, IReadOnlyList{double}, LayoutRect, double?, double?, IReadOnlyList{double}?)"/>
    /// for why this is the recommended entry point over the <c>rightToLeft</c>-parameter overloads.
    /// </summary>
    public static void VisitLineLayout<TConsumer>(
        SparklineModel sparkline,
        IReadOnlyList<double> values,
        LayoutRect rect,
        ref TConsumer consumer,
        double? overrideMin = null,
        double? overrideMax = null,
        IReadOnlyList<double>? datePositions = null)
        where TConsumer : struct, ISparklineLineLayoutConsumer
    {
        ArgumentNullException.ThrowIfNull(sparkline);
        VisitLineLayout(values, rect, ref consumer, overrideMin, overrideMax, datePositions, sparkline.RightToLeft);
    }

    /// <summary>
    /// Streams a line sparkline's geometry into <paramref name="consumer"/> without allocating a list.
    /// Same math as <see cref="CalculateLineLayout"/>; a renderer can consume points/segments directly.
    /// </summary>
    public static void VisitLineLayout<TConsumer>(
        IReadOnlyList<double> values,
        LayoutRect rect,
        ref TConsumer consumer)
        where TConsumer : struct, ISparklineLineLayoutConsumer =>
        VisitLineLayout(values, rect, ref consumer, overrideMin: null, overrideMax: null);

    /// <summary>
    /// Streams a line sparkline's geometry into <paramref name="consumer"/> with optional axis-bound
    /// overrides for group or custom scaling. When <paramref name="overrideMin"/> or
    /// <paramref name="overrideMax"/> is non-null the supplied value replaces the per-sparkline
    /// min/max so that all sparklines in a group share the same scale.
    /// </summary>
    public static void VisitLineLayout<TConsumer>(
        IReadOnlyList<double> values,
        LayoutRect rect,
        ref TConsumer consumer,
        double? overrideMin,
        double? overrideMax)
        where TConsumer : struct, ISparklineLineLayoutConsumer =>
        VisitLineLayout(values, rect, ref consumer, overrideMin, overrideMax, datePositions: null);

    /// <summary>
    /// Streams a line sparkline's geometry into <paramref name="consumer"/>, optionally spacing
    /// points by <paramref name="datePositions"/> (a Date Axis Type's per-point date serial numbers)
    /// instead of evenly by array index. See
    /// <see cref="CalculateLineLayout(IReadOnlyList{double}, LayoutRect, double?, double?, IReadOnlyList{double}?)"/>
    /// for the fallback rules when <paramref name="datePositions"/> cannot be used.
    /// </summary>
    public static void VisitLineLayout<TConsumer>(
        IReadOnlyList<double> values,
        LayoutRect rect,
        ref TConsumer consumer,
        double? overrideMin,
        double? overrideMax,
        IReadOnlyList<double>? datePositions)
        where TConsumer : struct, ISparklineLineLayoutConsumer =>
        VisitLineLayout(values, rect, ref consumer, overrideMin, overrideMax, datePositions, rightToLeft: false);

    /// <summary>
    /// Streams a line sparkline's geometry into <paramref name="consumer"/>, optionally spacing
    /// points by <paramref name="datePositions"/> and honoring the sparkline group's "Plot Data
    /// Right-to-Left" option. When <paramref name="rightToLeft"/> is true every point's horizontal
    /// fraction is mirrored (<c>1 - fraction</c>) before it is scaled into <paramref name="rect"/>, so
    /// the first data point lands at the right edge and the last at the left; this single mirroring
    /// step composes correctly with date-axis spacing since it is applied to the fraction the date
    /// spacing already produced, not recomputed from scratch.
    /// </summary>
    public static void VisitLineLayout<TConsumer>(
        IReadOnlyList<double> values,
        LayoutRect rect,
        ref TConsumer consumer,
        double? overrideMin,
        double? overrideMax,
        IReadOnlyList<double>? datePositions,
        bool rightToLeft)
        where TConsumer : struct, ISparklineLineLayoutConsumer
    {
        if (values.Count == 0 || rect.Width <= 0 || rect.Height <= 0)
            return;

        var firstIndex = -1;
        var min = 0d;
        var max = 0d;
        for (var i = 0; i < values.Count; i++)
        {
            var value = values[i];
            if (!double.IsFinite(value))
                continue;

            firstIndex = i;
            min = value;
            max = value;
            break;
        }

        if (firstIndex < 0)
            return;

        if (values.Count == 1)
        {
            consumer.AcceptSinglePoint(new LayoutPoint(rect.Left + (rect.Width / 2), rect.Top + (rect.Height / 2)));
            return;
        }

        for (var i = firstIndex + 1; i < values.Count; i++)
        {
            var value = values[i];
            if (!double.IsFinite(value))
                continue;

            if (value < min)
                min = value;
            if (value > max)
                max = value;
        }

        // Apply axis-bound overrides (group or custom scaling).
        if (overrideMin.HasValue && double.IsFinite(overrideMin.Value))
            min = overrideMin.Value;
        if (overrideMax.HasValue && double.IsFinite(overrideMax.Value))
            max = overrideMax.Value;

        // A configured Date Axis Type spaces points proportionally to elapsed time instead of
        // evenly by index (matching Excel). Usable only when a position is supplied for every
        // value and the positions actually span a non-zero range; otherwise fall back to even
        // by-index spacing below.
        var useDatePositions = false;
        var minPos = 0d;
        var maxPos = 0d;
        if (datePositions is not null && datePositions.Count == values.Count)
        {
            for (var i = 0; i < datePositions.Count; i++)
            {
                if (!double.IsFinite(values[i]))
                    continue;

                var position = datePositions[i];
                if (!double.IsFinite(position))
                {
                    minPos = maxPos = 0;
                    useDatePositions = false;
                    break;
                }

                if (!useDatePositions)
                {
                    minPos = position;
                    maxPos = position;
                    useDatePositions = true;
                }
                else
                {
                    if (position < minPos) minPos = position;
                    if (position > maxPos) maxPos = position;
                }
            }

            if (maxPos - minPos < Epsilon)
                useDatePositions = false;
        }

        var posSpan = useDatePositions ? maxPos - minPos : 1;

        var span = Math.Abs(max - min) < Epsilon ? 1 : max - min;
        LayoutPoint? previous = null;
        var visiblePointCount = 0;
        LayoutPoint lastPoint = default;
        for (var i = firstIndex; i < values.Count; i++)
        {
            var value = values[i];
            if (!double.IsFinite(value))
            {
                previous = null;
                continue;
            }

            var xFraction = useDatePositions
                ? (datePositions![i] - minPos) / posSpan
                : (double)i / (values.Count - 1);
            if (rightToLeft)
                xFraction = 1 - xFraction;
            var point = new LayoutPoint(
                rect.Left + (rect.Width * xFraction),
                rect.Bottom - (Math.Clamp((value - min) / span, 0, 1) * rect.Height));

            if (previous is { } start)
                consumer.AcceptSegment(start, point);

            previous = point;
            lastPoint = point;
            visiblePointCount++;
        }

        if (visiblePointCount == 1)
            consumer.AcceptSinglePoint(lastPoint);
    }

    /// <summary>
    /// Lays out a column or win/loss sparkline. When <paramref name="winLoss"/> is false, each bar's
    /// height is scaled by its magnitude relative to the largest absolute value; when true, every
    /// non-zero value yields a fixed half-height bar keyed only on its sign. Bars are centered in
    /// even-width slots. When the data is entirely one sign the zero baseline sits at the matching
    /// cell edge (bottom for all-positive, top for all-negative) so the largest bar fills the full
    /// cell height, matching Excel; mixed-sign data keeps the bars centered on the horizontal
    /// midline, each side capped at half height.
    /// </summary>
    public static SparklineColumnLayout CalculateColumnLayout(IReadOnlyList<double> values, LayoutRect rect, bool winLoss)
    {
        var consumer = new ColumnLayoutCollector(values.Count);
        VisitColumnLayout(values, rect, winLoss, ref consumer);
        return consumer.ToLayout();
    }

    /// <summary>
    /// Lays out a column or win/loss sparkline with an optional maximum-absolute-value override for
    /// group or custom scaling. When <paramref name="overrideMaxAbs"/> is non-null it replaces the
    /// per-sparkline max so all sparklines in a group share the same bar scale.
    /// </summary>
    public static SparklineColumnLayout CalculateColumnLayout(
        IReadOnlyList<double> values,
        LayoutRect rect,
        bool winLoss,
        double? overrideMaxAbs)
    {
        var consumer = new ColumnLayoutCollector(values.Count);
        VisitColumnLayout(values, rect, winLoss, ref consumer, overrideMaxAbs);
        return consumer.ToLayout();
    }

    /// <summary>
    /// Lays out a column or win/loss sparkline with an optional maximum-absolute-value override and
    /// the sparkline group's "Plot Data Right-to-Left" option. When <paramref name="rightToLeft"/> is
    /// true each bar's slot is mirrored within <paramref name="rect"/> so the first value's bar lands
    /// in the rightmost slot and the last in the leftmost, matching Excel; the vertical scale, axis
    /// baseline, and bar sign coloring are unchanged.
    /// </summary>
    public static SparklineColumnLayout CalculateColumnLayout(
        IReadOnlyList<double> values,
        LayoutRect rect,
        bool winLoss,
        double? overrideMaxAbs,
        bool rightToLeft)
    {
        var consumer = new ColumnLayoutCollector(values.Count);
        VisitColumnLayout(values, rect, winLoss, ref consumer, overrideMaxAbs, rightToLeft);
        return consumer.ToLayout();
    }

    /// <summary>
    /// Lays out a column or win/loss sparkline, deriving both its kind and its "Plot Data
    /// Right-to-Left" option directly from <paramref name="sparkline"/> instead of requiring the
    /// caller to thread separate <c>winLoss</c>/<c>rightToLeft</c> arguments. This is the recommended
    /// entry point for any caller that already has the <see cref="SparklineModel"/> in hand: unlike the
    /// <c>rightToLeft</c>-parameter overloads (which a caller can silently omit and so miss the
    /// option), this overload makes it impossible to plot a sparkline without honoring its own group
    /// setting.
    /// </summary>
    public static SparklineColumnLayout CalculateColumnLayout(
        SparklineModel sparkline,
        IReadOnlyList<double> values,
        LayoutRect rect,
        double? overrideMaxAbs = null)
    {
        ArgumentNullException.ThrowIfNull(sparkline);
        return CalculateColumnLayout(values, rect, sparkline.Kind == SparklineKind.WinLoss, overrideMaxAbs, sparkline.RightToLeft);
    }

    /// <summary>
    /// Streams a column / win-loss sparkline's bar geometry into <paramref name="consumer"/> without
    /// allocating a list. Same math as <see cref="CalculateColumnLayout(IReadOnlyList{double}, LayoutRect, bool)"/>.
    /// </summary>
    public static void VisitColumnLayout<TConsumer>(
        IReadOnlyList<double> values,
        LayoutRect rect,
        bool winLoss,
        ref TConsumer consumer)
        where TConsumer : struct, ISparklineColumnLayoutConsumer =>
        VisitColumnLayout(values, rect, winLoss, ref consumer, overrideMaxAbs: null);

    /// <summary>
    /// Streams a column / win-loss sparkline's bar geometry into <paramref name="consumer"/>, deriving
    /// both its kind and its "Plot Data Right-to-Left" option directly from <paramref name="sparkline"/>.
    /// See <see cref="CalculateColumnLayout(SparklineModel, IReadOnlyList{double}, LayoutRect, double?)"/>
    /// for why this is the recommended entry point over the <c>rightToLeft</c>-parameter overloads.
    /// </summary>
    public static void VisitColumnLayout<TConsumer>(
        SparklineModel sparkline,
        IReadOnlyList<double> values,
        LayoutRect rect,
        ref TConsumer consumer,
        double? overrideMaxAbs = null)
        where TConsumer : struct, ISparklineColumnLayoutConsumer
    {
        ArgumentNullException.ThrowIfNull(sparkline);
        VisitColumnLayout(values, rect, sparkline.Kind == SparklineKind.WinLoss, ref consumer, overrideMaxAbs, sparkline.RightToLeft);
    }

    /// <summary>
    /// Streams a column / win-loss sparkline's bar geometry into <paramref name="consumer"/> with an
    /// optional maximum-absolute-value override for group scaling. When <paramref name="overrideMaxAbs"/>
    /// is non-null it replaces the per-sparkline max so all sparklines in a group share the same bar scale.
    /// </summary>
    public static void VisitColumnLayout<TConsumer>(
        IReadOnlyList<double> values,
        LayoutRect rect,
        bool winLoss,
        ref TConsumer consumer,
        double? overrideMaxAbs)
        where TConsumer : struct, ISparklineColumnLayoutConsumer =>
        VisitColumnLayout(values, rect, winLoss, ref consumer, overrideMaxAbs, rightToLeft: false);

    /// <summary>
    /// Streams a column / win-loss sparkline's bar geometry into <paramref name="consumer"/> with an
    /// optional maximum-absolute-value override and the sparkline group's "Plot Data Right-to-Left"
    /// option. When <paramref name="rightToLeft"/> is true each bar's slot index is mirrored
    /// (<c>count - 1 - i</c>) so the first value's bar lands in the rightmost slot and the last in the
    /// leftmost; the per-bar width, vertical scale, and axis baseline are unchanged.
    /// </summary>
    public static void VisitColumnLayout<TConsumer>(
        IReadOnlyList<double> values,
        LayoutRect rect,
        bool winLoss,
        ref TConsumer consumer,
        double? overrideMaxAbs,
        bool rightToLeft)
        where TConsumer : struct, ISparklineColumnLayoutConsumer
    {
        if (values.Count == 0 || rect.Width <= 0 || rect.Height <= 0)
            return;

        var maxAbs = 0d;
        foreach (var value in values)
        {
            if (!double.IsFinite(value))
                continue;

            var absolute = Math.Abs(value);
            if (absolute > maxAbs)
                maxAbs = absolute;
        }

        // Apply axis-bound override (group or custom scaling). The override replaces the
        // data-derived max outright — including clamping/rescaling bars when it is smaller
        // than the data's own magnitude — matching VisitLineLayout's unconditional override.
        if (overrideMaxAbs.HasValue && double.IsFinite(overrideMaxAbs.Value) && overrideMaxAbs.Value > 0)
            maxAbs = overrideMaxAbs.Value;

        if (maxAbs < Epsilon)
            maxAbs = 1;

        // Excel's column sparkline places the zero baseline at the cell edge — not the vertical
        // midline — whenever the data is entirely one sign, so the largest bar fills the full cell
        // height instead of only the half nearest its side. Mixed-sign data keeps the traditional
        // centered axis with each side capped at half height. Win/loss bars are always fixed
        // half-height keyed on sign alone, so they keep the centered axis regardless of data shape.
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

        double axis;
        double maxBarHeight;
        if (hasPositive && !hasNegative)
        {
            axis = rect.Bottom;
            maxBarHeight = rect.Height;
        }
        else if (hasNegative && !hasPositive)
        {
            axis = rect.Top;
            maxBarHeight = rect.Height;
        }
        else
        {
            axis = rect.Top + (rect.Height / 2);
            maxBarHeight = rect.Height / 2;
        }

        var slot = rect.Width / values.Count;
        var barWidth = Math.Min(slot, Math.Max(1, slot * 0.65));

        for (var i = 0; i < values.Count; i++)
        {
            if (!double.IsFinite(values[i]))
                continue;
            if (Math.Abs(values[i]) < Epsilon)
                continue;

            var value = winLoss ? Math.Sign(values[i]) : values[i];
            var height = winLoss
                ? rect.Height / 2
                : Math.Abs(value) / maxAbs * maxBarHeight;
            height = Math.Min(maxBarHeight, Math.Max(1, height));
            var slotIndex = rightToLeft ? values.Count - 1 - i : i;
            var x = rect.Left + (slotIndex * slot) + ((slot - barWidth) / 2);
            var y = value >= 0 ? axis - height : axis;

            consumer.AcceptBar(new LayoutRect(x, y, barWidth, height), value < 0);
        }
    }

    /// <summary>
    /// Lays out a sparkline of the given kind, dispatching to the line or column/win-loss math.
    /// </summary>
    public static SparklineColumnLayout CalculateColumnLayout(IReadOnlyList<double> values, LayoutRect rect, SparklineKind kind) =>
        CalculateColumnLayout(values, rect, kind == SparklineKind.WinLoss);

    /// <summary>
    /// Lays out a sparkline of the given kind, dispatching to the line or column/win-loss math, and
    /// honoring the sparkline group's "Plot Data Right-to-Left" option.
    /// </summary>
    public static SparklineColumnLayout CalculateColumnLayout(IReadOnlyList<double> values, LayoutRect rect, SparklineKind kind, bool rightToLeft) =>
        CalculateColumnLayout(values, rect, kind == SparklineKind.WinLoss, overrideMaxAbs: null, rightToLeft);

    /// <summary>
    /// Returns the per-point Y positions for a line sparkline with the given axis bounds.
    /// Used by the renderer to place marker dots at each data point. The returned <c>Index</c> is
    /// always the point's original data index (0 = first, Count-1 = last) regardless of
    /// <paramref name="rightToLeft"/>, so a caller identifying "first/last/high/low" markers by index
    /// keeps working unchanged; only the geometry moves.
    /// </summary>
    public static IReadOnlyList<(int Index, LayoutPoint Point)> GetLinePoints(
        IReadOnlyList<double> values,
        LayoutRect rect,
        double? overrideMin,
        double? overrideMax,
        bool rightToLeft = false)
    {
        if (values.Count == 0 || rect.Width <= 0 || rect.Height <= 0)
            return [];

        var firstIndex = -1;
        var min = 0d;
        var max = 0d;
        for (var i = 0; i < values.Count; i++)
        {
            if (!double.IsFinite(values[i])) continue;
            firstIndex = i;
            min = values[i];
            max = values[i];
            break;
        }

        if (firstIndex < 0)
            return [];

        for (var i = firstIndex + 1; i < values.Count; i++)
        {
            if (!double.IsFinite(values[i])) continue;
            if (values[i] < min) min = values[i];
            if (values[i] > max) max = values[i];
        }

        if (overrideMin.HasValue && double.IsFinite(overrideMin.Value)) min = overrideMin.Value;
        if (overrideMax.HasValue && double.IsFinite(overrideMax.Value)) max = overrideMax.Value;

        var span = Math.Abs(max - min) < Epsilon ? 1 : max - min;
        var result = new List<(int, LayoutPoint)>(values.Count);
        for (var i = 0; i < values.Count; i++)
        {
            if (!double.IsFinite(values[i])) continue;
            var n = values.Count == 1 ? 0 : i;
            var denom = values.Count == 1 ? 1 : values.Count - 1;
            var xFraction = (double)n / denom;
            if (rightToLeft)
                xFraction = 1 - xFraction;
            var pt = new LayoutPoint(
                rect.Left + (rect.Width * xFraction),
                rect.Bottom - (Math.Clamp((values[i] - min) / span, 0, 1) * rect.Height));
            result.Add((i, pt));
        }

        return result;
    }

    private struct LineLayoutCollector(int valueCount) : ISparklineLineLayoutConsumer
    {
        private readonly int _segmentCapacity = Math.Max(0, valueCount - 1);
        private LayoutPoint? _singlePoint;
        private List<SparklineSegment>? _segments;

        public void AcceptSinglePoint(LayoutPoint point) => _singlePoint = point;

        public void AcceptSegment(LayoutPoint start, LayoutPoint end)
        {
            _segments ??= new List<SparklineSegment>(_segmentCapacity);
            _segments.Add(new SparklineSegment(start, end));
        }

        public readonly SparklineLineLayout ToLayout() => new(_singlePoint, _segments ?? []);
    }

    private struct ColumnLayoutCollector(int valueCount) : ISparklineColumnLayoutConsumer
    {
        private readonly int _barCapacity = valueCount;
        private List<SparklineColumnBar>? _bars;

        public void AcceptBar(LayoutRect rect, bool isNegative)
        {
            _bars ??= new List<SparklineColumnBar>(_barCapacity);
            _bars.Add(new SparklineColumnBar(rect, isNegative));
        }

        public readonly SparklineColumnLayout ToLayout() => new(_bars ?? []);
    }
}

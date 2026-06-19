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
    /// Streams a line sparkline's geometry into <paramref name="consumer"/> without allocating a list.
    /// Same math as <see cref="CalculateLineLayout"/>; a renderer can consume points/segments directly.
    /// </summary>
    public static void VisitLineLayout<TConsumer>(
        IReadOnlyList<double> values,
        LayoutRect rect,
        ref TConsumer consumer)
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

            var point = new LayoutPoint(
                rect.Left + (rect.Width * i / (values.Count - 1)),
                rect.Bottom - ((value - min) / span * rect.Height));

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
    /// even-width slots; negative values grow downward from the horizontal midline.
    /// </summary>
    public static SparklineColumnLayout CalculateColumnLayout(IReadOnlyList<double> values, LayoutRect rect, bool winLoss)
    {
        var consumer = new ColumnLayoutCollector(values.Count);
        VisitColumnLayout(values, rect, winLoss, ref consumer);
        return consumer.ToLayout();
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

        if (maxAbs < Epsilon)
            maxAbs = 1;

        var axis = rect.Top + (rect.Height / 2);
        var slot = rect.Width / values.Count;
        var barWidth = Math.Min(slot, Math.Max(1, slot * 0.65));
        var maxBarHeight = rect.Height / 2;

        for (var i = 0; i < values.Count; i++)
        {
            if (!double.IsFinite(values[i]))
                continue;
            if (Math.Abs(values[i]) < Epsilon)
                continue;

            var value = winLoss ? Math.Sign(values[i]) : values[i];
            var height = winLoss
                ? rect.Height / 2
                : Math.Abs(value) / maxAbs * rect.Height / 2;
            height = Math.Min(maxBarHeight, Math.Max(1, height));
            var x = rect.Left + (i * slot) + ((slot - barWidth) / 2);
            var y = value >= 0 ? axis - height : axis;

            consumer.AcceptBar(new LayoutRect(x, y, barWidth, height), value < 0);
        }
    }

    /// <summary>
    /// Lays out a sparkline of the given kind, dispatching to the line or column/win-loss math.
    /// </summary>
    public static SparklineColumnLayout CalculateColumnLayout(IReadOnlyList<double> values, LayoutRect rect, SparklineKind kind) =>
        CalculateColumnLayout(values, rect, kind == SparklineKind.WinLoss);

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

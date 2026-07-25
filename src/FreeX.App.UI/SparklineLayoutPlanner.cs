using System.Windows;

using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Sparklines;

using EngineLineConsumer = FreeX.App.Presentation.Sparklines.ISparklineLineLayoutConsumer;
using EngineColumnConsumer = FreeX.App.Presentation.Sparklines.ISparklineColumnLayoutConsumer;

namespace FreeX.App.UI;

public sealed record SparklineLineLayout(Point? SinglePoint, IReadOnlyList<(Point Start, Point End)> Segments);

public sealed record SparklineColumnLayout(IReadOnlyList<SparklineColumnBar> Bars);

public readonly record struct SparklineColumnBar(Rect Rect, bool IsNegative);

internal interface ISparklineLineLayoutConsumer
{
    void AcceptSinglePoint(Point point);

    void AcceptSegment(Point start, Point end);
}

internal interface ISparklineColumnLayoutConsumer
{
    void AcceptBar(Rect rect, bool isNegative);
}

/// <summary>
/// Thin WPF adapter over the portable <see cref="SparklineLayoutEngine"/>: the layout math lives in
/// the Presentation layer (no WPF types); this surface only translates between WPF
/// <see cref="Point"/>/<see cref="Rect"/> and the engine's <see cref="LayoutPoint"/>/<see cref="LayoutRect"/>.
/// The zero-allocation <c>Visit*</c> streaming path is preserved by wrapping the caller's WPF consumer
/// in an engine consumer that converts each geometry as it is produced.
/// </summary>
public static class SparklineLayoutPlanner
{
    public static SparklineLineLayout CalculateLineLayout(IReadOnlyList<double> values, Rect rect)
    {
        var consumer = new SparklineLineLayoutCollector(values.Count);
        VisitLineLayout(values, rect, ref consumer);
        return consumer.ToLayout();
    }

    internal static void VisitLineLayout<TConsumer>(
        IReadOnlyList<double> values,
        Rect rect,
        ref TConsumer consumer)
        where TConsumer : struct, ISparklineLineLayoutConsumer =>
        VisitLineLayout(values, rect, ref consumer, overrideMin: null, overrideMax: null);

    internal static void VisitLineLayout<TConsumer>(
        IReadOnlyList<double> values,
        Rect rect,
        ref TConsumer consumer,
        double? overrideMin,
        double? overrideMax)
        where TConsumer : struct, ISparklineLineLayoutConsumer =>
        VisitLineLayout(values, rect, ref consumer, overrideMin, overrideMax, rightToLeft: false);

    /// <summary>
    /// Streams a line sparkline's geometry honoring the sparkline group's "Plot Data Right-to-Left"
    /// option (<see cref="FreeX.Core.Model.SparklineModel.RightToLeft"/>). When
    /// <paramref name="rightToLeft"/> is true every point's horizontal position is mirrored so the
    /// first data point lands at the right edge and the last at the left, matching Excel.
    /// </summary>
    internal static void VisitLineLayout<TConsumer>(
        IReadOnlyList<double> values,
        Rect rect,
        ref TConsumer consumer,
        double? overrideMin,
        double? overrideMax,
        bool rightToLeft)
        where TConsumer : struct, ISparklineLineLayoutConsumer
    {
        var bridge = new LineConsumerBridge<TConsumer>(consumer);
        SparklineLayoutEngine.VisitLineLayout(
            values, ToLayoutRect(rect), ref bridge, overrideMin, overrideMax, datePositions: null, rightToLeft);
        consumer = bridge.Inner;
    }

    public static SparklineColumnLayout CalculateColumnLayout(IReadOnlyList<double> values, Rect rect, bool winLoss)
    {
        var consumer = new SparklineColumnLayoutCollector(values.Count);
        VisitColumnLayout(values, rect, winLoss, ref consumer);
        return consumer.ToLayout();
    }

    internal static void VisitColumnLayout<TConsumer>(
        IReadOnlyList<double> values,
        Rect rect,
        bool winLoss,
        ref TConsumer consumer)
        where TConsumer : struct, ISparklineColumnLayoutConsumer =>
        VisitColumnLayout(values, rect, winLoss, ref consumer, overrideMaxAbs: null);

    internal static void VisitColumnLayout<TConsumer>(
        IReadOnlyList<double> values,
        Rect rect,
        bool winLoss,
        ref TConsumer consumer,
        double? overrideMaxAbs)
        where TConsumer : struct, ISparklineColumnLayoutConsumer =>
        VisitColumnLayout(values, rect, winLoss, ref consumer, overrideMaxAbs, rightToLeft: false);

    /// <summary>
    /// Streams a column/win-loss sparkline's bars honoring the sparkline group's "Plot Data
    /// Right-to-Left" option (<see cref="FreeX.Core.Model.SparklineModel.RightToLeft"/>). When
    /// <paramref name="rightToLeft"/> is true each bar's slot is mirrored so the first value's bar
    /// lands in the rightmost slot and the last in the leftmost, matching Excel.
    /// </summary>
    internal static void VisitColumnLayout<TConsumer>(
        IReadOnlyList<double> values,
        Rect rect,
        bool winLoss,
        ref TConsumer consumer,
        double? overrideMaxAbs,
        bool rightToLeft)
        where TConsumer : struct, ISparklineColumnLayoutConsumer
    {
        var bridge = new ColumnConsumerBridge<TConsumer>(consumer);
        SparklineLayoutEngine.VisitColumnLayout(
            values, ToLayoutRect(rect), winLoss, ref bridge, overrideMaxAbs, rightToLeft);
        consumer = bridge.Inner;
    }

    internal static IReadOnlyList<(int Index, Point Point)> GetLinePoints(
        IReadOnlyList<double> values,
        Rect rect,
        double? overrideMin,
        double? overrideMax,
        bool rightToLeft = false)
    {
        var enginePoints = SparklineLayoutEngine.GetLinePoints(values, ToLayoutRect(rect), overrideMin, overrideMax, rightToLeft);
        if (enginePoints.Count == 0)
            return [];

        var result = new (int, Point)[enginePoints.Count];
        for (var i = 0; i < enginePoints.Count; i++)
            result[i] = (enginePoints[i].Index, ToPoint(enginePoints[i].Point));
        return result;
    }

    private static LayoutRect ToLayoutRect(Rect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    private static Point ToPoint(LayoutPoint point) => new(point.X, point.Y);

    private static Rect ToRect(LayoutRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    // Adapts the caller's WPF line consumer to the engine's LayoutPoint consumer, converting each
    // point/segment to WPF space as it streams through. Wraps the consumer by value (consumers are
    // mutating structs), so the caller reads the mutated state back from Inner after the visit.
    private struct LineConsumerBridge<TConsumer>(TConsumer inner) : EngineLineConsumer
        where TConsumer : struct, ISparklineLineLayoutConsumer
    {
        public TConsumer Inner = inner;

        public void AcceptSinglePoint(LayoutPoint point) => Inner.AcceptSinglePoint(ToPoint(point));

        public void AcceptSegment(LayoutPoint start, LayoutPoint end) =>
            Inner.AcceptSegment(ToPoint(start), ToPoint(end));
    }

    private struct ColumnConsumerBridge<TConsumer>(TConsumer inner) : EngineColumnConsumer
        where TConsumer : struct, ISparklineColumnLayoutConsumer
    {
        public TConsumer Inner = inner;

        public void AcceptBar(LayoutRect rect, bool isNegative) => Inner.AcceptBar(ToRect(rect), isNegative);
    }

    private struct SparklineLineLayoutCollector(int valueCount) : ISparklineLineLayoutConsumer
    {
        private readonly int _segmentCapacity = Math.Max(0, valueCount - 1);
        private Point? _singlePoint;
        private List<(Point Start, Point End)>? _segments;

        public void AcceptSinglePoint(Point point) => _singlePoint = point;

        public void AcceptSegment(Point start, Point end)
        {
            _segments ??= new List<(Point Start, Point End)>(_segmentCapacity);
            _segments.Add((start, end));
        }

        public readonly SparklineLineLayout ToLayout() => new(_singlePoint, _segments ?? []);
    }

    private struct SparklineColumnLayoutCollector(int valueCount) : ISparklineColumnLayoutConsumer
    {
        private readonly int _barCapacity = valueCount;
        private List<SparklineColumnBar>? _bars;

        public void AcceptBar(Rect rect, bool isNegative)
        {
            _bars ??= new List<SparklineColumnBar>(_barCapacity);
            _bars.Add(new SparklineColumnBar(rect, isNegative));
        }

        public readonly SparklineColumnLayout ToLayout() => new(_bars ?? []);
    }
}

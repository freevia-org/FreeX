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
        where TConsumer : struct, ISparklineLineLayoutConsumer
    {
        var bridge = new LineConsumerBridge<TConsumer>(consumer);
        SparklineLayoutEngine.VisitLineLayout(values, ToLayoutRect(rect), ref bridge);
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
        where TConsumer : struct, ISparklineColumnLayoutConsumer
    {
        var bridge = new ColumnConsumerBridge<TConsumer>(consumer);
        SparklineLayoutEngine.VisitColumnLayout(values, ToLayoutRect(rect), winLoss, ref bridge);
        consumer = bridge.Inner;
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

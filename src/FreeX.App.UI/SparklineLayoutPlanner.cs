using System.Windows;

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
            consumer.AcceptSinglePoint(new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2));
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

        var span = Math.Abs(max - min) < 0.0000001 ? 1 : max - min;
        Point? previous = null;
        var visiblePointCount = 0;
        for (var i = firstIndex; i < values.Count; i++)
        {
            var value = values[i];
            if (!double.IsFinite(value))
            {
                previous = null;
                continue;
            }

            var point = new Point(
                rect.Left + rect.Width * i / (values.Count - 1),
                rect.Bottom - ((value - min) / span * rect.Height));

            if (previous is { } start)
                consumer.AcceptSegment(start, point);
            previous = point;
            visiblePointCount++;
        }

        if (visiblePointCount == 1 && previous is { } singlePoint)
            consumer.AcceptSinglePoint(singlePoint);
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

        if (maxAbs < 0.0000001)
            maxAbs = 1;

        var axis = rect.Top + rect.Height / 2;
        var slot = rect.Width / values.Count;
        var barWidth = Math.Min(slot, Math.Max(1, slot * 0.65));
        var maxBarHeight = rect.Height / 2;

        for (var i = 0; i < values.Count; i++)
        {
            if (!double.IsFinite(values[i]))
                continue;
            if (Math.Abs(values[i]) < 0.0000001)
                continue;

            var value = winLoss ? Math.Sign(values[i]) : values[i];
            var height = winLoss
                ? rect.Height / 2
                : Math.Abs(value) / maxAbs * rect.Height / 2;
            height = Math.Min(maxBarHeight, Math.Max(1, height));
            var x = rect.Left + i * slot + (slot - barWidth) / 2;
            var y = value >= 0 ? axis - height : axis;
            consumer.AcceptBar(new Rect(x, y, barWidth, height), value < 0);
        }
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

        public SparklineLineLayout ToLayout() => new(_singlePoint, _segments ?? []);
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

        public SparklineColumnLayout ToLayout() => new(_bars ?? []);
    }
}

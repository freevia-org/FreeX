using System.Windows;

namespace Freexcel.App.UI;

public sealed record SparklineLineLayout(Point? SinglePoint, IReadOnlyList<(Point Start, Point End)> Segments);

public sealed record SparklineColumnLayout(IReadOnlyList<SparklineColumnBar> Bars);

public readonly record struct SparklineColumnBar(Rect Rect, bool IsNegative);

public static class SparklineLayoutPlanner
{
    public static SparklineLineLayout CalculateLineLayout(IReadOnlyList<double> values, Rect rect)
    {
        if (values.Count == 0)
            return new SparklineLineLayout(null, []);

        if (values.Count == 1)
            return new SparklineLineLayout(new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2), []);

        var min = values.Min();
        var max = values.Max();
        var span = Math.Abs(max - min) < 0.0000001 ? 1 : max - min;
        var points = values
            .Select((value, index) => new Point(
                rect.Left + rect.Width * index / (values.Count - 1),
                rect.Bottom - ((value - min) / span * rect.Height)))
            .ToArray();

        var segments = new List<(Point Start, Point End)>(points.Length - 1);
        for (var i = 1; i < points.Length; i++)
            segments.Add((points[i - 1], points[i]));

        return new SparklineLineLayout(null, segments);
    }

    public static SparklineColumnLayout CalculateColumnLayout(IReadOnlyList<double> values, Rect rect, bool winLoss)
    {
        if (values.Count == 0)
            return new SparklineColumnLayout([]);

        var maxAbs = values.Select(Math.Abs).DefaultIfEmpty(1).Max();
        if (maxAbs < 0.0000001)
            maxAbs = 1;

        var axis = rect.Top + rect.Height / 2;
        var slot = rect.Width / values.Count;
        var barWidth = Math.Max(1, slot * 0.65);
        var bars = new List<SparklineColumnBar>(values.Count);

        for (var i = 0; i < values.Count; i++)
        {
            var value = winLoss ? Math.Sign(values[i]) : values[i];
            var height = winLoss
                ? rect.Height / 2
                : Math.Abs(value) / maxAbs * rect.Height / 2;
            var x = rect.Left + i * slot + (slot - barWidth) / 2;
            var y = value >= 0 ? axis - height : axis;
            bars.Add(new SparklineColumnBar(new Rect(x, y, barWidth, Math.Max(1, height)), value < 0));
        }

        return new SparklineColumnLayout(bars);
    }
}

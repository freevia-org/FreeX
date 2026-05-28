using System.Windows;
using System.Windows.Media;

using Freexcel.Core.Model;

namespace Freexcel.App.UI;

public partial class GridView
{
    private static readonly SolidColorBrush SparklinePositiveBrush = FrozenBrush(Color.FromRgb(33, 115, 70));
    private static readonly SolidColorBrush SparklineNegativeBrush = FrozenBrush(Color.FromRgb(192, 0, 0));
    private static readonly Pen SparklineLinePen = FrozenPen(SparklinePositiveBrush, 1.25);

    private void RenderSparklines(DrawingContext dc)
    {
        if (Sparklines is not { Count: > 0 } ||
            SparklineValues is not { Count: > 0 } ||
            Viewport == null)
        {
            return;
        }

        var rowLookup = BuildSparklineRowMetricLookup(Viewport.RowMetrics);
        var colLookup = BuildSparklineColumnMetricLookup(Viewport.ColMetrics);

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

            dc.PushClip(new RectangleGeometry(rect));
            if (sparkline.Kind == SparklineKind.Line)
                DrawLineSparkline(dc, values, rect, SparklineLinePen);
            else
                DrawColumnSparkline(dc, values, rect, sparkline.Kind == SparklineKind.WinLoss, SparklinePositiveBrush, SparklineNegativeBrush);
            dc.Pop();
        }
    }

    private static Dictionary<uint, RowMetric> BuildSparklineRowMetricLookup(IReadOnlyList<RowMetric> rows)
    {
        var lookup = new Dictionary<uint, RowMetric>(rows.Count);
        foreach (var row in rows)
            lookup.Add(row.Row, row);

        return lookup;
    }

    private static Dictionary<uint, ColMetric> BuildSparklineColumnMetricLookup(IReadOnlyList<ColMetric> columns)
    {
        var lookup = new Dictionary<uint, ColMetric>(columns.Count);
        foreach (var column in columns)
            lookup.Add(column.Col, column);

        return lookup;
    }

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

    private static void DrawLineSparkline(DrawingContext dc, IReadOnlyList<double> values, Rect rect, Pen pen)
    {
        var layout = SparklineLayoutPlanner.CalculateLineLayout(values, rect);
        if (layout.SinglePoint is { } point)
        {
            dc.DrawEllipse(pen.Brush, null, point, 1.5, 1.5);
            return;
        }

        foreach (var segment in layout.Segments)
            dc.DrawLine(pen, segment.Start, segment.End);
    }

    private static void DrawColumnSparkline(
        DrawingContext dc,
        IReadOnlyList<double> values,
        Rect rect,
        bool winLoss,
        Brush positiveFill,
        Brush negativeFill)
    {
        foreach (var bar in SparklineLayoutPlanner.CalculateColumnLayout(values, rect, winLoss).Bars)
            dc.DrawRectangle(bar.IsNegative ? negativeFill : positiveFill, null, bar.Rect);
    }
}

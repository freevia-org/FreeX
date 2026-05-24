using System.Windows;
using System.Windows.Media;

using Freexcel.Core.Model;

namespace Freexcel.App.UI;

public partial class GridView
{
    private void RenderSparklines(DrawingContext dc)
    {
        if (Sparklines == null || SparklineValues == null || Viewport == null) return;

        var rowLookup = Viewport.RowMetrics.ToDictionary(r => r.Row);
        var colLookup = Viewport.ColMetrics.ToDictionary(c => c.Col);
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(33, 115, 70)), 1.25);
        var fill = new SolidColorBrush(Color.FromRgb(33, 115, 70));
        var negativeFill = new SolidColorBrush(Color.FromRgb(192, 0, 0));

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
                DrawLineSparkline(dc, values, rect, pen);
            else
                DrawColumnSparkline(dc, values, rect, sparkline.Kind == SparklineKind.WinLoss, fill, negativeFill);
            dc.Pop();
        }
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

using System.Windows;
using System.Windows.Media;

using FreeX.Core.Model;

namespace FreeX.App.UI;

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

        var lookups = GetRenderCellLookups(Viewport);
        var rowLookup = lookups.Rows;
        var colLookup = lookups.Columns;

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

            dc.PushClip(GetCellClipGeometry(rect));
            if (sparkline.Kind == SparklineKind.Line)
                DrawLineSparkline(dc, values, rect, SparklineLinePen);
            else
                DrawColumnSparkline(dc, values, rect, sparkline.Kind == SparklineKind.WinLoss, SparklinePositiveBrush, SparklineNegativeBrush);
            dc.Pop();
        }
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
        var consumer = new LineSparklineDrawingConsumer(dc, pen);
        SparklineLayoutPlanner.VisitLineLayout(values, rect, ref consumer);
    }

    private static void DrawColumnSparkline(
        DrawingContext dc,
        IReadOnlyList<double> values,
        Rect rect,
        bool winLoss,
        Brush positiveFill,
        Brush negativeFill)
    {
        var consumer = new ColumnSparklineDrawingConsumer(dc, positiveFill, negativeFill);
        SparklineLayoutPlanner.VisitColumnLayout(values, rect, winLoss, ref consumer);
    }

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
}

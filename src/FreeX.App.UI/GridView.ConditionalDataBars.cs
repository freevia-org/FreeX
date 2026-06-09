using System.Windows;
using System.Windows.Media;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public partial class GridView
{
    private static void DrawConditionalDataBar(
        DrawingContext dc,
        ConditionalFormatDataBar dataBar,
        Rect cellRect,
        Dictionary<CellColor, SolidColorBrush>? brushCache = null)
    {
        var start = Math.Clamp(dataBar.StartFraction, 0d, 1d);
        var end = Math.Clamp(dataBar.EndFraction, 0d, 1d);
        if (end < start)
            (start, end) = (end, start);

        const double horizontalInset = 2d;
        const double verticalInset = 3d;
        var drawableWidth = Math.Max(0d, cellRect.Width - horizontalInset * 2);
        var drawableHeight = Math.Max(0d, cellRect.Height - verticalInset * 2);
        var barWidth = drawableWidth * (end - start);
        if (barWidth <= 0d || drawableHeight <= 0d)
            return;

        var rect = new Rect(
            cellRect.Left + horizontalInset + drawableWidth * start,
            cellRect.Top + verticalInset,
            barWidth,
            drawableHeight);
        var color = dataBar.FillColor.ToCellColor();
        Brush fill = dataBar.Gradient
            ? CreateDataBarGradientBrush(color)
            : BrushForCellColor(color, brushCache);
        var border = dataBar.Border
            ? new Pen(BrushForCellColor(color, brushCache), 0.75)
            : null;
        if (border?.CanFreeze == true)
            border.Freeze();

        dc.DrawRectangle(fill, border, rect);
    }

    private static LinearGradientBrush CreateDataBarGradientBrush(CellColor color)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0)
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(90, color.R, color.G, color.B), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(color.R, color.G, color.B), 1));
        if (brush.CanFreeze)
            brush.Freeze();
        return brush;
    }
}

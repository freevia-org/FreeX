using System.Windows;
using System.Windows.Media;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public partial class GridView
{
    // Public (rather than private) so the WPF print/PDF path (PrintRenderer.GridCells.cs, a
    // different assembly) can draw the exact same data bar the interactive grid draws instead of
    // reimplementing the layout/gradient/axis logic a second time.
    public static void DrawConditionalDataBar(
        DrawingContext dc,
        ConditionalFormatDataBar dataBar,
        Rect cellRect,
        Dictionary<CellColor, SolidColorBrush>? brushCache = null)
    {
        if (ConditionalDataBarLayoutPlanner.Plan(dataBar.StartFraction, dataBar.EndFraction)
                is not { } layout)
            return;

        var drawableWidth = Math.Max(0d, cellRect.Width - layout.HorizontalInset * 2);
        var drawableHeight = Math.Max(0d, cellRect.Height - layout.VerticalInset * 2);
        var barWidth = drawableWidth * layout.FractionWidth;
        if (barWidth <= 0d || drawableHeight <= 0d)
            return;

        var rect = new Rect(
            cellRect.Left + layout.HorizontalInset + drawableWidth * layout.Start,
            cellRect.Top + layout.VerticalInset,
            barWidth,
            drawableHeight);
        var color = dataBar.FillColor.ToCellColor();

        // For negative bars with gradient, reverse gradient direction so the bar appears to flow
        // from the axis (right side of the bar) toward the left edge.
        Brush fill;
        if (dataBar.Gradient)
        {
            fill = dataBar.IsNegative
                ? CreateDataBarGradientBrushReversed(color)
                : CreateDataBarGradientBrush(color);
        }
        else
        {
            fill = BrushForCellColor(color, brushCache);
        }

        Pen? border = null;
        if (dataBar.Border)
        {
            // Use the authored border color when available; otherwise fall back to the fill color.
            CellColor borderColor;
            if (dataBar.BorderColor.HasValue)
            {
                var bc = dataBar.BorderColor.Value;
                borderColor = new CellColor(bc.R, bc.G, bc.B);
            }
            else
            {
                borderColor = color;
            }
            border = new Pen(BrushForCellColor(borderColor, brushCache), 0.75);
            if (border.CanFreeze)
                border.Freeze();
        }

        dc.DrawRectangle(fill, border, rect);

        // Draw the zero-crossing axis line when the data bar has an axis position (negative bar scenario).
        if (dataBar.AxisFraction > 0d && dataBar.AxisFraction < 1d)
        {
            var axisX = cellRect.Left + layout.HorizontalInset + drawableWidth * dataBar.AxisFraction;
            var axisTop = cellRect.Top + layout.VerticalInset;
            var axisBottom = cellRect.Top + layout.VerticalInset + drawableHeight;

            // Use the authored axis color if supplied, otherwise a neutral mid-grey.
            CellColor axisColor;
            if (dataBar.AxisColor.HasValue)
            {
                var ac = dataBar.AxisColor.Value;
                axisColor = new CellColor(ac.R, ac.G, ac.B);
            }
            else
            {
                axisColor = new CellColor(0, 0, 0);
            }

            var axisPen = new Pen(BrushForCellColor(axisColor, brushCache), 1d);
            if (axisPen.CanFreeze)
                axisPen.Freeze();
            dc.DrawLine(axisPen, new Point(axisX, axisTop), new Point(axisX, axisBottom));
        }
    }

    private static LinearGradientBrush CreateDataBarGradientBrushReversed(CellColor color)
    {
        // For negative bars: dark on the left (away from axis), fading toward the axis on the right.
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0)
        };
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(color.R, color.G, color.B), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(90, color.R, color.G, color.B), 1));
        if (brush.CanFreeze)
            brush.Freeze();
        return brush;
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

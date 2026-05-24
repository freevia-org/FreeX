using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Freexcel.Core.Model;

namespace Freexcel.App.UI;

public partial class GridView
{
    private void RenderFreezeDivider(DrawingContext dc)
    {
        if (Viewport?.FrozenPanes == null) return;
        var fp = Viewport.FrozenPanes;

        if (fp.Rows > 0)
        {
            var lastFrozenRow = Viewport.RowMetrics.FirstOrDefault(r => r.Row == fp.Rows);
            if (lastFrozenRow != null)
            {
                double y = lastFrozenRow.TopOffset + lastFrozenRow.Height + EffectiveColHeaderHeight;
                dc.DrawLine(FreezePen, new Point(0, y), new Point(ActualWidth, y));
            }
        }

        if (fp.Cols > 0)
        {
            var lastFrozenCol = Viewport.ColMetrics.FirstOrDefault(c => c.Col == fp.Cols);
            if (lastFrozenCol != null)
            {
                double x = lastFrozenCol.LeftOffset + lastFrozenCol.Width + ActualRowHeaderWidth;
                dc.DrawLine(FreezePen, new Point(x, 0), new Point(x, ActualHeight));
            }
        }
    }

    private void RenderHeaders(DrawingContext dc)
    {
        if (!ShowHeaders) return;

        var selectedRanges = SelectedRanges;
        var selRange = SelectedRange;

        foreach (var col in Viewport!.ColMetrics)
        {
            bool inSel = selectedRanges is { Count: > 0 }
                ? selectedRanges.Any(r => col.Col >= r.Start.Col && col.Col <= r.End.Col)
                : selRange.HasValue
                    && col.Col >= selRange.Value.Start.Col
                    && col.Col <= selRange.Value.End.Col;

            var bg = inSel ? HeaderHighlightBrush : HeaderBackgroundBrush;
            var rect = new Rect(col.LeftOffset + ActualRowHeaderWidth, 0, col.Width, EffectiveColHeaderHeight);
            dc.DrawRectangle(bg, GridPen, rect);

            var text = new FormattedText(
                FormatColumnHeader(col.Col, UseR1C1ReferenceStyle),
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                DefaultTypeface, 11, TextBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            dc.DrawText(text, new Point(
                rect.Left + (rect.Width - text.Width) / 2,
                rect.Top + (rect.Height - text.Height) / 2));
        }

        foreach (var row in Viewport!.RowMetrics)
        {
            bool inSel = selectedRanges is { Count: > 0 }
                ? selectedRanges.Any(r => row.Row >= r.Start.Row && row.Row <= r.End.Row)
                : selRange.HasValue
                    && row.Row >= selRange.Value.Start.Row
                    && row.Row <= selRange.Value.End.Row;

            var bg = inSel ? HeaderHighlightBrush : HeaderBackgroundBrush;
            var rect = new Rect(0, row.TopOffset + EffectiveColHeaderHeight, ActualRowHeaderWidth, row.Height);
            dc.DrawRectangle(bg, GridPen, rect);

            var text = new FormattedText(
                row.Row.ToString(),
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                DefaultTypeface, 11, TextBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            dc.DrawText(text, new Point(
                rect.Left + (rect.Width - text.Width) / 2,
                rect.Top + (rect.Height - text.Height) / 2));
        }

        dc.DrawRectangle(HeaderBackgroundBrush, GridPen,
            new Rect(0, 0, ActualRowHeaderWidth, EffectiveColHeaderHeight));
    }

    internal static string FormatColumnHeader(uint column, bool useR1C1ReferenceStyle) =>
        useR1C1ReferenceStyle
            ? column.ToString(CultureInfo.InvariantCulture)
            : CellAddress.NumberToColumnName(column);
}

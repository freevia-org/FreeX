using System.Collections.Concurrent;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public partial class GridView
{
    private static readonly ConcurrentDictionary<uint, string> ColumnHeaderCache = new();

    private void RenderFreezeDivider(DrawingContext dc)
    {
        if (Viewport?.FrozenPanes == null) return;
        var fp = Viewport.FrozenPanes;
        var rowHeaderWidth = ActualRowHeaderWidth;
        var columnHeaderHeight = EffectiveColHeaderHeight;

        if (fp.Rows > 0)
        {
            var lastFrozenRow = FindRowMetric(Viewport.RowMetrics, fp.Rows);
            if (lastFrozenRow != null)
            {
                double y = lastFrozenRow.TopOffset + lastFrozenRow.Height + columnHeaderHeight;
                dc.DrawLine(FreezePen, new Point(0, y), new Point(ActualWidth, y));
            }
        }

        if (fp.Cols > 0)
        {
            var lastFrozenCol = FindColMetric(Viewport.ColMetrics, fp.Cols);
            if (lastFrozenCol != null)
            {
                double x = lastFrozenCol.LeftOffset + lastFrozenCol.Width + rowHeaderWidth;
                dc.DrawLine(FreezePen, new Point(x, 0), new Point(x, ActualHeight));
            }
        }
    }

    private void RenderHeaders(DrawingContext dc)
    {
        if (!ShowHeaders) return;

        var selectedRanges = SelectedRanges;
        var selRange = SelectedRange;
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var rowHeaderWidth = ActualRowHeaderWidth;
        var columnHeaderHeight = EffectiveColHeaderHeight;

        foreach (var col in Viewport!.ColMetrics)
        {
            bool inSel = IsColumnHeaderSelected(col.Col, selectedRanges, selRange);

            var bg = inSel ? HeaderHighlightBrush : HeaderBackgroundBrush;
            var rect = new Rect(col.LeftOffset + rowHeaderWidth, 0, col.Width, columnHeaderHeight);
            dc.DrawRectangle(bg, GridPen, rect);

            var text = GetDefaultFormattedText(
                FormatColumnHeader(col.Col, UseR1C1ReferenceStyle),
                11,
                pixelsPerDip);

            dc.DrawText(text, new Point(
                rect.Left + (rect.Width - text.Width) / 2,
                rect.Top + (rect.Height - text.Height) / 2));
        }

        foreach (var row in Viewport!.RowMetrics)
        {
            bool inSel = IsRowHeaderSelected(row.Row, selectedRanges, selRange);

            var bg = inSel ? HeaderHighlightBrush : HeaderBackgroundBrush;
            var rect = new Rect(0, row.TopOffset + columnHeaderHeight, rowHeaderWidth, row.Height);
            dc.DrawRectangle(bg, GridPen, rect);

            var text = GetDefaultFormattedText(
                row.Row.ToString(CultureInfo.InvariantCulture),
                11,
                pixelsPerDip);

            dc.DrawText(text, new Point(
                rect.Left + (rect.Width - text.Width) / 2,
                rect.Top + (rect.Height - text.Height) / 2));
        }

        dc.DrawRectangle(HeaderBackgroundBrush, GridPen,
            new Rect(0, 0, rowHeaderWidth, columnHeaderHeight));
    }

    private static bool IsColumnHeaderSelected(uint column, IReadOnlyList<GridRange>? selectedRanges, GridRange? selectedRange)
    {
        if (selectedRanges is { Count: > 0 })
        {
            foreach (var range in selectedRanges)
            {
                if (column >= range.Start.Col && column <= range.End.Col)
                    return true;
            }

            return false;
        }

        return selectedRange.HasValue &&
            column >= selectedRange.Value.Start.Col &&
            column <= selectedRange.Value.End.Col;
    }

    private static bool IsRowHeaderSelected(uint row, IReadOnlyList<GridRange>? selectedRanges, GridRange? selectedRange)
    {
        if (selectedRanges is { Count: > 0 })
        {
            foreach (var range in selectedRanges)
            {
                if (row >= range.Start.Row && row <= range.End.Row)
                    return true;
            }

            return false;
        }

        return selectedRange.HasValue &&
            row >= selectedRange.Value.Start.Row &&
            row <= selectedRange.Value.End.Row;
    }

    internal static string FormatColumnHeader(uint column, bool useR1C1ReferenceStyle) =>
        useR1C1ReferenceStyle
            ? column.ToString(CultureInfo.InvariantCulture)
            : ColumnHeaderCache.GetOrAdd(column, static col => CellAddress.NumberToColumnName(col));
}

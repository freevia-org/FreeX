using Avalonia;
using Free.Shared.AppServices;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Rendering.Avalonia;

internal readonly record struct AvaloniaInlineTableCellTextPlan(
    Rect Area,
    Point Origin);

internal static class AvaloniaInlineTableLayoutPlanner
{
    internal const double PtToDip = 96.0 / 72.0;
    private const double DefaultCellInsetDip = 2;

    internal static AvaloniaInlineTableCellTextPlan PlanCellText(
        TableCell? cell,
        Rect bounds,
        double measuredTextHeight)
    {
        var area = GetTextArea(cell, bounds);
        return new(area, GetTextOrigin(cell, area, measuredTextHeight));
    }

    internal static Rect GetTextArea(TableCell? cell, Rect bounds)
    {
        double left = ResolveInset(cell?.InsetLeftPt);
        double top = ResolveInset(cell?.InsetTopPt);
        double right = ResolveInset(cell?.InsetRightPt);
        double bottom = ResolveInset(cell?.InsetBottomPt);
        var area = new Rect(
            bounds.X + left,
            bounds.Y + top,
            Math.Max(1, bounds.Width - left - right),
            Math.Max(1, bounds.Height - top - bottom));
        return area;
    }

    internal static double GetHorizontalOffset(
        TableRowHorizontalAlignment? alignment,
        double availableWidth,
        double rowWidth)
    {
        double extra = Math.Max(0, availableWidth - rowWidth);
        return alignment switch
        {
            TableRowHorizontalAlignment.Center => extra / 2,
            TableRowHorizontalAlignment.Right => extra,
            _ => 0,
        };
    }

    internal static Point GetTextOrigin(
        TableCell? cell,
        Rect area,
        double measuredTextHeight)
    {
        double extraHeight = Math.Max(0, area.Height - measuredTextHeight);
        double offset = cell?.Anchor switch
        {
            TableCellAnchor.Middle => extraHeight / 2,
            TableCellAnchor.Bottom => extraHeight,
            _ => 0,
        };
        return new(area.X, area.Y + offset);
    }

    private static double ResolveInset(double? points) =>
        points is { } value && value >= 0
            ? value * PtToDip
            : DefaultCellInsetDip;
}

internal sealed record AvaloniaInlineTableCellLayout(
    int RowIndex,
    int ColumnIndex,
    TableCell? Cell,
    Rect Bounds,
    int SourceCellIndex);

internal sealed class AvaloniaInlineTableGridLayout
{
    private readonly IReadOnlyList<AvaloniaInlineTableCellLayout> _cells;
    private readonly TableGridGeometry _geometry;
    private readonly InlineTableLogicalGridPlan _logicalGrid;
    private readonly TableShape _table;
    private readonly Point _origin;
    private readonly double[] _widths;
    private readonly double[] _heights;
    private readonly double _spacing;
    private readonly double _indent;
    private readonly double[] _rowOffsets;

    private AvaloniaInlineTableGridLayout(
        IReadOnlyList<AvaloniaInlineTableCellLayout> cells,
        TableGridGeometry geometry,
        InlineTableLogicalGridPlan logicalGrid,
        TableShape table,
        Point origin,
        double[] widths,
        double[] heights,
        double spacing,
        double indent,
        double[] rowOffsets)
    {
        _cells = cells;
        _geometry = geometry;
        _logicalGrid = logicalGrid;
        _table = table;
        _origin = origin;
        _widths = widths;
        _heights = heights;
        _spacing = spacing;
        _indent = indent;
        _rowOffsets = rowOffsets;
    }

    internal IReadOnlyList<AvaloniaInlineTableCellLayout> Cells => _cells;

    internal static AvaloniaInlineTableGridLayout Create(
        TableShape table,
        Point origin,
        double availableWidth)
    {
        int rowCount = Math.Max(1, table.Rows.Count);
        int columnCount = Math.Max(1, table.ColumnWidthsEmu.Count);
        var widths = Enumerable.Range(0, columnCount)
            .Select(index => index < table.ColumnWidthsEmu.Count
                ? Math.Max(24, table.ColumnWidthsEmu[index] / 9525.0)
                : 72)
            .ToArray();
        var heights = Enumerable.Range(0, rowCount)
            .Select(index => index < table.Rows.Count && table.Rows[index].HeightEmu > 0
                ? Math.Max(20, table.Rows[index].HeightEmu / 9525.0)
                : 24)
            .ToArray();
        double spacing = Math.Max(0, table.RichTextCellSpacingPt.GetValueOrDefault())
            * AvaloniaInlineTableLayoutPlanner.PtToDip;
        double indent = table.RichTextLeftIndentPt.GetValueOrDefault()
            * AvaloniaInlineTableLayoutPlanner.PtToDip;
        var logicalGrid = InlineTableLogicalGridPlan.Create(table);
        var geometry = new TableGridGeometry(
            widths,
            heights,
            logicalGrid.GridCells);
        var rowOffsets = new double[rowCount];
        for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            var row = table.Rows.ElementAtOrDefault(rowIndex);
            double rowWidth = row is null
                ? widths.Sum()
                : widths.Take(Math.Min(widths.Length, row.Cells.Sum(cell => Math.Max(1, cell.GridSpan)))).Sum();
            rowOffsets[rowIndex] = AvaloniaInlineTableLayoutPlanner.GetHorizontalOffset(
                row?.HorizontalAlignment,
                availableWidth,
                rowWidth);
        }

        var cells = new List<AvaloniaInlineTableCellLayout>();
        foreach (var logicalCell in logicalGrid.Cells)
        {
            var bounds = GetAnchorBounds(
                geometry,
                origin,
                logicalCell,
                widths,
                spacing,
                indent,
                rowOffsets);
            cells.Add(new AvaloniaInlineTableCellLayout(
                logicalCell.RowIndex,
                logicalCell.ColumnIndex,
                logicalCell.Cell,
                bounds,
                logicalCell.SourceCellIndex));
        }

        return new AvaloniaInlineTableGridLayout(
            cells,
            geometry,
            logicalGrid,
            table,
            origin,
            widths,
            heights,
            spacing,
            indent,
            rowOffsets);
    }

    internal AvaloniaInlineTableCellLayout? GetCell(int rowIndex, int columnIndex)
    {
        var logicalCell = _logicalGrid.ResolveCell(rowIndex, columnIndex);
        return logicalCell is null
            ? null
            : _cells.FirstOrDefault(cell =>
                cell.RowIndex == logicalCell.RowIndex
                && cell.ColumnIndex == logicalCell.ColumnIndex);
    }

    internal AvaloniaInlineTableCellLayout? HitTest(Point point)
    {
        int rowCount = Math.Max(1, _table.Rows.Count);
        int columnCount = Math.Max(1, _table.ColumnWidthsEmu.Count);
        double y = _origin.Y;
        for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            double x = _origin.X + _indent + _rowOffsets[rowIndex];
            for (int columnIndex = 0; columnIndex < columnCount; columnIndex++)
            {
                var slot = new Rect(
                    x,
                    y,
                    _widths[columnIndex],
                    _heights[rowIndex]);
                if (slot.Contains(point))
                    return GetCell(rowIndex, columnIndex);
                x += _widths[columnIndex] + _spacing;
            }

            y += _heights[rowIndex];
        }

        return null;
    }

    private static Rect GetAnchorBounds(
        TableGridGeometry geometry,
        Point origin,
        InlineTableLogicalCell logicalCell,
        IReadOnlyList<double> widths,
        double spacing,
        double indent,
        IReadOnlyList<double> rowOffsets)
    {
        var gridRect = TableGridGeometryPlanner.GetCellRect(
            geometry,
            origin.X + indent + rowOffsets[logicalCell.RowIndex],
            origin.Y,
            logicalCell.RowIndex,
            logicalCell.ColumnIndex)!.Value;
        var cell = logicalCell.Cell;
        int span = Math.Min(
            Math.Max(1, cell.GridSpan),
            widths.Count - logicalCell.ColumnIndex);
        return new Rect(
            gridRect.X + spacing * logicalCell.ColumnIndex,
            gridRect.Y,
            gridRect.Width + spacing * Math.Max(0, span - 1),
            gridRect.Height);
    }

}

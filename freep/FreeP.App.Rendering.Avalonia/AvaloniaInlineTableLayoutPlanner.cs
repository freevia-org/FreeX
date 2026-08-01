using Avalonia;
using Free.Shared.AppServices;
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
        var geometry = new TableGridGeometry(
            widths,
            heights,
            BuildGridCells(table, rowCount, columnCount));
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
        for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            for (int columnIndex = 0; columnIndex < columnCount; columnIndex++)
            {
                var anchor = TableGridGeometryPlanner.ResolveCell(
                    geometry,
                    rowIndex,
                    columnIndex);
                if (anchor is null
                    || anchor.Value.Row != rowIndex
                    || anchor.Value.Col != columnIndex)
                    continue;

                var sourceCell = GetSourceCell(
                    table,
                    rowIndex,
                    columnIndex,
                    columnCount);
                var bounds = GetAnchorBounds(
                    geometry,
                    table,
                    origin,
                    rowIndex,
                    columnIndex,
                    widths,
                    spacing,
                    indent,
                    rowOffsets);
                cells.Add(new AvaloniaInlineTableCellLayout(
                    rowIndex,
                    columnIndex,
                    sourceCell,
                    bounds,
                    GetSourceCellIndex(table, rowIndex, columnIndex, columnCount)));
            }
        }

        return new AvaloniaInlineTableGridLayout(
            cells,
            geometry,
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
        var anchor = TableGridGeometryPlanner.ResolveCell(
            _geometry,
            rowIndex,
            columnIndex);
        return anchor is null
            ? null
            : _cells.FirstOrDefault(cell =>
                cell.RowIndex == anchor.Value.Row
                && cell.ColumnIndex == anchor.Value.Col);
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
        TableShape table,
        Point origin,
        int rowIndex,
        int columnIndex,
        IReadOnlyList<double> widths,
        double spacing,
        double indent,
        IReadOnlyList<double> rowOffsets)
    {
        var gridRect = TableGridGeometryPlanner.GetCellRect(
            geometry,
            origin.X + indent + rowOffsets[rowIndex],
            origin.Y,
            rowIndex,
            columnIndex)!.Value;
        var cell = GetSourceCell(
            table,
            rowIndex,
            columnIndex,
            widths.Count);
        int span = Math.Min(
            Math.Max(1, cell?.GridSpan ?? 1),
            widths.Count - columnIndex);
        return new Rect(
            gridRect.X + spacing * columnIndex,
            gridRect.Y,
            gridRect.Width + spacing * Math.Max(0, span - 1),
            gridRect.Height);
    }

    private static IReadOnlyList<IReadOnlyList<TableGridCell>> BuildGridCells(
        TableShape table,
        int rowCount,
        int columnCount)
    {
        var result = new List<IReadOnlyList<TableGridCell>>(rowCount);
        for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            var row = table.Rows.ElementAtOrDefault(rowIndex);
            var cells = Enumerable.Repeat(default(TableGridCell), columnCount).ToArray();
            if (row is not null)
            {
                bool compact = row.Cells.Count < columnCount
                    && row.Cells.Sum(cell => Math.Max(1, cell.GridSpan)) <= columnCount;
                int sourceIndex = 0;
                int columnIndex = 0;
                foreach (var sourceCell in row.Cells)
                {
                    int targetColumn = compact ? columnIndex : sourceIndex;
                    if (targetColumn >= columnCount)
                        break;

                    int gridSpan = Math.Min(
                        Math.Max(1, sourceCell.GridSpan),
                        columnCount - targetColumn);
                    cells[targetColumn] = ToGridCell(sourceCell);
                    for (int coveredColumn = targetColumn + 1;
                         coveredColumn < targetColumn + gridSpan;
                         coveredColumn++)
                    {
                        cells[coveredColumn] = new TableGridCell(1, 1, true, false);
                    }

                    if (compact)
                        columnIndex += gridSpan;
                    sourceIndex++;
                }
            }

            result.Add(cells);
        }

        for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            var row = table.Rows.ElementAtOrDefault(rowIndex);
            if (row is null)
                continue;
            bool compact = row.Cells.Count < columnCount
                && row.Cells.Sum(cell => Math.Max(1, cell.GridSpan)) <= columnCount;
            int sourceIndex = 0;
            int columnIndex = 0;
            foreach (var sourceCell in row.Cells)
            {
                int targetColumn = compact ? columnIndex : sourceIndex;
                if (targetColumn >= columnCount)
                    break;
                int gridSpan = Math.Min(
                    Math.Max(1, sourceCell.GridSpan),
                    columnCount - targetColumn);
                if (sourceCell.HMerge || sourceCell.VMerge)
                {
                    sourceIndex++;
                    if (compact)
                        columnIndex += gridSpan;
                    continue;
                }

                int rowSpan = Math.Min(
                    Math.Max(1, sourceCell.RowSpan),
                    rowCount - rowIndex);
                for (int coveredRow = rowIndex + 1;
                     coveredRow < rowIndex + rowSpan;
                     coveredRow++)
                {
                    for (int coveredColumn = targetColumn;
                         coveredColumn < targetColumn + gridSpan;
                         coveredColumn++)
                    {
                        var coveredCells = result[coveredRow].ToArray();
                        coveredCells[coveredColumn] = new TableGridCell(1, 1, false, true);
                        result[coveredRow] = coveredCells;
                    }
                }

                sourceIndex++;
                if (compact)
                    columnIndex += gridSpan;
            }
        }

        return result;
    }

    private static TableGridCell ToGridCell(TableCell cell) =>
        new(cell.GridSpan, cell.RowSpan, cell.HMerge, cell.VMerge);

    private static TableCell? GetSourceCell(
        TableShape table,
        int rowIndex,
        int columnIndex,
        int columnCount)
    {
        var row = table.Rows.ElementAtOrDefault(rowIndex);
        if (row is null)
            return null;

        int sourceIndex = GetSourceCellIndex(table, rowIndex, columnIndex, columnCount);
        return sourceIndex >= 0
            ? row.Cells.ElementAtOrDefault(sourceIndex)
            : null;
    }

    private static int GetSourceCellIndex(
        TableShape table,
        int rowIndex,
        int columnIndex,
        int columnCount)
    {
        var row = table.Rows.ElementAtOrDefault(rowIndex);
        if (row is null || columnIndex < 0)
            return -1;

        bool compact = row.Cells.Count < columnCount
            && row.Cells.Sum(cell => Math.Max(1, cell.GridSpan)) <= columnCount;
        if (!compact)
            return columnIndex < row.Cells.Count ? columnIndex : -1;

        int currentColumn = 0;
        for (int sourceIndex = 0; sourceIndex < row.Cells.Count; sourceIndex++)
        {
            int span = Math.Max(1, row.Cells[sourceIndex].GridSpan);
            if (columnIndex >= currentColumn && columnIndex < currentColumn + span)
                return sourceIndex;

            currentColumn += span;
        }

        return -1;
    }
}

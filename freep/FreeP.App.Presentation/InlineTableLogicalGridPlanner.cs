using Free.Shared.AppServices;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record InlineTableLogicalCell(
    int RowIndex,
    int ColumnIndex,
    int SourceCellIndex,
    int ColumnSpan,
    int RowSpan,
    TableCell Cell);

public readonly record struct InlineTableRowHorizontalLayout(
    double RowWidth,
    double Offset);

public sealed record InlineTableColumnLayout(
    int ColumnIndex,
    double WidthDip,
    double TrackWidthDip);

public sealed record InlineTableRowLayout(
    int RowIndex,
    double HeightDip,
    bool UsesMinimumHeight,
    double MinimumHeightDip,
    double ContentWidthDip,
    double HorizontalOffsetDip,
    TableRowHorizontalAlignment? HorizontalAlignment);

public sealed record InlineTableCellPlacement(
    InlineTableLogicalCell LogicalCell,
    int ColumnSpan,
    int RowSpan,
    TableGridRect Bounds,
    double TrailingSpacingDip)
{
    public int RowIndex => LogicalCell.RowIndex;

    public int ColumnIndex => LogicalCell.ColumnIndex;

    public int SourceCellIndex => LogicalCell.SourceCellIndex;

    public TableCell Cell => LogicalCell.Cell;
}

public sealed class InlineTableLayoutPlan
{
    private const double EmuPerDip = 9525.0;
    private const double PointsToDip = 96.0 / 72.0;
    private const double MinimumColumnWidthDip = 24;
    private const double DefaultColumnWidthDip = 72;
    private const double MinimumRowHeightDip = 20;
    private const double DefaultRowHeightDip = 24;
    private const double MaximumIndentMagnitudeDip = 1000;

    private readonly InlineTableLogicalGridPlan _logicalGrid;

    private InlineTableLayoutPlan(
        InlineTableLogicalGridPlan logicalGrid,
        IReadOnlyList<InlineTableColumnLayout> columns,
        IReadOnlyList<InlineTableRowLayout> rows,
        IReadOnlyList<InlineTableCellPlacement> cells,
        double cellSpacingDip,
        double leftIndentDip,
        double contentWidthDip,
        double widthDip,
        double heightDip,
        double availableWidthDip,
        TableRowHorizontalAlignment? frameAlignment)
    {
        _logicalGrid = logicalGrid;
        Columns = columns;
        Rows = rows;
        Cells = cells;
        CellSpacingDip = cellSpacingDip;
        LeftIndentDip = leftIndentDip;
        ContentWidthDip = contentWidthDip;
        WidthDip = widthDip;
        HeightDip = heightDip;
        AvailableWidthDip = availableWidthDip;
        FrameAlignment = frameAlignment;
    }

    public IReadOnlyList<InlineTableColumnLayout> Columns { get; }

    public IReadOnlyList<InlineTableRowLayout> Rows { get; }

    public IReadOnlyList<InlineTableCellPlacement> Cells { get; }

    public double CellSpacingDip { get; }

    public double LeftIndentDip { get; }

    public double ContentWidthDip { get; }

    public double WidthDip { get; }

    public double HeightDip { get; }

    public double AvailableWidthDip { get; }

    public TableRowHorizontalAlignment? FrameAlignment { get; }

    public InlineTableCellPlacement? ResolveCell(int rowIndex, int columnIndex)
    {
        var logicalCell = _logicalGrid.ResolveCell(rowIndex, columnIndex);
        return logicalCell is null
            ? null
            : Cells.FirstOrDefault(cell => cell.LogicalCell.Equals(logicalCell));
    }

    public bool TryGetAdjacent(
        InlineTableCellPlacement current,
        bool backwards,
        out InlineTableCellPlacement target)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (_logicalGrid.TryGetAdjacent(current.LogicalCell, backwards, out var logicalTarget)
            && Cells.FirstOrDefault(cell => cell.LogicalCell.Equals(logicalTarget)) is { } next)
        {
            target = next;
            return true;
        }

        target = null!;
        return false;
    }

    public InlineTableCellPlacement? HitTest(double x, double y)
    {
        double rowTop = 0;
        foreach (var row in Rows)
        {
            if (y >= rowTop && y <= rowTop + row.HeightDip)
            {
                double columnLeft = LeftIndentDip + row.HorizontalOffsetDip;
                foreach (var column in Columns)
                {
                    if (x >= columnLeft && x <= columnLeft + column.WidthDip)
                        return ResolveCell(row.RowIndex, column.ColumnIndex);

                    columnLeft += column.TrackWidthDip;
                }

                return null;
            }

            rowTop += row.HeightDip;
        }

        return null;
    }

    internal static InlineTableLayoutPlan Create(
        TableShape table,
        double? availableWidthDip)
    {
        ArgumentNullException.ThrowIfNull(table);

        var logicalGrid = InlineTableLogicalGridPlan.Create(table);
        int columnCount = logicalGrid.GridCells.FirstOrDefault()?.Count ?? 1;
        int rowCount = logicalGrid.GridCells.Count;
        double spacingDip = Math.Max(
            0,
            Sanitize(table.RichTextCellSpacingPt.GetValueOrDefault()) * PointsToDip);
        double leftIndentDip = Math.Clamp(
            Sanitize(table.RichTextLeftIndentPt.GetValueOrDefault()) * PointsToDip,
            -MaximumIndentMagnitudeDip,
            MaximumIndentMagnitudeDip);

        var columns = Enumerable.Range(0, columnCount)
            .Select(columnIndex =>
            {
                double widthDip = columnIndex < table.ColumnWidthsEmu.Count
                    ? Math.Max(
                        MinimumColumnWidthDip,
                        table.ColumnWidthsEmu[columnIndex] / EmuPerDip)
                    : DefaultColumnWidthDip;
                return new InlineTableColumnLayout(
                    columnIndex,
                    widthDip,
                    widthDip + (columnIndex + 1 < columnCount ? spacingDip : 0));
            })
            .ToArray();
        double contentWidthDip = columns.Sum(column => column.TrackWidthDip);
        double widthDip = Math.Max(
            MinimumColumnWidthDip,
            contentWidthDip + Math.Max(0, leftIndentDip));
        double requestedAvailableWidthDip = availableWidthDip is { } requested
            ? Sanitize(requested)
            : widthDip;
        double effectiveAvailableWidthDip = Math.Max(widthDip, requestedAvailableWidthDip);
        double rowAvailableWidthDip = Math.Max(
            contentWidthDip,
            effectiveAvailableWidthDip - Math.Max(0, leftIndentDip));
        var baseColumnWidths = columns.Select(column => column.WidthDip).ToArray();

        var rows = new InlineTableRowLayout[rowCount];
        double heightDip = 0;
        for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            var row = table.Rows.ElementAtOrDefault(rowIndex);
            double resolvedHeightDip = row is { HeightEmu: > 0 }
                ? Math.Max(MinimumRowHeightDip, row.HeightEmu / EmuPerDip)
                : DefaultRowHeightDip;
            var horizontal = InlineTableLogicalGridPlan.ResolveRowHorizontalLayout(
                row,
                baseColumnWidths,
                rowAvailableWidthDip,
                spacingDip);
            bool usesMinimumHeight = row is
            {
                HeightEmu: > 0,
                HeightRule: TableRowHeightRule.AtLeast,
            };
            rows[rowIndex] = new InlineTableRowLayout(
                rowIndex,
                resolvedHeightDip,
                usesMinimumHeight,
                resolvedHeightDip,
                horizontal.RowWidth,
                horizontal.Offset,
                row?.HorizontalAlignment);
            heightDip += resolvedHeightDip;
        }

        var cells = new List<InlineTableCellPlacement>(logicalGrid.Cells.Count);
        foreach (var logicalCell in logicalGrid.Cells)
        {
            double x = leftIndentDip
                + rows[logicalCell.RowIndex].HorizontalOffsetDip
                + columns.Take(logicalCell.ColumnIndex).Sum(column => column.TrackWidthDip);
            double y = rows.Take(logicalCell.RowIndex).Sum(row => row.HeightDip);
            double cellWidthDip = columns
                .Skip(logicalCell.ColumnIndex)
                .Take(logicalCell.ColumnSpan)
                .Sum(column => column.WidthDip)
                + spacingDip * Math.Max(0, logicalCell.ColumnSpan - 1);
            double cellHeightDip = rows
                .Skip(logicalCell.RowIndex)
                .Take(logicalCell.RowSpan)
                .Sum(row => row.HeightDip);
            cells.Add(new InlineTableCellPlacement(
                logicalCell,
                logicalCell.ColumnSpan,
                logicalCell.RowSpan,
                new TableGridRect(x, y, cellWidthDip, cellHeightDip),
                logicalCell.ColumnIndex + logicalCell.ColumnSpan < columnCount
                    ? spacingDip
                    : 0));
        }

        return new InlineTableLayoutPlan(
            logicalGrid,
            columns,
            rows,
            cells,
            spacingDip,
            leftIndentDip,
            contentWidthDip,
            widthDip,
            Math.Max(MinimumRowHeightDip, heightDip),
            effectiveAvailableWidthDip,
            table.Rows.FirstOrDefault()?.HorizontalAlignment);
    }

    private static double Sanitize(double value) =>
        double.IsFinite(value) ? value : 0;
}

public sealed class InlineTableLogicalGridPlan
{
    private readonly InlineTableLogicalCell?[,] _owners;

    private InlineTableLogicalGridPlan(
        InlineTableLogicalCell?[,] owners,
        IReadOnlyList<InlineTableLogicalCell> cells,
        IReadOnlyList<IReadOnlyList<TableGridCell>> gridCells)
    {
        _owners = owners;
        Cells = cells;
        GridCells = gridCells;
    }

    public IReadOnlyList<InlineTableLogicalCell> Cells { get; }

    public IReadOnlyList<IReadOnlyList<TableGridCell>> GridCells { get; }

    public InlineTableLogicalCell? ResolveCell(int rowIndex, int columnIndex)
    {
        if (rowIndex < 0 || rowIndex >= _owners.GetLength(0)
            || columnIndex < 0 || columnIndex >= _owners.GetLength(1))
            return null;

        return _owners[rowIndex, columnIndex];
    }

    public bool TryGetAdjacent(
        InlineTableLogicalCell current,
        bool backwards,
        out InlineTableLogicalCell target)
    {
        int currentIndex = -1;
        for (int index = 0; index < Cells.Count; index++)
        {
            if (Cells[index].Equals(current))
            {
                currentIndex = index;
                break;
            }
        }
        int targetIndex = currentIndex + (backwards ? -1 : 1);
        if (currentIndex < 0 || targetIndex < 0 || targetIndex >= Cells.Count)
        {
            target = null!;
            return false;
        }

        target = Cells[targetIndex];
        return true;
    }

    public static InlineTableLogicalGridPlan Create(TableShape table)
    {
        ArgumentNullException.ThrowIfNull(table);

        int rowCount = Math.Max(1, table.Rows.Count);
        int columnCount = Math.Max(1, table.ColumnWidthsEmu.Count);
        var owners = new InlineTableLogicalCell?[rowCount, columnCount];
        var cells = new List<InlineTableLogicalCell>();
        var placements = new List<SourcePlacement>[rowCount];

        for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            var row = table.Rows.ElementAtOrDefault(rowIndex);
            placements[rowIndex] = BuildPlacements(row, columnCount);
        }

        for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            foreach (var placement in placements[rowIndex])
            {
                if (placement.Cell.HMerge || placement.Cell.VMerge)
                    continue;

                var logicalCell = new InlineTableLogicalCell(
                    rowIndex,
                    placement.ColumnIndex,
                    placement.SourceCellIndex,
                    placement.GridSpan,
                    Math.Min(
                        Math.Max(1, placement.Cell.RowSpan),
                        rowCount - rowIndex),
                    placement.Cell);
                cells.Add(logicalCell);

                for (int coveredRow = rowIndex;
                     coveredRow < rowIndex + logicalCell.RowSpan;
                     coveredRow++)
                {
                    for (int coveredColumn = placement.ColumnIndex;
                         coveredColumn < placement.ColumnIndex + logicalCell.ColumnSpan
                         && coveredColumn < columnCount;
                         coveredColumn++)
                    {
                        owners[coveredRow, coveredColumn] ??= logicalCell;
                    }
                }
            }
        }

        // Explicit continuation cells have no anchor of their own. Resolve any
        // remaining slots from their nearest owner above or to the left.
        for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            foreach (var placement in placements[rowIndex])
            {
                if (!placement.Cell.HMerge && !placement.Cell.VMerge)
                    continue;

                for (int coveredColumn = placement.ColumnIndex;
                     coveredColumn < placement.ColumnIndex + placement.GridSpan
                     && coveredColumn < columnCount;
                     coveredColumn++)
                {
                    if (owners[rowIndex, coveredColumn] is not null)
                        continue;

                    owners[rowIndex, coveredColumn] = FindContinuationOwner(
                        owners,
                        rowIndex,
                        coveredColumn,
                        placement.Cell.HMerge,
                        placement.Cell.VMerge);
                }
            }
        }

        var gridCells = new List<IReadOnlyList<TableGridCell>>(rowCount);
        for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            var row = new TableGridCell[columnCount];
            for (int columnIndex = 0; columnIndex < columnCount; columnIndex++)
            {
                var owner = owners[rowIndex, columnIndex];
                row[columnIndex] = owner is null
                    ? new TableGridCell(1, 1, false, false)
                    : owner.RowIndex < rowIndex
                        ? new TableGridCell(1, 1, false, true)
                        : owner.ColumnIndex < columnIndex
                            ? new TableGridCell(1, 1, true, false)
                            : new TableGridCell(
                                owner.ColumnSpan,
                                owner.RowSpan,
                                false,
                                false);
            }

            gridCells.Add(row);
        }

        return new InlineTableLogicalGridPlan(owners, cells, gridCells);
    }

    public static InlineTableLayoutPlan CreateLayout(
        TableShape table,
        double? availableWidthDip = null) =>
        InlineTableLayoutPlan.Create(table, availableWidthDip);

    public static TableRow CreateAppendRow(TableShape table)
    {
        ArgumentNullException.ThrowIfNull(table);

        var row = new TableRow
        {
            HeightEmu = table.Rows.LastOrDefault()?.HeightEmu ?? 0,
            HeightRule = table.Rows.LastOrDefault()?.HeightRule ?? TableRowHeightRule.AtLeast,
            HorizontalAlignment = table.Rows.LastOrDefault()?.HorizontalAlignment,
        };
        int columnCount = Math.Max(1, table.ColumnWidthsEmu.Count);
        for (int columnIndex = 0; columnIndex < columnCount; columnIndex++)
            row.Cells.Add(new TableCell { TextBody = new TextBody() });

        return row;
    }

    public static InlineTableRowHorizontalLayout ResolveRowHorizontalLayout(
        TableRow? row,
        IReadOnlyList<double> columnWidths,
        double availableWidth,
        double cellSpacing = 0)
    {
        ArgumentNullException.ThrowIfNull(columnWidths);

        double normalizedSpacing = Math.Max(
            0,
            double.IsFinite(cellSpacing) ? cellSpacing : 0);
        int lastCoveredColumn = -1;
        if (row is null)
        {
            lastCoveredColumn = columnWidths.Count - 1;
        }
        else
        {
            foreach (var placement in BuildPlacements(row, columnWidths.Count))
                lastCoveredColumn = Math.Max(
                    lastCoveredColumn,
                    placement.ColumnIndex + placement.GridSpan - 1);
        }

        double rowWidth = columnWidths
            .Take(lastCoveredColumn + 1)
            .Sum(width => Math.Max(0, double.IsFinite(width) ? width : 0))
            + normalizedSpacing * Math.Max(0, lastCoveredColumn);
        double normalizedAvailableWidth = Math.Max(
            0,
            double.IsFinite(availableWidth) ? availableWidth : 0);
        double extra = Math.Max(0, normalizedAvailableWidth - rowWidth);
        double offset = row?.HorizontalAlignment switch
        {
            TableRowHorizontalAlignment.Center => extra / 2,
            TableRowHorizontalAlignment.Right => extra,
            _ => 0,
        };
        return new InlineTableRowHorizontalLayout(rowWidth, offset);
    }

    private static List<SourcePlacement> BuildPlacements(
        TableRow? row,
        int columnCount)
    {
        var result = new List<SourcePlacement>();
        if (row is null)
            return result;

        bool compact = row.Cells.Count < columnCount
            && row.Cells.Sum(cell => (long)Math.Max(1, cell.GridSpan)) <= columnCount;
        int compactColumn = 0;
        for (int sourceCellIndex = 0; sourceCellIndex < row.Cells.Count; sourceCellIndex++)
        {
            var cell = row.Cells[sourceCellIndex];
            int columnIndex = compact ? compactColumn : sourceCellIndex;
            if (columnIndex >= columnCount)
                break;

            int gridSpan = Math.Min(
                Math.Max(1, cell.GridSpan),
                columnCount - columnIndex);
            result.Add(new SourcePlacement(
                sourceCellIndex,
                columnIndex,
                gridSpan,
                cell));
            if (compact)
                compactColumn += gridSpan;
        }

        return result;
    }

    private static InlineTableLogicalCell? FindContinuationOwner(
        InlineTableLogicalCell?[,] owners,
        int rowIndex,
        int columnIndex,
        bool horizontal,
        bool vertical)
    {
        if (vertical)
        {
            for (int candidateRow = rowIndex - 1; candidateRow >= 0; candidateRow--)
            {
                if (owners[candidateRow, columnIndex] is { } owner)
                    return owner;
            }
        }

        if (horizontal)
        {
            for (int candidateColumn = columnIndex - 1; candidateColumn >= 0; candidateColumn--)
            {
                if (owners[rowIndex, candidateColumn] is { } owner)
                    return owner;
            }
        }

        return null;
    }

    private sealed record SourcePlacement(
        int SourceCellIndex,
        int ColumnIndex,
        int GridSpan,
        TableCell Cell);
}

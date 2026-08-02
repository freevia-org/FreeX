using Free.Shared.AppServices;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record InlineTableLogicalCell(
    int RowIndex,
    int ColumnIndex,
    int SourceCellIndex,
    TableCell Cell);

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
                    placement.Cell);
                cells.Add(logicalCell);

                int rowSpan = Math.Min(
                    Math.Max(1, placement.Cell.RowSpan),
                    rowCount - rowIndex);
                for (int coveredRow = rowIndex;
                     coveredRow < rowIndex + rowSpan;
                     coveredRow++)
                {
                    for (int coveredColumn = placement.ColumnIndex;
                         coveredColumn < placement.ColumnIndex + placement.GridSpan
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
                                Math.Max(1, owner.Cell.GridSpan),
                                Math.Max(1, owner.Cell.RowSpan),
                                false,
                                false);
            }

            gridCells.Add(row);
        }

        return new InlineTableLogicalGridPlan(owners, cells, gridCells);
    }

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

    private static List<SourcePlacement> BuildPlacements(
        TableRow? row,
        int columnCount)
    {
        var result = new List<SourcePlacement>();
        if (row is null)
            return result;

        bool compact = row.Cells.Count < columnCount
            && row.Cells.Sum(cell => Math.Max(1, cell.GridSpan)) <= columnCount;
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

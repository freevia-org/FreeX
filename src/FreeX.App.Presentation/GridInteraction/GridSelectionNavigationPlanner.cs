using System.Collections;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.GridInteraction;

public enum GridSelectionCycleKey
{
    Enter,
    Tab
}

public readonly record struct GridSelectionCyclePlan(
    CellAddress Target,
    int SourceAreaIndex,
    int TargetAreaIndex,
    bool WrappedWithinArea,
    bool MovedToAnotherArea,
    bool WrappedAcrossSelection);

/// <summary>
/// Portable selection policy for Enter/Tab cycling, whole-row/column construction, disjoint-area
/// accumulation, and the live Name Box projection shown during a drag. Native hosts retain key
/// conversion, focus, scrolling, redraw, and application of the resulting selection plan.
/// </summary>
public static class GridSelectionNavigationPlanner
{
    public static GridSelectionCyclePlan? PlanCycle(
        Sheet? sheet,
        GridRange? primaryRange,
        IReadOnlyList<GridRange>? selectedAreas,
        CellAddress current,
        GridSelectionCycleKey key,
        bool forward)
    {
        IReadOnlyList<GridRange>? areas = selectedAreas is { Count: > 0 }
            ? selectedAreas
            : primaryRange is { } range
                ? [range]
                : null;
        if (areas is null || !IsEligibleForCycle(sheet, areas))
            return null;

        var sourceAreaIndex = FindContainingAreaIndex(areas, current);
        if (sourceAreaIndex < 0)
            sourceAreaIndex = areas.Count - 1;

        var target = AdvanceWithinArea(
            areas[sourceAreaIndex],
            current,
            key,
            forward,
            out var wrappedWithinArea);
        var targetAreaIndex = sourceAreaIndex;
        var movedToAnotherArea = false;
        var wrappedAcrossSelection = false;

        if (wrappedWithinArea && areas.Count > 1)
        {
            targetAreaIndex = WrapIndex(sourceAreaIndex + (forward ? 1 : -1), areas.Count);
            target = forward ? areas[targetAreaIndex].Start : areas[targetAreaIndex].End;
            movedToAnotherArea = true;
            wrappedAcrossSelection = forward
                ? sourceAreaIndex == areas.Count - 1
                : sourceAreaIndex == 0;
        }

        return new GridSelectionCyclePlan(
            target,
            sourceAreaIndex,
            targetAreaIndex,
            wrappedWithinArea,
            movedToAnotherArea,
            wrappedAcrossSelection);
    }

    public static GridRange CreateWholeRowsRange(SheetId sheetId, uint anchorRow, uint targetRow) =>
        new(
            new CellAddress(sheetId, Math.Min(anchorRow, targetRow), 1),
            new CellAddress(sheetId, Math.Max(anchorRow, targetRow), CellAddress.MaxCol));

    public static GridRange CreateWholeColumnsRange(SheetId sheetId, uint anchorCol, uint targetCol) =>
        new(
            new CellAddress(sheetId, 1, Math.Min(anchorCol, targetCol)),
            new CellAddress(sheetId, CellAddress.MaxRow, Math.Max(anchorCol, targetCol)));

    public static GridRange CreateWholeGridRange(SheetId sheetId) =>
        new(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, CellAddress.MaxRow, CellAddress.MaxCol));

    public static IReadOnlyList<GridRange> AppendDisjointSelectionArea(
        IReadOnlyList<GridRange>? selectedAreas,
        GridRange? currentPrimaryRange,
        GridRange newArea) =>
        UpdateDisjointSelectionAreas(selectedAreas, currentPrimaryRange, newArea, startNewArea: true);

    /// <summary>
    /// Starts a fresh disjoint area or replaces the last area while a Ctrl+drag continues. The
    /// returned list remains mutable behind its read-only contract so repeated drag updates avoid
    /// allocating and copying the complete area list on every pointer move.
    /// </summary>
    public static IReadOnlyList<GridRange> UpdateDisjointSelectionAreas(
        IReadOnlyList<GridRange>? selectedAreas,
        GridRange? currentPrimaryRange,
        GridRange activeArea,
        bool startNewArea)
    {
        var areas = selectedAreas as MutableSelectionAreas ??
            (selectedAreas is { Count: > 0 }
                ? new MutableSelectionAreas(selectedAreas)
                : currentPrimaryRange is { } seed
                    ? new MutableSelectionAreas(seed)
                    : new MutableSelectionAreas([]));

        if (!startNewArea && areas.Count > 0)
            areas.ReplaceLast(activeArea);
        else
            areas.Add(activeArea);

        return areas;
    }

    public static string FormatDragDimensionText(GridRange range)
    {
        var rowCount = range.End.Row - range.Start.Row + 1;
        var colCount = range.End.Col - range.Start.Col + 1;
        return $"{rowCount}R x {colCount}C";
    }

    private static bool IsEligibleForCycle(Sheet? sheet, IReadOnlyList<GridRange> areas) =>
        areas.Count > 1 ||
        (areas.Count == 1 &&
         areas[0].Start != areas[0].End &&
         !IsSingleMergedCellRange(sheet, areas[0]));

    private static bool IsSingleMergedCellRange(Sheet? sheet, GridRange range) =>
        sheet is { MergedRegions.Count: > 0 } &&
        sheet.GetMergeRegion(range.Start) is { } merge &&
        merge.Start == range.Start &&
        merge.End == range.End;

    private static int FindContainingAreaIndex(IReadOnlyList<GridRange> areas, CellAddress cell)
    {
        for (var index = 0; index < areas.Count; index++)
        {
            if (areas[index].Contains(cell))
                return index;
        }

        return -1;
    }

    private static CellAddress AdvanceWithinArea(
        GridRange range,
        CellAddress current,
        GridSelectionCycleKey key,
        bool forward,
        out bool wrappedPastEnd)
    {
        var minRow = range.Start.Row;
        var maxRow = range.End.Row;
        var minCol = range.Start.Col;
        var maxCol = range.End.Col;
        var row = Math.Clamp(current.Row, minRow, maxRow);
        var col = Math.Clamp(current.Col, minCol, maxCol);

        wrappedPastEnd = forward
            ? row == maxRow && col == maxCol
            : row == minRow && col == minCol;

        if (key == GridSelectionCycleKey.Tab)
        {
            if (forward)
            {
                if (col < maxCol)
                    col++;
                else
                {
                    col = minCol;
                    row = row < maxRow ? row + 1 : minRow;
                }
            }
            else if (col > minCol)
            {
                col--;
            }
            else
            {
                col = maxCol;
                row = row > minRow ? row - 1 : maxRow;
            }
        }
        else if (forward)
        {
            if (row < maxRow)
                row++;
            else
            {
                row = minRow;
                col = col < maxCol ? col + 1 : minCol;
            }
        }
        else if (row > minRow)
        {
            row--;
        }
        else
        {
            row = maxRow;
            col = col > minCol ? col - 1 : maxCol;
        }

        return new CellAddress(range.Start.Sheet, row, col);
    }

    private static int WrapIndex(int index, int count) => ((index % count) + count) % count;

    private sealed class MutableSelectionAreas : IReadOnlyList<GridRange>
    {
        private GridRange[] _areas;

        public MutableSelectionAreas(GridRange area)
        {
            _areas = [area];
            Count = 1;
        }

        public MutableSelectionAreas(IReadOnlyList<GridRange> areas)
        {
            Count = areas.Count;
            _areas = new GridRange[Math.Max(Count, 1)];
            for (var index = 0; index < Count; index++)
                _areas[index] = areas[index];
        }

        public int Count { get; private set; }

        public GridRange this[int index] => _areas[index];

        public void ReplaceLast(GridRange area)
        {
            if (Count == 0)
            {
                Add(area);
                return;
            }

            _areas[Count - 1] = area;
        }

        public void Add(GridRange area)
        {
            if (Count == _areas.Length)
                Array.Resize(ref _areas, Math.Max(Count * 2, 1));

            _areas[Count++] = area;
        }

        public IEnumerator<GridRange> GetEnumerator()
        {
            for (var index = 0; index < Count; index++)
                yield return _areas[index];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

using FreeX.Core.Model;

namespace FreeX.App.Presentation;

public readonly record struct SelectionNavigationPlan(
    CellAddress Target,
    int SourceAreaIndex,
    int TargetAreaIndex,
    bool CrossedAreaBoundary);

/// <summary>
/// Plans active-cell movement that must preserve an existing rectangular or multi-area selection.
/// Renderers apply the returned target using their toolkit-specific selection surface.
/// </summary>
public static class SelectionNavigationPlanner
{
    /// <summary>
    /// Plans Excel-style Enter/Tab movement inside the selected areas. A single multi-cell area wraps
    /// within itself; multiple areas continue in selection order after the current area is exhausted.
    /// A lone merged region is treated as one logical cell and is therefore not handled here.
    /// </summary>
    public static bool TryAdvanceWithinSelection(
        IReadOnlyList<GridRange>? areas,
        Sheet? sheet,
        CellAddress current,
        bool isTab,
        bool forward,
        out SelectionNavigationPlan plan)
    {
        plan = default;
        if (areas is not { Count: > 0 })
            return false;

        if (areas.Count == 1)
        {
            var onlyArea = areas[0];
            if (onlyArea.Start == onlyArea.End || IsSingleMergedCellRange(sheet, onlyArea))
                return false;
        }

        var sourceAreaIndex = FindContainingAreaIndex(areas, current);
        if (sourceAreaIndex < 0)
            sourceAreaIndex = areas.Count - 1;

        var target = AdvanceWithinRange(
            areas[sourceAreaIndex],
            current,
            isTab,
            forward,
            out var wrappedPastAreaEnd);
        var targetAreaIndex = sourceAreaIndex;

        if (wrappedPastAreaEnd && areas.Count > 1)
        {
            var areaStep = forward ? 1 : -1;
            targetAreaIndex = ((sourceAreaIndex + areaStep) % areas.Count + areas.Count) % areas.Count;
            var targetArea = areas[targetAreaIndex];
            target = forward ? targetArea.Start : targetArea.End;
        }

        plan = new SelectionNavigationPlan(
            target,
            sourceAreaIndex,
            targetAreaIndex,
            targetAreaIndex != sourceAreaIndex);
        return true;
    }

    public static CellAddress GetNextCorner(GridRange range, CellAddress current)
    {
        var corners = GetUniqueCorners(range);
        var index = corners.IndexOf(current);
        return index < 0 ? range.Start : corners[(index + 1) % corners.Count];
    }

    private static CellAddress AdvanceWithinRange(
        GridRange range,
        CellAddress current,
        bool isTab,
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

        if (isTab)
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

        return new CellAddress(current.Sheet, row, col);
    }

    private static int FindContainingAreaIndex(IReadOnlyList<GridRange> areas, CellAddress cell)
    {
        for (var index = 0; index < areas.Count; index++)
        {
            if (areas[index].Contains(cell))
                return index;
        }

        return -1;
    }

    private static bool IsSingleMergedCellRange(Sheet? sheet, GridRange range) =>
        sheet is { MergedRegions.Count: > 0 } &&
        sheet.GetMergeRegion(range.Start) is { } merge &&
        merge.Start == range.Start &&
        merge.End == range.End;

    private static List<CellAddress> GetUniqueCorners(GridRange range)
    {
        var ordered = new[]
        {
            range.Start,
            new CellAddress(range.Start.Sheet, range.Start.Row, range.End.Col),
            range.End,
            new CellAddress(range.Start.Sheet, range.End.Row, range.Start.Col),
        };

        var corners = new List<CellAddress>(4);
        foreach (var corner in ordered)
        {
            if (!corners.Contains(corner))
                corners.Add(corner);
        }

        return corners;
    }
}

using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

public enum PageBreakMenuAction
{
    Insert,
    Remove,
    ResetAll,
}

/// <summary>
/// The new manual page-break sets to write after an Insert / Remove / Reset action, plus a short
/// human-readable status describing what changed. Row and column break collections are 1-based and
/// already de-duplicated and sorted ascending.
/// </summary>
public sealed record PageBreakActionPlan(
    IReadOnlyList<uint> RowBreaks,
    IReadOnlyList<uint> ColumnBreaks,
    string Status);

/// <summary>
/// Portable planner for the Page Layout ▸ Breaks menu actions (Insert Page Break, Remove Page Break,
/// Reset All Page Breaks). It mirrors Excel: inserting at the active cell adds a horizontal break above
/// the cell's row and a vertical break to the left of its column (skipping breaks at the very top-left
/// edge, which Excel cannot place); removing clears the breaks adjacent to the active cell; resetting
/// clears every manual break. Pure data in, pure data out — the host writes the resulting break sets
/// back through its Core page-break command. No view-framework or host types.
/// </summary>
public static class PageBreakActionPlanner
{
    public static PageBreakActionPlan Plan(
        PageBreakMenuAction action,
        GridRange selection,
        IEnumerable<uint> currentRowBreaks,
        IEnumerable<uint> currentColumnBreaks) =>
        action switch
        {
            PageBreakMenuAction.Insert => Insert(selection, currentRowBreaks, currentColumnBreaks),
            PageBreakMenuAction.Remove => Remove(selection, currentRowBreaks, currentColumnBreaks),
            _ => ResetAll(),
        };

    /// <summary>
    /// Inserts a page break at the active cell: a horizontal break above its row (when the row is below
    /// the first) and a vertical break to the left of its column (when the column is past the first).
    /// </summary>
    public static PageBreakActionPlan Insert(
        CellAddress active,
        IReadOnlyCollection<uint> currentRowBreaks,
        IReadOnlyCollection<uint> currentColumnBreaks) =>
        Insert(new GridRange(active, active), currentRowBreaks, currentColumnBreaks);

    public static PageBreakActionPlan Insert(
        GridRange selection,
        IEnumerable<uint> currentRowBreaks,
        IEnumerable<uint> currentColumnBreaks)
    {
        var rows = new SortedSet<uint>(currentRowBreaks ?? []);
        var columns = new SortedSet<uint>(currentColumnBreaks ?? []);
        var plan = PageBreakSelectionPlanner.Insert(selection, rows, columns);

        var addedRow = HasAddedBreak(rows, plan.RowBreaks);
        var addedColumn = HasAddedBreak(columns, plan.ColumnBreaks);

        var status = (addedRow, addedColumn) switch
        {
            (true, true) => "Inserted page breaks",
            (true, false) => "Inserted a page break above the row",
            (false, true) => "Inserted a page break left of the column",
            _ => "No page break to insert at the top-left corner",
        };

        return new PageBreakActionPlan(plan.RowBreaks, plan.ColumnBreaks, status);
    }

    /// <summary>
    /// Removes the page break(s) adjacent to the active cell: the horizontal break above its row and the
    /// vertical break to its left, whichever are present.
    /// </summary>
    public static PageBreakActionPlan Remove(
        CellAddress active,
        IReadOnlyCollection<uint> currentRowBreaks,
        IReadOnlyCollection<uint> currentColumnBreaks) =>
        Remove(new GridRange(active, active), currentRowBreaks, currentColumnBreaks);

    public static PageBreakActionPlan Remove(
        GridRange selection,
        IEnumerable<uint> currentRowBreaks,
        IEnumerable<uint> currentColumnBreaks)
    {
        var rows = new SortedSet<uint>(currentRowBreaks ?? []);
        var columns = new SortedSet<uint>(currentColumnBreaks ?? []);
        var plan = PageBreakSelectionPlanner.Remove(selection, rows, columns);

        var removedRow = HasRemovedBreak(rows, plan.RowBreaks);
        var removedColumn = HasRemovedBreak(columns, plan.ColumnBreaks);

        var status = removedRow || removedColumn
            ? "Removed page break"
            : "No page break next to the selection";

        return new PageBreakActionPlan(plan.RowBreaks, plan.ColumnBreaks, status);
    }

    /// <summary>Clears every manual page break on the sheet.</summary>
    public static PageBreakActionPlan ResetAll() =>
        new([], [], "Reset all page breaks");

    private static bool HasAddedBreak(SortedSet<uint> before, IReadOnlyList<uint> after) =>
        after.Any(value => !before.Contains(value));

    private static bool HasRemovedBreak(SortedSet<uint> before, IReadOnlyList<uint> after)
    {
        var afterSet = new HashSet<uint>(after);
        return before.Any(value => !afterSet.Contains(value));
    }
}

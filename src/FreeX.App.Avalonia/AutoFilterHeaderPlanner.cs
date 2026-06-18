using FreeX.App.Presentation.AutoFilter;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// UI-free planner deciding which header cells show a filter-dropdown button when the active sheet has an
/// AutoFilter. Resolves the AutoFilter range from a worksheet-level <c>&lt;autoFilter&gt;</c> reference or
/// the first filtered structured table (mirroring the desktop host's <c>AutoFilterDropdownPlanner</c>'s
/// range resolution), then yields one button cell per column of that range's header row. Portable (Core
/// model only) so the resolution is unit testable; the Avalonia grid renders a button at each returned cell.
/// </summary>
internal static class AutoFilterHeaderPlanner
{
    /// <summary>
    /// The AutoFilter range on <paramref name="sheet"/>, or null when none is active. A worksheet-level
    /// AutoFilter takes precedence; otherwise the first structured table whose <see cref="StructuredTableModel.HasAutoFilter"/>
    /// is set supplies the range so its header still shows filter arrows.
    /// </summary>
    internal static GridRange? TryGetAutoFilterRange(Sheet sheet) =>
        AutoFilterRangeResolver.TryGetAutoFilterRange(sheet);

    /// <summary>
    /// The header cells that should show a filter-dropdown button — one per column across the AutoFilter
    /// range's header row (its first row). Empty when no AutoFilter is active.
    /// </summary>
    internal static IReadOnlyList<CellAddress> GetHeaderButtonCells(Sheet sheet)
    {
        if (TryGetAutoFilterRange(sheet) is not { } range)
            return [];

        var headerRow = range.Start.Row;
        var cells = new List<CellAddress>((int)range.ColCount);
        for (var col = range.Start.Col; col <= range.End.Col; col++)
            cells.Add(new CellAddress(sheet.Id, headerRow, col));

        return cells;
    }

    /// <summary>True when the given cell is a header cell that should show a filter-dropdown button.</summary>
    internal static bool IsFilterButtonCell(Sheet sheet, uint row, uint col)
    {
        if (TryGetAutoFilterRange(sheet) is not { } range)
            return false;

        return row == range.Start.Row && col >= range.Start.Col && col <= range.End.Col;
    }
}

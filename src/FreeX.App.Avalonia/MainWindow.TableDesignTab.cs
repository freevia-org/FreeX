using Free.Shared.Ribbon;
using FreeX.App.Presentation.TableUI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// Real handlers for the contextual Table Design tab (activation key "table.active"). Each handler finds
/// the structured table that contains the active cell and runs the shared, platform-neutral Core command
/// that mutates it — the same command logic the WPF host's Table Design tab runs. The six Style Options
/// toggles (Total Row, First Column, Last Column, Banded Rows, Banded Columns, Filter Button) and Convert
/// to Range are wired here; if the active cell is not inside a table, each reports an honest status line.
/// </summary>
/// <remarks>
/// The six Style Options toggles render their checked state via <see cref="GetTableStyleOptionRibbonState"/>,
/// registered against their canonical command ids ("Total Row", "First Column", etc.) in MainWindow.cs's
/// AvaloniaRibbonHostCallbacks.ExtraCommandStates -- the same StatefulRelayRibbonCommand/SyncToggleStates
/// mechanism the View-tab checkboxes use -- so the toggle buttons reflect the active table's real
/// TotalsRowShown/ShowFirstColumn/etc. flags, matching the WPF host's
/// <c>_ribbonState.SetChecked(...)</c> calls in RefreshTableContextualTab. The style repaint itself is
/// delegated to the Core <see cref="ReapplyStructuredTableStyleCommand"/>, which captures the table's
/// current banding (<see cref="StructuredTableStyleBanding.CaptureCurrent"/>) so there is no dependency on
/// the WPF-only TableStyleGalleryPlanner. Convert to Range removes the structured table via
/// <see cref="ConvertStructuredTableToRangeCommand"/> (no confirmation dialog in this Phase; the WPF host
/// asks Yes/No first).
/// </remarks>
public sealed partial class MainWindow
{
    /// <summary>
    /// Finds the structured table on the active sheet that contains the active cell. When several tables
    /// overlap the cell (they should not, but be defensive), the smallest-area table wins — mirroring the
    /// WPF host's <c>TryGetActiveStructuredTable</c>.
    /// </summary>
    private bool TryGetActiveStructuredTable(out StructuredTableModel table)
    {
        table = null!;
        if (_selectedDrawingObjectKind is not null)
            return false;

        var sheet = _session.ActiveSheet;
        var activeCell = _session.ActiveCell;

        return TableDesignCommandPlanner.TryGetActiveStructuredTable(sheet, activeCell, out table);
    }

    /// <summary>Whether the active cell currently sits inside a structured table (drives "table.active").</summary>
    private bool IsActiveCellInStructuredTable() => TryGetActiveStructuredTable(out _);

    /// <summary>
    /// Recomputes the Table Design contextual-tab visibility from the current selection and raises the
    /// signal on the ribbon context source. Call this after any selection/navigation change and after any
    /// table mutation (insert / convert-to-range / resize) that can change whether the active cell is in a
    /// table. Cheap and idempotent: the source only fires ContextChanged when the flag actually flips.
    /// </summary>
    private void RefreshTableContextualTab()
        => _ribbonContextSource.OnTableActive(IsActiveCellInStructuredTable());

    // --- Convert to Range --------------------------------------------------------------------------

    /// <summary>Table Design ▸ Tools ▸ Convert to Range: drops the structured table, leaving a plain range.</summary>
    private void ConvertActiveTableToRange()
    {
        if (!TryGetActiveStructuredTable(out var table))
        {
            RefreshShell(UiText.Get("TableLoc_ConvertToRangeNotInTable"));
            return;
        }

        var plan = TableDesignCommandPlanner.BuildConvertToRangePlan(_session.ActiveSheet.Id, table);
        var result = _session.ExecuteReviewCommand(plan.Command);

        if (result.Success)
        {
            // The table is gone, so the contextual tab must retract.
            RefreshTableContextualTab();
            RefreshShell(UiText.Format("TableLoc_ConvertedTableToRange", plan.TableDisplayName));
        }
        else
        {
            RefreshShell(result.ErrorMessage ?? UiText.Get("TableLoc_ConvertToRangeFailed"));
        }
    }

    // --- Style Options toggles ---------------------------------------------------------------------

    private void ToggleActiveTableTotalRow()
        => ToggleActiveTableTotalsRow(table => !table.TotalsRowShown);

    private void ToggleActiveTableFirstColumn()
        => ApplyActiveTableStyleOption(UiText.Get("TableLoc_StyleOptionFirstColumn"), t => !t.ShowFirstColumn, showFirstColumn: true);

    private void ToggleActiveTableLastColumn()
        => ApplyActiveTableStyleOption(UiText.Get("TableLoc_StyleOptionLastColumn"), t => !t.ShowLastColumn, showLastColumn: true);

    private void ToggleActiveTableBandedRows()
        => ApplyActiveTableStyleOption(UiText.Get("TableLoc_StyleOptionBandedRows"), t => !t.ShowRowStripes, showRowStripes: true);

    private void ToggleActiveTableBandedColumns()
        => ApplyActiveTableStyleOption(UiText.Get("TableLoc_StyleOptionBandedColumns"), t => !t.ShowColumnStripes, showColumnStripes: true);

    private void ToggleActiveTableFilterButton()
        => ApplyActiveTableStyleOption(UiText.Get("TableLoc_StyleOptionFilterButton"), t => !t.HasAutoFilter, hasAutoFilter: true);

    /// <summary>
    /// Toggles the Total Row on the active cell's table. Unlike the four banding/filter flags (which live in
    /// the table's style options), showing/hiding the totals row inserts or deletes a worksheet row, so it
    /// runs the dedicated <see cref="SetStructuredTableTotalsRowCommand"/> and then reapplies the banding so
    /// the new (or removed) totals row paints consistently.
    /// </summary>
    private void ToggleActiveTableTotalsRow(Func<StructuredTableModel, bool> nextValue)
    {
        if (!TryGetActiveStructuredTable(out var table))
        {
            RefreshShell(UiText.Get("TableLoc_TotalRowNotInTable"));
            return;
        }

        var show = nextValue(table);
        var sheetId = _session.ActiveSheet.Id;
        var command = TableDesignCommandPlanner.BuildStyleOptionsCommand(
            sheetId,
            table,
            _session.Workbook.Theme,
            totalsRowShown: show);
        if (command is null)
            return;

        var result = _session.ExecuteReviewCommand(command);
        if (result.Success)
        {
            RefreshTableContextualTab();
            RefreshShell(UiText.Format(
                show ? "TableLoc_TotalRowOnForTable" : "TableLoc_TotalRowOffForTable",
                TableDisplayName(table)));
        }
        else
        {
            RefreshShell(result.ErrorMessage ?? UiText.Get("TableLoc_TotalRowFailed"));
        }
    }

    /// <summary>
    /// Flips one of the four style-option flags (first/last column, banded rows/columns) or the filter
    /// button on the active cell's table by running <see cref="ReapplyStructuredTableStyleCommand"/> with the
    /// single changed flag. The command captures the table's current banding internally, so the table
    /// repaints with the toggled option applied.
    /// </summary>
    private void ApplyActiveTableStyleOption(
        string label,
        Func<StructuredTableModel, bool> nextValue,
        bool showFirstColumn = false,
        bool showLastColumn = false,
        bool showRowStripes = false,
        bool showColumnStripes = false,
        bool hasAutoFilter = false)
    {
        if (!TryGetActiveStructuredTable(out var table))
        {
            RefreshShell(UiText.Format("TableLoc_StyleOptionNotInTable", label));
            return;
        }

        var value = nextValue(table);
        var sheetId = _session.ActiveSheet.Id;
        var command = TableDesignCommandPlanner.BuildStyleOptionsCommand(
            sheetId,
            table,
            _session.Workbook.Theme,
            showFirstColumn: showFirstColumn ? value : null,
            showLastColumn: showLastColumn ? value : null,
            showRowStripes: showRowStripes ? value : null,
            showColumnStripes: showColumnStripes ? value : null,
            hasAutoFilter: hasAutoFilter ? value : null);
        if (command is null)
            return;

        var result = _session.ExecuteReviewCommand(command);
        if (result.Success)
            RefreshShell(UiText.Format(
                value ? "TableLoc_StyleOptionOnForTable" : "TableLoc_StyleOptionOffForTable",
                label,
                TableDisplayName(table)));
        else
            RefreshShell(result.ErrorMessage ?? UiText.Format("TableLoc_StyleOptionFailed", label));
    }

    private static string TableDisplayName(StructuredTableModel table)
        => TableDesignCommandPlanner.GetDisplayName(table);

    // --- Style Options toggle checked-state (ribbon parity) --------------------------------------

    /// <summary>
    /// Render-time <see cref="RibbonCommandState"/> for one of the six Table Design ▸ Style Options
    /// toggles: checked when the active cell's table currently has the corresponding flag on, and the
    /// planner default (unchecked) when the active cell is not inside a table. Registered per-toggle in
    /// MainWindow.cs's ExtraCommandStates so <c>AvaloniaRibbonRenderer.SyncToggleStates</c> paints the
    /// pressed state on every ribbon refresh, mirroring the WPF host's
    /// <c>_ribbonState.SetChecked("Total Row", table.TotalsRowShown)</c> (and siblings) in
    /// RefreshTableContextualTab.
    /// </summary>
    private RibbonCommandState GetTableStyleOptionRibbonState(Func<StructuredTableModel, bool> flag) =>
        TryGetActiveStructuredTable(out var table)
            ? new RibbonCommandState(IsChecked: flag(table))
            : RibbonCommandState.Default;
}

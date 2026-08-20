using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal static partial class RowColumnShiftHelpers
{
    internal static void RewriteAllFormulas(
        Workbook workbook, RewriteOperation op, Dictionary<CellAddress, string> snapshot)
    {
        foreach (var sheet in workbook.Sheets)
        {
            foreach (var addr in sheet.EnumerateFormulaCells())
            {
                var cell = sheet.GetCell(addr);
                if (cell?.FormulaText is null) continue;
                var rewritten = FormulaRewriter.Rewrite(cell.FormulaText, op, sheet.Name);
                if (rewritten is null) continue;
                snapshot[addr] = cell.FormulaText;
                SetFormulaTextPreservingArrayIdentity(cell, rewritten);
            }
        }
    }

    internal static void RestoreFormulas(
        Workbook workbook, Dictionary<CellAddress, string> snapshot)
    {
        foreach (var (addr, original) in snapshot)
        {
            var s = workbook.GetSheet(addr.Sheet);
            var cell = s?.GetCell(addr.Row, addr.Col);
            if (cell is not null)
                SetFormulaTextPreservingArrayIdentity(cell, original);
        }
        snapshot.Clear();
    }

    // The FormulaText setter (Cell.cs) unconditionally resets ArrayMode/LegacyArrayRows/
    // LegacyArrayCols to "freshly authored modern formula" defaults on every assignment, on the
    // assumption that assigning FormulaText always means a user edit. RewriteAllFormulas/
    // RestoreFormulas instead reassign the SAME cell's FormulaText to adjust reference text after a
    // structural edit elsewhere (row/column/cell insert-delete, move/cut, sheet rename, table
    // rename) -- the cell itself is not being re-authored and its legacy CSE array identity (and the
    // "can't split the array" protection that depends on LegacyArrayRows/Cols being non-zero) must
    // survive unchanged. Mirrors the identical save/assign/restore done for the same reason in
    // Sheet.Clone.cs's CopyCellContentTo and in CellStateSnapshot.ToCell.
    private static void SetFormulaTextPreservingArrayIdentity(Cell cell, string? formulaText)
    {
        var arrayMode = cell.ArrayMode;
        var legacyArrayRows = cell.LegacyArrayRows;
        var legacyArrayCols = cell.LegacyArrayCols;
        cell.FormulaText = formulaText;
        cell.ArrayMode = arrayMode;
        cell.LegacyArrayRows = legacyArrayRows;
        cell.LegacyArrayCols = legacyArrayCols;
    }

    internal static IReadOnlyList<CellAddress> BuildAffectedCellsForFormulaRewrite(
        IEnumerable<CellAddress> primaryCells,
        Dictionary<CellAddress, string> formulaSnapshot)
    {
        var affected = new List<CellAddress>();
        var seen = new HashSet<CellAddress>();

        foreach (var address in primaryCells)
        {
            if (seen.Add(address))
                affected.Add(address);
        }

        foreach (var address in formulaSnapshot.Keys)
        {
            if (seen.Add(address))
                affected.Add(address);
        }

        return affected;
    }

    // R96-commands-undo-affected-cells-1: InsertRowsCommand/DeleteRowsCommand/InsertColumnsCommand/
    // DeleteColumnsCommand's Revert() physically relocates each moved/shifted formula cell back to
    // the ORIGINAL address captured in its CellStateSnapshot (the address the snapshot was taken
    // at, before Apply ever touched the sheet) -- a completely different address than the
    // POST-shift address Apply's own AffectedCells reported forward. CommandBus.Undo has no way to
    // learn Revert's true target addresses on its own (Revert returns void), so each of those four
    // commands recomputes its own post-Revert AffectedCells (exposed via IAffectedCellsCommand) by
    // feeding this into BuildAffectedCellsForFormulaRewrite as the "primary cells" for the CURRENT
    // (post-Revert) direction, mirroring how RelocatedFormulaCellsPendingDependencyRefresh in each
    // command feeds the POST-shift address for the forward (Apply) direction. Without this, a
    // formula cell that relocates back to its original address on Undo is left with NO dependency
    // graph registration at all once something (e.g. Calculate Now / F9) has rebuilt the graph from
    // current sheet occupancy in between Apply and Undo -- see WorkbookCellEditService.
    /// UpdateFormulaDependencies and RecalcEngine.RebuildFormulaDependencies.
    internal static IEnumerable<CellAddress> RelocatedFormulaCellsAtCapturedAddress(
        IEnumerable<CellStateSnapshot> snapshots, SheetId sheetId)
    {
        foreach (var snapshot in snapshots)
        {
            if (snapshot.FormulaText is null)
                continue;

            yield return snapshot.ToAddress(sheetId);
        }
    }

    // R96-commands-undo-affected-cells-2: InsertCellsCommand/DeleteCellsCommand (the band-scoped,
    // shift-direction siblings of the whole-row/whole-column commands above) capture their moved
    // cells via CellShiftSnapshot/CellShiftCapture as raw (CellAddress Address, Cell Cell) pairs
    // (Address = the ORIGINAL, pre-Apply address the cell is restored back to by
    // CellShiftSnapshot.Restore), not CellStateSnapshot -- so this overload mirrors the
    // CellStateSnapshot overload above for that representation.
    internal static IEnumerable<CellAddress> RelocatedFormulaCellsAtCapturedAddress(
        IEnumerable<(CellAddress Address, Cell Cell)> cells)
    {
        foreach (var (address, cell) in cells)
        {
            if (cell.FormulaText is null)
                continue;

            yield return address;
        }
    }
}

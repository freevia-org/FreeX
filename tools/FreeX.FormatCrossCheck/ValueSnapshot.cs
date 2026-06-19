using System.Collections.Generic;
using FreeX.Core.Model;

namespace FreeX.FormatCrossCheck;

/// <summary>
/// A values+formulas+structure snapshot of a workbook — the interop-critical core. Styles are out of
/// scope for v1 (the in-FreeX <c>FormatFidelity</c> harness already covers style ceilings); this tool
/// proves the data itself survives an external (LibreOffice) consumer. Extraction goes through the same
/// model APIs as <c>FormatFidelity.WorkbookSnapshot</c>: <c>Sheet.GetOccupiedCellMap()</c>,
/// <c>Cell.Value/HasFormula/FormulaText</c>.
/// </summary>
internal sealed class ValueSnapshot
{
    public sealed record CellEntry(ScalarValue Value, bool HasFormula, string? FormulaText);

    public sealed class SheetSnapshot
    {
        public required string Name { get; init; }
        public Dictionary<(uint Row, uint Col), CellEntry> Cells { get; } = new();
        /// <summary>
        /// Pivot-table OUTPUT regions on this sheet. The cells a pivot renders are LITERAL in the source
        /// xlsx (cached layout) but an external consumer REGENERATES them with its own default layout
        /// ("Row Labels"/"Sum of …"), shuffling addresses. Comparing those cells one-for-one would be a
        /// false defect, so the runner excludes any cell inside a pivot output range from the value diff.
        /// </summary>
        public List<((uint R, uint C) Start, (uint R, uint C) End)> PivotRanges { get; } = new();

        public bool IsInPivot(uint row, uint col)
        {
            foreach (var (start, end) in PivotRanges)
                if (row >= start.R && row <= end.R && col >= start.C && col <= end.C)
                    return true;
            return false;
        }
    }

    public List<SheetSnapshot> Sheets { get; } = new();

    public static ValueSnapshot Capture(Workbook wb)
    {
        var snap = new ValueSnapshot();
        foreach (var sheet in wb.Sheets)
        {
            var ss = new SheetSnapshot { Name = sheet.Name };
            foreach (var ((row, col), cell) in sheet.GetOccupiedCellMap())
                ss.Cells[(row, col)] = new CellEntry(cell.Value, cell.HasFormula, cell.FormulaText);

            foreach (var pivot in sheet.PivotTables)
            {
                var t = pivot.LastRenderedRange ?? pivot.TargetRange;
                ss.PivotRanges.Add((
                    (t.Start.Row, t.Start.Col),
                    (t.End.Row, t.End.Col)));
            }

            snap.Sheets.Add(ss);
        }
        return snap;
    }
}

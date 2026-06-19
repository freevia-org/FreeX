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
            snap.Sheets.Add(ss);
        }
        return snap;
    }
}

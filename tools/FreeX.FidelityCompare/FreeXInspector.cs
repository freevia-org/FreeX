using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;

internal static class FreeXInspector
{
    public static void Inspect(string path, FileResult result, bool recalc)
    {
        Workbook workbook;
        using (var stream = File.OpenRead(path))
            workbook = new XlsxFileAdapter().Load(stream);

        if (recalc)
        {
            // Compute-fidelity: recompute every formula through FreeX's engine (RecalculateAllFormulas passes
            // each cell's address as currentCell, so COLUMN()/ROW()/relative refs resolve correctly), then
            // compare FreeX's computed values to Excel's live values instead of the file's cached results.
            try
            {
                new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()).RecalculateAllFormulas(workbook);
            }
            catch (Exception ex)
            {
                result.FreeXRecalcError = $"{ex.GetType().Name}: {ex.Message}";
            }
        }

        var inv = new Inventory { Sheets = workbook.SheetCount };
        try { inv.NamedRanges = workbook.NamedRanges.Count; } catch { /* optional */ }

        for (var i = 0; i < workbook.SheetCount; i++)
        {
            var sheet = workbook.GetSheetAt(i);
            inv.Charts += sheet.Charts.Count;
            inv.PivotTables += sheet.PivotTables.Count;
            inv.ConditionalFormats += sheet.ConditionalFormats.Count;
            inv.Tables += sheet.StructuredTables.Count;
            inv.Hyperlinks += sheet.Hyperlinks.Count;
            inv.Comments += sheet.Comments.Count;
            try { inv.DataValidations += CountDataValidations(sheet); } catch { /* collection shape varies */ }

            var cells = new Dictionary<(int, int), CellVal>();
            foreach (var ((row, col), cell) in sheet.GetOccupiedCellMap())
            {
                var value = ToCellVal(cell.Value);
                if (!value.IsEmpty)
                    cells[((int)row, (int)col)] = value;
            }

            // Disambiguate duplicate sheet names defensively.
            var key = result.FreeXCells.ContainsKey(sheet.Name) ? $"{sheet.Name}#{i}" : sheet.Name;
            result.FreeXCells[key] = cells;
        }

        result.FreeX = inv;
    }

    private static int CountDataValidations(Sheet sheet)
    {
        var count = 0;
        foreach (var _ in (System.Collections.IEnumerable)sheet.DataValidations)
            count++;
        return count;
    }

    private static CellVal ToCellVal(ScalarValue value) => value switch
    {
        NumberValue n => CellVal.FromNumber(n.Value),
        TextValue t => CellVal.FromText(t.Value),
        BoolValue b => CellVal.FromBool(b.Value),
        DateTimeValue d => CellVal.FromNumber(d.Value),
        ErrorValue e => CellVal.FromError(e.ToString() ?? "#ERR"),
        _ => CellVal.Blank,
    };
}

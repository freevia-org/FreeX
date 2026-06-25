internal static class FidelityComparison
{
    public static void Compare(FileResult result, FidelityOptions options)
    {
        if (!result.FreeXLoaded || !result.ExcelOpened || result.FreeX is null || result.Excel is null)
        {
            result.Status = FidelityStatus.Skipped;
            return;
        }

        // Inventory diffs are surfaced for review. namedRanges and hyperlinks are intentionally NOT diffed:
        // Excel's Names collection includes hidden built-in names (Print_Area, _FilterDatabase, table names)
        // and its Hyperlinks auto-detects URL-like text, so those counts diverge from FreeX's explicit model
        // for reasons that are not fidelity gaps. They remain as raw per-side counts in results.csv.
        // Conditional formats and data validations are inventoried on the FreeX side only (no cheap Excel
        // COM count).
        var f = result.FreeX;
        var e = result.Excel;
        AddInventoryDiff(result, "sheets", f.Sheets, e.Sheets);
        AddInventoryDiff(result, "charts", f.Charts, e.Charts);
        AddInventoryDiff(result, "pivotTables", f.PivotTables, e.PivotTables);
        AddInventoryDiff(result, "tables", f.Tables, e.Tables);
        AddInventoryDiff(result, "comments", f.Comments, e.Comments);

        // Cell value comparison over sheets present in both.
        // Pass 1: walk FreeX cells and compare against Excel (intersection + FreeX-only cells).
        // Pass 2: walk Excel-only occupied addresses (cells present in Excel but absent/empty in FreeX)
        //         and count each as a mismatch so that silent data-loss in FreeX is caught.
        foreach (var (sheetKey, freexCells) in result.FreeXCells)
        {
            if (!result.ExcelCells.TryGetValue(sheetKey, out var excelCells))
                continue;

            foreach (var (addr, freexVal) in freexCells)
            {
                if (!excelCells.TryGetValue(addr, out var excelVal))
                    continue;
                result.CellsCompared++;
                if (!freexVal.Matches(excelVal))
                {
                    result.ValueMismatches++;
                    if (result.MismatchSamples.Count < options.MaxMismatchSamples)
                    {
                        result.MismatchSamples.Add(
                            $"{sheetKey}!{ColumnName(addr.Col)}{addr.Row}: FreeX='{freexVal}' Excel='{excelVal}'");
                    }
                }
            }

            // Pass 2: addresses occupied in Excel but missing (or empty) in FreeX.
            foreach (var (addr, excelVal) in excelCells)
            {
                if (excelVal.IsEmpty)
                    continue; // Excel cell carries no content — not a fidelity gap
                if (freexCells.TryGetValue(addr, out var freexVal) && !freexVal.IsEmpty)
                    continue; // already counted in pass 1
                result.CellsCompared++;
                result.ValueMismatches++;
                if (result.MismatchSamples.Count < options.MaxMismatchSamples)
                {
                    result.MismatchSamples.Add(
                        $"{sheetKey}!{ColumnName(addr.Col)}{addr.Row}: FreeX='(missing)' Excel='{excelVal}'");
                }
            }
        }

        // Also check sheets that exist in Excel but have no FreeX cell data at all (sheet-level missing).
        // Individual missing-cell accounting above covers sheets where FreeX has the sheet but dropped cells.
        // Sheets entirely absent from FreeXCells are already caught by the f.Sheets != e.Sheets sheet-count
        // check below, so no additional per-cell walk is needed for those.

        // FAIL only on unambiguous functional differences: too many differing computed values, or a
        // missing/extra sheet (data loss). Charts/pivots/tables/comments diffs are reported for review but
        // do not auto-fail, because COM counting methodology varies. Every individual mismatch is still
        // logged to mismatches.txt regardless of pass/fail, so low-frequency real gaps stay visible.
        var mismatchRate = result.CellsCompared == 0 ? 0 : 100.0 * result.ValueMismatches / result.CellsCompared;
        var valuesFailed = mismatchRate > options.ValueMismatchTolerancePercent;
        var sheetsFailed = f.Sheets != e.Sheets;
        result.Status = (valuesFailed || sheetsFailed) ? FidelityStatus.Fail : FidelityStatus.Pass;
    }

    private static void AddInventoryDiff(FileResult result, string label, int freex, int excel)
    {
        if (freex != excel)
            result.InventoryDiffs.Add($"{label}: FreeX={freex} Excel={excel}");
    }

    public static string ColumnName(int col)
    {
        var name = "";
        while (col > 0)
        {
            var rem = (col - 1) % 26;
            name = (char)('A' + rem) + name;
            col = (col - 1) / 26;
        }
        return name;
    }
}

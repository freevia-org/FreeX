using System.Globalization;
using System.Text;

internal static class FidelityReport
{
    public static void Write(string runDir, IReadOnlyList<FileResult> results, FidelityOptions options)
    {
        WriteCsv(Path.Combine(runDir, "results.csv"), results);
        WriteMismatches(Path.Combine(runDir, "mismatches.txt"), results);
        WriteReadme(Path.Combine(runDir, "README.md"), results, options);
    }

    private static void WriteCsv(string path, IReadOnlyList<FileResult> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("file,status,freexLoaded,excelOpened,sheetsF,sheetsE,cellsCompared,valueMismatches,mismatchPct,chartsF,chartsE,pivotsF,pivotsE,tablesF,tablesE,hyperlinksF,hyperlinksE,commentsF,commentsE,cfF,dvF,namedF,namedE,inventoryDiffs,freexError,excelError");
        foreach (var r in results)
        {
            var f = r.FreeX; var e = r.Excel;
            var pct = r.CellsCompared == 0 ? 0 : 100.0 * r.ValueMismatches / r.CellsCompared;
            sb.AppendLine(string.Join(",",
                Csv(r.File), r.Status, r.FreeXLoaded, r.ExcelOpened,
                f?.Sheets, e?.Sheets, r.CellsCompared, r.ValueMismatches, pct.ToString("0.###", CultureInfo.InvariantCulture),
                f?.Charts, e?.Charts, f?.PivotTables, e?.PivotTables, f?.Tables, e?.Tables,
                f?.Hyperlinks, e?.Hyperlinks, f?.Comments, e?.Comments,
                f?.ConditionalFormats, f?.DataValidations, f?.NamedRanges, e?.NamedRanges,
                Csv(string.Join("; ", r.InventoryDiffs)), Csv(r.FreeXError), Csv(r.ExcelError)));
        }
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    private static void WriteMismatches(string path, IReadOnlyList<FileResult> results)
    {
        var sb = new StringBuilder();
        foreach (var r in results.Where(r => r.MismatchSamples.Count > 0 || r.InventoryDiffs.Count > 0))
        {
            sb.AppendLine($"### {r.File}  [{r.Status}]");
            foreach (var d in r.InventoryDiffs) sb.AppendLine($"  inventory  {d}");
            foreach (var m in r.MismatchSamples) sb.AppendLine($"  value      {m}");
            if (r.ValueMismatches > r.MismatchSamples.Count)
                sb.AppendLine($"  ... and {r.ValueMismatches - r.MismatchSamples.Count} more value mismatch(es)");
            sb.AppendLine();
        }
        File.WriteAllText(path, sb.Length == 0 ? "No mismatches.\n" : sb.ToString(), Encoding.UTF8);
    }

    private static void WriteReadme(string path, IReadOnlyList<FileResult> results, FidelityOptions options)
    {
        var pass = results.Count(r => r.Status == FidelityStatus.Pass);
        var fail = results.Count(r => r.Status == FidelityStatus.Fail);
        var skip = results.Count(r => r.Status == FidelityStatus.Skipped);

        var sb = new StringBuilder();
        sb.AppendLine("# FreeX ↔ Excel functional fidelity run");
        sb.AppendLine();
        sb.AppendLine($"- Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"- Files: {results.Count}  →  **Pass {pass}, Fail {fail}, Skipped {skip}**");
        sb.AppendLine($"- Value-mismatch tolerance: {options.ValueMismatchTolerancePercent}% of compared cells");
        sb.AppendLine();
        sb.AppendLine("A file **passes** when FreeX and Excel both open it, the computed cell values match within");
        sb.AppendLine("tolerance, and the sheet counts agree. Chart/pivot/table/comment inventory diffs are reported");
        sb.AppendLine("for review but do not fail. Named ranges and hyperlinks are not diffed (Excel's counts include");
        sb.AppendLine("hidden built-in names and auto-detected links). Conditional formats and data validations are");
        sb.AppendLine("inventoried on the FreeX side only. Every value mismatch is logged to `mismatches.txt` even on");
        sb.AppendLine("a passing file. Visual (pixel) comparison is a planned next phase.");
        sb.AppendLine();
        sb.AppendLine("| File | Status | Cells | Mismatch% | Inventory diffs |");
        sb.AppendLine("|---|---|---:|---:|---|");
        foreach (var r in results.OrderBy(r => r.Status).ThenBy(r => r.File, StringComparer.OrdinalIgnoreCase))
        {
            var pct = r.CellsCompared == 0 ? 0 : 100.0 * r.ValueMismatches / r.CellsCompared;
            var note = r.Status == FidelityStatus.Skipped
                ? (r.FreeXError ?? r.ExcelError ?? "not openable in both")
                : (r.InventoryDiffs.Count == 0 ? "—" : string.Join("; ", r.InventoryDiffs));
            sb.AppendLine($"| {r.File} | {r.Status} | {r.CellsCompared} | {pct:0.##} | {note} |");
        }
        sb.AppendLine();
        sb.AppendLine("See `results.csv` for full per-file metrics and `mismatches.txt` for sampled value/inventory diffs.");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    private static string Csv(object? value)
    {
        var s = value?.ToString() ?? "";
        return s.Contains(',') || s.Contains('"') || s.Contains('\n')
            ? "\"" + s.Replace("\"", "\"\"") + "\""
            : s;
    }
}

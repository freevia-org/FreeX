using System.Globalization;
using System.Text;
using System.Threading;

// FreeX <-> Microsoft Excel functional fidelity batch (ON DEMAND ONLY — not wired into build/test/CI).
//
// For each workbook in the fidelity corpus it opens the file in BOTH FreeX (FreeX.Core.IO loader) and
// desktop Excel (COM automation) and compares:
//   * openability (FreeX loads without throwing; Excel opens without raising),
//   * computed/displayed cell values cell-by-cell (numbers within tolerance, text/bool exact),
//   * a feature inventory (sheets, charts, pivot tables, conditional formats, data validations, tables,
//     hyperlinks, comments).
// Results land in a timestamped run folder: a per-file CSV, a mismatch detail log, and a README summary.
//
// Visual (pixel) comparison is a planned second phase — FreeX has no headless worksheet->image API yet
// (only ChartRenderer for charts), so whole-sheet rendering needs the WPF grid hosted headlessly or a
// PDF-export + rasterizer. This batch covers the functional axis today.

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var options = FidelityOptions.Parse(args);
        if (options.ShowHelp)
        {
            FidelityOptions.WriteUsage();
            return 0;
        }

        var enUs = CultureInfo.GetCultureInfo("en-US");
        Thread.CurrentThread.CurrentCulture = enUs;
        Thread.CurrentThread.CurrentUICulture = enUs;

        var files = CorpusFiles.Resolve(options);
        // Also include the synthetic oracle workbooks that arbitrate uncertain/disputed formula semantics
        // (YEARFRAC basis-0 Feb edge cases, DATEDIF MD boundary, TEXT sign-prefix format, VDB fractional
        // periods).  These are generated fresh at the start of each run so they always reflect current
        // FreeX formula output; Excel recalculates on open and provides the ground-truth comparison.
        // Oracle files are included even when no corpus files are found (early-exit guard is skipped for them).
        var oracleDir = Path.Combine(options.CorpusRoot, "oracle-generated");
        var oracleFiles = FormulaOracleCases.GenerateOracleWorkbooks(oracleDir);
        var allFiles = files.Concat(oracleFiles.Where(f => options.Filter is null ||
            Path.GetFileName(f).Contains(options.Filter, StringComparison.OrdinalIgnoreCase))).ToList();
        if (allFiles.Count == 0)
        {
            Console.Error.WriteLine($"No corpus files found under '{options.FilesDirectory}' and no oracle cases generated. Run tools/Fetch-FidelityCorpus.ps1 first.");
            return 2;
        }
        files = allFiles;

        var runDir = options.OutputDirectory ?? Path.Combine(
            options.CorpusRoot, "runs", DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(runDir);

        Console.WriteLine("FreeX / Excel functional fidelity batch");
        Console.WriteLine($"Mode: {(options.Recalc ? "compute-fidelity (FreeX formulas recalculated)" : "load-fidelity (FreeX cached values)")}");
        Console.WriteLine($"Files: {files.Count}");
        Console.WriteLine($"Run directory: {runDir}");

        var results = new List<FileResult>();
        try
        {
            foreach (var (index, file) in files.Select((f, i) => (i, f)))
            {
                Console.WriteLine($"[{index + 1}/{files.Count}] {Path.GetFileName(file)}");
                var result = new FileResult(Path.GetFileName(file));
                try { FreeXInspector.Inspect(file, result, options.Recalc); }
                catch (Exception ex) { result.FreeXError = $"{ex.GetType().Name}: {ex.Message}"; }
                try { ExcelInspector.Inspect(file, result, options); }
                catch (Exception ex) { result.ExcelError = $"{ex.GetType().Name}: {ex.Message}"; }
                FidelityComparison.Compare(result, options);
                results.Add(result);
                Console.WriteLine($"      {result.StatusLine()}");
            }

            // The first Excel Open on a cold process can fail; retry those skips now that Excel is warm.
            foreach (var result in results.Where(r => r.Status == FidelityStatus.Skipped && r.FreeXLoaded && !r.ExcelOpened).ToList())
            {
                var file = files.First(f => Path.GetFileName(f) == result.File);
                Console.WriteLine($"[retry] {result.File}");
                result.ExcelError = null;
                result.ExcelCells.Clear();
                try { ExcelInspector.Inspect(file, result, options); }
                catch (Exception ex) { result.ExcelError = $"{ex.GetType().Name}: {ex.Message}"; }
                FidelityComparison.Compare(result, options);
                Console.WriteLine($"      {result.StatusLine()}");
            }
        }
        finally
        {
            ExcelInspector.Shutdown();
        }

        FidelityReport.Write(runDir, results, options);

        var failed = results.Count(r => r.Status == FidelityStatus.Fail);
        var skipped = results.Count(r => r.Status == FidelityStatus.Skipped);
        var passed = results.Count - failed - skipped;
        Console.WriteLine();
        Console.WriteLine($"Pass {passed}/{results.Count}  Fail {failed}  Skipped {skipped}");
        Console.WriteLine($"Report: {Path.Combine(runDir, "README.md")}");
        return failed > 0 ? 1 : 0;
    }
}

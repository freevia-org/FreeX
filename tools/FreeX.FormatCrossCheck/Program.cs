using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace FreeX.FormatCrossCheck;

/// <summary>
/// FreeX Format Cross-Check — proves FreeX's written files open faithfully in an EXTERNAL application
/// (LibreOffice headless), closing the "FreeX read its own output back" validation gap.
///
/// For each FreeX-writable interchange format that LibreOffice also understands (xlsx, ods,
/// SpreadsheetML .xml, html, csv) it runs:
///     FreeX writes file  ->  soffice --headless --convert-to xlsx  ->  FreeX loads LibreOffice's xlsx
///     ->  compare VALUES + FORMULAS + sheet structure to the source (FidelityCompare semantics).
///
/// Exit code: 0 when no FreeX-output-defect is found across every source x format; 1 otherwise.
///
/// Usage:
///   FreeX.FormatCrossCheck                 # runs the default source set
///   FreeX.FormatCrossCheck a.xlsx b.xlsx   # runs the given source workbooks
///   FreeX.FormatCrossCheck --format=ods    # restrict to one interchange format key
/// Set FREEX_SOFFICE to a soffice path to override auto-detection.
/// </summary>
internal static class Program
{
    // Cross-checker exercises the example workbook + two contextures corpus files (multi-sheet, formulas,
    // conditional formats). The corpus lives outside this worktree (git-ignored); point at the main
    // checkout. Override by passing source paths on the command line.
    private static readonly string[] DefaultSources =
    {
        @"C:\Users\quinn\OneDrive\Documents\FreeX\_fidelity-assets\ExcelExamples1.xlsx",
        @"C:\Users\quinn\OneDrive\Documents\FreeX\FreeX\test-corpus\public\contextures\01_pivot-tables_customer-products.xlsx",
        @"C:\Users\quinn\OneDrive\Documents\FreeX\FreeX\test-corpus\public\contextures\05_conditional-formatting_expiry-dates.xlsx",
    };

    public static int Main(string[] args)
    {
        var culture = CultureInfo.GetCultureInfo("en-US");
        System.Threading.Thread.CurrentThread.CurrentCulture = culture;
        System.Threading.Thread.CurrentThread.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        var formatFilter = args.FirstOrDefault(a => a.StartsWith("--format=", StringComparison.Ordinal))?["--format=".Length..];
        var sources = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();
        if (sources.Count == 0) sources = DefaultSources.ToList();

        var outputDir = Path.Combine(Path.GetTempPath(), "formatcrosscheck");
        Directory.CreateDirectory(outputDir);
        var reportPath = Path.Combine(outputDir, "REPORT.txt");

        var sb = new StringBuilder();
        void Emit(string line) { sb.AppendLine(line); Console.WriteLine(line); }

        Emit("================================================================================");
        Emit("  FreeX Format Cross-Check (LibreOffice-backed external validation)");
        Emit($"  Generated : {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        Emit($"  Report    : {reportPath}");

        var soffice = SofficeRunner.Locate();
        if (soffice is null)
        {
            Emit("================================================================================");
            Emit("  FATAL: LibreOffice 'soffice' not found.");
            Emit("  Install: winget install --id TheDocumentFoundation.LibreOffice -e \\");
            Emit("             --accept-source-agreements --accept-package-agreements");
            Emit("  Or set FREEX_SOFFICE=<path-to-soffice.com>. Then re-run this tool.");
            Emit("================================================================================");
            File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
            return 2;
        }
        Emit($"  soffice   : {soffice.ExecutablePath}");
        Emit("================================================================================");
        Emit("");

        var runner = new CrossCheckRunner(Path.Combine(outputDir, "scratch"), soffice);
        int totalDefects = 0;
        int totalMissingSources = 0;
        int totalProcessedSources = 0;

        foreach (var source in sources)
        {
            Emit("################################################################################");
            Emit($"SOURCE: {source}");
            if (!File.Exists(source))
            {
                Emit("  SKIPPED: source workbook not found on disk.");
                Emit("");
                totalMissingSources++;
                continue;
            }
            Emit("################################################################################");
            totalProcessedSources++;

            var results = formatFilter is null
                ? runner.RunAll(source)
                : runner.RunAll(source).Where(r => r.Format.Key.Contains(formatFilter, StringComparison.OrdinalIgnoreCase)).ToList();

            foreach (var r in results)
            {
                ReportOne(Emit, r);
                if (r.Kind == CrossKind.OutputDefect) totalDefects++;
            }
            Emit("");
        }

        Emit("================================================================================");
        Emit("SUMMARY (per source x format)");
        Emit("================================================================================");
        Emit($"  {"",-2}{"format",-18}{"LO open",-9}{"values",-14}{"formulas",-14}verdict");
        Emit($"  {new string('-', 76)}");
        // (the per-row grid is already printed above; this block prints just the final tally)
        Emit("");
        Emit($"  FreeX-output-defects (real bugs): {totalDefects}");
        if (totalMissingSources > 0)
            Emit($"  sources missing on disk          : {totalMissingSources}");
        if (totalProcessedSources == 0)
            Emit("  processed sources                : 0");
        Emit("================================================================================");

        File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
        Console.WriteLine($"\n[Report written to {reportPath}]");
        if (totalDefects > 0)
            return 1;
        return totalMissingSources == 0 ? 0 : 2;
    }

    private static void ReportOne(Action<string> emit, CrossCheckResult r)
    {
        emit("--------------------------------------------------------------------------------");
        emit($"FORMAT: {r.Format.Key}  ({r.Format.Extension})");
        emit($"  ceiling: {r.Format.Notes}");

        switch (r.Kind)
        {
            case CrossKind.FreeXError:
                emit($"  RESULT : FREEX-ERROR — {r.Detail}");
                if (r.Diagnostics is { Length: > 0 }) emit($"           soffice: {Indent(r.Diagnostics)}");
                emit("");
                return;
            case CrossKind.LibreOfficeOpenFailed:
                emit("  LibreOffice opened FreeX file: NO");
                emit($"  RESULT : LIBREOFFICE-OPEN-FAILED — {r.Detail}");
                if (r.Diagnostics is { Length: > 0 }) emit($"           {Indent(r.Diagnostics)}");
                emit("");
                return;
        }

        emit("  LibreOffice opened FreeX file: YES");
        emit($"  sheets  : {r.RefSheetCount} -> {r.GotSheetCount}");
        emit($"  values  : {r.ValuesMatched}/{r.ValuesCompared} literal cells survived" +
             (r.ValuesCompared == 0 ? "" : $"  ({Pct(r.ValuesMatched, r.ValuesCompared)})"));
        if (r.Format.PreservesFormulas)
            emit($"  formulas: {r.FormulasMatched}/{r.FormulasCompared} survived intact" +
                 (r.FormulasCompared == 0 ? "" : $"  ({Pct(r.FormulasMatched, r.FormulasCompared)})") +
                 $";  {r.FormulasRewritten} LO-dialect-rewritten, {r.FormulasVanished} flattened");
        if (!string.IsNullOrEmpty(r.Detail))
            emit($"  note    : {r.Detail}");

        var verdict = r.Kind switch
        {
            CrossKind.Ok => "OK — FreeX output is faithful for an external consumer",
            CrossKind.OutputDefect => "FREEX-OUTPUT-DEFECT — values/formulas the format should keep did not survive",
            _ => r.Kind.ToString(),
        };
        emit($"  VERDICT : {verdict}");

        if (r.ValueDefectSamples.Count > 0)
        {
            emit("  value mismatches (source -> LibreOffice xlsx):");
            foreach (var s in r.ValueDefectSamples.Take(8)) emit($"      {s}");
        }
        if (r.FormulaDefectSamples.Count > 0)
        {
            emit("  formula mismatches (source -> LibreOffice xlsx):");
            foreach (var s in r.FormulaDefectSamples.Take(8)) emit($"      {s}");
        }
        emit("");
    }

    private static string Pct(int n, int d) => d == 0 ? "n/a" : $"{100.0 * n / d:0.0}%";
    private static string Indent(string s) => s.Replace("\n", "\n           ");
}

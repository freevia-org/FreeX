using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using FreeX.App.Services;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.FormatFidelity;

/// <summary>
/// FreeX Format Fidelity — round-trips a source workbook through conversion chains across formats and
/// asserts no information loss beyond each format's documented capability ceiling (§3 of the
/// file-format-support audit). Adapters are obtained ONLY through
/// <c>WorkbookFileAdapterCatalog.CreateDefaultAdapters()</c> + <c>FileFormatResolver</c>, so any
/// newly-registered format is picked up automatically.
///
/// Exit code: 0 when every Full/Lossy dimension across every chain is OK (only None-dim expected loss);
/// 1 when any BUG is found.
/// </summary>
internal static class Program
{
    private const string DefaultWorkbookPath =
        @"C:\Users\quinn\OneDrive\Documents\FreeX\_fidelity-assets\ExcelExamples1.xlsx";

    public static int Main(string[] args)
    {
        var culture = CultureInfo.GetCultureInfo("en-US");
        System.Threading.Thread.CurrentThread.CurrentCulture = culture;
        System.Threading.Thread.CurrentThread.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        var sourcePath = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)) ?? DefaultWorkbookPath;
        var chainFilter = args.FirstOrDefault(a => a.StartsWith("--chain=", StringComparison.Ordinal))?["--chain=".Length..];

        var outputDir = Path.Combine(Path.GetTempPath(), "formatfidelity");
        Directory.CreateDirectory(outputDir);
        var reportPath = Path.Combine(outputDir, "REPORT.txt");

        var sb = new StringBuilder();
        void Emit(string line) { sb.AppendLine(line); Console.WriteLine(line); }

        Emit("================================================================================");
        Emit("  FreeX Format Fidelity Report");
        Emit($"  Generated : {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        Emit($"  Source    : {sourcePath}");
        Emit($"  Report    : {reportPath}");
        Emit("================================================================================");
        Emit("");

        if (!File.Exists(sourcePath))
        {
            Emit($"  FATAL: source workbook not found: {sourcePath}");
            File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
            return 1;
        }

        var runner = new ChainRunner(outputDir);
        var chains = Chains.Phase0(sourcePath);
        chains.AddRange(Chains.Phase2(sourcePath));
        if (chainFilter is not null)
            chains = chains.Where(c => c.Name.Contains(chainFilter, StringComparison.OrdinalIgnoreCase)).ToList();

        var allResults = new List<ChainOutcome>();
        foreach (var chain in chains)
        {
            var outcome = runner.Run(chain);
            allResults.Add(outcome);
            ReportChain(Emit, outcome);
        }

        // ---- BUG clusters grouped by (format, dimension) (§3g) -----------------------------------
        Emit("--------------------------------------------------------------------------------");
        Emit("BUG CLUSTERS (grouped by offending format + dimension)");
        Emit("--------------------------------------------------------------------------------");
        var clusters = allResults
            .Where(o => o.HopError is null)
            .SelectMany(o => o.Results
                .Where(r => r.Kind == ResultKind.Bug)
                .Select(r => (Format: o.Chain.OffendingFormatFor(r.Dimension), r.Dimension, Result: r, o.Chain.Name)))
            .GroupBy(x => (x.Format, x.Dimension))
            .OrderBy(g => g.Key.Format).ThenBy(g => g.Key.Dimension)
            .ToList();

        if (clusters.Count == 0)
        {
            Emit("  (none)");
        }
        else
        {
            foreach (var g in clusters)
            {
                var samples = g.SelectMany(x => x.Result.SampleAddresses).Distinct().Take(5).ToList();
                Emit($"  [{g.Key.Format} / {g.Key.Dimension}] in {g.Select(x => x.Name).Distinct().Count()} chain(s)");
                if (samples.Count > 0)
                    Emit($"      samples: {string.Join(", ", samples)}");
            }
        }
        Emit("");

        int totalBugs = allResults.Where(o => o.HopError is null).Sum(o => o.Results.Count(r => r.Kind == ResultKind.Bug));
        int hopErrors = allResults.Count(o => o.HopError is not null);
        if (hopErrors > 0) totalBugs += hopErrors; // a failed hop is a hard failure too.

        Emit("================================================================================");
        Emit($"  BUGS: {totalBugs}   (hop errors: {hopErrors})");
        Emit("================================================================================");

        File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
        Console.WriteLine($"\n[Report written to {reportPath}]");
        return totalBugs == 0 ? 0 : 1;
    }

    private static void ReportChain(Action<string> emit, ChainOutcome outcome)
    {
        emit("--------------------------------------------------------------------------------");
        emit($"CHAIN: {outcome.Chain.Name}");
        emit($"  hops: {outcome.Chain.HopDescription}");
        if (outcome.HopError is not null)
        {
            emit($"  HOP ERROR: {outcome.HopError}");
            emit("");
            return;
        }

        emit($"  hops OK: save/load succeeded at every stage");
        emit($"  {"Dimension",-18} {"ChainCap",-9} {"Result",-15} Detail");
        emit($"  {new string('-', 76)}");
        foreach (var r in outcome.Results)
        {
            // Only print None dimensions when they actually lost something (EXPECTED-LOSS) or are
            // surprisingly preserved; otherwise the grid is dominated by no-op None rows.
            if (r.ChainCap == Cap.None && r.Kind == ResultKind.PreservedAnyway && r.Total == 0)
                continue;
            var kind = r.Kind switch
            {
                ResultKind.Ok => "OK",
                ResultKind.Bug => "BUG",
                ResultKind.ExpectedLoss => "EXPECTED-LOSS",
                ResultKind.PreservedAnyway => "PRESERVED",
                _ => "?",
            };
            emit($"  {r.Dimension,-18} {r.ChainCap,-9} {kind,-15} {r.Detail}");
            if (r.Kind == ResultKind.Bug && r.SampleAddresses.Count > 0)
                emit($"  {"",-18} {"",-9} {"",-15} samples: {string.Join(", ", r.SampleAddresses.Take(4))}");
        }

        int bugs = outcome.Results.Count(r => r.Kind == ResultKind.Bug);
        int loss = outcome.Results.Count(r => r.Kind == ResultKind.ExpectedLoss);
        int preserved = outcome.Results.Count(r => r.Kind == ResultKind.PreservedAnyway && r.Total > 0);
        emit($"  --> {bugs} BUG, {loss} expected-loss, {preserved} preserved-anyway");
        emit("");
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

internal static partial class ChartInteropCompare
{
    private static void WriteResults(string runDirectory, IReadOnlyList<ChartCompareResult> results)
    {
        var csv = new StringBuilder();
        csv.AppendLine("Chart,Type,Family,FreeXRendererPng,FreeXXlsxOpenedInExcel,FreeXExcelChartCount,FreeXExcelPng,ExcelNativeCreated,ExcelNativePng,ExcelLoadedByFreeX,ExcelRoundTripOpenedInExcel,ExcelRoundTripChartCount,ExcelRoundTripPng,OpenabilityPassed,VisualStatus,VisualGatePassed,Passed,FailureCategory,Notes,OpenabilityError,VisualFailure");
        foreach (var result in results)
        {
            csv.AppendCsvRow(
                result.Chart,
                result.Type,
                result.Family,
                result.FreeXRendererPng,
                result.FreeXXlsxOpenedInExcel,
                result.FreeXExcelChartCount,
                result.FreeXExcelPng,
                result.ExcelNativeCreated,
                result.ExcelNativePng,
                result.ExcelLoadedByFreeX,
                result.ExcelRoundTripOpenedInExcel,
                result.ExcelRoundTripChartCount,
                result.ExcelRoundTripPng,
                result.OpenabilityPassed,
                result.VisualStatus,
                result.VisualGatePassed,
                result.Passed,
                result.FailureCategory,
                result.Notes,
                result.Error,
                result.VisualFailure);
        }

        File.WriteAllText(Path.Combine(runDirectory, "chart_compare_results.csv"), csv.ToString(), Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(runDirectory, "chart_compare_results.json"),
            JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }),
            Encoding.UTF8);
        File.WriteAllText(Path.Combine(runDirectory, "README.md"), CreateMarkdownSummary(results), Encoding.UTF8);
    }

    private static string CreateMarkdownSummary(IReadOnlyList<ChartCompareResult> results)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# FreeX / Excel Chart Interop Comparison");
        builder.AppendLine();
        builder.AppendLine("## Gate Summary");
        builder.AppendLine();
        builder.AppendLine("| Gate | Result |");
        builder.AppendLine("|---|---:|");
        builder.AppendLine($"| Openability/export | {results.Count(result => result.OpenabilityPassed)}/{results.Count} |");
        builder.AppendLine($"| FreeX renderer PNG | {results.Count(result => result.FreeXRendererPng)}/{results.Count} |");
        builder.AppendLine($"| Visual gate | {results.Count(result => result.VisualGatePassed)}/{results.Count(result => result.OpenabilityPassed)} evaluated |");
        builder.AppendLine($"| Known visual gap charts | {results.Count(result => result.KnownVisualGap)} |");
        builder.AppendLine($"| Known-gap threshold allowances used | {results.Count(result => result.VisualStatus == VisualStatuses.KnownGap)} |");
        builder.AppendLine($"| Byte-identical round-trip packages | {results.Count(result => result.ExcelNativeRoundTripXlsxByteIdentical)}/{results.Count(result => result.ExcelNativeCreated && result.ExcelLoadedByFreeX)} |");
        builder.AppendLine($"| Full pass | {results.Count(result => result.Passed)}/{results.Count} |");
        builder.AppendLine();
        builder.AppendLine("Visual status values: `pass` is within the family hash threshold; `known-gap` exceeds the normal threshold but is inside the known-gap allowance; `fail` is a blocking visual mismatch or blank/missing image; `skipped-openability` means Excel open/export did not pass first.");
        builder.AppendLine();
        builder.AppendLine("## Per-Family Visual Summary");
        builder.AppendLine();
        builder.AppendLine("| Family | Charts | Openability pass | Visual pass | Known gaps | Visual fail | Max native-vs-FreeX hash | Threshold(s) |");
        builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|---|");
        foreach (var group in results.GroupBy(result => result.Family, StringComparer.OrdinalIgnoreCase).OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            var evaluated = group.Where(result => result.OpenabilityPassed).ToList();
            var maxDistance = evaluated
                .Select(result => result.HashDistanceNativeVsFreeXXlsx)
                .Where(distance => distance.HasValue)
                .Select(distance => distance!.Value)
                .DefaultIfEmpty()
                .Max();
            var thresholds = string.Join(
                ", ",
                group.Select(result => result.VisualHashThreshold.ToString(CultureInfo.InvariantCulture))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal));
            builder.AppendLine($"| {group.Key} | {group.Count()} | {group.Count(result => result.OpenabilityPassed)} | {group.Count(result => result.VisualStatus == VisualStatuses.Pass)} | {group.Count(result => result.VisualStatus == VisualStatuses.KnownGap)} | {group.Count(result => result.VisualStatus == VisualStatuses.Fail)} | {maxDistance} | {thresholds} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Chart Matrix");
        builder.AppendLine();
        builder.AppendLine("| Chart | Family | Openability/export | Visual status | Native vs FreeX hash | Threshold | Known gap | Excel round-trip hash | Notes |");
        builder.AppendLine("|---|---|---:|---|---:|---:|---|---:|---|");
        foreach (var result in results)
        {
            builder.AppendLine($"| {result.Chart} | {result.Family} | {Mark(result.OpenabilityPassed)} | {result.VisualStatus} | {Metric(result.HashDistanceNativeVsFreeXXlsx)} | {result.VisualHashThreshold} | {EscapeMarkdown(result.KnownVisualGapReason)} | {Metric(result.HashDistanceNativeVsRoundTrip)} | {EscapeMarkdown(result.SummaryNote)} |");
        }

        builder.AppendLine();
        builder.AppendLine("Generated folders:");
        builder.AppendLine("- `freex-xlsx/`: workbooks written by FreeX.");
        builder.AppendLine("- `excel-xlsx/`: matching workbooks authored by Excel COM.");
        builder.AppendLine("- `excel-roundtrip-xlsx/`: Excel-authored workbooks loaded and saved by FreeX.");
        builder.AppendLine("- `png-*`: chart image exports from FreeX renderer or Excel.");
        builder.AppendLine("- `visual_metrics.csv`: nonblank and perceptual hash-distance metrics.");
        builder.AppendLine("- `visual_contact_sheet_*.png`: side-by-side image evidence.");
        return builder.ToString();
    }

    private static void WriteChartList(IEnumerable<ChartCase> cases)
    {
        Console.WriteLine("Available chart cases:");
        foreach (var chartCase in cases)
            Console.WriteLine($"  {chartCase.Name} ({chartCase.Family})");
    }

    private static string CreateDefaultRunDirectory()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile))
            throw new InvalidOperationException("USERPROFILE could not be resolved.");

        return Path.Combine(
            userProfile,
            "freex-xlsx-verify",
            "chart-interop",
            DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
    }

    private static string AppendError(string? existing, string error) =>
        string.IsNullOrWhiteSpace(existing) ? error : $"{existing} | {error}";

    private static string Mark(bool value) => value ? "yes" : "no";

    private static string Metric(int? value) =>
        value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "";

    private static string EscapeMarkdown(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "" : value.Replace("|", "\\|", StringComparison.Ordinal);

    private static void WriteUsage()
    {
        Console.WriteLine(
            """
            Usage:
              dotnet run --project tools/FreeX.ChartInteropCompare -- [options]

            Options:
              --out <directory>                  Output directory. Defaults to %USERPROFILE%\freex-xlsx-verify\chart-interop\<timestamp>.
              --chart <name>[,<name>]            Run only the named chart case(s). Can be repeated.
              --family <classic|chartEx>         Run only the requested chart family. Can be repeated.
              --classic-visual-threshold <0-256> Native-vs-FreeX hash threshold for classic charts. Default: 96.
              --chartex-visual-threshold <0-256> Native-vs-FreeX hash threshold for chartEx charts. Default: 72.
              --known-gap-threshold <0-256>      Allowed hash threshold for declared known visual gaps. Default: 128.
              --roundtrip-threshold <0-256>      Native-vs-roundtrip hash threshold. Default: 4.
              --list-charts                      Print chart cases and exit.
              --help                             Show this help text.

            Exit codes:
              0  Openability/export and visual gate passed (known gaps may be allowed).
              1  Openability/export failure.
              2  Visual mismatch failure after openability passed.
              3  FreeX renderer PNG failure after openability and visual gate passed.
            """);
    }
}

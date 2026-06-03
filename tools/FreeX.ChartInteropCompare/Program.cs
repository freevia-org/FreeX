using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) => ChartInteropCompare.Run(args);
}

internal static partial class ChartInteropCompare
{
    public static int Run(string[] args)
    {
        var options = CompareOptions.Parse(args);
        if (options.ShowHelp)
        {
            WriteUsage();
            return 0;
        }

        if (options.ListCharts)
        {
            WriteChartList(CreateCases());
            return 0;
        }

        var automationCulture = CultureInfo.GetCultureInfo("en-US");
        Thread.CurrentThread.CurrentCulture = automationCulture;
        Thread.CurrentThread.CurrentUICulture = automationCulture;
        CultureInfo.CurrentCulture = automationCulture;
        CultureInfo.CurrentUICulture = automationCulture;

        var runDirectory = options.OutputDirectory ?? CreateDefaultRunDirectory();
        Directory.CreateDirectory(runDirectory);

        var directories = ComparisonDirectories.Create(runDirectory);
        var cases = FilterCases(CreateCases(), options);
        if (cases.Count == 0)
            throw new InvalidOperationException("No chart cases matched the requested filter.");

        var results = cases.Select(chartCase => new ChartCompareResult(chartCase.Name, chartCase.Type.ToString(), chartCase.Family)).ToList();

        Console.WriteLine("FreeX / Excel chart interop comparison");
        Console.WriteLine($"Run directory: {runDirectory}");
        Console.WriteLine($"Chart cases: {cases.Count}");
        Console.WriteLine($"Visual thresholds: classic={options.ClassicVisualHashThreshold}, chartEx={options.ChartExVisualHashThreshold}, known-gap={options.KnownGapVisualHashThreshold}, roundtrip={options.RoundTripVisualHashThreshold}");

        for (var index = 0; index < cases.Count; index++)
        {
            var chartCase = cases[index];
            var result = results[index];
            Console.WriteLine($"[{index + 1}/{cases.Count}] FreeX fixture: {chartCase.Name}");
            GenerateFreeXFixture(chartCase, directories, result);
        }

        for (var index = 0; index < cases.Count; index++)
        {
            var chartCase = cases[index];
            var result = results[index];
            Console.WriteLine($"[{index + 1}/{cases.Count}] Excel interop: {chartCase.Name}");
            RunExcelInteropCase(chartCase, directories, result);
        }

        EvaluateVisualParity(directories, results, options);
        WriteResults(runDirectory, results);
        TryWriteVisualContactSheets(runDirectory, results);
        Console.WriteLine($"Results: {Path.Combine(runDirectory, "chart_compare_results.csv")}");
        Console.WriteLine($"Visual metrics: {Path.Combine(runDirectory, "visual_metrics.csv")}");
        Console.WriteLine($"Summary: {Path.Combine(runDirectory, "README.md")}");

        var openabilityFailed = results.Count(result => !result.OpenabilityPassed);
        var rendererFailed = results.Count(result => result.OpenabilityPassed && !result.FreeXRendererPng);
        var visualFailed = results.Count(result => result.OpenabilityPassed && !result.VisualGatePassed);
        var knownVisualGapCharts = results.Count(result => result.KnownVisualGap);
        var knownVisualGapAllowances = results.Count(result => result.VisualStatus == VisualStatuses.KnownGap);

        Console.WriteLine(openabilityFailed == 0
            ? $"Openability/export: PASS {results.Count}/{results.Count}"
            : $"Openability/export: FAIL {results.Count - openabilityFailed}/{results.Count}; {openabilityFailed} failed.");
        Console.WriteLine(visualFailed == 0
            ? $"Visual gate: PASS {results.Count - openabilityFailed}/{results.Count - openabilityFailed} evaluated; {knownVisualGapCharts} known-gap chart(s), {knownVisualGapAllowances} allowance(s) used."
            : $"Visual gate: FAIL {visualFailed} mismatch(es); {knownVisualGapCharts} known-gap chart(s), {knownVisualGapAllowances} allowance(s) used.");

        if (openabilityFailed > 0)
            return 1;
        if (visualFailed > 0)
            return 2;
        if (rendererFailed > 0)
            return 3;
        return 0;
    }

    private static void RunExcelInteropCase(
        ChartCase chartCase,
        ComparisonDirectories directories,
        ChartCompareResult result)
    {
        var baselineExcelPids = GetExcelProcessIds();
        var ownedExcelPids = new HashSet<int>();
        object? excel = null;
        try
        {
            excel = CreateExcelApplication();
            ownedExcelPids = GetExcelProcessIds().Except(baselineExcelPids).ToHashSet();
            dynamic app = excel;

            ExportFreeXWorkbookThroughExcel(app, chartCase, directories, result);
            GenerateExcelNativeFixture(app, chartCase, directories, result);
            RoundTripExcelWorkbookThroughFreeX(app, chartCase, directories, result);
        }
        catch (Exception ex)
        {
            result.Error = AppendError(result.Error, $"Excel automation failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            if (excel is not null)
            {
                try
                {
                    ((dynamic)excel).Quit();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Excel.Quit failed during cleanup for {chartCase.Name}: {ex.Message}");
                }

                ReleaseComObject(excel);
            }

            WaitForExcelProcessesToExit(ownedExcelPids, 2000);
            KillExcelProcesses(ownedExcelPids);
        }
    }
}

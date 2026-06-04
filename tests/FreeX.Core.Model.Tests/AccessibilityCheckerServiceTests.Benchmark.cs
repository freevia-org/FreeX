using System.Diagnostics;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public sealed partial class AccessibilityCheckerServiceTests
{
    [Fact]
    public void FindIssues_StreamsOccupiedCellsWithoutCopyingUsedCellDictionary()
    {
        var servicePath = FindWorkspaceFile("src", "FreeX.Core.Commands", "AccessibilityCheckerService.cs");
        var serviceDirectory = Path.GetDirectoryName(servicePath)!;
        var source = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(serviceDirectory, "AccessibilityCheckerService*.cs")
                .OrderBy(path => path)
                .Select(File.ReadAllText));

        source.Should().NotContain("GetUsedCells()");
        source.Should().Contain("GetOccupiedCellMap()");
        source.Should().Contain("GetConditionalContrastRules(workbook, sheet, occupiedCells)");
        source.Should().Contain("ConditionalFormatEvaluationCache");
        source.Should().Contain("MatchesTopBottomRule");
        source.Should().Contain("MatchesFormulaRule");
        source.Should().Contain("TryCreateFormulaComparison");
        source.Should().Contain("SharedAppliesToRange");
    }

    [BenchmarkFact]
    public void Benchmark_LowContrastTextWithConditionalFormats_ReportsTimingAndAllocatedBytes()
    {
        const int rows = 20_000;
        const int ruleCount = 8;
        const int iterations = 3;
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Orders");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, rows, 1));

        for (uint row = 1; row <= rows; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"Order {row}"));

        for (var i = 0; i < ruleCount; i++)
        {
            sheet.ConditionalFormats.Add(new ConditionalFormat
            {
                AppliesTo = range,
                Priority = ruleCount - i,
                RuleType = CfRuleType.NoBlanks,
                FormatIfTrue = new CellStyle
                {
                    FontColor = CellColor.Black,
                    FillColor = CellColor.White
                }
            });
        }

        AccessibilityCheckerService.FindIssues(workbook);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var timings = new List<double>(iterations);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var total = Stopwatch.StartNew();
        IReadOnlyList<AccessibilityIssue> issues = [];
        for (var i = 0; i < iterations; i++)
        {
            var step = Stopwatch.StartNew();
            issues = AccessibilityCheckerService.FindIssues(workbook);
            step.Stop();
            timings.Add(step.Elapsed.TotalMilliseconds);
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var ordered = timings.OrderBy(value => value).ToArray();
        var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

        Console.WriteLine(
            "PERF ACCESSIBILITY_LOW_CONTRAST_CF_TEXT " +
            $"rows={rows} rules={ruleCount} steps={iterations} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0}");

        issues.Should().NotContain(issue => issue.Kind == AccessibilityIssueKind.LowContrastCellText);
    }
}

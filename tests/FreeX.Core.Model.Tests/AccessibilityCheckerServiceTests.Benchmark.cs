using System.Diagnostics;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public sealed partial class AccessibilityCheckerServiceTests
{
    [Fact]
    public void FindIssues_SparseFullColumnConditionalFormat_DoesNotMaterializeDenseRange()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sparse");
        var first = new CellAddress(sheet.Id, 1, 1);
        var last = new CellAddress(sheet.Id, CellAddress.MaxRow, 1);
        sheet.SetCell(first, new NumberValue(1));
        sheet.SetCell(last, new NumberValue(100));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(first, last),
            Priority = 1,
            RuleType = CfRuleType.Top10,
            TopBottomRank = 1,
            AboveAverage = true,
            FormatIfTrue = new CellStyle
            {
                FontColor = new CellColor(120, 120, 120),
                FillColor = new CellColor(130, 130, 130)
            }
        });

        AccessibilityCheckerService.FindIssues(workbook);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();

        var issues = AccessibilityCheckerService.FindIssues(workbook);
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        issues.Should().ContainSingle(issue =>
            issue.Kind == AccessibilityIssueKind.LowContrastCellText &&
            issue.Location == last.ToA1());
        allocatedBytes.Should().BeLessThan(8_000_000,
            "a sparse scan must scale with occupied cells rather than the 1,048,576-cell applies-to range");
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

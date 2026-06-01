using System.Diagnostics;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class DataValidationServiceTests
{
    [Fact]
    public void ValidateList_AllowsCaseInsensitiveInlineMatch()
    {
        var rule = NewListRule(SheetId.New(), "Red,Green,Blue");

        DataValidationService.Validate(rule, new TextValue("green")).Should().BeNull();
    }

    [Fact]
    public void ValidateListRange_AllowsCaseInsensitiveRangeMatch()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new TextValue("Red")));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new TextValue("Green")));

        var rule = NewListRule(sheet.Id, "=$A$1:$A$2");

        DataValidationService.Validate(
            rule,
            new TextValue("green"),
            sheet,
            new CellAddress(sheet.Id, 10, 1),
            workbook).Should().BeNull();
    }

    [Fact]
    public void Benchmark_ValidateLargeRangeListMatch_ReportsTimingAndAllocatedBytes()
    {
        const int itemCount = 5_000;
        const int steps = 50;

        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        for (uint row = 1; row <= itemCount; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), Cell.FromValue(new TextValue($"Item {row}")));

        var rule = NewListRule(sheet.Id, $"=$A$1:$A${itemCount}");
        var target = new CellAddress(sheet.Id, (uint)itemCount + 10, 1);
        var value = new TextValue($"Item {itemCount}");

        DataValidationService.Validate(rule, value, sheet, target, workbook).Should().BeNull();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var timings = new double[steps];
        var total = Stopwatch.StartNew();

        for (var i = 0; i < steps; i++)
        {
            var step = Stopwatch.StartNew();
            DataValidationService.Validate(rule, value, sheet, target, workbook).Should().BeNull();
            step.Stop();
            timings[i] = step.Elapsed.TotalMilliseconds;
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        Console.WriteLine(
            "PERF DATAVALIDATION_RANGE_LIST_MATCH " +
            $"items={itemCount} steps={steps} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} " +
            $"mean_ms={timings.Average():F2} " +
            $"p95_ms={timings.OrderBy(x => x).ElementAt((int)Math.Ceiling(steps * 0.95) - 1):F2} " +
            $"max_ms={timings.Max():F2} " +
            $"allocated_bytes={allocatedBytes:N0}");
    }

    private static DataValidation NewListRule(SheetId sheetId, string formula1) =>
        new()
        {
            Type = DvType.List,
            Formula1 = formula1,
            AppliesTo = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 10, 1)),
        };
}

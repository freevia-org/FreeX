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
    public void ValidateListRange_RechecksSheetValuesAfterSourceRangeChanges()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new TextValue("Red")));

        var rule = NewListRule(sheet.Id, "=$A$1:$A$5000");
        rule.ErrorMessage = "No match";
        var target = new CellAddress(sheet.Id, 10, 1);

        DataValidationService.Validate(rule, new TextValue("Green"), sheet, target, workbook)
            .Should().Be("No match");

        sheet.SetCell(new CellAddress(sheet.Id, 100, 1), Cell.FromValue(new TextValue("Green")));

        DataValidationService.Validate(rule, new TextValue("Green"), sheet, target, workbook)
            .Should().BeNull();
    }

    [Fact]
    public void GetListItemsRange_RechecksSheetValuesAfterSourceRangeChanges()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new TextValue("Red")));

        var rule = NewListRule(sheet.Id, "=$A$1:$A$2");

        DataValidationService.GetListItems(rule, sheet, workbook)
            .Should()
            .Equal("Red", BlankValue.Instance.ToString());

        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new TextValue("Green")));

        DataValidationService.GetListItems(rule, sheet, workbook)
            .Should()
            .Equal("Red", "Green");
    }

    [Fact]
    public void GetInputPrompt_RebuildsLookupAfterSameCountRuleReplacement()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var firstAddress = new CellAddress(sheet.Id, 1, 1);
        var secondAddress = new CellAddress(sheet.Id, 2, 1);
        sheet.DataValidations.Add(NewPromptRule(firstAddress, "First"));

        DataValidationService.GetInputPrompt(sheet, firstAddress)
            .Should()
            .Be(new DataValidationService.InputPrompt("Input", "First"));

        sheet.DataValidations[0] = NewPromptRule(secondAddress, "Second");

        DataValidationService.GetInputPrompt(sheet, firstAddress).Should().BeNull();
        DataValidationService.GetInputPrompt(sheet, secondAddress)
            .Should()
            .Be(new DataValidationService.InputPrompt("Input", "Second"));
    }

    [Fact]
    public void GetApplicable_PreservesRuleOrderAcrossExactAndRangeRules()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var exact = NewPromptRule(new CellAddress(sheet.Id, 5, 1), "Exact");
        var range = NewPromptRule(
            new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 10, 1)),
            "Range");
        sheet.DataValidations.Add(exact);
        sheet.DataValidations.Add(range);

        DataValidationService.GetApplicable(sheet, new CellAddress(sheet.Id, 5, 1))
            .Should()
            .Equal(exact, range);
    }

    [Fact]
    public void GetInputPrompt_MatchesAdditionalRanges()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var rule = NewPromptRule(new CellAddress(sheet.Id, 1, 1), "Additional");
        rule.AdditionalRanges.Add(new GridRange(
            new CellAddress(sheet.Id, 3, 2),
            new CellAddress(sheet.Id, 5, 2)));
        sheet.DataValidations.Add(rule);

        DataValidationService.GetInputPrompt(sheet, new CellAddress(sheet.Id, 4, 2))
            .Should()
            .Be(new DataValidationService.InputPrompt("Input", "Additional"));
    }

    [Fact]
    public void InputPrompt_IsValueTypedToAvoidLookupAllocations()
    {
        typeof(DataValidationService.InputPrompt).IsValueType.Should().BeTrue();
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

    [Fact]
    public void Benchmark_GetInputPromptManyRules_ReportsTimingAndAllocatedBytes()
    {
        const int ruleCount = 10_000;
        const int steps = 100;

        var sheet = new Sheet(SheetId.New(), "Sheet1");
        for (uint row = 1; row <= ruleCount; row++)
        {
            sheet.DataValidations.Add(new DataValidation
            {
                AppliesTo = new GridRange(
                    new CellAddress(sheet.Id, row, 1),
                    new CellAddress(sheet.Id, row, 1)),
                ShowInputMessage = true,
                PromptTitle = "Input",
                PromptMessage = $"Rule {row}"
            });
        }

        var target = new CellAddress(sheet.Id, ruleCount, 1);
        DataValidationService.GetInputPrompt(sheet, target)
            .Should()
            .Be(new DataValidationService.InputPrompt("Input", $"Rule {ruleCount}"));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var timings = new double[steps];
        var total = Stopwatch.StartNew();
        DataValidationService.InputPrompt? prompt = null;

        for (var i = 0; i < steps; i++)
        {
            var step = Stopwatch.StartNew();
            prompt = DataValidationService.GetInputPrompt(sheet, target);
            step.Stop();
            timings[i] = step.Elapsed.TotalMilliseconds;
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        Console.WriteLine(
            "PERF DATAVALIDATION_INPUT_PROMPT_LOOKUP " +
            $"rules={ruleCount} steps={steps} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} " +
            $"mean_ms={timings.Average():F2} " +
            $"p95_ms={timings.OrderBy(x => x).ElementAt((int)Math.Ceiling(steps * 0.95) - 1):F2} " +
            $"max_ms={timings.Max():F2} " +
            $"allocated_bytes={allocatedBytes:N0}");

        prompt.Should().Be(new DataValidationService.InputPrompt("Input", $"Rule {ruleCount}"));
        allocatedBytes.Should().BeLessThan(6_000);
    }

    [Fact]
    public void Benchmark_GetListItemsLargeSameSheetRange_ReportsTimingAndAllocatedBytes()
    {
        const int itemCount = 5_000;
        const int steps = 50;

        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        for (uint row = 1; row <= itemCount; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), Cell.FromValue(new TextValue($"Item {row}")));

        var rule = NewListRule(sheet.Id, $"=$A$1:$A${itemCount}");

        var firstItems = DataValidationService.GetListItems(rule, sheet, workbook);
        firstItems.Should().HaveCount(itemCount);
        firstItems[^1].Should().Be($"Item {itemCount}");

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var timings = new double[steps];
        var total = Stopwatch.StartNew();
        IReadOnlyList<string>? items = null;

        for (var i = 0; i < steps; i++)
        {
            var step = Stopwatch.StartNew();
            items = DataValidationService.GetListItems(rule, sheet, workbook);
            step.Stop();
            timings[i] = step.Elapsed.TotalMilliseconds;
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        Console.WriteLine(
            "PERF DATAVALIDATION_GET_LIST_ITEMS_RANGE " +
            $"items={itemCount} steps={steps} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} " +
            $"mean_ms={timings.Average():F2} " +
            $"p95_ms={timings.OrderBy(x => x).ElementAt((int)Math.Ceiling(steps * 0.95) - 1):F2} " +
            $"max_ms={timings.Max():F2} " +
            $"allocated_bytes={allocatedBytes:N0}");

        items.Should().NotBeNull();
        items!.Should().HaveCount(itemCount);
        items[^1].Should().Be($"Item {itemCount}");
        allocatedBytes.Should().BeLessThan(100_000);
    }

    private static DataValidation NewListRule(SheetId sheetId, string formula1) =>
        new()
        {
            Type = DvType.List,
            Formula1 = formula1,
            AppliesTo = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 10, 1)),
        };

    private static DataValidation NewPromptRule(CellAddress address, string message) =>
        NewPromptRule(new GridRange(address, address), message);

    private static DataValidation NewPromptRule(GridRange range, string message) =>
        new()
        {
            AppliesTo = range,
            ShowInputMessage = true,
            PromptTitle = "Input",
            PromptMessage = message
        };
}

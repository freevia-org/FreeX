using System.Diagnostics;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit.Abstractions;

namespace FreeX.Core.Formula.Tests;

public sealed class BuiltInFunctionsPerformanceTests
{
    private readonly ITestOutputHelper _output;

    public BuiltInFunctionsPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Names_ReusesCachedReadOnlyCatalog()
    {
        var names = BuiltInFunctions.Names;

        BuiltInFunctions.Names.Should().BeSameAs(names);
        names.Should().Contain(["SUM", "LET", "LAMBDA"]);
        names.Should().NotBeAssignableTo<string[]>();
    }

    [Fact]
    public void Names_RepeatedAccessDoesNotAllocateFunctionNameArrays()
    {
        _ = BuiltInFunctions.Names.Count;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var i = 0; i < 10_000; i++)
            _ = BuiltInFunctions.Names.Count;

        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        allocatedBytes.Should().BeLessThan(1_024);
    }

    [Fact]
    public void Rept_LargeResultPreallocatesOutputBuffer()
    {
        var evaluator = new FormulaEvaluator();
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        const string formula = "=REPT(\"abcd\",8191)";

        evaluator.Evaluate(formula, sheet).Should().BeOfType<TextValue>()
            .Which.Value.Length.Should().Be(32_764);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();

        var result = evaluator.Evaluate(formula, sheet);

        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        result.Should().BeOfType<TextValue>().Which.Value.Length.Should().Be(32_764);
        Console.WriteLine($"PERF REPT_LARGE_RESULT allocated_bytes={allocatedBytes}");
        allocatedBytes.Should().BeLessThan(
            80_000,
            "REPT should write directly into the final output string instead of allocating an intermediate builder buffer");
    }

    [Fact]
    public void Unique_LargeSingleColumnAvoidsSelectionAllocationChurn()
    {
        var evaluator = new FormulaEvaluator();
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        for (uint row = 1; row <= 20_000; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));

        var warmup = evaluator.Evaluate("=UNIQUE(A1:A20000)", sheet)
            .Should().BeOfType<RangeValue>().Subject;
        warmup.RowCount.Should().Be(20_000);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();

        var stopwatch = Stopwatch.StartNew();
        var result = evaluator.Evaluate("=UNIQUE(A1:A20000)", sheet)
            .Should().BeOfType<RangeValue>().Subject;
        stopwatch.Stop();

        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        result.RowCount.Should().Be(20_000);
        Console.WriteLine($"PERF UNIQUE_SINGLE_COLUMN allocated_bytes={allocatedBytes}");
        _output.WriteLine(
            $"UNIQUE large single-column elapsed={stopwatch.Elapsed.TotalMilliseconds:F2}ms allocated={allocatedBytes:N0} bytes");
        allocatedBytes.Should().BeLessThan(
            900_000,
            "UNIQUE should append discovered single-column values directly instead of tracking source row indexes");
    }

    [Fact]
    public void Xnpv_LargeCashFlowRangeAvoidsDateListAllocationChurn()
    {
        var evaluator = new FormulaEvaluator();
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var firstDate = new DateTime(2026, 1, 1);
        for (uint row = 1; row <= 20_000; row++)
        {
            var value = row == 1 ? -10_000d : 1d;
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(value));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(firstDate.AddDays(row - 1).ToOADate()));
        }

        evaluator.Evaluate("=XNPV(0.08,A1:A20000,B1:B20000)", sheet)
            .Should().BeOfType<NumberValue>();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();

        var result = evaluator.Evaluate("=XNPV(0.08,A1:A20000,B1:B20000)", sheet);

        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        result.Should().BeOfType<NumberValue>();
        _output.WriteLine($"XNPV large cash-flow range allocated={allocatedBytes:N0} bytes");
        allocatedBytes.Should().BeLessThan(
            80_000,
            "scalar-rate XNPV should stream direct value/date ranges instead of materializing RangeValue arrays");
    }

    [Fact]
    public void Npv_LargeCashFlowRangeAvoidsRangeMaterializationChurn()
    {
        var evaluator = new FormulaEvaluator();
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        for (uint row = 1; row <= 20_000; row++)
        {
            var value = row == 1 ? -10_000d : 1d;
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(value));
        }

        evaluator.Evaluate("=NPV(0.08,A1:A20000)", sheet)
            .Should().BeOfType<NumberValue>();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();

        var stopwatch = Stopwatch.StartNew();
        var result = evaluator.Evaluate("=NPV(0.08,A1:A20000)", sheet);
        stopwatch.Stop();

        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        result.Should().BeOfType<NumberValue>();
        _output.WriteLine(
            $"NPV large cash-flow range elapsed={stopwatch.Elapsed.TotalMilliseconds:F2}ms allocated={allocatedBytes:N0} bytes");
        allocatedBytes.Should().BeLessThan(
            80_000,
            "scalar-rate NPV should stream direct value ranges instead of materializing RangeValue arrays");
    }

    [Fact]
    public void Irr_LargeCashFlowRangeAvoidsRangeMaterializationChurn()
    {
        var evaluator = new FormulaEvaluator();
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(-10_000d));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(3_000d));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(4_000d));
        sheet.SetCell(new CellAddress(sheet.Id, 20_000, 1), new NumberValue(5_000d));

        evaluator.Evaluate("=IRR(A1:A20000)", sheet)
            .Should().BeOfType<NumberValue>();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();

        var stopwatch = Stopwatch.StartNew();
        var result = evaluator.Evaluate("=IRR(A1:A20000)", sheet);
        stopwatch.Stop();

        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        result.Should().BeOfType<NumberValue>();
        _output.WriteLine(
            $"IRR large cash-flow range elapsed={stopwatch.Elapsed.TotalMilliseconds:F2}ms allocated={allocatedBytes:N0} bytes");
        allocatedBytes.Should().BeLessThan(
            40_000,
            "IRR should collect direct value ranges without materializing RangeValue arrays");
    }
}

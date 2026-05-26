using System.Diagnostics;
using Freexcel.Core.Formula;
using Freexcel.Core.Model;
using FluentAssertions;
using Xunit.Abstractions;

namespace Freexcel.Core.Formula.Tests;

public sealed class FormulaEvaluatorPerformanceTests
{
    private const int RowCount = 100_000;
    private readonly ITestOutputHelper _output;

    public FormulaEvaluatorPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [InlineData("=SUM(A1:A100000)", 5_000_050_000d)]
    [InlineData("=AVERAGE(A1:A100000)", 50_000.5d)]
    [InlineData("=MIN(A1:A100000)", 1d)]
    [InlineData("=MAX(A1:A100000)", 100_000d)]
    [InlineData("=COUNT(A1:A100000)", 100_000d)]
    public void SingleDirectRangeAggregate_AvoidsPerCellReferenceAllocations(string formula, double expected)
    {
        var evaluator = new FormulaEvaluator();
        var sheet = MakeNumericSheet();

        evaluator.Evaluate(formula, sheet).Should().Be(new NumberValue(expected));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        var result = evaluator.Evaluate(formula, sheet);
        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        result.Should().Be(new NumberValue(expected));
        _output.WriteLine($"{formula}: elapsed={stopwatch.Elapsed.TotalMilliseconds:F2}ms allocated={allocatedBytes:N0} bytes");
        allocatedBytes.Should().BeLessThan(1_000_000);
        stopwatch.Elapsed.Should().BeLessThan(MaxElapsedForPerformanceAssertion());
    }

    [Fact]
    public void MultiRangeAggregateExpansion_AvoidsExcessAllocationChurn()
    {
        var evaluator = new FormulaEvaluator();
        var sheet = MakeNumericSheet();
        const string formula = "=SUM(A1:A100000,A1:A100000)";
        const double expected = 10_000_100_000d;

        evaluator.Evaluate(formula, sheet).Should().Be(new NumberValue(expected));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        var result = evaluator.Evaluate(formula, sheet);
        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        result.Should().Be(new NumberValue(expected));
        _output.WriteLine($"{formula}: elapsed={stopwatch.Elapsed.TotalMilliseconds:F2}ms allocated={allocatedBytes:N0} bytes");
        allocatedBytes.Should().BeLessThan(11_500_000);
    }

    [Theory]
    [InlineData("=SUMIF(B1:B100000,\"A\",A1:A100000)", 2_500_050_000d, 2_500_000)]
    [InlineData("=COUNTIF(B1:B100000,\"A\")", 50_000d, 1_500_000)]
    [InlineData("=AVERAGEIF(B1:B100000,\"A\",A1:A100000)", 50_001d, 2_500_000)]
    [InlineData("=SUMIFS(A1:A100000,B1:B100000,\"A\",C1:C100000,\">50000\")", 1_875_025_000d, 5_800_000)]
    [InlineData("=COUNTIFS(B1:B100000,\"A\",C1:C100000,\">50000\")", 25_000d, 5_000_000)]
    [InlineData("=AVERAGEIFS(A1:A100000,B1:B100000,\"A\",C1:C100000,\">50000\")", 75_001d, 5_800_000)]
    public void ConditionalAggregatesLargeRanges_AvoidFlatteningRangeLists(string formula, double expected, long maxAllocatedBytes)
    {
        var evaluator = new FormulaEvaluator();
        var sheet = MakeConditionalAggregateSheet();

        evaluator.Evaluate(formula, sheet).Should().Be(new NumberValue(expected));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        var result = evaluator.Evaluate(formula, sheet);
        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        result.Should().Be(new NumberValue(expected));
        _output.WriteLine($"{formula}: elapsed={stopwatch.Elapsed.TotalMilliseconds:F2}ms allocated={allocatedBytes:N0} bytes");
        allocatedBytes.Should().BeLessThan(maxAllocatedBytes);
        stopwatch.Elapsed.Should().BeLessThan(MaxElapsedForPerformanceAssertion());
    }

    [Fact]
    public void CountblankSingleDirectRange_AvoidsRangeAndFlattenAllocations()
    {
        var evaluator = new FormulaEvaluator();
        var sheet = MakeCountBlankSheet();
        const string formula = "=COUNTBLANK(A1:A100000)";
        const double expected = 50_000d;

        evaluator.Evaluate(formula, sheet).Should().Be(new NumberValue(expected));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        var result = evaluator.Evaluate(formula, sheet);
        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        result.Should().Be(new NumberValue(expected));
        _output.WriteLine($"{formula}: elapsed={stopwatch.Elapsed.TotalMilliseconds:F2}ms allocated={allocatedBytes:N0} bytes");
        allocatedBytes.Should().BeLessThan(1_000_000);
        stopwatch.Elapsed.Should().BeLessThan(MaxElapsedForPerformanceAssertion());
    }

    [Theory]
    [InlineData("=LARGE(A1:A100000,10)", 99_991d, 2_000_000)]
    [InlineData("=SMALL(A1:A100000,10)", 10d, 2_000_000)]
    [InlineData("=PERCENTILE(A1:A100000,0.5)", 50_000.5d, 5_000_000)]
    public void StatisticalSelectionLargeRanges_AvoidExcessAllocationChurn(string formula, double expected, long maxAllocatedBytes)
    {
        AssertLargeRangeSelectionPerformance(formula, expected, maxAllocatedBytes);
    }

    private void AssertLargeRangeSelectionPerformance(string formula, double expected, long maxAllocatedBytes)
    {
        var evaluator = new FormulaEvaluator();
        var sheet = MakeNumericSheet();

        ((NumberValue)evaluator.Evaluate(formula, sheet)).Value.Should().BeApproximately(expected, 1e-10);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        var result = evaluator.Evaluate(formula, sheet);
        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        ((NumberValue)result).Value.Should().BeApproximately(expected, 1e-10);
        _output.WriteLine($"{formula}: elapsed={stopwatch.Elapsed.TotalMilliseconds:F2}ms allocated={allocatedBytes:N0} bytes");
        allocatedBytes.Should().BeLessThan(maxAllocatedBytes);
        stopwatch.Elapsed.Should().BeLessThan(MaxElapsedForPerformanceAssertion());
    }

    [Theory]
    [InlineData("=XMATCH(100000,A1:A100000,0,1)", 100_000d)]
    [InlineData("=XMATCH(1,A1:A100000,0,-1)", 1d)]
    public void XmatchLargeDirectRangeLinearSearch_AvoidsIndexListAllocation(string formula, double expected)
    {
        var evaluator = new FormulaEvaluator();
        var sheet = MakeNumericSheet();

        evaluator.Evaluate(formula, sheet).Should().Be(new NumberValue(expected));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        var result = evaluator.Evaluate(formula, sheet);
        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        result.Should().Be(new NumberValue(expected));
        _output.WriteLine($"{formula}: elapsed={stopwatch.Elapsed.TotalMilliseconds:F2}ms allocated={allocatedBytes:N0} bytes");
        allocatedBytes.Should().BeLessThan(1_850_000);
        stopwatch.Elapsed.Should().BeLessThan(MaxElapsedForPerformanceAssertion());
    }

    [Theory]
    [InlineData("=XLOOKUP(100000,A1:A100000,B1:B100000,,0,1)", 200_000d)]
    [InlineData("=XLOOKUP(1,A1:A100000,B1:B100000,,0,-1)", 2d)]
    public void XlookupLargeDirectRangeLinearSearch_AvoidsIndexListAllocation(string formula, double expected)
    {
        var evaluator = new FormulaEvaluator();
        var sheet = MakeLookupSheet();

        evaluator.Evaluate(formula, sheet).Should().Be(new NumberValue(expected));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        var result = evaluator.Evaluate(formula, sheet);
        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        result.Should().Be(new NumberValue(expected));
        _output.WriteLine($"{formula}: elapsed={stopwatch.Elapsed.TotalMilliseconds:F2}ms allocated={allocatedBytes:N0} bytes");
        allocatedBytes.Should().BeLessThan(2_650_000);
        stopwatch.Elapsed.Should().BeLessThan(MaxElapsedForPerformanceAssertion());
    }

    private static Sheet MakeNumericSheet()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        for (uint row = 1; row <= RowCount; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
        return sheet;
    }

    private static Sheet MakeConditionalAggregateSheet()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        for (uint row = 1; row <= RowCount; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(row % 2 == 0 ? "A" : "B"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 3), new NumberValue(row));
        }

        return sheet;
    }

    private static Sheet MakeCountBlankSheet()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        for (uint row = 1; row <= RowCount; row++)
        {
            switch (row % 4)
            {
                case 0:
                    sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
                    break;
                case 2:
                    sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue(""));
                    break;
                case 3:
                    sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue("x"));
                    break;
            }
        }

        return sheet;
    }

    private static Sheet MakeLookupSheet()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        for (uint row = 1; row <= RowCount; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row * 2));
        }

        return sheet;
    }

    private static TimeSpan MaxElapsedForPerformanceAssertion()
    {
        return string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase)
            ? TimeSpan.FromSeconds(30)
            : TimeSpan.FromSeconds(2);
    }
}

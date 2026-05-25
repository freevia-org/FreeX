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
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
    }

    private static Sheet MakeNumericSheet()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        for (uint row = 1; row <= RowCount; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
        return sheet;
    }
}

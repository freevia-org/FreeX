using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public sealed class BuiltInFunctionsPerformanceTests
{
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
        allocatedBytes.Should().BeLessThan(
            180_000,
            "REPT should size the StringBuilder once instead of growing through intermediate buffers");
    }
}

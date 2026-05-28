using Freexcel.Core.Formula;
using FluentAssertions;

namespace Freexcel.Core.Formula.Tests;

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
}

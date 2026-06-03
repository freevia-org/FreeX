using System.Diagnostics;
using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class TextToColumnsPlannerTests
{
    [Fact]
    public void SplitFixedWidthText_SourceAvoidsEmptyBreakNormalizationAndPreallocatesParts()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "TextToColumnsSplitter.cs"));

        source.Should().Contain("if (breakPositions.Count == 0)");
        source.Should().Contain("new List<string>(positions.Count + 1)");
    }

    [Fact]
    public void SplitText_SourceAvoidsDelimiterArrayAllocation()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "TextToColumnsSplitter.cs"));

        source.Should().Contain("private static bool IsDelimiter(char ch, string delimiters)");
        source.Should().NotContain("delimiters.Distinct().ToArray()");
    }

    [Fact]
    public void SplitText_LongSingleDelimiterInput_StaysWithinInteractiveBudget()
    {
        var row = string.Join(",", Enumerable.Range(0, 200).Select(index => $"Value{index}"));

        var stopwatch = Stopwatch.StartNew();
        for (var index = 0; index < 1_000; index++)
            TextToColumnsPlanner.SplitText(row, ",", '"', false).Should().HaveCount(200);
        stopwatch.Stop();

        Console.WriteLine($"Text-to-columns single-delimiter split benchmark: {stopwatch.Elapsed.TotalMilliseconds:F2}ms for 1000 runs");
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void SplitText_UnqualifiedInput_AvoidsBuilderAndListOverhead()
    {
        var row = string.Join(",", Enumerable.Range(0, 200).Select(index => $"Value{index}"));

        TextToColumnsPlanner.SplitText(row, ",", '"', false).Should().HaveCount(200);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 500; index++)
            TextToColumnsPlanner.SplitText(row, ",", '"', false).Should().HaveCount(200);
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;

        Console.WriteLine($"Text-to-columns unqualified split allocations: {allocatedBytes:N0} bytes for 500 runs");
        allocatedBytes.Should().BeLessThan(7_000_000);
    }
}

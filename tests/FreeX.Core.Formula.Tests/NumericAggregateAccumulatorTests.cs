using FluentAssertions;
using FreeX.Core.Formula;

namespace FreeX.Core.Formula.Tests;

public sealed class NumericAggregateAccumulatorTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(9)]
    public void Sum_and_average_modes_accumulate_sum(int functionNumber)
    {
        var accumulator = Accumulate(functionNumber, 2, 4, 6);

        accumulator.Count.Should().Be(3);
        accumulator.Sum.Should().Be(12);
        accumulator.Average.Should().Be(4);
    }

    [Fact]
    public void Min_max_and_product_modes_preserve_first_value_initialization()
    {
        Accumulate(4, -5, -2, -8).Max.Should().Be(-2);
        Accumulate(5, 5, 2, 8).Min.Should().Be(2);
        Accumulate(6, -2, 3, 4).Product.Should().Be(-24);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(11)]
    public void Statistical_modes_share_stable_welford_variance(int functionNumber)
    {
        var accumulator = Accumulate(functionNumber, 1_000_000_000_001, 1_000_000_000_002, 1_000_000_000_003);

        accumulator.Count.Should().Be(3);
        accumulator.VarianceM2.Should().BeApproximately(2, 1e-9);
        accumulator.SampleVariance.Should().BeApproximately(1, 1e-9);
        accumulator.PopulationVariance.Should().BeApproximately(2d / 3d, 1e-9);
    }

    [Fact]
    public void Aggregate_and_direct_range_paths_adopt_the_shared_accumulator()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var aggregate = File.ReadAllText(Path.Combine(
            root, "src", "FreeX.Core.Formula", "BuiltInFunctions.InformationA2.cs"));
        var directRange = File.ReadAllText(Path.Combine(
            root, "src", "FreeX.Core.Formula", "FormulaEvaluator.SubtotalAggregateFastPaths.cs"));

        aggregate.Should().Contain("new NumericAggregateAccumulator()")
            .And.NotContain("struct AggregateNumericAccumulator");
        directRange.Should().Contain("new NumericAggregateAccumulator()")
            .And.NotContain("struct DirectRangeNumericAccumulator");
    }

    private static NumericAggregateAccumulator Accumulate(int functionNumber, params double[] values)
    {
        var accumulator = new NumericAggregateAccumulator();
        foreach (var value in values)
            accumulator.Add(value, functionNumber);
        return accumulator;
    }
}

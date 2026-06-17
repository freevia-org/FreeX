using FluentAssertions;
using FreeX.App.Presentation.PivotUI;

namespace FreeX.App.Presentation.Tests.PivotUI;

public sealed class PivotAggregationFunctionsTests
{
    [Fact]
    public void All_StartsWithSumAndCount()
    {
        PivotAggregationFunctions.All.Take(2).Select(f => f.FunctionCode)
            .Should().Equal("sum", "count");
    }

    [Fact]
    public void All_FunctionCodesAreLowercaseAndUnique()
    {
        var codes = PivotAggregationFunctions.All.Select(f => f.FunctionCode).ToList();

        codes.Should().OnlyHaveUniqueItems();
        codes.Should().OnlyContain(code => code == code.ToLowerInvariant());
    }

    [Fact]
    public void FromCode_ResolvesKnownCodeCaseInsensitively()
    {
        PivotAggregationFunctions.FromCode("SUM").Should().BeSameAs(PivotAggregationFunctions.Sum);
        PivotAggregationFunctions.FromCode(" average ").Should().BeSameAs(PivotAggregationFunctions.Average);
    }

    [Fact]
    public void FromCode_TreatsAvgAsAverageAlias()
    {
        PivotAggregationFunctions.FromCode("avg").Should().BeSameAs(PivotAggregationFunctions.Average);
    }

    [Fact]
    public void FromCode_ReturnsNullForUnknownOrBlank()
    {
        PivotAggregationFunctions.FromCode("bogus").Should().BeNull();
        PivotAggregationFunctions.FromCode(null).Should().BeNull();
        PivotAggregationFunctions.FromCode("   ").Should().BeNull();
    }
}

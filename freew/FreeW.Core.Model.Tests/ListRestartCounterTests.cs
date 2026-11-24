namespace FreeW.Core.Model.Tests;

public sealed class ListRestartCounterTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(5, 5)]
    [InlineData(-3, 1)]
    public void ExplicitStartSupportsZeroAndKeepsInvalidNegativeFallback(int start, int expected)
    {
        ListRestartCounter.NextCount(20, start).Should().Be(expected);
        ListRestartCounter.NextCount(expected, null).Should().Be(expected + 1);
    }

    [Fact]
    public void UnspecifiedStartStillBeginsAtOne()
    {
        ListRestartCounter.NextCount(0, null).Should().Be(1);
    }
}

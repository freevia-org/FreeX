using FluentAssertions;
using Freexcel.Core.Commands;

namespace Freexcel.Core.Model.Tests;

public sealed class SubtotalFunctionServiceTests
{
    [Theory]
    [InlineData("sum", 9)]
    [InlineData("average", 1)]
    [InlineData("count", 2)]
    [InlineData("max", 4)]
    [InlineData("min", 5)]
    [InlineData("11", 11)]
    public void TryParse_AcceptsExcelSubtotalFunctions(string text, int expectedFunctionNumber)
    {
        SubtotalFunctionService.TryParse(text, out var functionNumber).Should().BeTrue();
        functionNumber.Should().Be(expectedFunctionNumber);
    }

    [Theory]
    [InlineData("")]
    [InlineData("median")]
    [InlineData("12")]
    public void TryParse_RejectsUnsupportedFunctions(string text)
    {
        SubtotalFunctionService.TryParse(text, out _).Should().BeFalse();
    }
}

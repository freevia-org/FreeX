using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.Core.Calc.Tests;

public sealed class NumberFormatterDateTests
{
    [Fact]
    public void Format_InvalidDateSerialFallsBackToSerialInsteadOfThrowing()
    {
        var action = () => NumberFormatter.Format(new DateTimeValue(9_999_999), "General");

        action.Should().NotThrow();
        action().Should().Be("9999999");
    }
}

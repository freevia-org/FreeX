using FluentAssertions;
using Freexcel.App.Host;

namespace Freexcel.App.Host.Tests;

public sealed class BackstageGreetingFormatterTests
{
    [Theory]
    [InlineData(0, "Good morning")]
    [InlineData(11, "Good morning")]
    [InlineData(12, "Good afternoon")]
    [InlineData(16, "Good afternoon")]
    [InlineData(17, "Good evening")]
    [InlineData(23, "Good evening")]
    public void FormatGreeting_UsesExcelBackstageDaypartBoundaries(int hour, string expected)
    {
        BackstageGreetingFormatter.FormatGreeting(hour).Should().Be(expected);
    }
}
